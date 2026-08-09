# Module 13 — TypeScript and V8 Performance Realities

### The module where your existing expertise becomes an advantage — if you learn what the JIT is actually doing underneath it

*~13 min read · Part IV: Engine Breadth · Prerequisites: Modules 01, 03, 10*

---

The Engine JD says it plainly: *"Bakest is written entirely in TypeScript and a deep understanding of JS language semantics, TS type systems, and V8 performance realities is a huge bonus."*

You already have the first two. This module is the third — and it is the one place where a senior TypeScript engineer can walk in with an edge over a career C++ graphics programmer, because most of them have never had to reason about hidden classes or GC pressure.

> The central fact: **TypeScript's types vanish at runtime.** V8 has no idea your variable is a `number`. It infers everything dynamically, speculates, optimizes on those speculations, and deoptimizes when they break. Writing fast engine code in TS means writing code whose *runtime shape* is predictable enough that V8's speculation always wins.

---

## How V8 actually runs your code

Four tiers, escalating:

1. **Ignition** — a bytecode interpreter. Everything starts here.
2. **Sparkplug** — a fast non-optimizing baseline compiler.
3. **Maglev** — a mid-tier optimizing compiler (newer; balances compile time against quality).
4. **TurboFan** — the heavy optimizing compiler.

Functions get promoted as they run hot. TurboFan makes **speculative assumptions** based on the types it has observed — "this argument has always been a small integer," "this object has always had this shape" — and generates code that's fast *given those assumptions*, guarded by cheap checks.

When a guard fails, V8 **deoptimizes**: it throws away the optimized code and falls back to the interpreter, re-warming from scratch. A function that deoptimizes repeatedly can end up permanently slow, and — critically — **it will look fine in a microbenchmark and be slow in your engine**, because the microbenchmark only ever passed it one shape.

---

## Hidden classes (shapes) and inline caches

V8 gives every object a **hidden class** (internally a "Map," commonly called a *shape*) describing its layout: which properties exist, in what order, at what offsets. Objects created identically share a shape, and property access on a known shape is a single offset load instead of a hash lookup.

**Shape transitions happen when you add properties.** So:

```ts
// Same shape — fast
function makeParticle(x: number, y: number, z: number) {
  return { x, y, z, vx: 0, vy: 0, vz: 0, life: 1 };
}

// Different shapes — slow, and worse, two DIFFERENT shapes
const a = { x: 1, y: 2 };  a.z = 3;   // shape: {} → {x} → {x,y} → {x,y,z}
const b = { x: 1, z: 3 };  b.y = 2;   // shape: {} → {x} → {x,z} → {x,z,y}
// a and b hold the same data and are NOT interchangeable to V8
```

Rules that follow:

- **Initialize every property in the constructor**, in a consistent order. Even ones you'll set later — assign `null` or `0`.
- **Never `delete` a property.** It can force the object into slow dictionary mode, which is dramatically slower and often permanent for that object. Set to `null` instead.
- **Property order matters**, not just the set of properties.
- **Class syntax helps** because it naturally enforces both.

**Inline caches (ICs)** memoize property lookups at each call site. A site that only ever sees one shape is **monomorphic** — fastest. Two to four shapes: **polymorphic** — slower. More than four: **megamorphic** — V8 gives up and does a generic hash lookup, and the cost is roughly an order of magnitude.

This is the mechanism behind a rule that feels wrong to an OO developer:

```ts
// Interface polymorphism: every call site sees N shapes → megamorphic
interface Renderable { draw(): void; }
for (const r of renderables) r.draw();

// Sorted by concrete type, or better, separate arrays per type → monomorphic
for (const chunk of chunks) drawChunk(chunk);
for (const sprite of sprites) drawSprite(sprite);
```

**Polymorphism is fine at architectural boundaries and expensive in inner loops.** That single sentence reconciles most of what you know about clean code with what an engine needs. Keep your interfaces; just don't dispatch through them ten thousand times per frame.

---

## Numbers: Smis, doubles, and boxing

V8 represents numbers two ways:

- **Smi** ("small integer") — a 31-bit signed integer stored directly in the pointer slot. No allocation, extremely fast.
- **HeapNumber** — a boxed 64-bit double, requiring an allocation and a dereference.

Consequences:

- Integers within roughly ±2³⁰ stay as Smis. Exceeding that, or introducing a fractional value, transitions to doubles.
- **A number field that receives both Smis and doubles causes a shape transition** and can pull in boxing.
- `Math.floor(x)` on a double still produces a double in an unboxed-double field context; use `| 0` or `>>> 0` or `Math.trunc` deliberately when you want integer semantics — but be aware `| 0` is a 32-bit signed operation and silently wraps above 2³¹.
- **`NaN`, `Infinity`, and `-0`** propagate silently through math and can poison downstream results. Assert against them in debug builds.

**Typed arrays sidestep all of this.** `Float32Array`, `Int32Array`, and `Uint8Array` are contiguous, unboxed, and never allocate per element. This is why the ECS pattern in Module 10 uses them: it isn't just cache friendliness, it's the elimination of an entire class of representation issues.

**Element kinds matter for regular arrays too.** V8 tracks whether an array holds only Smis (`PACKED_SMI_ELEMENTS`), only doubles (`PACKED_DOUBLE_ELEMENTS`), or arbitrary values (`PACKED_ELEMENTS`), plus `HOLEY_*` variants. Transitions go one way — once an array is holey or generic, it never goes back. So:

- **Never create holes**: no `new Array(n)` without filling, no assigning past the end, no `delete arr[i]`.
- **Don't mix types** in an array you care about.
- Prefer `new Array(n).fill(0)` or, better, a typed array.

---

## Allocation and GC: the real enemy

V8's garbage collector is generational: a small **young generation** collected frequently with a fast scavenger, and an **old generation** collected with a mostly-concurrent mark-compact.

Young-generation scavenges are fast — often under a millisecond. But "under a millisecond" out of 16.67 is still 6% of your budget, and a major GC can be 10+ ms. Worse, **you cannot schedule it.** It happens when V8 decides, which will be during a boss fight.

**The rule: allocate nothing in the frame loop.**

This is the biggest habit change from web development, and it's mechanical rather than clever. Things that allocate, many of which don't look like it:

```ts
const v = { x, y, z };                     // object literal
const arr = [a, b, c];                     // array literal
const s = `chunk ${x},${z}`;               // template string
const sub = arr.slice(0, 10);              // array method returning new array
arr.map(f); arr.filter(f); arr.concat(b);  // all allocate
const f = () => x + 1;                     // closure capturing a variable
for (const x of arr) {}                    // iterator object (usually escape-analyzed away — verify)
[...args]  /  {...obj}                     // spread
JSON.parse / JSON.stringify                // heavily
new Error()                                // captures a stack trace — very expensive
```

The countermeasures:

**Destination-first APIs.** Instead of `add(a, b): Vec3`, write `add(out, a, b): void`. Ugly, universal in engine math libraries, and the reason gl-matrix looks the way it does.

**Object pools** for entities, particles, and events. Acquire from a free list, release when done, never `new` in the loop.

**Scratch buffers** — module-level pre-allocated temporaries reused every frame. Document that they're transient, because aliasing bugs here are nasty.

**Typed arrays with manual indexing** for anything bulk.

**Reuse iteration variables**, and prefer indexed `for` loops in the hottest paths — not because `for...of` is inherently slow (V8 usually escapes the iterator), but because it's one less thing to verify.

**Verify, don't assume.** Take a heap allocation profile in DevTools, watch the memory graph during gameplay, and look for the sawtooth. A flat line in the frame loop is the goal.

---

## Workers, SharedArrayBuffer, and the boundary tax

Covered architecturally in Module 10; here's the performance shape:

- **`postMessage` with structured clone copies.** A 32 KB chunk copied per message, at 100 chunks/second, is 3.2 MB/s of pure copying plus GC pressure on both sides.
- **Transferables move an `ArrayBuffer`** with no copy — the sender loses it. Ideal for handing a finished mesh back to the main thread.
- **`SharedArrayBuffer` + `Atomics`** gives real shared memory. It requires cross-origin isolation headers. Once you have it, workers can read voxel data directly with no messaging at all, which is the difference between an architecture that scales to eight workers and one that chokes on message overhead.
- **Message overhead is per-message**, so batch. One message with 20 results beats 20 messages.

---

## WebAssembly: when and whether

The honest assessment, because you may be asked:

**WASM wins** for tight numeric inner loops with manual memory management — mesh generation, noise, compression, physics solvers. It has predictable performance (no deopt cliffs), real 64-bit integers, SIMD, and no GC.

**WASM loses** to the JS/WASM boundary. Each crossing has overhead, and passing anything but numbers requires copying through linear memory. A function called ten thousand times per frame from JS will lose everything it gained.

**Well-written TypeScript on typed arrays is often within 1.5–2× of WASM**, and sometimes matches it. Given that, the pragmatic position for a TS-first engine is: **stay in TypeScript by default; reach for WASM only for a measured, self-contained hot spot with a coarse-grained interface** (e.g., "mesh this entire chunk," called once per chunk, not per voxel). And note the maintenance cost of a second language and toolchain in a small studio — a real factor, not a footnote.

Saying exactly that in an interview — with the boundary cost as the reason, and a measurement-first stance — is a much stronger answer than either "rewrite it in Rust" or "JS is fast enough."

---

## Measuring properly

Microbenchmarks lie, constantly. The specific ways:

- **Dead code elimination** removes work whose result you don't use. Always consume the result.
- **Monomorphic warm-up** — a benchmark that passes one shape doesn't reflect a call site that sees five.
- **Tier-up timing** — the first thousand iterations run in the interpreter. Warm up before measuring.
- **GC timing** — a collection landing inside your measurement window skews everything. Run many trials and report the *median* and the distribution, not the mean.

Better practice:

- Measure **in the engine, in a real scene**, with your Module 09 HUD.
- Use `performance.now()` around named phases and keep a rolling histogram.
- Use DevTools' **flame chart** and the **allocation timeline**.
- For real depth, run Node with `--trace-deopt`, `--trace-opt`, and `--print-opt-code`, or use `--prof` and process the log. Knowing these flags exist and having used them once is a genuine credibility marker.
- **`%GetOptimizationStatus()`** and friends via `node --allow-natives-syntax` will tell you definitively whether a function is optimized or has been deoptimized. This is the direct answer to "is V8 doing what I think?"

---

## TypeScript-specific notes

The type system is a design tool with zero runtime cost — use it aggressively for correctness, and don't confuse it with performance.

Where types genuinely help an engine:

- **Branded primitives** for handles and IDs: `type EntityId = number & { __brand: 'EntityId' }`. Prevents an entire class of "passed the wrong integer" bugs at zero runtime cost — and in an engine full of integer handles, that's a lot of bugs.
- **Discriminated unions** for messages, events, and asset variants, with exhaustiveness checking via `never`.
- **`const enum`** compiles to inline literals with no runtime object (note: incompatible with `isolatedModules`, so check your build setup; a plain `const` object with `as const` is the safer modern idiom).
- **`readonly`** to document and enforce immutability at boundaries — free at runtime.
- **Strict mode everywhere.** `strictNullChecks` in particular catches the null-dereference class that would otherwise be a crash in a shipped game.

Where TypeScript can mislead: a typed field is *not* a guaranteed runtime representation. `x: number` may be a Smi, a double, or a HeapNumber, and TS won't tell you which. Enums, decorators, and some downlevel transforms emit runtime code — check your compiler output when it matters. And `any` at a boundary silently poisons everything downstream, including your ability to reason about shapes.

---

## The interview answer

*"What do you watch for writing performance-sensitive TypeScript?"*

> "Two things dominate: allocation and shape stability. No allocation in the frame loop — destination-first math APIs, object pools, scratch buffers, typed arrays — because GC pauses aren't schedulable and a 10 ms major GC is most of a frame. And keeping call sites monomorphic: initialize all properties in the constructor in a fixed order, never `delete`, don't mix Smis and doubles in the same field, and don't dispatch through an interface with five implementations in an inner loop. Beyond that I'd use typed arrays and structure-of-arrays for bulk data, keep heavy work on workers with `SharedArrayBuffer` or transferables rather than structured clone, and verify with allocation profiles and `--trace-deopt` rather than guessing — microbenchmarks lie because they're always monomorphic and warm."

*"Would you use WebAssembly?"*

> "Only where I've measured a self-contained hot spot with a coarse interface — chunk meshing or compression, called once per chunk. The boundary cost kills fine-grained use, well-written TS on typed arrays is often within 2×, and a second toolchain is real maintenance cost for a small team."

---

## Exercise — Voxelforge, Stage 13

1. **Profile allocations** in your current build. Run gameplay for 60 seconds with the DevTools memory timeline recording. Find every sawtooth source and eliminate it. Target: a flat allocation line during steady-state play.
2. Convert your math library to **destination-first, allocation-free** APIs. Re-measure.
3. Build a **particle system two ways** — array of objects vs. structure-of-arrays typed buffers — with 100,000 particles. Measure update time and allocation for both. Write down the ratio; it will be the number you quote in interviews.
4. Write a function that receives 5 different object shapes and time it against the same function receiving one. That's your megamorphic penalty, measured on your own hardware.
5. Run your mesher under `node --allow-natives-syntax` with `%GetOptimizationStatus()` and confirm the hot function is optimized and stays optimized. If it deopts, use `--trace-deopt` to find out why and fix it.
6. Benchmark your binary greedy mesher (Module 08) three ways: `BigUint64Array`, two `Uint32Array` halves, and (optionally) a WASM implementation. **BigInt operations allocate**, so expect the naive `BigUint64Array` version to lose badly — this is one of the most instructive experiments in the whole course.
7. Add branded types for `EntityId`, `ChunkId`, and `AssetHandle`, and see how many latent bugs the compiler finds.

---

## Go deeper

- **v8.dev/blog** — especially "Fast properties in V8," "Elements kinds in V8," and "Sea of Nodes"/TurboFan posts. Written by the engineers who built it.
- **Mathias Bynens & Benedikt Meurer, "JavaScript engine fundamentals: Shapes and Inline Caches"** (mathiasbynens.be) — the clearest explanation of shapes and ICs in existence.
- **Vyacheslav Egorov's blog (mrale.ph)** — deep V8 internals from a former V8 engineer. "What's up with monomorphism?" is required reading.
- **gl-matrix source** — a working example of allocation-free numeric JS.
- **`--trace-deopt`, `--trace-opt`, `--allow-natives-syntax`, `--prof`** — spend an hour with these on a toy program before you need them.
- **Chrome DevTools Performance and Memory panel docs** — the allocation timeline specifically.
- **"WebAssembly vs JavaScript performance" benchmarks** — read several critically; most measure the wrong thing. Trust your own measurement of your own workload.

---

**Next:** [Module 14 — Tech Art, VFX, and Art Direction](./14-tech-art-and-vfx.md)
