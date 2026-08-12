# Module 10 — Engine Architecture

### ECS, data-oriented design, job systems, and the structural decisions that decide whether a codebase survives contact with a shipping schedule

*~28 min read · Part IV: Engine Breadth · Prerequisites: Modules 01, 03*

---

## Read this first

**This module is the one where your existing experience is worth the most** — you have architected real systems, and most of that transfers. The job is knowing which of your instincts to keep and which to actively suppress.

| Instinct | Verdict in an engine |
|---|---|
| Separation of concerns | ✅ Keep |
| Dependency inversion at module boundaries | ✅ Keep |
| Testability | ✅ Keep |
| Incremental refactoring | ✅ Keep |
| Fear of premature abstraction | ✅ Keep, and turn it up |
| Deep inheritance hierarchies | ❌ Actively harmful |
| Allocating freely | ❌ Actively harmful (Module 01) |
| Pointer-chasing through object graphs | ❌ Actively harmful (Module 03) |
| Polymorphism in inner loops | ❌ Actively harmful |
| "Memory layout is an implementation detail" | ❌ The core mistake |

> **The distinguishing constraint of engine architecture is that the same code runs 60 times a second over thousands of entities.** That turns *layout* into an architectural concern, on the same level as coupling.

Everything below follows from that one sentence.

---

## Why inheritance failed in games

The historical arc is worth knowing, because it explains why the industry landed where it did — and because "why not just use inheritance?" is a real interview question.

### Act 1: the class hierarchy

Early engines modelled game objects as class hierarchies:

```
Entity → Actor → Character → Player
                           → NPC
       → Prop → Door
              → Chest
```

It reads beautifully in a design document and collapses within a month of real content. You need:
- A door that's also a damageable object
- A vehicle that's also a container
- An NPC that's sometimes a physics ragdoll
- A chest that's also a mimic that's also an NPC

The hierarchy either **explodes combinatorially** (`DamageableContainerDoor`) or becomes a **god class** with every possible field on every object, 90% of them null.

### Act 2: composition

**Composition over inheritance** was the first fix: an entity is a bag of components (`Transform`, `Renderable`, `RigidBody`, `Health`), assembled from *data* rather than declared in *code*. A designer can make a mimic by adding an `NPCBehavior` component to a chest, with no programmer involved.

Unity's `GameObject`/`MonoBehaviour` model is this, and it genuinely solved the modelling problem. Every engine adopted it.

**But it left the layout problem.** Every component is an individually allocated object with a vtable pointer, scattered across the heap, updated via a virtual call. Iterating 10,000 of them is 10,000 potential cache misses and 10,000 indirect branches that the CPU can't predict. **The logic is fine; the machine hates it.**

### Act 3: ECS

**ECS (Entity-Component-System)** fixes both problems:

- **Entity** — just an ID. Usually a packed integer with a **generation counter**.
- **Component** — plain data, no behavior, stored in **tightly packed contiguous arrays**.
- **System** — a function that operates on all entities having a given set of components, iterating linearly.

> **What's a generation counter?** An entity handle is two fields: `{ index, generation }`. When entity 42 is destroyed and slot 42 is reused for a new entity, the generation increments. Now an old handle `{42, gen 3}` can be detected as stale by comparing against the current `{42, gen 4}`. This gives you the safety of weak references with the performance of raw array indices — no reference counting, no GC pressure, no dangling pointers. It's a pattern worth stealing for anything with recycled slots.

The performance argument is **entirely about memory**: a system that updates positions touches only positions, in order, with perfect hardware prefetching. No wasted cache lines, no indirect calls.

This is **data-oriented design** — designing around the data's shape and access pattern rather than around conceptual object models. Mike Acton's framing: *"the purpose of all programs, and all parts of those programs, is to transform data from one form to another."*

### Two storage strategies

**Archetype / chunk-based** (Unity DOTS, flecs):
Entities with **identical component sets** live together in contiguous chunks. All entities with exactly `{Transform, Velocity, Renderable}` are in one block of memory.
- ✅ Iteration is maximally fast — a query is "find matching archetypes, iterate each linearly."
- ❌ Adding or removing a component **moves the entity to a different archetype**, which costs a copy of all its data.

**Sparse set** (EnTT):
Each component type has a **dense packed array** of data plus a **sparse array** mapping entity ID → dense index.
- ✅ Adding/removing components is O(1) and cheap.
- ❌ Multi-component queries need to intersect sets, so iteration is slightly less optimal.

**Pick based on whether your entities change shape often.** For a roguelike with status effects, temporary buffs, and dynamically-attached behaviors, **sparse sets are usually the more comfortable fit** — entities gain and lose components constantly, and archetype migration would dominate.

---

## ECS in TypeScript, honestly

The pattern translates, with a caveat you must be clear-eyed about — and being clear-eyed about it is exactly the kind of judgment this role wants.

```ts
// Components as parallel typed arrays — SoA, not AoS (Module 03)
class TransformStore {
  x = new Float32Array(MAX);
  y = new Float32Array(MAX);
  z = new Float32Array(MAX);
  // NOT: positions: {x, y, z}[]  ← every element is a separate heap object
}

function movementSystem(t: TransformStore, v: VelocityStore, ids: Uint32Array, dt: number) {
  for (let i = 0; i < ids.length; i++) {
    const e = ids[i];
    t.x[e] += v.x[e] * dt;
    t.y[e] += v.y[e] * dt;
    t.z[e] += v.z[e] * dt;
  }
}
```

This is genuinely fast in V8: typed arrays are **real contiguous memory** (they're backed by an `ArrayBuffer`, not a JS object graph), the loop is monomorphic so it JITs to tight machine code, and **nothing allocates** so the GC never runs.

### The caveat

**The win in a JS engine comes overwhelmingly from avoiding allocation and megamorphism — not from L1 cache tuning the way it does in C++.**

Why: JS objects have header overhead you don't control, `Map` lookups are not free, property access goes through hidden classes, and you don't control layout precisely enough for the fine-grained cache tricks that make C++ ECS shine. You get the big wins (no GC, linear typed-array access) and not the small ones.

So: **take ECS for the allocation discipline and the linear typed-array iteration, and be skeptical of framework-heavy ECS libraries whose query machinery costs more than the naive loop it replaced.** Measure.

In a TS engine, a hand-rolled *"arrays of components + explicit system functions"* design frequently beats a general ECS library. **Choosing that deliberately is good engineering, not laziness** — and being able to explain the reasoning is exactly the "know when not to copy the industry pattern" judgment the JD asks for.

### ECS is not mandatory

Plenty of shipped games use a simpler model — a handful of typed arrays and explicit update functions — and are better for it. Especially a voxel game, where **most of the world isn't entities at all**; it's a grid. Your entity count might be 200, not 200,000.

What's non-negotiable is the **data-oriented instinct**: contiguous storage, linear iteration, no allocation in hot loops.

---

## Engine subsystems and their boundaries

A working engine is roughly this set of subsystems, in rough initialization order:

```
Platform      — canvas, input devices, timing, file/network access
Memory        — pools, arenas, typed-array allocators
Jobs          — worker pool, task graph
Assets        — loading, caching, hot reload, reference counting
World/Scene   — entities, components, spatial structures
Physics       — collision, integration, queries
Gameplay      — game-specific systems
Animation     — skeletal/procedural, sampling at render rate
Audio         — mixing, spatialization
Renderer      — RHI, render graph, passes, materials
UI/Debug      — HUD, console, tools, profiler overlays
```

**Two architectural rules matter more than the exact list.**

### Rule 1: dependencies point downward

The renderer must not know about gameplay. Gameplay must not know about WebGPU.

When gameplay needs something drawn, it **sets data that the renderer reads** — it doesn't call a draw function. When the renderer needs to know what's visible, it queries the world's spatial structure; it doesn't ask entities about themselves.

This is what lets you:
- Swap a backend (or add a headless/null one for tests)
- Write tests for gameplay without a GPU
- **Critically for a small studio: let two people work in the same codebase without constant conflicts**

That last one is the real payoff at a studio of this size, and it's worth saying out loud in an interview — architecture as a *team throughput* decision, not an aesthetic one.

### Rule 2: isolate the API behind an RHI

> **RHI = Render Hardware Interface.** A thin layer of your own types (`Buffer`, `Texture`, `Pipeline`, `CommandList`) that wrap the actual graphics API. The term comes from Unreal but the pattern is universal.

A thin RHI over WebGPU costs you a day and buys you:
- A debug/null backend for headless tests
- One place to instrument every call (counters, validation, capture)
- Survival when the API changes — and WebGPU is still evolving

**Don't over-abstract it.** A *leaky, thin* RHI is right: it should look like WebGPU with your names on it. A fully generic abstraction that also supports WebGL is usually a waste unless you actually need WebGL, because the lowest common denominator costs you everything WebGPU is good at.

The rule of thumb: abstract the *objects*, not the *concepts*. Wrapping `GPUBuffer` is cheap and useful. Inventing your own shading language that compiles to both WGSL and GLSL is a project.

---

## The render graph

As passes multiply — shadow, depth prepass, opaque, raymarch, transparent, bloom chain, tonemap — manually managing transient textures and pass ordering becomes error-prone. Which texture is the bloom's third downsample? Can it share memory with the SSAO buffer? Did I clear it?

A **render graph** (a.k.a. frame graph, popularized by Frostbite's 2017 GDC talk) makes each pass **declare its inputs and outputs** rather than grabbing resources directly:

```ts
graph.addPass('bloom-downsample-2', {
  reads:  ['bloom-mip-1'],
  writes: ['bloom-mip-2'],
  execute: (ctx) => { /* ... */ },
});
```

The engine builds a **DAG** (directed acyclic graph — nodes with dependencies and no cycles) from those declarations, then automatically:

- **Allocates and aliases transient resources.** Two passes that never overlap in time can share the same physical memory. On a large frame this can halve your render target memory.
- **Orders passes and inserts barriers.**
- **Culls passes whose outputs nobody consumes.** Turn off the debug overlay and its pass — and every pass that only fed it — costs literally nothing, automatically.
- **Produces a visualizable graph.** Excellent for debugging, and a great screenshot.

**WebGPU handles barriers for you** (Module 05), which removes the hardest part of implementing one. But the declarative structure is still worth it once you have more than about eight passes. Before that, explicit code is clearer and shorter.

**Knowing when to introduce it — and being willing to say "not yet" — is the judgment being tested.**

---

## Jobs and parallelism on the web

Games parallelize heavily. In the browser you have **Web Workers**, and the model is more restrictive than native threads: separate global scopes, no shared objects by default, message-passing only.

### What belongs on a worker in a voxel engine

- Terrain generation (noise, structure placement, cave carving)
- **Chunk meshing** — the greedy mesher, perfectly parallel per chunk (Module 08)
- Light propagation flood fills
- Pathfinding
- Asset decoding and decompression
- Save/serialization

### What must stay on the main thread

- **All WebGPU calls** — the device isn't generally transferable to workers
- DOM and input handling
- Anything that must be frame-synchronous

### The data transfer problem is the whole design

Three options, and the choice shapes your architecture:

| Mechanism | Cost | Semantics |
|---|---|---|
| **`postMessage` + structured clone** | A full copy | Both sides keep their data |
| **Transferable `ArrayBuffer`** | Zero-copy | **Ownership moves**; sender's copy is detached and unusable |
| **`SharedArrayBuffer`** | Zero-copy | True shared memory, with `Atomics` for synchronization |

**Structured clone** is fine for small messages and fatal for a 32 KB chunk per frame at scale — that's a full memcpy plus serialization overhead, on the main thread.

**Transferables** are excellent for *"worker produces a mesh buffer, hands it over."* The pattern fits perfectly: the worker doesn't need the buffer after it's done.

**`SharedArrayBuffer` requires cross-origin isolation** (Module 01):
```
Cross-Origin-Opener-Policy: same-origin
Cross-Origin-Embedder-Policy: require-corp
```
**Decide this early; retrofitting it is painful**, because `require-corp` breaks every cross-origin resource that doesn't opt in — third-party embeds, analytics, CDN assets.

### A pragmatic architecture that works

- A **fixed worker pool** sized to `navigator.hardwareConcurrency - 1` (leave one core for the main thread).
- A **job queue with priorities** — in-frustum chunks before behind-the-camera ones (Module 07).
- Chunk **voxel data in a `SharedArrayBuffer`** region so workers can read neighbours without copying (this is what makes the halo approach from Module 08 free).
- Finished **mesh buffers returned as transferables**.
- **Cap how many completed jobs you integrate per frame** so a burst never blows the budget (Module 01's amortization rule).

### Two classic traps

**Spawning a worker per task.** Worker startup is *milliseconds* — it has to spin up a whole JS realm. Pool them, always.

**Chatty message protocols.** Each `postMessage` has real overhead (serialization, event loop scheduling, structured clone even for small objects). Batch them: send 20 chunk requests in one message, not 20 messages.

---

## Resource management and hot reload

### Handle-based assets

**Assets need identity, lifetime, and asynchrony.** The design that handles all three:

```ts
type AssetHandle<T> = { index: number; generation: number };  // 8 bytes, copyable, serializable
```

- **Handles rather than direct references** mean an asset can be reloaded, moved, or evicted without every holder knowing.
- **Reference counting or explicit ownership** for lifetime.
- **Async loading with a placeholder** — a checkerboard texture, a unit cube mesh — so nothing blocks.
- **A central registry** mapping handles to actual resources.

Handles also make **serialization** dramatically easier: you save a stable ID, not a pointer. And they're the foundation of hot reload, because swapping what a handle points to is invisible to every holder.

### Hot reload is a productivity multiplier, not a luxury

Being able to change a shader, a texture, or a tuning value and see it **without restarting** turns a 40-second iteration loop into a 1-second one. If you tweak a shader 50 times in a day, that's 30 minutes of waiting versus 50 seconds.

**Over a project, that is worth more than most optimizations.**

In a web engine you get much of it nearly free:

```ts
// dev only
const ws = new WebSocket('ws://localhost:5173');   // your dev server's HMR channel
ws.onmessage = async (e) => {
  const { path } = JSON.parse(e.data);
  if (path.endsWith('.wgsl')) {
    const src = await fetch(path + '?t=' + Date.now()).then(r => r.text());
    const result = await recreatePipeline(path, src);
    if (result.error) showInGameError(result.error);   // NOT a black screen
  }
};
```

**Surface compile errors in-game, not as a black screen.** A shader that fails to compile should keep the last working version and show the error as an overlay. This one detail is the difference between hot reload being delightful and being a trap.

**Build it in the first month.**

The Engine JD's *"Improve the Development Experience"* and *"Build tools, workflows, and systems that allow the team to work efficiently"* bullets point directly at this. **In a small studio, engine engineers are judged substantially on how fast everyone *else* can work.** That framing is worth saying explicitly in an interview.

---

## Purpose-built vs. copied patterns

The Engine JD is unusually explicit here:

> *"Bakest and Levers and Chests embrace purpose-built tech to serve unique artistic and technical constraints; simply copying common industry engine patterns won't always work. Wisdom will be needed to know when to stick with what works and when to forge a new path."*

**That is a request for a specific kind of judgment**, and it's worth having concrete examples ready in both directions. This is very likely to be an actual interview question.

### Patterns worth copying, because the reasoning is universal

- Fixed timestep with interpolation (Module 01)
- Data-oriented storage
- Bind groups organized by update frequency (Module 05)
- Sort keys to minimize state changes (Module 09)
- Handle-based resources
- Render graphs at scale
- Two-pass Hi-Z occlusion culling (Module 09)

### Patterns worth questioning in a voxel game

| Standard pattern | Why it may not fit |
|---|---|
| **General mesh LOD systems** | Voxel LOD comes from the data structure's mip levels, not a mesh simplifier (Module 07) |
| **PBR material graphs** | A stylized voxel game with a fixed palette may need three material parameters, not a node graph (Module 06) |
| **Generic scene graphs** with arbitrary parent-child transforms | Most of a voxel world is a static grid; a full transform hierarchy is overhead for the 1% that moves |
| **A general physics engine** | AABB-vs-grid collision is a few hundred lines, is faster, is deterministic, and has no dependency. Integrating Rapier or Havok may cost more than it saves (Module 12) |
| **Shadow map cascades** | Marching the grid may simply be better here (Module 06) |
| **Deferred rendering's G-buffer** | If you ray trace primary visibility, you already have surface data in registers (Module 04) |

**The pattern behind the pattern: general engines pay for generality with abstraction. When your content is uniform, that generality is pure cost.**

But the discipline is to be able to say **why** in each case, and — just as importantly — **to recognize the cases where the general solution is right and reinventing it is ego.** Nobody should write their own audio mixer, their own font shaper, or their own compression codec without a specific reason. Have one example ready of something you *wouldn't* rebuild; it makes the rest of the answer credible.

---

## Testing an engine

Real, and a JD bullet (*"help keep things organized and improve testing"*). Here's what actually works, ranked by value-per-effort.

**Unit tests for pure functions.** Math, meshing, packing/unpacking, serialization round-trips, noise determinism. High value, cheap, fast. Your Module 02 math library should have these already.

**Golden / snapshot tests for the mesher.** Feed a fixed voxel volume, assert the exact vertex buffer bytes. Catches regressions instantly, and when it fails you have a precise diff rather than "the world looks weird."

**Determinism tests.** Run the simulation N steps from a seed, hash the state, compare to a stored hash. **This one test catches an enormous class of bugs** — anywhere `Math.random()` sneaks in, anywhere iteration order changes, anywhere render code writes back into simulation state (Module 01).

**Image-diff tests for rendering.** Render a fixed scene headlessly, compare against a reference image with a perceptual threshold. Genuinely valuable, genuinely fiddly — driver and vendor differences require real tolerance, and you'll need per-platform references.

**Performance regression tests.** Assert that a benchmark scene stays under a frame time budget in CI. **Catches the slow creep that nobody notices day to day** — the 0.3 ms per week that becomes 5 ms per quarter.

### What generally doesn't pay

Heavy mocking of engine subsystems, and unit tests over rendering code whose correctness is visual. **Know the difference and say so** — *"improve testing while improving the pace of development, not slowing it down with unnecessary process"* is a direct quote from the JD, and it's a stated preference for pragmatism. Answering "I'd add 90% coverage" to that JD would be a wrong answer.

---

## Common confusions

**"ECS is the modern way, so I should use an ECS library."** ECS solves a specific problem — iterating many entities with varied component sets, cache-efficiently. If you have 200 entities and a voxel grid, you may be buying machinery you don't need. Keep the data-oriented storage; skip the framework until it earns its place.

**"I'll abstract the graphics API properly so I can swap backends."** You will never swap backends. Abstract it thinly for testing and instrumentation, not for portability you won't use. (WebGPU is *already* the portability layer.)

**"A render graph is over-engineering for my project."** True at 5 passes. False at 20. The mistake is picking a side permanently instead of noticing when the count crossed over.

**"Workers will make it faster."** Workers make it *parallel*. If your bottleneck is the main thread integrating results, or `postMessage` serialization, adding workers makes it slower. Profile the transfer cost, not just the compute.

**"Hot reload is a nice-to-have I'll add later."** The value is proportional to how many iterations remain. Adding it in month one is worth 10× adding it in month ten, which is the opposite of most infrastructure.

---

## The interview answer

***"How would you architect a voxel engine?"***

> "Layered, with dependencies pointing down: platform, jobs, assets, world, gameplay, renderer, with a thin RHI over WebGPU — thin enough to leak, because a fully generic abstraction would cost me everything WebGPU is good at.
>
> The world is chunked voxel storage, palette-compressed, mirrored to a GPU brickmap. Meshing, generation, and lighting run on a fixed worker pool with results returned as transferable buffers, integrated under a per-frame budget so a burst can't blow the frame.
>
> Entities use data-oriented storage — typed arrays per component, systems as plain functions — but I'd start with the simplest thing that isn't allocation-heavy rather than adopting a full ECS framework on day one, because in JS most of the win is allocation discipline rather than cache-line tuning.
>
> A render graph once the pass count justifies it, not before. And handle-based assets with hot reload early, because iteration speed compounds and in a small studio the engine's job is partly to make everyone else faster."

***"When would you not use an ECS?"***

> "When entity count is low and the query machinery costs more than the loops it replaces — which happens more often in JS than in C++, because the win there is mostly about allocation discipline rather than cache lines. I'd keep the data-oriented storage regardless and skip the framework. And in a voxel game a lot of what would be entities elsewhere is just grid data, so the entity count may be small enough that it never pays."

---

## Exercise — Voxelforge, Stage 10

**1. Refactor into explicit layers** with a **dependency lint rule** — a simple ESLint `no-restricted-imports` config enforcing that the renderer can't import gameplay. Automating the rule is the point; a convention nobody enforces isn't architecture.

**2. Build a worker pool** with a priority job queue. Move terrain generation and meshing onto it. Return meshes as **transferable `ArrayBuffer`s**, and confirm with a benchmark that transferring beats cloning.

**3. Cap integration at 2 ms per frame**; verify with your Module 09 HUD that sprinting across the world produces **no spikes** in the p99.

**4. Implement handle-based assets** (`{index, generation}`) with a registry, plus **shader hot reload**: edit a WGSL file, see it applied within a second, **with compile errors surfaced in-game rather than as a black screen.**

**5. Add data-oriented entity storage** — typed arrays per component, systems as functions — and a player entity with transform and velocity. Resist adding a framework.

**⭐ 6. Write the three highest-value tests** and wire them into CI:
   - A mesher **golden test** (fixed volume → exact vertex bytes)
   - A **determinism hash test** (N steps from a seed → stable hash)
   - A **frame-budget performance test** (benchmark scene stays under X ms)

**Stretch:** implement a minimal render graph — passes declaring reads/writes, automatic transient allocation and dead-pass culling — and verify that toggling your debug heatmap off makes its pass disappear entirely from the timestamp HUD.

---

## Go deeper

- **Jason Gregory, *Game Engine Architecture* (3rd ed.)** — the field's standard reference. Read Chapters 1–6 and 14–15.
- **Robert Nystrom, *Game Programming Patterns*** — free online. Read Component, Service Locator, Dirty Flag, Object Pool, Spatial Partition, and Data Locality. Each is short.
- **Mike Acton, "Data-Oriented Design and C++" (CppCon 2014)** — abrasive, correct, and the clearest statement of the philosophy in existence. Watch it once even though you don't write C++.
- **Richard Fabian, *Data-Oriented Design*** — free at dataorienteddesign.com/dodbook.
- **Sander Mertens's ECS FAQ and flecs articles** — the best practical writing on ECS storage strategies and their real tradeoffs, by someone who built one.
- **Yuriy O'Donnell, "FrameGraph: Extensible Rendering Architecture in Frostbite" (GDC 2017)** — the render graph talk everyone cites.
- **Christian Gyrling, "Parallelizing the Naughty Dog Engine Using Fibers" (GDC 2015)** — job systems done properly, and a genuinely great talk.

---

**Next:** [Module 11 — Asset Pipelines and Working with Artists](./11-asset-pipelines-and-artists.md)
