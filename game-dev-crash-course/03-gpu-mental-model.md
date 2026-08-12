# Module 03 — The GPU Mental Model

### What the hardware actually is, why it punishes branches and rewards batches, and the vocabulary that separates graphics programmers from people who have used a graphics API

*~30 min read · Part I: Foundations · Prerequisites: Modules 01–02*

---

## Read this first

Most developers learn a graphics API before they learn the machine underneath it. That is backwards, and it produces engineers who can get a triangle on screen but cannot explain why their shader got 4× slower after adding one `if`.

This module has no API in it. It is about the device.

> A CPU is a small number of very smart cores optimized to make **one** thread finish fast. A GPU is a large number of very simple cores optimized to keep **thousands** of threads in flight so that no one is ever waiting on memory.

Every counterintuitive rule in graphics programming follows from that sentence. Read it twice.

### The size of the difference

| | A modern CPU | A modern GPU |
|---|---|---|
| Cores | 8–16 | 2,000–20,000 "cores" (see the caveat below) |
| Clock speed | 3–5 GHz | 1–2.5 GHz |
| Cache per core | Large (L1 32–64 KB, L2 1 MB+) | Tiny (L1 ~16–128 KB *shared by hundreds of threads*) |
| Branch prediction | Sophisticated | Essentially none |
| Out-of-order execution | Yes, deep | No |
| Memory bandwidth | ~50–100 GB/s | ~200–1000+ GB/s |
| Optimized for | Latency (finish this task now) | Throughput (finish a million tasks per second) |

The "core count" needs a caveat, because marketing numbers mislead. An NVIDIA "CUDA core" is closer to a single **ALU lane** than to a CPU core. The real unit of independent scheduling is a **Streaming Multiprocessor** (SM) on NVIDIA, a **Compute Unit** (CU) on AMD, or an **Execution Unit / core** on Intel and Apple. A high-end GPU has maybe 60–150 of those, each running many groups of threads. So "10,000 cores" really means "~100 independent schedulers, each juggling ~100 lanes."

Two terms you'll see constantly, defined once:

- **ALU** — Arithmetic Logic Unit. The circuit that does actual math (add, multiply, compare). "ALU-bound" means you're limited by math throughput.
- **FLOP** — a floating-point operation. **TFLOPs** = trillions of them per second. It's the standard headline number for GPU compute capability.

---

## SIMT: the thing that explains everything

### The model

GPUs do not schedule threads individually. They execute in **lockstep groups**:

| Vendor | Name for the group | Typical size |
|---|---|---|
| NVIDIA | warp | 32 |
| AMD | wave / wavefront | 32 or 64 |
| Intel | SIMD sub-slice | 8, 16, or 32 |
| Apple | SIMD-group | 32 |
| **WebGPU (portable term)** | **subgroup** | query at runtime |

The vendor-neutral model is **SIMT** — *Single Instruction, Multiple Threads*. Every thread in a subgroup shares **one program counter**. They execute the *same instruction* at the *same time* on *different data*.

If you've used SIMD on a CPU (SSE, AVX, WebAssembly SIMD), this is the same idea with a friendlier programming model: instead of writing explicitly vectorized code, you write scalar-looking code and the hardware runs 32 copies of it in lockstep.

### The brutal consequence: divergence

```wgsl
if (someCondition) {
  expensiveA();   // half the threads want this
} else {
  expensiveB();   // the other half want this
}
```

If `someCondition` differs *within* a subgroup, the hardware **cannot run two paths at once** — there's only one program counter. So it executes **both branches serially**, masking off the threads that shouldn't participate in each:

```
Cycle:  1  2  3  4  5  6  7  8
Path A: ▓▓▓▓▓▓▓▓▓▓▓▓ (threads 0-15 active, 16-31 masked off doing nothing)
Path B:              ▓▓▓▓▓▓▓▓▓▓▓▓ (threads 16-31 active, 0-15 masked off)
```

**Cost = A + B, not max(A, B).** Every thread pays for both branches even though each only needed one. This is **divergence**, and it is the number one shader performance concept.

Nested divergent branches compound: two levels can cost you 4× if the conditions are uncorrelated. A divergent loop is worse still — the whole subgroup runs until the *last* thread finishes, so one thread taking 200 iterations makes all 32 threads take 200 iterations' worth of time.

### The nuance people get wrong

**Branching is not slow. Divergence is slow.**

A branch where *all* threads in a subgroup agree is nearly free — the hardware skips the untaken side entirely, exactly like a CPU. So:

| Branch condition | Cost |
|---|---|
| `if (uniforms.enableFog)` — same value for every thread | ~free |
| `if (instanceIndex == 0)` — same within a draw's subgroups | ~free |
| `if (pixelIsInShadow)` — varies per pixel | expensive **if** shadow edges cut through subgroups |
| `if (rayHitSomething)` after a bounce | very expensive — rays go everywhere |

Note the qualifier on the third row. Per-pixel conditions are *usually* fine, because **neighboring pixels are in the same subgroup and are usually in the same shadow region**. You only pay at the boundary. A shadow edge is a thin line through a mostly-uniform image, so the divergent fraction is small.

### Coherence is a resource

Threads that are adjacent in the dispatch tend to be adjacent in the subgroup. Pixels are assigned to subgroups in small 2D tiles (typically 8×4 or similar), not in scanline order, precisely so that neighbors stay together.

That fact explains a lot:

- **Screen-space effects are fast.** Neighboring pixels take the same branches and sample nearby texels.
- **Incoherent ray tracing is slow.** After one bounce, 32 rays in a subgroup are going 32 different directions, hitting 32 different objects, taking 32 different branches, and reading 32 scattered memory locations. Every mechanism the GPU has for going fast is defeated at once.
- **Voxel *primary* rays are fast.** Rays from the camera through neighboring pixels travel nearly parallel through the grid, stepping through the same chunks in the same order, reading the same cache lines. This is a real architectural advantage of voxels and a big part of why a voxel raytracer is viable in a browser when a general one isn't.
- **Voxel *secondary* rays (shadows, bounces, GI) are much slower**, for the same reason as general ray tracing. Every voxel raytracer's architecture is shaped by this asymmetry — which is why you'll see cheap primary raymarching combined with heavily approximated secondary lighting rather than uniform path tracing.

**This is a genuinely good thing to say in an interview about voxel rendering.** It shows you understand *why* the technique fits the hardware, not just that it exists.

---

## Latency hiding: the actual design goal

### The problem

A memory fetch from VRAM costs **hundreds of cycles**. If a thread has to wait for it, that's hundreds of cycles of an ALU doing nothing.

A CPU fights this with enormous silicon: multi-megabyte caches, hardware prefetchers that guess what you'll want next, out-of-order execution that runs later instructions while an earlier one waits, and branch predictors so it can speculate past unresolved conditions. All of that is spent making *one thread* not stall.

### The GPU's answer

A GPU does none of that. Instead: when a subgroup issues a memory read and stalls, the scheduler **swaps in another resident subgroup** and keeps the ALUs busy. Then another. And another. With enough subgroups resident, the memory latency is completely hidden behind other work, and the ALUs never idle.

The context switch is free because **every resident subgroup's registers stay live simultaneously** — there's no saving and restoring. That's why GPUs have enormous register files (megabytes, versus a CPU's few hundred bytes per core).

### Occupancy

**Occupancy** is the measure of how many subgroups can be resident on an SM/CU at once, usually expressed as a percentage of the hardware maximum.

And here is where the counterintuitive part lives: **occupancy is limited by your shader's resource usage.** Each SM/CU has a fixed budget of registers and shared memory, and it gets divided among resident threads. So:

```
registers available per SM:  65,536
your shader uses:            64 registers per thread
threads you can host:        65,536 / 64 = 1,024 threads = 32 subgroups ✅

registers available per SM:  65,536
your shader uses:            128 registers per thread
threads you can host:        65,536 / 128 = 512 threads = 16 subgroups ⚠️ half the latency hiding
```

A shader that uses lots of registers — many live variables, deep expressions, big unrolled loops, long-lived intermediate values — allows fewer resident subgroups, which means less latency hiding, which means the ALUs idle waiting on memory.

> A shader can get *slower* when you add a "cheap" optimization, because holding one extra value increased register pressure past a threshold and halved occupancy.

This is the mechanism behind advice that otherwise sounds like superstition:

- Long shaders with many temporaries can underperform shorter ones doing *more* math.
- Aggressive loop unrolling sometimes hurts (it creates more live values).
- Large workgroup shared-memory allocations directly trade against occupancy.
- Hoisting a computation out of a loop into a variable — the classic CPU optimization — can be a pessimization on a GPU, because the variable has to live in a register across the whole loop.

**High occupancy is not automatically good, either.** A shader that is pure ALU with almost no memory traffic doesn't need latency hiding at all — there's nothing to hide. Low occupancy with high register use may be the optimal point for that shader.

The right framing: **occupancy is a tool for hiding memory latency, and you want just enough of it.** Measure, don't assume. Vendor tools (Nsight, Radeon GPU Profiler) report occupancy and what's limiting it; in WebGPU today you mostly infer it from timing experiments, which is a real limitation worth knowing about.

---

## The memory hierarchy is the performance story

Rough orders of magnitude on a discrete GPU:

| Level | Latency | Bandwidth | Notes |
|---|---|---|---|
| Registers | ~1 cycle | enormous | Per-thread, scarce, limits occupancy |
| Shared / workgroup memory | ~20–30 cycles | very high | Explicitly managed scratchpad, per workgroup |
| L1 / texture cache | ~30–80 cycles | high | Per SM/CU, small |
| L2 cache | ~200 cycles | moderate | Shared across the whole chip |
| VRAM (device memory) | ~400–800 cycles | 200–1000+ GB/s | The bottleneck for most real workloads |
| Host (CPU) memory over PCIe | *very* slow | ~16–64 GB/s | Avoid touching per frame |

**VRAM** is the GPU's own dedicated memory, physically on the graphics card. **PCIe** is the bus connecting the graphics card to the rest of the computer — a highway that is 10–50× narrower than the GPU's connection to its own memory. That's why uploading a big texture mid-frame causes a hitch, and why readback (Module 01) is so expensive.

(On integrated GPUs — laptops, Apple Silicon, phones — there is no separate VRAM; CPU and GPU share the same physical memory and the same bandwidth. That removes the PCIe transfer cost but means the GPU is competing with the CPU for bandwidth, and total bandwidth is much lower. It is a genuinely different performance shape, and worth testing on.)

Two rules follow from the table.

### Rule 1: Coalesce your accesses

Memory is never fetched one value at a time. It's fetched in **cache lines** — a fixed block, typically 32–128 bytes on a GPU.

If the 32 threads of a subgroup read 32 *consecutive* floats (128 bytes total), that's **one or two memory transactions** and every fetched byte gets used.

If those same 32 threads read 32 floats *scattered* across memory, that's up to **32 separate transactions**, each dragging in 128 bytes to use 4 of them. You've done the same amount of logical work, issued the same instructions, and paid **an order of magnitude more time** — and burned 32× the bandwidth budget.

```
Coalesced:    thread 0→[0] 1→[1] 2→[2] ... 31→[31]   ██ one fetch, 100% used
Strided:      thread 0→[0] 1→[16] 2→[32] ...         ████████ many fetches, 6% used
```

This is the hardware reason behind **structure-of-arrays (SoA)** layouts:

```ts
// Array of Structs (AoS) — natural, and bad for this access pattern
struct Entity { x, y, z, vx, vy, vz, health, flags }   // 32 bytes
entities: Entity[]
// Reading just .x from 32 entities touches 32 × 32 = 1024 bytes to use 128.

// Struct of Arrays (SoA) — awkward, and fast
positionsX: Float32Array
positionsY: Float32Array
positionsZ: Float32Array
// Reading .x from 32 entities touches exactly 128 consecutive bytes.
```

Module 13 revisits this on the CPU side, where the same effect applies for the same reason. It's one of the few optimizations that helps on both processors simultaneously.

### Rule 2: Arithmetic is nearly free; memory is not

Modern GPUs offer on the order of **20–100 FLOPs of compute per byte of bandwidth**. Concretely: a GPU with 30 TFLOPs and 600 GB/s can do 50 floating-point operations in the time it takes to read one byte from VRAM.

The consequences invert your CPU instincts:

- **Recomputing a value is very often cheaper than looking it up.** The classic CPU trick of caching results in a table is frequently a *pessimization* on a GPU.
- **Packing data smaller and unpacking it with ALU work is usually a win.** Storing a normal as two 8-bit numbers and reconstructing the third component with a square root beats storing three floats.
- **Compressed texture formats (BC, ASTC, ETC) win because of bandwidth, not capacity.** They stay compressed in memory and in cache; the texture unit decompresses on read, for free.

### The roofline model

The framework for reasoning about all of this. Plot **arithmetic intensity** (FLOPs performed per byte of memory traffic) on the X axis and achieved performance on the Y axis:

```
performance
    │           ┌──────────── peak ALU (the flat "roof")
    │          ╱
    │        ╱   ← the sloped part is the bandwidth limit
    │      ╱
    │    ╱
    └──────────────────────── arithmetic intensity (FLOP/byte)
         ↑
      the "ridge point"
```

- **Left of the ridge → bandwidth-bound.** You're waiting on memory. Fix: compress data, improve locality, recompute instead of fetching, reduce how much you read.
- **Right of the ridge → compute-bound.** You're waiting on math. Fix: cut operations, use lower precision (f16), move work out of the inner loop, use cheaper approximations.

**The fixes are opposite, so guessing wrong wastes your week.** When the Engine JD says *"ALU vs memory bandwidth tradeoffs"* and *"peak-performance-percentage analysis,"* this is exactly what it means: measure what fraction of theoretical peak ALU or peak bandwidth you're actually achieving, and optimize whichever is saturated.

A rough triage: if you halve the resolution of your textures and the frame time drops meaningfully, you're bandwidth-bound. If you halve the math in your shader and nothing changes, you're not ALU-bound.

---

## Texture units: hardware you don't get on the CPU

Sampling a texture is not just an array read. Dedicated fixed-function hardware does all of this in a single instruction:

- Address computation, plus wrapping/clamping/mirroring at the edges
- **Bilinear filtering** — blend between the four nearest texels, weighted by exact position. Free, in hardware.
- **Trilinear filtering** — blend between two mip levels as well. Also free.
- **Anisotropic filtering** — multiple taps along the projected footprint, for surfaces viewed at a glancing angle
- Format decompression (BC / ASTC / ETC)
- Caching optimized for **2D/3D spatial locality**, not linear locality

That last point matters enormously for voxels, and it deserves unpacking.

### Swizzled layouts

A normal array is laid out linearly: element `(x, y)` lives at `y * width + x`. So `(0,0)` and `(1,0)` are adjacent in memory, but `(0,0)` and `(0,1)` are `width` elements apart. Reading a 2×2 square touches two widely separated regions.

Texture memory isn't laid out that way. GPUs store textures in **swizzled** or **tiled** layouts — commonly a Morton order (also called Z-order) curve, or a vendor-specific tiling — where texels near each other *in 2D or 3D space* are near each other *in memory*:

```
Linear order:              Morton / Z-order:
 0  1  2  3                 0  1  4  5
 4  5  6  7                 2  3  6  7
 8  9 10 11                 8  9 12 13
12 13 14 15                10 11 14 15
```

Now reading the 2×2 block `{0,1,2,3}` is one contiguous fetch. Since bilinear filtering *always* reads a 2×2 block, and neighboring pixels read overlapping blocks, this layout is worth a large multiple in cache efficiency.

For 3D textures the same principle extends to all three axes. **A 3D texture read of a voxel and its neighbors is cache-friendly in all three directions; the same data in a linear storage buffer is only cache-friendly along one axis.**

> **This is why voxel engines store bricks in 3D textures rather than storage buffers**, even when they don't need filtering at all. You are buying the cache layout and the free trilinear interpolation. It's a non-obvious design decision and a great thing to be able to justify.

### Mipmaps

A **mipmap** is a precomputed chain of progressively half-sized versions of a texture: 512², 256², 128², … down to 1×1. The hardware picks the level whose texels are roughly pixel-sized for the current view.

Two problems solved at once:

1. **Aliasing.** Without mips, a minified texture makes each pixel sample one texel out of a region covering many — so tiny camera movements make the sampled texel jump around and the surface shimmers violently. This is the same aliasing you know from signal processing: sampling below the Nyquist rate. Mips are a prefiltered (pre-averaged) version, which is the correct fix.
2. **Cache thrash.** Without mips, neighboring pixels read *distant* texels, so every sample is a cache miss.

Cost: 33% extra memory (½² + ¼² + ⅛² + … converges to ⅓). Always worth it. Not generating mips is one of the most common causes of "why does my game look so noisy and cheap."

---

## Compute shaders and the dispatch model

Compute shaders let you use the GPU as a general parallel processor, outside the rasterization pipeline. No triangles, no pixels — just "run this function N times."

### The model

- You **dispatch** a 3D grid of **workgroups** — e.g. `dispatchWorkgroups(64, 64, 1)`.
- Each **workgroup** has a fixed size in threads, declared in the shader: `@workgroup_size(8, 8, 1)` means 64 threads per workgroup.
- Total threads = workgroups × workgroup size. The example above is 64 × 64 × 64 = 262,144 threads.
- Threads within a workgroup can share **workgroup memory** (a fast explicit scratchpad) and **synchronize** with `workgroupBarrier()`.
- Threads in *different* workgroups **cannot synchronize at all**. There is no global barrier inside a dispatch. If you need one, that's what separate dispatches are for.

That last rule surprises people coming from CPU threading. The reason is scheduling: workgroups may run at completely different times — workgroup 0 might finish before workgroup 5000 starts — so a global barrier could deadlock. Design around it.

### Choosing workgroup size

This is a real decision with real performance consequences:

- **Make it a multiple of the subgroup size.** 64 is a safe default (divisible by both 32 and 64). A workgroup of 40 threads on 32-wide hardware occupies two subgroups and leaves 24 lanes permanently idle — you're throwing away 37% of the hardware before writing a line of logic.
- **Shape it to your access pattern.** `8×8` for 2D image work so each workgroup covers a square tile with good cache behavior. `4×4×4` for 3D voxel work, same reason.
- **Bigger is not better.** Larger workgroups mean coarser scheduling granularity (harder to fill the machine at the end of a dispatch) and more shared memory and registers held per group, which costs occupancy.
- WebGPU guarantees at least 256 threads per workgroup and at least 16 KB of workgroup memory. Don't exceed the guaranteed minimums unless you're checking limits at runtime.

### Workgroup shared memory: the key primitive

The pattern that makes compute shaders worth using:

1. All threads in the workgroup **cooperatively load** a tile of data from VRAM into shared memory — each thread fetches one element, coalesced.
2. `workgroupBarrier()` — wait for everyone.
3. Every thread now reads that tile from shared memory **repeatedly**, at ~25 cycles instead of ~500.

A 5×5 blur naively reads each pixel 25 times from VRAM. With this pattern it reads each pixel *once* from VRAM and 25 times from the scratchpad. Blur kernels, prefix sums, reductions, matrix operations, and voxel neighborhood operations all follow this shape.

### Atomics

**Atomic operations** (`atomicAdd`, `atomicMax`, `atomicCompareExchange`) let threads cooperate on shared counters without racing. Essential for building **compacted output lists** — "which of these 100,000 chunks survived culling, packed into a dense array with no gaps."

```wgsl
let slot = atomicAdd(&outputCount, 1u);   // claim a unique index
output[slot] = myChunkIndex;
```

But **contended atomics serialize**. If 10,000 threads all hammer the same counter, they queue up one at a time and you've built a sequential bottleneck inside a parallel program. The standard fix:

1. Accumulate within the workgroup using workgroup-memory atomics (fast, only 64 threads contending).
2. One thread per workgroup does a *single* global atomic to reserve a block of slots.
3. Everyone writes into their reserved block.

That turns 10,000 global atomics into ~150. This pattern shows up everywhere in GPU-driven rendering.

---

## Why everything is batched: the draw call

### What a draw call costs

On the CPU side, a **draw call** — one `draw()` or `drawIndexed()` — involves validation, state binding, and driver work: checking that your bind groups match the pipeline layout, translating to native API calls, and potentially patching command buffers. On the GPU side, changing pipeline state can force a flush of in-flight work.

Order-of-magnitude intuition: **a modern API can handle thousands of draw calls per frame, not hundreds of thousands.** At ~2 µs of CPU time each, 5,000 draws is 10 ms — most of your frame budget spent before the GPU does anything.

The concrete consequence for voxels: **if you draw each voxel as its own cube, you will manage a few thousand voxels at 60 Hz.** A Minecraft-scale view is millions of voxels. Batching that same geometry into merged chunk meshes gets you there. This is not an optimization; it's the difference between the technique working and not working.

### The escalating ladder of batching

Every renderer climbs this ladder. Know all four rungs and which one you're on.

**1. One draw per object.** Naive. Fine for a few hundred objects.

**2. Instancing.** One draw call, N copies of the same geometry, each reading its own per-instance data (transform, color) from a buffer indexed by `instance_index`. Great for identical geometry: trees, particles, bullets, crowd characters.

**3. Merged / static batching.** Combine many objects' geometry into shared vertex and index buffers, offline or at load time, so one draw covers many logical objects. **This is exactly what voxel chunk meshing is**: 32³ voxels become one merged mesh in one draw.

**4. GPU-driven rendering.** The GPU itself decides what to draw. A compute shader culls objects and writes draw arguments (vertex count, instance count, offsets) into a buffer, then the CPU issues an **indirect draw** — `drawIndirect(argBuffer, offset)` — whose parameters are read from GPU memory at execution time. The CPU no longer knows or cares how many objects get drawn; it may be zero, it may be 50,000, and the CPU cost is identical.

The trend across the last decade is unambiguous: **move decisions from CPU to GPU, and reduce per-object CPU work toward zero.** Module 09 covers the mechanics; know the trajectory now. (Note: WebGPU has `drawIndirect` but *not* multi-draw-indirect yet, which limits how far up rung 4 you can climb today — see Module 05.)

### State changes have a cost hierarchy

Roughly most to least expensive:

```
render pass / render target change   ←── most expensive (flushes, resolves, cache invalidation)
  → pipeline (shader) change
    → bind group / resource binding change
      → uniform / push-constant data change
        → draw call                   ←── least expensive
```

Sorting your draws to minimize the expensive changes is standard practice. **"Sort by pipeline, then by material, then by mesh"** is a sentence you should be able to say without hesitating, and it's a common interview question in disguise ("how would you organize your render queue?").

---

## The pipelining you don't see

Inside a single frame, the GPU is itself a pipeline with many stages in flight simultaneously: vertex shading for one triangle batch, rasterization for another, fragment shading for a third, and **ROP** work (Raster Operations — the fixed-function units that do depth testing and blending and write to the framebuffer) for a fourth.

A **barrier** — required when one pass writes a resource that a later pass reads — drains that pipeline. All in-flight work must complete before the next pass starts, and the machine sits partially idle while it empties and refills. That gap is a **bubble**.

This is why:

**Fewer, larger passes beat many small passes.** Ten full-screen post-process passes at 1080p each cost a barrier, plus a full read *and* a full write of the framebuffer — that's 10 × 2 × 8 MB = 160 MB of bandwidth for effects that might be a handful of ALU operations each. Merging them into one shader saves the barriers and 90% of the bandwidth. "Merge your post chain" is one of the standard first optimizations on any renderer.

**Async compute exists.** If a graphics pass is bottlenecked on fixed-function units (rasterizer, ROPs) while the ALUs sit idle, you can overlap an independent compute workload onto the same hardware and fill the gaps. Consoles and native APIs (Vulkan, D3D12) expose explicit async compute queues for this, and it's routinely worth 10–20%.

**WebGPU does not currently expose multiple queues**, so async compute is a concept to understand and discuss rather than something you can use in a browser today. But the underlying idea — *keep independent work available so idle units get filled* — still guides how you order passes, and knowing that WebGPU lacks it (and why) is exactly the kind of platform awareness an engine role wants.

---

## Common confusions

**"The GPU has thousands of cores, so it must be thousands of times faster."** It has thousands of *lanes* running in lockstep at half the clock speed with no branch prediction. It's thousands of times faster at embarrassingly parallel, coherent, arithmetic-heavy work — and *slower* than a CPU at anything sequential, branchy, or pointer-chasing.

**"I'll move this to a compute shader to speed it up."** Only if it's parallel, coherent, and big enough to amortize the dispatch overhead. A dispatch has fixed cost (microseconds); if the work is 500 items of divergent logic, the CPU wins. Also remember getting the answer *back* costs you frames (Module 01).

**"My shader has no branches so divergence isn't my problem."** Loops with data-dependent trip counts diverge too, and so does `discard`. A DDA voxel raymarch is one big data-dependent loop — that's the primary divergence source in a voxel renderer, not `if` statements.

**"Occupancy is at 40%, that's my bug."** Maybe. If the shader is ALU-bound with little memory traffic, 40% occupancy might be optimal. Occupancy is an input to a diagnosis, not a diagnosis.

**"I reduced my draw calls from 5,000 to 500 and nothing got faster."** Then you weren't CPU-bound on draw submission. This is the Module 01 lesson again: **find out which processor you're bound by before optimizing.** Halving CPU work when you're GPU-bound changes nothing at all.

**"Texture compression is for saving VRAM."** It's mostly for saving *bandwidth*. The data stays compressed in memory and in cache, so a compressed texture is also a smaller cache footprint and fewer transactions. That's where the speed comes from.

---

## The interview answer

***"Why is my shader slow when I added a branch?"***

> "Branches themselves aren't expensive — divergence is. The hardware runs threads in subgroups sharing a program counter, so if the condition differs within a subgroup both sides execute with masking and you pay for both. If the branch is uniform across the subgroup it's basically free. I'd also check whether the change increased register pressure, because that can reduce occupancy and cost you latency hiding — which shows up as a slowdown that looks completely unrelated to the line you touched."

***"How do you decide whether to optimize ALU or memory?"***

> "Measure achieved bandwidth and achieved ALU throughput against the device's theoretical peak — roofline. If I'm near peak bandwidth and far from peak ALU, I compress data, improve locality, or recompute instead of fetching. If it's the reverse, I cut math, drop to f16 where precision allows, or hoist work out of the inner loop. The fixes are opposite, so guessing costs you a week."

***"Why do voxel renderers use 3D textures instead of storage buffers?"***

> "Cache layout. Texture memory is swizzled — Morton order or a vendor tiling — so texels that are near each other in 3D space are near each other in memory. A storage buffer is linear, so it's only cache-friendly along one axis. Voxel access patterns are inherently 3D, so you get a big win even if you never use the filtering hardware. And when you do want trilinear interpolation for smooth LODs, that's free too."

***"How would you organize your render queue?"***

> "Sort to minimize the expensive state changes: render pass, then pipeline, then bind group, then draw. Beyond that I'd want to move culling and draw generation onto the GPU with indirect draws so the CPU cost stops scaling with object count — though in WebGPU today there's no multi-draw indirect, so there's a ceiling on how far that goes."

---

## Exercise — Voxelforge, Stage 3 (paper + measurement)

**No new code required, but do this before touching WebGPU.** These three numbers will make the rest of the course concrete rather than abstract, and every optimization decision you make later will refer back to them.

**1. Find your own GPU's specs.** Look up: number of SMs/CUs, subgroup width, memory bandwidth in GB/s, and peak FP32 TFLOPs. (`chrome://gpu` tells you the model; the vendor's spec page or TechPowerUp's GPU database has the rest.) Compute its **FLOPs per byte** ratio:

```
FLOP/byte = (TFLOPs × 10¹²) / (bandwidth GB/s × 10⁹)
```

Write the number down. It's the ridge point of the roofline you'll be optimizing against for the rest of this course. For most desktop GPUs it lands between 30 and 80.

**2. The voxel bandwidth calculation.** For a 512×512×512 voxel grid at 1 byte per voxel:
- What's the raw size in MB?
- What bandwidth would you need to read it *once per frame at 60 Hz*?
- What fraction of your GPU's total bandwidth is that?

**This single calculation explains why sparse structures (Module 07) are non-optional.** Do it before reading Module 07 and that module will land much harder.

**3. The raymarching budget.** Given a 1920×1080 render at 60 Hz where every pixel casts a ray taking an average of 40 voxel steps:
- Total steps per second?
- If your GPU does N TFLOPs, how many floating-point operations can you afford *per step*?
- How many nanoseconds is that?

**⭐ Stretch (worth doing):** write a tiny WebGPU compute shader that does nothing but read a large buffer and sum it, time it with timestamp queries, and compare achieved bandwidth to the spec sheet. You'll typically hit 60–85% of peak. Now you know what "peak" actually means on *your* machine, and you have a calibration point for every future measurement. This is the beginning of the discipline Module 09 is entirely about.

---

## Go deeper

- **Fabian "ryg" Giesen, "A trip through the Graphics Pipeline 2011"** — fgiesen.wordpress.com. Thirteen parts, still the best explanation of GPU internals in existence. Free. If you read one thing from this list, read this.
- **"GPU Performance for Game Artists"** — fragmentbuffer.com. Short, visual, and useful precisely *because* it explains costs without API detail.
- **NVIDIA CUDA C++ Best Practices Guide**, occupancy and memory coalescing chapters — vendor-specific but the concepts transfer directly to every GPU.
- **Samuel Williams et al., "Roofline: An Insightful Visual Performance Model"** — the original paper; short and genuinely readable.
- **Sebastian Aaltonen's writing and talks** (ex-Ubisoft, ex-Unity) — consistently the clearest public source on GPU-driven rendering and occupancy tradeoffs. His Twitter/X threads are a graduate course.
- **"A Trip Through the Graphics Pipeline" companion:** Kayvon Fatahalian's "How a GPU Works" lecture slides (CMU 15-462) — the clearest visual explanation of SIMT and latency hiding available.

---

**Next:** [Module 04 — The Rasterization Pipeline](./04-rasterization-pipeline.md)
