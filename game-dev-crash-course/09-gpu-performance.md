# Module 09 — GPU Performance Engineering

### How to find out what is actually slow, and the ladder of techniques that fix each cause

*~13 min read · Part III: Voxels & Performance · Prerequisites: Modules 03–08*

---

The Engine JD's longest bullet is this one: *"peak-performance-percentage analysis, draw call optimizations, material batching, indirect draw, culling, cache friendliness, bindless, occupancy, latency hiding, overdraw, overlapping pipelines, ALU vs memory bandwidth tradeoffs, async compute, PIX, RenderDoc, Nvidia Nsight/AMD Radeon tools."*

That list is a job description for a **performance engineer who happens to work on graphics**. This module maps it.

The organizing principle first:

> Optimization is a diagnosis problem, not a coding problem. The skill is not knowing techniques — it's determining, with evidence, which of six possible bottlenecks you have. Applying the right technique to the wrong bottleneck produces zero improvement and a week of lost time.

---

## Step 1: are you CPU-bound or GPU-bound?

Everything starts here, and getting it wrong is the most common failure mode in graphics performance work.

The test is simple. **Drop the resolution to 1/4** (say, render at 480p and upscale). If the frame time barely changes, the GPU was not the bottleneck — you're **CPU-bound**. If frame time drops proportionally, you're **GPU-bound** on something resolution-dependent (fragment shading, bandwidth, overdraw).

Complementary test: **remove half the objects** but keep resolution. If frame time halves, you're bound by per-object work (draw calls, culling, vertex processing).

Then get real numbers:

- **CPU time:** `performance.now()` around your frame; Chrome DevTools Performance panel with a flame chart. Look for GC bars, worker messaging, and time inside your own systems.
- **GPU time:** WebGPU's **timestamp queries** (request the `timestamp-query` feature, then use `timestampWrites` on render/compute pass descriptors). Resolve into a query buffer, copy to a mappable buffer, read it back **two or three frames later** — never the same frame, or you stall (Module 01).

Wrap each pass in timestamps and build a **per-pass GPU time HUD** early. It is the single highest-value piece of engine infrastructure for performance work, and you should build it before you need it.

---

## Step 2: which of the six bottlenecks?

If GPU-bound, it's one of these. Each has a distinct signature and a distinct fix.

**1. Draw call / CPU submission bound.** Symptom: GPU is idle, CPU time is dominated by command encoding. Signature: reducing object count helps enormously; reducing resolution does nothing. Fix: batching, instancing, render bundles, indirect draw.

**2. Vertex bound.** Symptom: resolution changes don't matter, triangle count does. Rare in practice unless you have very dense meshes or expensive vertex shaders. Fix: LOD, better meshing, packed vertex formats, vertex cache ordering.

**3. Fragment/ALU bound.** Symptom: scales directly with resolution; scales with shader complexity. Fix: simplify shaders, reduce overdraw, depth prepass, render effects at lower resolution.

**4. Bandwidth bound.** Symptom: scales with resolution *and* with texture/buffer sizes; achieved bandwidth is near the device's peak. Fix: compression, smaller formats, better locality, fewer full-screen passes, merged passes, correct load/store ops.

**5. Overdraw bound.** Symptom: heavy alpha blending, or many opaque layers; scales badly with how much geometry overlaps. Fix: front-to-back sorting, depth prepass, fewer/smaller particles, reduced transparent layers.

**6. Sync/barrier/latency bound.** Symptom: neither CPU nor GPU appears busy, yet frame time is high. Signature: gaps in the GPU timeline. Fix: remove readbacks, remove unnecessary barriers, merge passes, restructure dependencies.

**Peak-performance-percentage analysis** — the phrase in the JD — means quantifying this. Take the measured GPU time for a pass, compute the bytes it moved and the FLOPs it performed, and express each as a percentage of the device's theoretical peak. A pass at 85% of peak bandwidth is bandwidth-bound and no amount of shader simplification will help. A pass at 6% of both is *latency-bound* and needs more parallelism or better access patterns. This is the roofline model from Module 03 applied as a daily practice.

---

## The tools

**In-browser:**
- **Chrome DevTools Performance panel** — CPU side, GC, workers, frame timing.
- **WebGPU Inspector** (Chrome extension) — RenderDoc-style frame capture inside DevTools: inspect resources, bind groups, pipeline state, and it can auto-inject pass timestamp queries. The best first-line WebGPU debugging tool available today.
- **`chrome://gpu`** and `about:support` for backend and driver info.
- **WebGPU timestamp queries** — your own HUD, as above.

**Native, via capture:**
- **PIX** (Windows, D3D12) — captures Chrome's WebGPU work when running the D3D12 backend. Full timing breakdown per draw and per pipeline stage. Brandon Jones's toji.dev guides walk through the exact setup, including the required Chrome flags.
- **RenderDoc** — Dawn integrates the RenderDoc API to begin/end captures at frame boundaries; works with the D3D12 backend on Windows in recent Chrome builds. Best-in-class for inspecting *what* was drawn and with what state.
- **Nvidia Nsight Graphics / AMD Radeon GPU Profiler** — hardware counters: occupancy, cache hit rates, ALU vs memory utilization, per-unit timing. This is where "peak percentage" numbers actually come from.
- **WebGPUReconstruct** — records a WebGPU trace and replays it as a native Dawn/wgpu application, so you can point any native profiler at it. Useful when in-browser capture is blocked.

Learning to read a **frame capture** — the list of passes, the draw calls in each, the state at each draw, the timing bar next to each — is a concrete, learnable skill that will show up in interviews. Practice on a capture of your own Voxelforge frames.

**Debug visualizations you should build into the engine:**
- Overdraw heatmap (additive `+1` per fragment, false-colored)
- Ray step count heatmap (for the raymarcher — see Module 08)
- Wireframe / triangle density
- Per-pass GPU time HUD
- Draw call and triangle counters
- Chunk/frustum culling visualization from a detached camera

Those are worth more than any external tool, because they're always on and they're specific to your engine.

---

## The technique ladder: reducing CPU-side cost

**Batching and instancing.** Merge geometry that shares a pipeline and material. In a voxel engine the merge already happened at meshing time — the remaining question is whether each chunk is its own draw (typical) or whether many chunks share one buffer with per-chunk offsets (better).

**Sort to minimize state changes.** Cost hierarchy from Module 03: render pass > pipeline > bind group > dynamic offset > draw. Sort your draw list by pipeline, then material, then mesh. A `u64` sort key packing `[pass | pipeline | material | depth]` is the standard implementation, and radix-sorting it is fast.

**Render bundles.** Pre-record draws once and replay them across frames. Because JavaScript's per-call overhead is much higher than C++'s, bundles matter more in WebGPU than the equivalent technique does in native APIs. Static voxel chunk geometry is the ideal case — rebuild the bundle only when the visible chunk set changes.

**Dynamic offsets over per-object bind groups.** One big uniform buffer, `setBindGroup` with a byte offset per object. Avoids thousands of bind group allocations.

**Don't rebuild what didn't change.** Cache culling results, cache sorted lists, cache bundles, and invalidate precisely.

---

## The technique ladder: culling

Culling is the cheapest possible optimization — work you never do costs nothing.

**Frustum culling.** Six-plane vs AABB test per chunk (Module 02). Basic and mandatory.

**Distance culling / view distance.** Simple, and the primary knob players will adjust.

**Backface culling.** Free in hardware. For voxel chunks you can go further: a chunk entirely behind you contributes nothing, and for a chunk you can determine which of the six face directions could possibly be visible from the camera's position, skipping up to half the faces at meshing or draw time.

**Occlusion culling** is the big one, and voxel worlds — especially dungeons — are the case where it pays most, because you're usually inside a solid volume looking through small openings.

Three approaches worth knowing:

1. **Hardware occlusion queries** — draw bounding boxes, ask how many pixels passed. Latency-prone (results arrive frames later), and not currently exposed in WebGPU.
2. **Hi-Z / depth pyramid, two-pass.** The modern standard: (a) render objects that were visible *last* frame, (b) build a mip pyramid of that depth buffer, (c) in a compute shader, test every object's screen-space bounding box against the appropriate pyramid level and produce a visibility list, (d) render the newly-visible false negatives. Fully GPU-side, no CPU readback, no stalls. Introduced in Ubisoft's *GPU-Driven Rendering Pipelines* (SIGGRAPH 2015) and now near-universal.
3. **Portal / visibility precomputation.** For a dungeon crawler with rooms and corridors, precomputing which regions can see which others is extremely effective and very cheap at runtime. Classic technique, undervalued today, and *exactly* the sort of purpose-built choice that beats a generic engine feature for a specific game.

**For voxels specifically, cave culling** deserves a mention: precompute, per chunk, which of its 6 faces are connected to which others through air (a small flood fill, 15 bits of data). Then flood-fill the visible chunk set outward from the camera's chunk, only entering a chunk through a face that connects to the face you'd exit. Minecraft uses a version of this, and it's dramatically effective underground — it's what stops a cave system from rendering the entire world behind it.

---

## The technique ladder: GPU-side

**Depth prepass** (Module 04) — when fragment-bound with high overdraw.

**Reduce resolution for expensive effects.** Bloom at 1/4, SSAO at 1/2, volumetrics at 1/4 with a bilateral upsample. Almost always invisible, always a large saving.

**Merge full-screen passes.** Each one is a full framebuffer read + write. Combining tonemap + dither + vignette + chromatic aberration into one shader saves three round trips.

**Correct load/store ops.** `clear` instead of `load`; `discard` depth you won't reuse. Free, and on tile-based GPUs (Apple Silicon, mobile) it can be a double-digit percentage.

**Indirect draw.** `drawIndexedIndirect` reads its parameters (index count, instance count, offsets) from a GPU buffer. A compute shader culls and writes those parameters, so the CPU issues the draw without knowing what will be drawn. Setting `instanceCount = 0` culls an object entirely, GPU-side. WebGPU supports single indirect draws today; **multi-draw indirect is not yet available**, so you can't collapse N objects into one CPU-issued call — you either issue one indirect draw per object (still cheap, and instance count zero costs almost nothing) or batch aggressively into fewer, larger indirect draws.

**Bindless** would let a shader index any texture/buffer from GPU-resident data, which is what makes fully GPU-driven material systems work. **Not available in WebGPU yet.** The available substitutes: 2D texture arrays (one layer per material — a natural fit for voxels), texture atlases, and one big storage buffer of material parameters indexed by ID. Know the term, know why it matters, know the workaround.

**Async compute** — overlapping compute work with graphics work to fill idle units. WebGPU exposes a single queue, so this is unavailable in the browser today. The transferable idea is *ordering passes so independent work is available* and avoiding unnecessary barriers that drain the pipeline.

**Occupancy tuning** (Module 03) — reduce register pressure, size workgroups as multiples of the subgroup width, trim workgroup shared memory. Measure with Nsight/RGP; guessing here is worthless.

---

## Memory and cache friendliness

The JD lists "cache friendliness" alongside GPU items, and it belongs on both sides.

**GPU side:** Morton/Z-order layouts, 3D textures over linear buffers for 3D access, structure-of-arrays, packed formats, and coalesced access within a subgroup.

**CPU side:** the same principles (Module 13 goes deep). Contiguous typed arrays over object graphs; iterate in memory order; avoid pointer chasing.

**Allocation is the CPU-side killer in a JS engine.** Not because allocation is slow, but because GC pauses are unschedulable. Pool everything in hot paths, reuse output objects, and pre-size typed arrays.

---

## A worked diagnosis

You're at 24 ms. Here's the sequence:

1. **Timestamp queries** say GPU = 9 ms, CPU = 23 ms. → CPU-bound. All shader optimization is off the table.
2. **DevTools flame chart** shows 14 ms inside command encoding, spread across 4,200 `setBindGroup` + `drawIndexed` pairs. → Draw submission bound.
3. **Fix A:** sort by pipeline and material; state changes fall from 4,200 to 300. Now 17 ms.
4. **Fix B:** move per-chunk uniforms into one buffer with dynamic offsets; drop 4,200 bind groups to 1. Now 13 ms.
5. **Fix C:** cache a render bundle for the visible static chunk set, rebuilt only when that set changes. Now 8 ms CPU, 9 ms GPU. → **Now GPU-bound.**
6. Re-measure per pass: the raymarch pass is 6 of the 9 ms. Ray step heatmap shows 300+ steps in open areas. → Add hierarchical DDA + occupancy bitmasks. Now 3 ms.
7. Total ~11 ms. Ship it and move on.

**Notice that steps 3–5 are all CPU work, and every one of them would have been skipped by someone who started by optimizing shaders.** That discipline — measure, identify the bound, fix that, re-measure — is the actual skill.

---

## The interview answer

*"The game is at 30 FPS. What do you do?"*

> "First I find out whether I'm CPU- or GPU-bound — drop resolution and see if frame time moves, and get real numbers from timestamp queries per pass plus a CPU flame chart. Then I identify which bound: draw submission, vertex, fragment, bandwidth, overdraw, or sync. I'd want achieved bandwidth and ALU as a percentage of peak so I know which side of the roofline I'm on. Then apply the matching fix and re-measure — and I'd be suspicious of any change I can't attribute to a number. I'd also check the frame time graph and p99, not the average, because a 30 FPS average with spikes is a different problem than a steady 30."

*"What would you look at first in a voxel renderer specifically?"*

> "Overdraw and ray step counts, via debug heatmaps. Then culling effectiveness — how many chunks am I submitting versus how many are actually visible? In a dungeon, cave-culling or portal visibility usually beats everything else by a wide margin."

---

## Exercise — Voxelforge, Stage 9

1. Add **timestamp queries** per pass with a proper 3-frame-deferred readback, and put a per-pass GPU time HUD on screen.
2. Add CPU-side counters: draw calls, triangles, chunks submitted vs chunks in frustum, bind group switches.
3. Build the **overdraw heatmap** and the **ray step heatmap**. Fly around and find your worst-case view.
4. Deliberately make yourself CPU-bound (one draw per chunk, one bind group per chunk, unsorted). Record the numbers. Then apply sorting → dynamic offsets → render bundles, recording after each. **Write the four numbers down.** This exercise teaches more than any amount of reading.
5. Implement **frustum culling** and then **cave/connectivity culling** for chunks. Measure the reduction in submitted chunks while standing inside a cave system.
6. Implement a **compute-based cull writing indirect draw args**, with `instanceCount = 0` for culled chunks. Compare against CPU culling on both CPU and GPU time.
7. Capture a frame with **WebGPU Inspector**, and if you're on Windows, with **PIX** or **RenderDoc** via the toji.dev instructions. Find the most expensive draw in the capture and explain why it's expensive.

---

## Go deeper

- **toji.dev/webgpu-profiling** — Brandon Jones's guides for PIX and RenderDoc with Chrome. Start here; it's the exact workflow.
- **WebGPU Inspector** (`brendan-duncan/webgpu_inspector`) — install it today.
- **"GPU-Driven Rendering Pipelines" — Ulrich Haar & Sebastian Aaltonen, SIGGRAPH 2015** — the two-pass Hi-Z occlusion culling architecture. Foundational.
- **vkguide.dev "GPU Driven Rendering"** chapters — the clearest tutorial-level treatment of compute culling + indirect draw.
- **"Optimizing the Graphics Pipeline with Compute" — Graham Wihlidal, GDC 2016** — how far you can push GPU-side work.
- **AMD GPUOpen and NVIDIA developer blogs** — performance guides written by the people who build the counters.
- **Nathan Gitter / "Understanding the roofline model"** and the original Williams et al. paper — for peak-percentage reasoning.
- **Minecraft's "cave culling" write-ups** (Tommaso Checchi's talk / community analyses) — the voxel-specific occlusion technique.

---

**Next:** [Module 10 — Engine Architecture](./10-engine-architecture.md)
