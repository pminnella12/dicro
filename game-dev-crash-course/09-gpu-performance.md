# Module 09 — GPU Performance Engineering

### How to find out what is actually slow, and the ladder of techniques that fix each cause

*~32 min read · Part III: Voxels & Performance · Prerequisites: Modules 03–08*

---

## Read this first

The Engine JD's longest bullet is this one:

> *"peak-performance-percentage analysis, draw call optimizations, material batching, indirect draw, culling, cache friendliness, bindless, occupancy, latency hiding, overdraw, overlapping pipelines, ALU vs memory bandwidth tradeoffs, async compute, PIX, RenderDoc, Nvidia Nsight/AMD Radeon tools."*

That list is a job description for a **performance engineer who happens to work on graphics**. This module maps it, term by term.

The organizing principle first, because it reframes everything:

> **Optimization is a diagnosis problem, not a coding problem.** The skill is not knowing techniques — it's determining, with evidence, which of six possible bottlenecks you have. Applying the right technique to the wrong bottleneck produces zero improvement and a week of lost time.

You already know this instinct from backend work: you don't add an index before you've looked at the query plan. Graphics is the same discipline with different instruments, and the instruments are less familiar, which is why people skip straight to guessing.

**A useful frame from your existing experience:** Amdahl's law. If a pass is 20% of your frame and you make it infinitely fast, you gained 20%. Always know what fraction of the frame you're attacking *before* you attack it. Half of graphics optimization work in the wild is spent making a 2 ms pass into a 1 ms pass on a 30 ms frame.

---

## Step 1: are you CPU-bound or GPU-bound?

**Everything starts here**, and getting it wrong is the most common failure mode in graphics performance work. Module 01 established that the CPU and GPU run in parallel and you're limited by the slower one; this is how you find out which.

### The two five-minute tests

**Test 1 — drop the resolution to 1/4.** Render at 480p and upscale.

| Result | Meaning |
|---|---|
| Frame time barely changes | The GPU was **not** the bottleneck — you're **CPU-bound** |
| Frame time drops roughly proportionally | You're **GPU-bound** on something resolution-dependent (fragment shading, bandwidth, overdraw) |
| Frame time drops a little | Mixed, or GPU-bound on something resolution-*independent* (vertex work, culling) |

**Test 2 — remove half the objects, keep the resolution.** If frame time halves, you're bound by **per-object** work: draw calls, culling, vertex processing.

These two tests take ten minutes and they eliminate most of the search space. Do them before opening any tool.

### Then get real numbers

**CPU time** — `performance.now()` around your frame, plus the Chrome DevTools Performance panel. In the flame chart, look for:
- **GC bars** (yellow in the Memory track) correlating with frame spikes
- Time spent in worker message handling (structured clone cost)
- Which of *your* systems dominate — name your functions so the flame chart is readable

**GPU time** — WebGPU's **timestamp queries**. This is the instrument you cannot work without, so here's the whole thing:

```ts
// 1. Request the feature at device creation (it may not be available!)
const device = await adapter.requestDevice({ requiredFeatures: ['timestamp-query'] });

// 2. Create a query set and the buffers to get results back
const querySet = device.createQuerySet({ type: 'timestamp', count: 2 * MAX_PASSES });
const resolveBuf = device.createBuffer({
  size: 8 * 2 * MAX_PASSES,               // 8 bytes per timestamp (u64 nanoseconds)
  usage: GPUBufferUsage.QUERY_RESOLVE | GPUBufferUsage.COPY_SRC,
});
// A POOL of readback buffers — you need one per frame in flight (Module 01)
const readbackPool = [ /* 3 buffers with MAP_READ | COPY_DST */ ];

// 3. Attach timestamps to a pass
const pass = encoder.beginRenderPass({
  colorAttachments: [...],
  timestampWrites: { querySet, beginningOfPassWriteIndex: 0, endOfPassWriteIndex: 1 },
});

// 4. Resolve and copy, at the end of the frame
encoder.resolveQuerySet(querySet, 0, 2 * passCount, resolveBuf, 0);
const readback = readbackPool[frameIndex % 3];
if (readback.mapState === 'unmapped') {
  encoder.copyBufferToBuffer(resolveBuf, 0, readback, 0, resolveBuf.size);
}

// 5. Read it back LATER — never this frame
readback.mapAsync(GPUMapMode.READ).then(() => {
  const times = new BigUint64Array(readback.getMappedRange());
  const passMs = Number(times[1] - times[0]) / 1_000_000;   // ns → ms
  readback.unmap();
});
```

**Read it back two or three frames later — never the same frame**, or you force a sync point and stall the pipeline (Module 01). The `mapState` check and the buffer pool exist precisely to enforce that.

Two caveats:
- `timestamp-query` is an **optional** feature. Check `adapter.features.has('timestamp-query')` and degrade gracefully; some configurations disable it for fingerprinting reasons.
- Timestamps are quantized (often to ~100 µs) for the same reason. They're excellent for comparing passes, imprecise for micro-benchmarks.

> **Wrap each pass in timestamps and build a per-pass GPU time HUD early.** It is the single highest-value piece of engine infrastructure for performance work, and **you should build it before you need it** — because the moment you need it, you'll be under time pressure and you'll guess instead.

---

## Step 2: which of the six bottlenecks?

If GPU-bound, it's one of these. Each has a distinct **signature** and a distinct **fix**. Learn the signatures; the fixes are lookup-able.

### 1. Draw call / CPU submission bound

- **Symptom:** GPU timeline has gaps; CPU time is dominated by command encoding.
- **Signature:** reducing *object count* helps enormously; reducing *resolution* does nothing.
- **Fix:** batching, instancing, render bundles, sorting to reduce state changes, indirect draw.
- **JS-specific note:** this bites much harder in a JavaScript engine than in C++, because every `setBindGroup`/`draw` crosses the JS↔native boundary. Budget accordingly.

### 2. Vertex bound

- **Symptom:** resolution changes don't matter; triangle count does.
- **Signature:** halving triangle count halves frame time; halving resolution changes nothing.
- **Fix:** LOD, better meshing (Module 08), packed vertex formats, vertex cache ordering.
- Rare in practice — **except in a voxel engine with millions of merged quads**, where it's a genuine possibility. This is one place your domain differs from the general case.

### 3. Fragment / ALU bound

- **Symptom:** scales directly with resolution *and* with shader complexity.
- **Signature:** cutting the shader in half cuts the pass time roughly in half; you're far from peak bandwidth.
- **Fix:** simplify shaders, reduce overdraw, depth prepass, render expensive effects at lower resolution, lower precision (f16).

### 4. Bandwidth bound

- **Symptom:** scales with resolution *and* with texture/buffer sizes; achieved bandwidth is near the device's peak.
- **Signature:** halving your texture resolution helps a lot even though the shader didn't change.
- **Fix:** compression, smaller formats, better locality (Module 03), fewer full-screen passes, merged passes, correct load/store ops.

### 5. Overdraw bound

- **Symptom:** heavy alpha blending, or many opaque layers; scales badly with how much geometry overlaps.
- **Signature:** the overdraw heatmap is on fire; moving the camera so less overlaps helps dramatically.
- **Fix:** front-to-back sorting, depth prepass, fewer/smaller particles, fewer transparent layers.

### 6. Sync / barrier / latency bound

- **Symptom:** neither CPU nor GPU appears busy, yet frame time is high.
- **Signature:** **gaps in the GPU timeline** in a frame capture. This is the one you can only see with the right tool.
- **Fix:** remove readbacks, remove unnecessary barriers, merge passes, restructure dependencies so independent work is available.

### Peak-performance-percentage analysis

The phrase in the JD, decoded. It means **quantifying** the above rather than guessing:

1. Take the measured GPU time for a pass.
2. Compute the bytes it moved (textures read, buffers written, framebuffer traffic) and the FLOPs it performed.
3. Express each as a percentage of the device's theoretical peak (which you looked up in Module 03's exercise).

| Result | Diagnosis |
|---|---|
| 85% of peak bandwidth, 10% of peak ALU | **Bandwidth-bound.** No amount of shader simplification will help. |
| 12% of peak bandwidth, 80% of peak ALU | **Compute-bound.** Cut math, drop precision. |
| 6% of both | **Latency-bound.** You need more parallelism, better access patterns, or higher occupancy. |

**That third row is the interesting one**, and the one people miss — a pass that's nowhere near either limit isn't "fine," it's stalling. This is the roofline model from Module 03 applied as a daily practice rather than a lecture topic.

---

## The tools

### In-browser

| Tool | What it gives you |
|---|---|
| **Chrome DevTools Performance panel** | CPU flame chart, GC, workers, frame timing |
| **WebGPU Inspector** (Chrome extension) | RenderDoc-style frame capture inside DevTools |
| **`chrome://gpu`** | Backend (D3D12/Metal/Vulkan), driver version, blocklist status |
| **Timestamp queries** | Your own per-pass HUD |

**WebGPU Inspector** (`brendan-duncan/webgpu_inspector`) deserves emphasis: it captures a frame, lets you inspect resources, bind groups, and pipeline state per draw, and it can **auto-inject pass timestamp queries**. **It is the best first-line WebGPU debugging tool available today.** Install it before you need it.

### Native, via capture

These attach to Chrome's underlying native API and give you what the browser can't:

- **PIX** (Windows, D3D12) — captures Chrome's WebGPU work when running the D3D12 backend. Full timing breakdown per draw and per pipeline stage. Brandon Jones's toji.dev guides walk through the exact setup, including the required Chrome flags.
- **RenderDoc** — Dawn integrates the RenderDoc API to begin/end captures at frame boundaries. Best-in-class for inspecting *what* was drawn and with what state — you can click a pixel and see every draw that touched it.
- **Nvidia Nsight Graphics / AMD Radeon GPU Profiler** — **hardware counters**: occupancy, cache hit rates, ALU vs memory utilization, per-unit timing, wave occupancy over time. **This is where "peak percentage" numbers actually come from.** Nothing in the browser can tell you your occupancy; these can.
- **WebGPUReconstruct** — records a WebGPU trace and replays it as a native Dawn/wgpu application, so you can point any native profiler at it. Useful when in-browser capture is blocked.

**Learning to read a frame capture** — the list of passes, the draws in each, the state at each draw, the timing bar beside each — is a concrete, learnable skill that shows up in interviews. Practice on a capture of your own Voxelforge frames, so that when someone says "walk me through how you'd use RenderDoc," you're describing something you did rather than something you read about.

### Debug visualizations you should build into the engine

These are worth more than any external tool, because they're always on and they're specific to your engine:

- **Overdraw heatmap** — additive `+1` per fragment, false-colored. Instantly shows you where you're shading the same pixel ten times.
- **Ray step count heatmap** — for the raymarcher (Module 08). The most useful single view in a voxel engine.
- **Wireframe / triangle density** — shows where your mesher is producing garbage.
- **Per-pass GPU time HUD** — always on, always visible.
- **Counters** — draw calls, triangles, chunks submitted vs. chunks in frustum, bind group switches.
- **Detached-camera culling view** — fly the debug camera outside the frustum and *see* what's being submitted. This finds culling bugs nothing else will.

---

## The technique ladder: reducing CPU-side cost

**Batching and instancing.** Merge geometry that shares a pipeline and material. In a voxel engine the merge already happened at meshing time — the remaining question is whether each chunk is its own draw (typical) or whether many chunks share one big vertex buffer with per-chunk offsets (better, and a prerequisite for indirect draw).

**Sort to minimize state changes.** The cost hierarchy from Module 03: render pass > pipeline > bind group > dynamic offset > draw. Sort your draw list by pipeline, then material, then mesh.

The standard implementation is a **`u64` sort key**:

```
[ 8 bits pass | 16 bits pipeline | 16 bits material | 24 bits depth ]
```

Pack it, radix-sort the array of keys (fast, linear, no comparisons), and walk it in order. Every renderer worth the name has one of these. Note the depth bits go *last*, so sorting is primarily by state and only secondarily front-to-back — a deliberate compromise between Early-Z benefit and state change cost.

**Render bundles.** Pre-record draws once and replay them across frames (Module 05). **Because JavaScript's per-call overhead is much higher than C++'s, bundles matter more in WebGPU than the equivalent technique does in native APIs.** Static voxel chunk geometry is the ideal case — rebuild the bundle only when the *set of visible chunks* changes, not when their contents change.

**Dynamic offsets over per-object bind groups.** One big uniform buffer, `setBindGroup` with a byte offset per object. Avoids thousands of bind group allocations per frame (Module 05).

**Don't rebuild what didn't change.** Cache culling results, cache sorted lists, cache bundles, and **invalidate precisely.** The classic failure is a cache that's invalidated by something that changes every frame, so you pay the cache machinery *and* the rebuild.

---

## The technique ladder: culling

**Culling is the cheapest possible optimization — work you never do costs nothing.** It should always be your first move.

**Frustum culling.** Six-plane vs AABB test per chunk (Module 02). Basic and mandatory.

**Distance culling / view distance.** Simple, and the primary knob players will adjust in the settings menu. Make sure it's a real knob and not a constant.

**Backface culling.** Free in hardware (Module 04). For voxel chunks you can go further: given the camera's position relative to a chunk, you can determine **which of the six face directions could possibly be visible** and skip up to half the faces at meshing or draw time. If the camera is above and to the +X side of a chunk, its −Y and −X faces are unreachable.

### Occlusion culling

The big one, and voxel worlds — especially **dungeons** — are the case where it pays most, because you're usually inside a solid volume looking through small openings. The vast majority of the world is behind a wall.

Three approaches worth knowing:

**1. Hardware occlusion queries.** Draw bounding boxes, ask how many pixels passed. Latency-prone (results arrive frames later, so you're always acting on stale data), and **not currently exposed in WebGPU**. Mention it as history.

**2. Hi-Z / depth pyramid, two-pass.** The modern standard:
   - (a) Render objects that were visible **last** frame.
   - (b) Build a mip pyramid of that depth buffer, where each level stores the *max* depth of its 2×2 children (so a lookup answers "what's the furthest thing in this region").
   - (c) In a compute shader, test every object's screen-space bounding box against the appropriate pyramid level, producing a visibility list.
   - (d) Render the newly-visible **false negatives** — things that weren't visible last frame but are now.

   Fully GPU-side, no CPU readback, no stalls. Introduced in Ubisoft's *GPU-Driven Rendering Pipelines* (SIGGRAPH 2015) and now near-universal. Being able to describe those four steps is a strong signal.

**3. Portal / visibility precomputation.** For a dungeon crawler with rooms and corridors, precomputing which regions can see which others is extremely effective and nearly free at runtime. Classic technique (Quake's PVS), undervalued today, and **exactly the sort of purpose-built choice that beats a generic engine feature for a specific game** — which is the judgment the JD asks for explicitly.

### Cave culling: the voxel-specific one

Worth knowing by name because it's the technique that makes underground voxel worlds viable.

Precompute, **per chunk**, which of its 6 faces are connected to which others **through air** — a small flood fill at meshing time producing 15 bits of data (the 15 unordered face pairs). Then, at render time, flood-fill the *visible chunk set* outward from the camera's chunk, only entering a neighbouring chunk through a face that its connectivity data says connects to the face you'd exit through.

If you're in a tunnel, the tunnel's chunks connect +X to −X but not to +Y, so the search never escapes into the rest of the world. **Minecraft uses a version of this**, and it's dramatically effective underground — it's what stops a cave system from rendering the entire world behind it.

For a **voxel dungeon crawler**, this plus portal visibility is likely worth more than every GPU-side optimization in this module combined. Say that in an interview.

---

## The technique ladder: GPU-side

**Depth prepass** (Module 04) — when fragment-bound with high overdraw. Not when vertex-bound.

**Reduce resolution for expensive effects.** Bloom at 1/4, SSAO at 1/2, volumetrics at 1/4 with a bilateral upsample (blur that respects depth edges so things don't leak across silhouettes). Almost always invisible, always a large saving. Bandwidth scales with the *square* of the resolution factor.

**Merge full-screen passes.** Each one is a full framebuffer read + write (Module 06). Combining tonemap + dither + vignette + chromatic aberration into one shader saves three round trips.

**Correct load/store ops.** `clear` instead of `load`; `discard` depth you won't reuse (Module 04). Free, and on tile-based GPUs (Apple Silicon, mobile) it can be a double-digit percentage of the frame.

**Indirect draw.** `drawIndexedIndirect` reads its parameters — index count, instance count, first index, base vertex, first instance — from a GPU buffer:

```ts
pass.drawIndexedIndirect(argBuffer, byteOffset);
```

A compute shader culls and writes those parameters, so the CPU issues the draw **without knowing what will be drawn**. Setting `instanceCount = 0` culls an object entirely, GPU-side, at no cost.

**WebGPU supports single indirect draws today; multi-draw indirect is not yet available** (Module 05), so you can't collapse N objects into one CPU-issued call. Your options: issue one indirect draw per object (still cheap — an `instanceCount` of 0 costs almost nothing) or batch aggressively into fewer, larger indirect draws by merging geometry.

**Bindless** would let a shader index any texture or buffer from GPU-resident data, which is what makes fully GPU-driven material systems work. **Not available in WebGPU yet.** The substitutes:
- **2D texture arrays** — one layer per material. A natural fit for voxels (Module 06).
- **Texture atlases** — with the bleeding caveats.
- **One big storage buffer of material parameters** indexed by ID — works fine for everything that isn't a texture.

Know the term, know why it matters, know the workaround. That trio is the answer.

**Async compute** — overlapping compute work with graphics work to fill idle fixed-function units (Module 03). **WebGPU exposes a single queue, so this is unavailable in the browser today.** The transferable idea is *ordering passes so independent work is available* and avoiding unnecessary barriers that drain the pipeline.

**Occupancy tuning** (Module 03) — reduce register pressure, size workgroups as multiples of the subgroup width, trim workgroup shared memory. **Measure with Nsight/RGP; guessing here is worthless** and frequently counterproductive.

---

## Memory and cache friendliness

The JD lists "cache friendliness" alongside GPU items, and it belongs on both sides.

**GPU side:** Morton/Z-order layouts, 3D textures over linear buffers for 3D access patterns, structure-of-arrays, packed formats, coalesced access within a subgroup. (All Module 03 and 07.)

**CPU side:** the same principles — contiguous typed arrays over object graphs, iterate in memory order, avoid pointer chasing. Module 13 goes deep.

**Allocation is the CPU-side killer in a JS engine.** Not because allocation is slow (it's ~30 ns), but because **GC pauses are unschedulable** (Module 01). Pool everything in hot paths, reuse output objects, pre-size typed arrays. This is the thing that separates a JS engine that stutters from one that doesn't, and it is where your existing expertise becomes a genuine advantage.

---

## A worked diagnosis

You're at 24 ms. Here's the whole sequence, and it's worth reading twice.

**1. Timestamp queries** say GPU = 9 ms, CPU = 23 ms.
→ **CPU-bound.** All shader optimization is off the table. If you'd spent the week optimizing the raymarcher you'd have gained *nothing*.

**2. DevTools flame chart** shows 14 ms inside command encoding, spread across 4,200 `setBindGroup` + `drawIndexed` pairs.
→ **Draw submission bound.**

**3. Fix A: sort by pipeline and material.** State changes fall from 4,200 to 300. → **17 ms.**

**4. Fix B: move per-chunk uniforms into one buffer with dynamic offsets**, dropping 4,200 bind groups to 1. → **13 ms.**

**5. Fix C: cache a render bundle** for the visible static chunk set, rebuilt only when that set changes. → **8 ms CPU, 9 ms GPU.**
→ **Now GPU-bound.** The bottleneck moved, so the diagnosis has to start over.

**6. Re-measure per pass.** The raymarch pass is 6 of the 9 ms. The ray step heatmap shows 300+ steps in open areas.
→ Add hierarchical DDA + occupancy bitmasks (Module 08). → **3 ms.**

**7. Total ~11 ms.** Under budget. Ship it and move on.

> **Notice that steps 3–5 are all CPU work, and every one of them would have been skipped by someone who started by optimizing shaders.**

That discipline — **measure, identify the bound, fix that, re-measure** — is the actual skill. Notice also step 5's lesson: **the bottleneck moves.** Every fix potentially invalidates your diagnosis, so you re-measure after each change, not at the end.

---

## Common confusions

**"I optimized the shader and nothing happened, so the tool must be wrong."** You were CPU-bound. Check first, every time.

**"Frame time went down 2 ms, that's a win."** Only if it was reproducible and attributable. Thermal throttling, background tabs, and browser JIT warm-up all move numbers by more than 2 ms. Measure the same scene, multiple times, after warm-up, and look at the distribution — not one number.

**"Average frame time improved."** Check p99 too (Module 01). An optimization that improves the average and worsens the spikes has made the game feel *worse*.

**"I'll optimize this pass because it's the slowest."** Amdahl: check what fraction of the frame it is. A 3 ms pass on a 30 ms frame caps your gain at 10%.

**"Occupancy is low, I should raise it."** Only if you're latency-bound. A compute-bound shader with high register use may be optimal at 30% occupancy (Module 03).

**"More culling is always better."** Culling costs CPU or GPU time too. Testing 200,000 objects individually to reject 199,000 can cost more than drawing some of them. Hierarchical culling (cull chunks, not voxels) is the answer, and knowing the cull itself has a cost is the maturity marker.

**"WebGPU is too abstracted to profile properly."** Timestamp queries per pass, plus PIX/RenderDoc/Nsight against the native backend, gets you most of the way. What you genuinely lose is fine-grained hardware counters correlated to your WGSL source, which is a real limitation worth naming honestly.

---

## The interview answer

***"The game is at 30 FPS. What do you do?"***

> "First I find out whether I'm CPU- or GPU-bound — drop the resolution and see if frame time moves, and get real numbers from timestamp queries per pass plus a CPU flame chart. Then I identify *which* bound: draw submission, vertex, fragment, bandwidth, overdraw, or sync. I'd want achieved bandwidth and ALU as a percentage of peak so I know which side of the roofline I'm on — and if I'm nowhere near either, that's a latency problem, not a throughput problem.
>
> Then apply the matching fix and re-measure, because the bottleneck moves. I'd be suspicious of any change I can't attribute to a number.
>
> I'd also look at the frame time graph and p99, not the average — a 30 FPS average with spikes is a completely different problem from a steady 30, and the fixes don't overlap."

***"What would you look at first in a voxel renderer specifically?"***

> "Overdraw and ray step counts, via debug heatmaps I'd have built into the engine. Then culling effectiveness — how many chunks am I submitting versus how many are actually visible? In a dungeon, cave-culling or portal visibility usually beats everything else by a wide margin, because you're inside a solid volume looking through small openings and the naive frustum test keeps almost everything."

***"How do you profile WebGPU?"***

> "Timestamp queries per pass with a three-frame-deferred readback for a per-pass HUD, WebGPU Inspector for frame captures in the browser, and PIX or RenderDoc against Chrome's D3D12 backend when I need per-draw timing. Nsight or Radeon GPU Profiler when I need actual hardware counters — occupancy, cache hit rates — because nothing in the browser exposes those."

---

## Exercise — Voxelforge, Stage 9

**1. Add timestamp queries per pass** with a proper 3-frame-deferred readback, and put a **per-pass GPU time HUD** on screen. Handle the case where the feature isn't available.

**2. Add CPU-side counters:** draw calls, triangles, chunks submitted vs chunks in frustum, bind group switches. Display them next to the GPU times.

**3. Build the overdraw heatmap and the ray step heatmap.** Fly around and find your worst-case view. Screenshot it.

**⭐ 4. Deliberately make yourself CPU-bound** — one draw per chunk, one bind group per chunk, unsorted. **Record the numbers.** Then apply, in order:
   - sorting by pipeline/material → record
   - dynamic offsets → record
   - render bundles → record

   **Write the four numbers down.** This exercise teaches more than any amount of reading, and the four numbers go straight into your project README where they're worth more than any description.

**5. Implement frustum culling, then cave/connectivity culling** for chunks. Measure the reduction in submitted chunks **while standing inside a cave system** — that's where the technique earns its keep.

**6. Implement a compute-based cull writing indirect draw args**, with `instanceCount = 0` for culled chunks. Compare against CPU culling on **both** CPU and GPU time — this is a case where the GPU version can lose, and finding out is the point.

**7. Capture a frame with WebGPU Inspector**, and if you're on Windows, with **PIX** or **RenderDoc** via the toji.dev instructions. **Find the most expensive draw in the capture and explain why it's expensive.** Then you can say "yes, I've used RenderDoc on a WebGPU app" and mean it.

---

## Go deeper

- **toji.dev/webgpu-profiling** — Brandon Jones's guides for PIX and RenderDoc with Chrome. **Start here**; it's the exact workflow, including the Chrome flags.
- **WebGPU Inspector** (`brendan-duncan/webgpu_inspector`) — install it today, before you need it.
- **"GPU-Driven Rendering Pipelines" — Ulrich Haar & Sebastian Aaltonen, SIGGRAPH 2015** — the two-pass Hi-Z occlusion culling architecture. Foundational, and still the design everyone uses.
- **vkguide.dev "GPU Driven Rendering"** chapters — the clearest tutorial-level treatment of compute culling + indirect draw anywhere.
- **"Optimizing the Graphics Pipeline with Compute" — Graham Wihlidal, GDC 2016** — how far you can push GPU-side work.
- **AMD GPUOpen and NVIDIA developer blogs** — performance guides written by the people who build the counters.
- **Williams, Waterman & Patterson, "Roofline"** — the original paper, for peak-percentage reasoning.
- **Minecraft's "cave culling" write-ups** (Tommaso Checchi's talk and community analyses) — the voxel-specific occlusion technique, explained by the people who shipped it.

---

**Next:** [Module 10 — Engine Architecture](./10-engine-architecture.md)
