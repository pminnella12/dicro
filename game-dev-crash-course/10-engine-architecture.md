# Module 10 — Engine Architecture

### ECS, data-oriented design, job systems, and the structural decisions that decide whether a codebase survives contact with a shipping schedule

*~13 min read · Part IV: Engine Breadth · Prerequisites: Modules 01, 03*

---

You already know software architecture. What you don't yet know is which of your instincts to keep.

Things that transfer directly: separation of concerns, dependency inversion at module boundaries, testability, incremental refactoring, and a healthy fear of premature abstraction.

Things that actively hurt: deep inheritance hierarchies, allocating freely, chasing pointers through object graphs, polymorphism in inner loops, and treating memory layout as an implementation detail.

> The distinguishing constraint of engine architecture is that the same code runs 60 times a second over thousands of entities. That turns *layout* into an architectural concern, on the same level as coupling.

---

## Why inheritance failed in games

The historical arc is worth knowing because it explains why the industry landed where it did.

Early engines modelled game objects as class hierarchies: `Entity → Actor → Character → Player`. It reads beautifully and collapses immediately. You need a door that's also a damageable object; a vehicle that's also a container; an NPC that's sometimes a physics object. The hierarchy either explodes combinatorially or becomes a god class with every possible field.

**Composition over inheritance** was the first fix: an entity is a bag of components (`Transform`, `Renderable`, `RigidBody`, `Health`), assembled from data rather than declared in code. Unity's `GameObject`/`MonoBehaviour` model is this, and it solved the modelling problem.

But it left the *layout* problem. Every component is an individually allocated object with a vtable, scattered across memory, updated via a virtual call. Iterating 10,000 of them is 10,000 cache misses and 10,000 indirect branches. The logic is fine; the machine hates it.

**ECS (Entity-Component-System)** is the fix for both:

- **Entity** — just an ID. Usually a packed integer with a generation counter to detect stale references.
- **Component** — plain data, no behavior, stored in **tightly packed contiguous arrays**.
- **System** — a function that operates on all entities having a given set of components, iterating linearly.

The performance argument is entirely about memory: a system that updates positions touches only positions, in order, with perfect prefetching. This is **data-oriented design** — designing around the data's shape and access pattern rather than around conceptual object models.

Two common storage strategies:

**Archetype/chunk-based** (Unity DOTS, flecs): entities with identical component sets live together in contiguous chunks. Iteration is maximally fast; adding/removing a component moves the entity between archetypes, which costs a copy.

**Sparse set** (EnTT): each component type has a packed dense array plus a sparse index. Adding/removing components is O(1) and cheap; iteration over multi-component queries is slightly less optimal.

Pick based on whether your entities change shape often. For a roguelike with status effects and dynamic behaviors, sparse sets are usually the more comfortable fit.

---

## ECS in TypeScript, honestly

The pattern translates, with a caveat you must be clear-eyed about.

```ts
// Components as parallel typed arrays — SoA, not AoS
class TransformStore {
  x = new Float32Array(MAX);
  y = new Float32Array(MAX);
  z = new Float32Array(MAX);
  // NOT: positions: {x,y,z}[]
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

This is genuinely fast in V8: typed arrays are real contiguous memory, the loop is monomorphic, and nothing allocates.

**The caveat:** the win in a JS engine comes overwhelmingly from *avoiding allocation and megamorphism*, not from L1 cache tuning the way it does in C++. JS objects have header overhead, `Map` lookups are not free, and you don't control layout precisely. So take ECS for the **allocation discipline and the linear typed-array iteration**, and be skeptical of framework-heavy ECS libraries whose query machinery costs more than the naive loop it replaced. Measure. In a TS engine, a hand-rolled "arrays of components + explicit system functions" design frequently beats a general ECS library, and choosing that deliberately is good engineering, not laziness.

Also: **ECS is not mandatory.** Plenty of shipped games use a simpler model — a handful of typed arrays and explicit update functions — and are better for it. What's non-negotiable is the *data-oriented instinct*: contiguous storage, linear iteration, no allocation in hot loops.

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

Two architectural rules matter more than the exact list:

**Dependencies point downward.** The renderer must not know about gameplay. Gameplay must not know about WebGPU. When gameplay needs something drawn, it sets data that the renderer reads. This is what lets you swap a backend, write tests, and — critically for a small studio — let two people work in the same codebase without constant conflicts.

**Isolate the API behind an RHI.** A thin Render Hardware Interface layer over WebGPU costs you a day and buys you the ability to add a debug/null backend, to instrument every call in one place, and to survive API changes. Don't over-abstract it — a leaky, thin RHI is right; a fully generic one that also supports WebGL is usually a waste unless you actually need WebGL.

---

## The render graph

As passes multiply (shadow, depth prepass, opaque, raymarch, transparent, bloom chain, tonemap), manually managing transient textures and pass ordering becomes error-prone.

A **render graph** (a.k.a. frame graph, from Frostbite's 2017 talk) makes each pass declare its inputs and outputs. The engine then builds a DAG and automatically:

- Allocates and **aliases** transient resources (two passes that never overlap in time can share memory)
- Orders passes and inserts barriers
- **Culls passes** whose outputs nobody consumes — debug views cost nothing when off
- Produces a visualizable graph, which is excellent for debugging

WebGPU handles barriers for you, which removes the hardest part, but the declarative structure is still worth it once you have more than about eight passes. Before that, explicit code is clearer. **Knowing when to introduce it** — and being willing to say "not yet" — is the judgment being tested.

---

## Jobs and parallelism on the web

Games parallelize heavily. In the browser you have **Web Workers**, and the model is more restrictive than native threads.

**What belongs on a worker in a voxel engine:**
- Terrain generation (noise, structure placement)
- Chunk meshing (the greedy mesher — perfectly parallel per chunk)
- Light propagation flood fills
- Pathfinding
- Asset decoding and decompression
- Save/serialization

**What must stay on the main thread:** all WebGPU calls (the device isn't transferable to workers in general use), DOM/input, and anything that must be frame-synchronous.

**The data transfer problem is the whole design.** Three options:

1. **`postMessage` with structured clone** — copies. Fine for small messages, fatal for a 32 KB chunk per frame at scale.
2. **Transferable `ArrayBuffer`** — zero-copy *move*. The sender loses access. Excellent for "worker produces a mesh buffer, hands it over."
3. **`SharedArrayBuffer`** — true shared memory, with `Atomics` for synchronization. **Requires cross-origin isolation** (`Cross-Origin-Opener-Policy: same-origin` and `Cross-Origin-Embedder-Policy: require-corp`), which constrains what third-party content you can embed. Decide this early; retrofitting it is painful.

A pragmatic architecture that works well: a **fixed worker pool** sized to `navigator.hardwareConcurrency - 1`, a job queue with priorities, chunk voxel data in a `SharedArrayBuffer` region so workers can read neighbours without copying, and finished mesh buffers returned as **transferables**. Cap how many completed jobs you integrate per frame so a burst never blows the budget.

Avoid the two classic traps: spawning workers per task (worker startup is milliseconds), and chatty message protocols (each `postMessage` has real overhead — batch them).

---

## Resource management and hot reload

**Assets need identity, lifetime, and asynchrony.** A handle-based system (an integer/generation handle rather than a direct reference), reference counting or explicit ownership, async loading with a placeholder, and a central registry. Handles rather than object references also make serialization and hot reload dramatically easier.

**Hot reload is a productivity multiplier, not a luxury.** Being able to change a shader, a texture, or a tuning value and see it without restarting turns a 40-second iteration loop into a 1-second one. Over a project, that is worth more than most optimizations. In a web engine you get much of it nearly free: watch files in dev, re-fetch, recreate the pipeline, swap the handle. Build it in the first month.

The Engine JD's *"Improve the Development Experience"* and *"Build tools, workflows, and systems that allow the team to work efficiently"* bullets are pointing directly at this. In a small studio, engine engineers are judged substantially on how fast everyone *else* can work.

---

## Purpose-built vs. copied patterns

The Engine JD is unusually explicit here: *"Bakest and Levers and Chests embrace purpose-built tech to serve unique artistic and technical constraints; simply copying common industry engine patterns won't always work. Wisdom will be needed to know when to stick with what works and when to forge a new path."*

That is a request for a specific kind of judgment, and it's worth having concrete examples ready:

**Patterns worth copying, because the reasoning is universal:** fixed timestep with interpolation; data-oriented storage; bind groups organized by update frequency; sort keys to minimize state changes; handle-based resources; render graphs at scale; two-pass Hi-Z occlusion culling.

**Patterns worth questioning in a voxel game:**
- **General mesh LOD systems** — voxel LOD comes from the data structure's mip levels, not from a mesh simplifier.
- **PBR material graphs** — a stylized voxel game with a fixed palette may need three material parameters, not a node graph.
- **Generic scene graphs with arbitrary parent-child transforms** — most of a voxel world is a static grid; a full transform hierarchy is overhead for the 1% that moves.
- **A general physics engine** — AABB-vs-grid collision is a few hundred lines, is faster, is deterministic, and has no third-party dependency. Integrating Rapier or Havok may cost more than it saves.
- **Shadow map cascades** — as Module 06 argued, marching the grid may simply be better here.
- **Deferred rendering's G-buffer** — if you ray trace primary visibility, you already have surface data in registers.

The pattern behind the pattern: **general engines pay for generality with abstraction. When your content is uniform, that generality is pure cost.** But the discipline is to be able to say *why* in each case, and to recognize the cases where the general solution is right and reinventing it is ego.

---

## Testing an engine

Real, and a JD bullet (*"help keep things organized and improve testing"*). What actually works:

- **Unit tests for pure functions** — math, meshing, packing/unpacking, serialization round-trips, noise determinism. High value, cheap.
- **Golden/snapshot tests for the mesher** — feed a fixed voxel volume, assert the exact vertex buffer bytes. Catches regressions instantly.
- **Determinism tests** — run the simulation N steps from a seed, hash the state, compare. This one test catches an enormous class of bugs.
- **Image-diff tests for rendering** — render a fixed scene headlessly, compare against a reference image with a perceptual threshold. Genuinely valuable, genuinely fiddly (driver differences require tolerance).
- **Performance regression tests** — assert that a benchmark scene stays under a frame time budget in CI. Catches the slow creep that nobody notices day to day.

What generally doesn't pay: heavy mocking of engine subsystems, and unit tests over rendering code whose correctness is visual. Know the difference and say so — "improve testing while improving the pace of development, not slowing it down with unnecessary process" is a direct quote from the JD, and it's a stated preference for pragmatism.

---

## The interview answer

*"How would you architect a voxel engine?"*

> "Layered, with dependencies pointing down: platform, jobs, assets, world, gameplay, renderer, with a thin RHI over WebGPU. The world is chunked voxel storage — palette-compressed, mirrored to a GPU brickmap. Meshing, generation, and lighting run on a fixed worker pool with results returned as transferable buffers, integrated under a per-frame budget so a burst can't blow the frame. Entities use data-oriented storage — typed arrays per component, systems as plain functions — but I'd start with the simplest thing that isn't allocation-heavy rather than adopting a full ECS framework on day one. A render graph once the pass count justifies it, not before. And handle-based assets with hot reload early, because iteration speed compounds."

*"When would you not use an ECS?"*

> "When entity count is low and the query machinery costs more than the loops it replaces — which happens more often in JS than in C++, because the win there is mostly about allocation discipline rather than cache lines. I'd keep the data-oriented storage regardless and skip the framework."

---

## Exercise — Voxelforge, Stage 10

1. Refactor into explicit layers with a dependency lint rule (a simple ESLint `no-restricted-imports` config enforcing that the renderer can't import gameplay).
2. Build a **worker pool** with a priority job queue. Move terrain generation and meshing onto it. Return meshes as transferable `ArrayBuffer`s.
3. Cap integration at 2 ms per frame; verify with your Module 09 HUD that sprinting across the world produces no spikes.
4. Implement handle-based assets (`{index, generation}`) with a registry, plus **shader hot reload**: edit a WGSL file, see it applied within a second, with compile errors surfaced in-game rather than as a black screen.
5. Add data-oriented entity storage — typed arrays per component, systems as functions — and a player entity with transform and velocity.
6. Write the three highest-value tests: a mesher golden test, a determinism hash test, and a frame-budget performance test. Wire them into CI.

---

## Go deeper

- **Jason Gregory, *Game Engine Architecture* (3rd ed.)** — the field's standard reference. Read Chapters 1–6, 14–15.
- **Robert Nystrom, *Game Programming Patterns*** — free online. Component, Service Locator, Dirty Flag, Object Pool, Spatial Partition, Data Locality.
- **Mike Acton, "Data-Oriented Design and C++" (CppCon 2014)** — abrasive, correct, and the clearest statement of the philosophy.
- **Richard Fabian, *Data-Oriented Design*** — free at dataorienteddesign.com/dodbook.
- **Sander Mertens's ECS FAQ and flecs articles** — the best practical writing on ECS storage strategies and their tradeoffs.
- **Yuriy O'Donnell, "FrameGraph: Extensible Rendering Architecture in Frostbite" (GDC 2017)** — the render graph talk everyone cites.
- **Christian Gyrling, "Parallelizing the Naughty Dog Engine Using Fibers" (GDC 2015)** — job systems done properly.

---

**Next:** [Module 11 — Asset Pipelines and Working with Artists](./11-asset-pipelines-and-artists.md)
