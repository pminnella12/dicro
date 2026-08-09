# Module 03 — The GPU Mental Model

### What the hardware actually is, why it punishes branches and rewards batches, and the vocabulary that separates graphics programmers from people who have used a graphics API

*~12 min read · Part I: Foundations · Prerequisites: Modules 01–02*

---

Most developers learn a graphics API before they learn the machine underneath it. That is backwards, and it produces engineers who can get a triangle on screen but cannot explain why their shader got 4× slower after adding one `if`.

This module has no API in it. It is about the device.

> A CPU is a small number of very smart cores optimized to make **one** thread finish fast. A GPU is a large number of very simple cores optimized to keep **thousands** of threads in flight so that no one is ever waiting on memory.

Every counterintuitive rule in graphics programming follows from that sentence.

---

## SIMT: the thing that explains everything

GPUs execute in **lockstep groups**. NVIDIA calls a group a **warp** (32 threads); AMD calls it a **wave** (32 or 64); Intel and Apple have their own widths. WebGPU's portable term is **subgroup**. The vendor-neutral model is **SIMT** — Single Instruction, Multiple Threads.

Every thread in a subgroup shares one program counter. They execute the *same instruction* at the *same time* on *different data*.

This has a brutal consequence:

```wgsl
if (someCondition) {
  expensiveA();   // half the threads want this
} else {
  expensiveB();   // the other half want this
}
```

If the condition differs within a subgroup, the hardware cannot run two paths at once. It executes **both branches serially**, masking off the threads that shouldn't participate in each. Cost = `A + B`, not `max(A, B)`. This is **divergence**, and it is the number one shader performance concept.

The nuance that matters, and that people get wrong: **branching is not slow. Divergence is slow.** A branch where *all* threads in a subgroup agree is nearly free — the hardware skips the untaken side entirely. So:

- `if (uniformFlag)` — reading the same uniform value everywhere — is cheap.
- `if (pixelIsInShadow)` — varying per pixel — is expensive, *unless* shadowed and lit regions happen to be spatially coherent, which they usually are, because neighboring pixels are in the same subgroup.

**Coherence is a resource.** Threads that are adjacent in the dispatch tend to be adjacent in the subgroup. That is why screen-space effects are fast (neighboring pixels take the same branches, sample nearby texels) and why incoherent ray tracing is slow (each ray goes somewhere different). It is also why voxel *primary* rays are fast — neighbors travel nearly parallel through the grid — while secondary/bounce rays are much slower, a fact that shapes every voxel raytracer's architecture.

---

## Latency hiding: the actual design goal

A memory fetch from VRAM costs hundreds of cycles. A CPU fights this with big caches, prefetchers, out-of-order execution, and branch prediction — enormous silicon spent making one thread not stall.

A GPU does the opposite. When a subgroup issues a memory read and stalls, the scheduler **swaps in another resident subgroup** and keeps the ALUs busy. Then another. With enough subgroups resident, the memory latency is completely hidden behind other work.

The measure of how many subgroups can be resident is **occupancy**.

And here is where the counterintuitive part lives: **occupancy is limited by your shader's resource usage.** Each SM/CU has a fixed budget of registers and shared (workgroup) memory, divided among resident threads. A shader that uses lots of registers — many live variables, deep expressions, big unrolled loops — allows fewer resident subgroups, which means less latency hiding, which means the ALUs idle waiting on memory.

> A shader can get *slower* when you add a "cheap" optimization, because holding one extra value increased register pressure past a threshold and halved occupancy.

This is the mechanism behind advice that otherwise sounds like superstition:

- Long shaders with many temporaries can underperform shorter ones doing more math.
- Aggressive loop unrolling sometimes hurts.
- Large workgroup shared-memory allocations directly trade against occupancy.

**High occupancy is not automatically good, either.** A shader that is pure ALU with almost no memory traffic doesn't need latency hiding, and low occupancy with high register use may be the optimal point. The right framing: occupancy is a *tool for hiding memory latency*, and you want just enough of it.

---

## The memory hierarchy is the performance story

Rough orders of magnitude on a discrete GPU:

| Level | Latency | Bandwidth | Notes |
|---|---|---|---|
| Registers | ~1 cycle | enormous | Per-thread, scarce, limits occupancy |
| Shared / workgroup memory | ~20–30 cycles | very high | Explicitly managed scratchpad, per workgroup |
| L1 / texture cache | ~30–80 cycles | high | Per SM/CU |
| L2 cache | ~200 cycles | moderate | Shared across the chip |
| VRAM (device memory) | ~400–800 cycles | 200–1000+ GB/s | The bottleneck for most real workloads |
| Host (CPU) memory over PCIe | *very* slow | ~16–64 GB/s | Avoid touching per frame |

Two rules follow:

**1. Coalesce your accesses.** Memory is fetched in cache lines (typically 32–128 bytes). If the 32 threads of a subgroup read 32 consecutive floats, that's a small number of transactions. If they read 32 floats scattered across memory, that's up to 32 separate transactions — and you have wasted most of the bytes you paid to fetch. Same instruction count, an order of magnitude difference in time.

This is the hardware reason behind **structure-of-arrays (SoA)** layouts. If each thread processes one entity and needs only its position, an array-of-structs layout drags the entire 64-byte entity struct through the cache to use 12 bytes of it. Split positions into their own tightly packed array and every byte you fetch gets used. Module 13 revisits this on the CPU side, where the same effect applies for the same reason.

**2. Arithmetic is nearly free; memory is not.** Modern GPUs offer on the order of 20–100 FLOPs of compute per byte of bandwidth. **Recomputing a value is very often cheaper than looking it up.** Packing data smaller and unpacking it with ALU work is usually a win. Compressed texture formats win not because of VRAM capacity but because of bandwidth.

The framework for reasoning about this is the **roofline model**: plot arithmetic intensity (FLOPs per byte) against achieved performance. A kernel is either **bandwidth-bound** (left of the ridge) or **compute-bound** (right of it), and the fix is completely different in each case. When the Engine JD says *"ALU vs memory bandwidth tradeoffs"* and *"peak-performance-percentage analysis,"* this is exactly what it means: measure what fraction of theoretical peak ALU or peak bandwidth you're achieving, and optimize the one that's saturated.

---

## Texture units: hardware you don't get on the CPU

Sampling a texture is not just an array read. Dedicated fixed-function hardware does, in a single instruction:

- Address computation and wrapping/clamping
- **Bilinear filtering** between four texels — free, in hardware
- **Trilinear filtering** between two mip levels
- **Anisotropic filtering** — multiple taps along the projected footprint
- Format decompression (BC/ASTC/ETC)
- Caching optimized for **2D/3D spatial locality**, not linear locality

That last point matters enormously for voxels. Texture caches are organized in **swizzled/tiled** layouts (Morton/Z-order or vendor-specific), so texels that are near each other *in 2D or 3D space* are near each other in memory. A 3D texture read of a voxel and its neighbors is cache-friendly in all three axes; the same data in a linear storage buffer is only cache-friendly along one axis.

**This is why voxel engines store bricks in 3D textures rather than storage buffers** even when they don't need filtering. You are buying the cache layout and the free trilinear interpolation.

Mip levels exist for the same reason. A minified texture sampled without mips makes neighboring pixels read distant texels — cache thrash *plus* aliasing. Mipmapping fixes both, at a 33% memory cost.

---

## Compute shaders and the dispatch model

Compute shaders let you use the GPU as a general parallel processor, outside the rasterization pipeline. The model:

- You **dispatch** a 3D grid of **workgroups**.
- Each workgroup has a fixed size in threads (declared in the shader, e.g. `@workgroup_size(8,8,1)`).
- Threads within a workgroup can share **workgroup memory** and **synchronize** with barriers.
- Threads in *different* workgroups cannot synchronize at all. There is no global barrier inside a dispatch — that is what separate dispatches are for.

Choosing workgroup size is a real decision:

- Make it a **multiple of the subgroup size** (64 is a safe default: divisible by both 32 and 64). A workgroup of 40 threads wastes a big fraction of every subgroup.
- Shape it to your access pattern: `8×8` for 2D image work so each workgroup covers a square tile with good cache behavior; `4×4×4` for 3D voxel work.
- Bigger is not better; larger workgroups mean coarser scheduling granularity and more shared memory per group.

**Workgroup shared memory** is the key optimization primitive: load a tile of data cooperatively from VRAM once, barrier, then have all threads read it repeatedly from the fast scratchpad. Blur kernels, prefix sums, reductions, and voxel neighborhood operations all follow this pattern.

**Atomics** let threads cooperate on shared counters — essential for building compacted output lists (e.g., "which chunks survived culling"), but contended atomics serialize, so the standard trick is to accumulate in workgroup memory and do one atomic per workgroup against global memory.

---

## Why everything is batched: the draw call

On the CPU side, a draw call involves validation, state binding, and driver work. On the GPU side, changing pipeline state can force a flush of in-flight work.

Order-of-magnitude intuition: a modern API can handle **thousands** of draw calls per frame, not hundreds of thousands. If you draw each voxel as its own cube, you will manage a few thousand voxels at 60 Hz. Batching that same geometry into merged chunk meshes gets you millions.

The escalating ladder of batching, which every renderer climbs:

1. **One draw per object** — naive.
2. **Instancing** — one draw, N copies with per-instance data. Great for identical geometry.
3. **Merged/static batching** — combine geometry into shared buffers offline or at load. This is what voxel chunk meshing is.
4. **GPU-driven rendering** — the GPU itself decides what to draw. A compute shader culls and writes draw arguments into a buffer, and the CPU issues an **indirect draw** that reads its parameters from GPU memory. The CPU no longer knows or cares how many objects are drawn.

The trend across the last decade is unambiguous: move decisions from CPU to GPU, and reduce per-object CPU work toward zero. Module 09 covers the mechanics; know the trajectory now.

**State changes have a cost hierarchy** worth memorizing, roughly most to least expensive: render pass / render target change → pipeline (shader) change → bind group / resource binding change → uniform data change → draw. Sorting your draws to minimize the expensive changes is standard practice, and "sort by pipeline, then by material, then by mesh" is a sentence you should be able to say.

---

## The pipelining you don't see

Inside a frame, the GPU is itself a pipeline with many stages in flight simultaneously: vertex shading for one triangle batch, rasterization for another, fragment shading for a third, ROP blending for a fourth. A **barrier** — required when one pass writes a resource that the next pass reads — drains that pipeline and creates a bubble where units sit idle.

This is why:

- **Fewer, larger passes beat many small passes.** Ten full-screen post-process passes at 1080p each cost a barrier plus a full read and write of the framebuffer. Merging them into one shader saves both.
- **Async compute** exists. If a graphics pass is bottlenecked on fixed-function units (rasterizer, ROPs) while the ALUs idle, you can overlap an independent compute workload onto the same hardware. Consoles and native APIs expose explicit async compute queues; **WebGPU does not currently expose multiple queues**, so this is a concept to understand and discuss rather than something you can use in a browser today — but the underlying idea, *keep independent work available so idle units get filled*, still guides how you order passes.

---

## The interview answer

*"Why is my shader slow when I added a branch?"*

> "Branches themselves aren't expensive — divergence is. The hardware runs threads in subgroups sharing a program counter, so if the condition differs within a subgroup both sides execute with masking. If the branch is uniform across the subgroup it's basically free. I'd also check whether the change increased register pressure, because that can reduce occupancy and cost you latency hiding, which shows up as a slowdown that looks unrelated to the code you touched."

*"How do you decide whether to optimize ALU or memory?"*

> "Measure achieved bandwidth and achieved ALU throughput against the device's theoretical peak — roofline. If I'm near peak bandwidth and far from peak ALU, I compress data, improve locality, or recompute instead of fetching. If it's the reverse, I cut math, use lower precision, or move work out of the inner loop."

---

## Exercise — Voxelforge, Stage 3 (paper + measurement)

No new code required, but do this before touching WebGPU:

1. Look up your own GPU's specs: number of compute units, subgroup width, memory bandwidth in GB/s, and peak FP32 TFLOPs. Compute its **FLOPs per byte** ratio. Write the number down — it's the shape of the roofline you'll be optimizing against for the rest of this course.
2. For a 512×512×512 voxel grid at 1 byte per voxel, compute the raw size. Now compute the bandwidth required to read it *once per frame at 60 Hz*. Compare to your GPU's bandwidth. This single calculation explains why sparse structures (Module 07) are non-optional.
3. Given a 1920×1080 render at 60 Hz where every pixel casts a ray taking an average of 40 voxel steps, compute total steps per second. Now decide how many nanoseconds per step you can afford.

Those three numbers will make the rest of the course concrete rather than abstract.

---

## Go deeper

- **Fabian "ryg" Giesen, "A trip through the Graphics Pipeline 2011"** — fgiesen.wordpress.com. Thirteen parts, still the best explanation of GPU internals in existence. Free.
- **"GPU Performance for Game Artists"** — fragmentbuffer.com. Short, visual, and useful precisely because it explains costs without API detail.
- **NVIDIA CUDA C++ Best Practices Guide**, occupancy and memory coalescing chapters — vendor-specific but the concepts transfer directly.
- **Samuel Williams et al., "Roofline: An Insightful Visual Performance Model"** — the original paper; short and readable.
- **Sebastian Aaltonen's writing/talks** (ex-Ubisoft, ex-Unity) — consistently the clearest public source on GPU-driven rendering and occupancy tradeoffs.

---

**Next:** [Module 04 — The Rasterization Pipeline](./04-rasterization-pipeline.md)
