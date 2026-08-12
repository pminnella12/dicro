# Module 13 — TypeScript and V8 Performance Realities

### The module where your existing expertise becomes an advantage — if you learn what the JIT is actually doing underneath it

*~28 min read · Part IV: Engine Breadth · Prerequisites: Modules 01, 03, 10*

---

## Read this first

The Engine JD says it plainly:

> *"Bakest is written entirely in TypeScript and a deep understanding of JS language semantics, TS type systems, and V8 performance realities is a huge bonus."*

You already have the first two. This module is the third — **and it is the one place where a senior TypeScript engineer can walk in with an edge over a career C++ graphics programmer**, because most of them have never had to reason about hidden classes or GC pressure, and their instincts about what's fast are formed on a language where you control layout.

**Lean into this module.** In a room where you're the one with less graphics experience, this is the topic where the asymmetry runs the other way.

> **The central fact: TypeScript's types vanish at runtime.** V8 has no idea your variable is a `number`. It infers everything dynamically, speculates, optimizes on those speculations, and deoptimizes when they break. Writing fast engine code in TS means writing code whose *runtime shape* is predictable enough that V8's speculation always wins.

---

## How V8 actually runs your code

Four tiers, escalating as a function gets hot:

| Tier | What it is | When |
|---|---|---|
| **Ignition** | A bytecode interpreter | Everything starts here |
| **Sparkplug** | A fast non-optimizing baseline compiler | After a few executions |
| **Maglev** | Mid-tier optimizing compiler | Balances compile time vs. quality |
| **TurboFan** | The heavy optimizing compiler | For genuinely hot code |

**TurboFan makes speculative assumptions** based on the types it has observed: *"this argument has always been a small integer," "this object has always had this shape,"* — and generates machine code that's fast *given those assumptions*, protected by cheap guard checks.

When a guard fails, V8 **deoptimizes**: it throws away the optimized code, falls back to the interpreter, and re-warms from scratch. A function that deoptimizes repeatedly can end up permanently slow.

**And critically: it will look fine in a microbenchmark and be slow in your engine**, because the microbenchmark only ever passed it one shape. This is the single most common way JS performance intuition goes wrong, and it's why the measurement section below matters as much as the technique sections.

---

## Hidden classes (shapes) and inline caches

### Shapes

V8 gives every object a **hidden class** (internally called a "Map," commonly called a *shape*) describing its layout: which properties exist, in what order, at what byte offsets.

Objects created identically **share** a shape. Property access on a known shape is a **single offset load** — `[object + 24]` — instead of a hash lookup. That's roughly the difference between a struct field access in C and a `HashMap::get`.

**Shape transitions happen when you add properties**, and the *order* is part of the identity:

```ts
// ✅ Same shape every time — fast
function makeParticle(x: number, y: number, z: number) {
  return { x, y, z, vx: 0, vy: 0, vz: 0, life: 1 };
}

// ❌ Two DIFFERENT shapes holding the same data
const a = { x: 1, y: 2 };  a.z = 3;   // {} → {x} → {x,y} → {x,y,z}
const b = { x: 1, z: 3 };  b.y = 2;   // {} → {x} → {x,z} → {x,z,y}
// To V8, a and b are as different as a Date and a RegExp.
```

**Rules that follow:**

- **Initialize every property in the constructor**, in a consistent order. Even ones you'll set later — assign `null` or `0`.
- **Never `delete` a property.** It can force the object into slow **dictionary mode** (a real hash map per object), which is dramatically slower and often permanent for that object. **Set to `null` instead.**
- **Property order matters**, not just the set of properties.
- **Class syntax helps** because it naturally enforces both — the constructor runs the same assignments in the same order every time.

### Inline caches

An **inline cache (IC)** memoizes property lookups **at each call site**. The site remembers "last time, the object had shape S, and the property was at offset 24."

| Shapes seen at a site | Name | Speed |
|---|---|---|
| 1 | **Monomorphic** | Fastest — one guard, one offset load |
| 2–4 | **Polymorphic** | Slower — a small linear check |
| 5+ | **Megamorphic** | V8 gives up; generic hash lookup. **~10× slower** |

This is the mechanism behind a rule that feels *wrong* to an OO developer:

```ts
// ❌ Interface polymorphism: this ONE call site sees N shapes → megamorphic
interface Renderable { draw(): void; }
for (const r of renderables) r.draw();

// ✅ Separate arrays per type → each call site is monomorphic
for (const chunk of chunks)   drawChunk(chunk);
for (const sprite of sprites) drawSprite(sprite);
```

> **Polymorphism is fine at architectural boundaries and expensive in inner loops.**

That single sentence reconciles most of what you know about clean code with what an engine needs. **Keep your interfaces; just don't dispatch through them ten thousand times per frame.** A factory that returns one of five implementations, called once at startup, costs nothing. The same dispatch in the update loop costs you milliseconds.

Note also that this is the *same* insight as Module 10's ECS argument, arriving from a different direction — data-oriented storage is monomorphic storage.

---

## Numbers: Smis, doubles, and boxing

V8 represents numbers two ways:

- **Smi** ("SMall Integer") — a 31-bit signed integer stored **directly in the pointer slot**. No allocation, no dereference, extremely fast.
- **HeapNumber** — a boxed 64-bit double: an allocation plus a pointer dereference to read it.

Consequences:

- Integers within roughly **±2³⁰** stay as Smis. Exceeding that, or introducing a fractional value, transitions to doubles.
- **A number field that receives both Smis and doubles causes a shape transition** and can pull in boxing. A `health` field that's `100` for most of the game and becomes `99.5` once has changed representation for every object of that shape.
- `Math.floor(x)` on a double still produces a double in an unboxed-double field context. Use `| 0`, `>>> 0`, or `Math.trunc` **deliberately** when you want integer semantics — but be aware **`| 0` is a 32-bit signed operation and silently wraps above 2³¹**, and that `| 0` truncates toward zero while `Math.floor` rounds down (Module 02's voxel-coordinate bug).
- **`NaN`, `Infinity`, and `-0` propagate silently** through math and can poison downstream results. Assert against them in debug builds — `if (!Number.isFinite(x)) debugger;` at a few key points will save you hours.

### Typed arrays sidestep all of this

`Float32Array`, `Int32Array`, `Uint8Array` are **contiguous, unboxed, and never allocate per element.** They are a raw block of memory with a typed view over it.

**This is why the ECS pattern in Module 10 uses them**: it isn't just cache friendliness, it's the elimination of an entire class of representation issues. There's no shape, no boxing, no Smi/double transition — just bytes.

### Element kinds matter for regular arrays too

V8 tracks what a plain `Array` holds:

```
PACKED_SMI_ELEMENTS      ← fastest
PACKED_DOUBLE_ELEMENTS
PACKED_ELEMENTS
HOLEY_SMI_ELEMENTS       ← "holey" = has gaps
HOLEY_DOUBLE_ELEMENTS
HOLEY_ELEMENTS           ← slowest
```

**Transitions go one way.** Once an array is holey or generic, it never goes back, for the lifetime of that array.

- **Never create holes:** no `new Array(n)` without filling it, no assigning past the end (`arr[arr.length + 5] = x`), no `delete arr[i]`.
- **Don't mix types** in an array you care about.
- Prefer `new Array(n).fill(0)` or — better — a typed array.

A "hole" is genuinely different from `undefined`: it means the index has no property at all, so every read has to walk the prototype chain to confirm nothing's there. That's why it's so much slower.

---

## Allocation and GC: the real enemy

V8's garbage collector is **generational**:
- A small **young generation** (the "nursery"), collected frequently with a fast **scavenger**.
- An **old generation**, collected with a mostly-concurrent **mark-compact**.

Young-generation scavenges are fast — often under a millisecond. **But "under a millisecond" out of 16.67 is still 6% of your budget**, and a major GC can be 10+ ms. Worse, **you cannot schedule it.** It happens when V8 decides, which will be during a boss fight.

> **The rule: allocate nothing in the frame loop.**

This is the biggest habit change from web development, and it's **mechanical rather than clever**.

### Things that allocate, many of which don't look like it

```ts
const v = { x, y, z };                     // object literal
const arr = [a, b, c];                     // array literal
const s = `chunk ${x},${z}`;               // template string
const sub = arr.slice(0, 10);              // array method returning a new array
arr.map(f); arr.filter(f); arr.concat(b);  // all allocate a new array
const f = () => x + 1;                     // closure capturing a variable
for (const x of arr) {}                    // iterator object (usually escape-analyzed — verify)
[...args]  /  {...obj}                     // spread
JSON.parse / JSON.stringify                // heavily
new Error()                                // captures a stack trace — VERY expensive
```

That last one bites people: throwing an exception in a hot path — even one you catch — costs far more than the branch you avoided.

### The countermeasures

**Destination-first APIs.** Instead of `add(a, b): Vec3`, write `add(out, a, b): void`. Ugly, universal in engine math libraries, and **the reason gl-matrix looks the way it does.** (You already did this in Module 02.)

**Object pools** for entities, particles, and events. Acquire from a free list, release when done, never `new` in the loop.

```ts
class ParticlePool {
  private free: number[] = [];
  private data = { x: new Float32Array(MAX), /* ... */ };
  acquire(): number { return this.free.pop() ?? this.count++; }
  release(i: number): void { this.free.push(i); }
}
```

**Scratch buffers** — module-level pre-allocated temporaries reused every frame:

```ts
const _tmpVec = new Float32Array(3);   // reused. NEVER hold a reference across frames.
```

**Document that they're transient**, because aliasing bugs here are nasty — two functions both using `_tmpVec`, one calling the other, is a genuinely hard bug to find.

**Typed arrays with manual indexing** for anything bulk.

**Prefer indexed `for` loops in the hottest paths** — not because `for...of` is inherently slow (V8 usually escape-analyzes the iterator away), but because it's one less thing to verify when you're chasing an allocation.

### Verify, don't assume

Take a heap allocation profile in DevTools, watch the memory graph during gameplay, and look for the **sawtooth** (memory climbing then dropping = allocation then collection). **A flat line during steady-state play is the goal**, and it's achievable.

---

## Workers, SharedArrayBuffer, and the boundary tax

Covered architecturally in Module 10; here's the performance shape.

| Mechanism | Per-message cost |
|---|---|
| **`postMessage` + structured clone** | A full serialize + copy + deserialize, on both threads |
| **Transferable `ArrayBuffer`** | ~Zero — ownership moves |
| **`SharedArrayBuffer` + `Atomics`** | Zero — genuinely shared memory |

Concretely: **a 32 KB chunk copied per message, at 100 chunks/second, is 3.2 MB/s of pure copying plus GC pressure on both sides.** That's not fatal, but multiply it by voxel data *and* mesh data *and* light data and you've built your bottleneck out of transport.

**`SharedArrayBuffer` requires cross-origin isolation** (Module 01). Once you have it, workers can read voxel data directly with **no messaging at all**, which is the difference between an architecture that scales to eight workers and one that chokes on message overhead.

**Message overhead is per-message**, so **batch**. One message with 20 results beats 20 messages, by a lot — the fixed cost per `postMessage` (event loop scheduling, serialization setup) dominates for small payloads.

---

## WebAssembly: when and whether

The honest assessment, because you may well be asked.

**WASM wins** for tight numeric inner loops with manual memory management — mesh generation, noise, compression, physics solvers. It has:
- Predictable performance with no deopt cliffs
- Real 64-bit integers (relevant for binary greedy meshing! Module 08)
- SIMD
- No GC

**WASM loses to the JS/WASM boundary.** Each crossing has overhead, and passing anything but numbers requires copying through linear memory. **A function called ten thousand times per frame from JS will lose everything it gained** on the boundary alone.

**Well-written TypeScript on typed arrays is often within 1.5–2× of WASM**, and sometimes matches it. The gap has narrowed a lot.

Given that, the pragmatic position for a TS-first engine:

> **Stay in TypeScript by default; reach for WASM only for a measured, self-contained hot spot with a coarse-grained interface** — e.g. *"mesh this entire chunk,"* called once per chunk, not once per voxel.

And note **the maintenance cost of a second language and toolchain in a small studio** — a real factor, not a footnote. Every build step, every debugging session, every new hire has to deal with it.

**Saying exactly that in an interview — with the boundary cost as the reason, and a measurement-first stance — is a much stronger answer than either "rewrite it in Rust" or "JS is fast enough."** Both of those extremes signal you haven't measured.

---

## Measuring properly

**Microbenchmarks lie, constantly.** The specific ways:

| Trap | What happens |
|---|---|
| **Dead code elimination** | V8 removes work whose result you don't use. Your loop measured nothing. |
| **Monomorphic warm-up** | A benchmark that passes one shape doesn't reflect a call site that sees five |
| **Tier-up timing** | The first thousand iterations run in the interpreter. Measure only after warm-up. |
| **GC timing** | A collection landing inside your window skews everything. Report the **median** and distribution, not the mean. |

### Better practice

- **Measure in the engine, in a real scene**, with your Module 09 HUD. This is the single most important line in the section.
- Use `performance.now()` around **named phases** and keep a rolling histogram (you built one in Module 01).
- Use DevTools' **flame chart** and the **allocation timeline**.
- For real depth, run Node with `--trace-deopt`, `--trace-opt`, and `--print-opt-code`, or use `--prof` and process the log with `--prof-process`.
- **`%GetOptimizationStatus()`** via `node --allow-natives-syntax` tells you **definitively** whether a function is optimized or has been deoptimized:

```bash
node --allow-natives-syntax --trace-deopt bench.js
```
```js
meshChunk(testChunk);              // warm it up
for (let i = 0; i < 1000; i++) meshChunk(testChunk);
console.log(%GetOptimizationStatus(meshChunk));   // bitfield; check the "optimized" bit
```

**Knowing these flags exist and having used them once is a genuine credibility marker** — it's specific, verifiable, and most candidates for a graphics role have never touched them. This is your differentiator, so spend the hour.

---

## TypeScript-specific notes

**The type system is a design tool with zero runtime cost.** Use it aggressively for correctness, and don't confuse it with performance.

### Where types genuinely help an engine

**Branded primitives** for handles and IDs:

```ts
type EntityId = number & { readonly __brand: 'EntityId' };
type ChunkId  = number & { readonly __brand: 'ChunkId' };

function getEntity(id: EntityId) { /* ... */ }
getEntity(chunkId);   // ❌ compile error, and it would have been a silent bug
```

**Prevents an entire class of "passed the wrong integer" bugs at zero runtime cost** — and in an engine full of integer handles (Module 10), that's a lot of bugs. This is one of the highest-value five-minute changes you can make to an engine codebase.

**Discriminated unions** for messages, events, and asset variants, with exhaustiveness checking:

```ts
type WorkerMessage =
  | { kind: 'mesh';     chunk: ChunkId; data: ArrayBuffer }
  | { kind: 'generate'; chunk: ChunkId; seed: number };

function handle(m: WorkerMessage) {
  switch (m.kind) {
    case 'mesh': return doMesh(m);
    case 'generate': return doGen(m);
    default: { const _exhaustive: never = m; return _exhaustive; }
    //        ↑ compile error the moment someone adds a message kind
  }
}
```

**`const enum`** compiles to inline literals with no runtime object. *(Note: incompatible with `isolatedModules`, which most modern bundlers require — so check your build setup. A plain `const` object with `as const` is the safer modern idiom.)*

**`readonly`** to document and enforce immutability at boundaries — free at runtime, and it stops the "someone mutated my scratch buffer" class of bug.

**Strict mode everywhere.** `strictNullChecks` in particular catches the null-dereference class that would otherwise be a crash in a shipped game.

### Where TypeScript can mislead

**A typed field is not a guaranteed runtime representation.** `x: number` may be a Smi, a double, or a HeapNumber, and TS won't tell you which. The type system describes your *intent*, not V8's *encoding*.

**Enums, decorators, and some downlevel transforms emit runtime code.** Check your compiler output when it matters — a `enum` is an object at runtime; a `const enum` isn't.

**`any` at a boundary silently poisons everything downstream**, including your ability to reason about shapes. In an engine, `any` in a hot path is a performance bug as well as a type bug.

---

## Common confusions

**"TypeScript makes my code faster."** It makes your code *correct*. V8 discards the types entirely. Everything in this module is about the JavaScript underneath.

**"I benchmarked it and the optimized version was slower."** Check: did you consume the result? Did you warm up? Did GC land in the window? Did the benchmark's monomorphic call site hide a megamorphic reality? Four questions, and one of them is usually the answer.

**"Object pools are premature optimization."** In a frame loop they're the baseline, not an optimization. GC pauses aren't a throughput problem you can amortize; they're a latency spike the player feels (Module 01).

**"`for...of` is slow."** Usually it isn't — V8 escape-analyzes the iterator away. But it *can* fail to, and in the hottest 1% of your code an indexed loop is one less thing to verify. Don't rewrite your whole codebase.

**"I should rewrite the mesher in WASM."** Measure first. The boundary cost may eat the gain, and you'd be adding a toolchain to a small team. Reach for it with a coarse interface and a number in hand.

**"Deopt happened once, so the function is permanently slow."** V8 will re-optimize. The problem is *repeated* deopt (a deopt loop), which is what `--trace-deopt` shows you and what you actually need to fix.

---

## The interview answer

***"What do you watch for writing performance-sensitive TypeScript?"***

> "Two things dominate: **allocation and shape stability.**
>
> No allocation in the frame loop — destination-first math APIs, object pools, scratch buffers, typed arrays — because GC pauses aren't schedulable and a 10 ms major GC is most of a frame. That's a latency problem, not a throughput problem, so you can't amortize it away.
>
> And keeping call sites monomorphic: initialize all properties in the constructor in a fixed order, never `delete`, don't mix Smis and doubles in the same field, and don't dispatch through an interface with five implementations in an inner loop. Polymorphism is fine at architectural boundaries and expensive at 10,000 calls a frame.
>
> Beyond that: typed arrays and structure-of-arrays for bulk data, heavy work on workers with `SharedArrayBuffer` or transferables rather than structured clone, and batched messages because the overhead is per-message.
>
> And I'd verify with allocation profiles and `--trace-deopt` rather than guessing — microbenchmarks lie, because they're always monomorphic and always warm."

***"Would you use WebAssembly?"***

> "Only where I've measured a self-contained hot spot with a coarse interface — chunk meshing or compression, called once per chunk rather than once per voxel. The boundary cost kills fine-grained use, well-written TS on typed arrays is often within 2×, and a second toolchain is real maintenance cost for a small team. I'd want the measurement before the rewrite, not after."

***"How would you find out if a function is being optimized?"***

> "`node --allow-natives-syntax` with `%GetOptimizationStatus()`, and `--trace-deopt` to see why it bailed. In the browser, the DevTools performance panel plus an allocation timeline gets you most of the way, but the Node flags are how you get a definitive answer."

---

## Exercise — Voxelforge, Stage 13

**⭐ 1. Profile allocations in your current build.** Run gameplay for 60 seconds with the DevTools memory timeline recording. Find every sawtooth source and eliminate it. **Target: a flat allocation line during steady-state play.**

**2. Convert your math library to destination-first, allocation-free APIs.** Re-measure.

**⭐ 3. Build a particle system two ways** — array of objects vs. structure-of-arrays typed buffers — with 100,000 particles. **Measure update time and allocation for both. Write down the ratio; it will be the number you quote in interviews.**

**4. Write a function that receives 5 different object shapes** and time it against the same function receiving one. **That's your megamorphic penalty, measured on your own hardware**, and it's a much better thing to cite than a number from a blog post.

**5. Run your mesher under `node --allow-natives-syntax`** with `%GetOptimizationStatus()` and confirm the hot function is optimized **and stays optimized**. If it deopts, use `--trace-deopt` to find out why and fix it.

**⭐ 6. Benchmark your binary greedy mesher (Module 08) three ways:** `BigUint64Array`, two `Uint32Array` halves, and (optionally) a WASM implementation.

**BigInt operations allocate**, so expect the naive `BigUint64Array` version to lose badly. **This is one of the most instructive experiments in the whole course** — it connects Module 08's algorithm to Module 13's runtime realities and produces a number that's genuinely yours.

**7. Add branded types** for `EntityId`, `ChunkId`, and `AssetHandle`, and see how many latent bugs the compiler finds. (Bet on "more than zero.")

---

## Go deeper

- **v8.dev/blog** — especially **"Fast properties in V8"**, **"Elements kinds in V8"**, and the TurboFan posts. Written by the engineers who built it.
- **Mathias Bynens & Benedikt Meurer, "JavaScript engine fundamentals: Shapes and Inline Caches"** (mathiasbynens.be) — **the clearest explanation of shapes and ICs in existence.** Read this one first.
- **Vyacheslav Egorov's blog (mrale.ph)** — deep V8 internals from a former V8 engineer. **"What's up with monomorphism?"** is required reading for this module.
- **gl-matrix source** — a working example of allocation-free numeric JS. You'll recognize every technique in it now.
- **`--trace-deopt`, `--trace-opt`, `--allow-natives-syntax`, `--prof`** — spend an hour with these on a toy program *before* you need them.
- **Chrome DevTools Performance and Memory panel docs** — the allocation timeline specifically.
- **"WebAssembly vs JavaScript performance" benchmarks** — read several critically; most measure the wrong thing (usually the boundary, or a monomorphic microbenchmark). **Trust your own measurement of your own workload.**

---

**Next:** [Module 14 — Tech Art, VFX, and Art Direction](./14-tech-art-and-vfx.md)
