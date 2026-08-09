# Module 08 — Voxel Rendering Techniques

### The two families — meshing and ray tracing — how each actually works, and how modern engines combine them

*~13 min read · Part III: Voxels & Performance · Prerequisites: Modules 04–07*

---

There are exactly two ways to get voxels on screen, and they are opposites.

**Meshing** converts voxels into triangles and feeds the rasterizer. You use the hardware the way it was designed to be used. Cost scales with *surface area* and with how often the world changes.

**Ray tracing** skips triangles entirely. For each pixel, march a ray through the voxel structure until it hits something. Cost scales with *screen resolution* and ray length, and is almost independent of world complexity.

Everything else — and every shipped voxel game — is a point on the spectrum between them or a hybrid of both.

---

# Part 1: Meshing

## The naive approach, and why it fails

Draw a cube per solid voxel. A 32³ chunk that's half full is 16,384 cubes × 12 triangles = ~200,000 triangles for a region you could represent with a few hundred. Multiply by hundreds of chunks and you're finished before you start.

## Step 1: cull hidden faces

Only emit a face if the neighbouring voxel in that direction is air. This alone typically removes **90–95%** of geometry, because the interior of solid rock contributes nothing.

The gotcha is chunk boundaries: to decide whether a face on the edge of chunk A is visible, you need to read a voxel in chunk B. Options are to pass in neighbour references, or to store a one-voxel **halo/apron** around each chunk. The halo costs memory (a 32³ chunk becomes 34³, ~20% more) and buys simple, branch-free meshing code with no cross-chunk dependencies. Most engines take the halo.

## Step 2: greedy meshing

After face culling you have a lot of coplanar unit quads. **Greedy meshing** merges adjacent quads with identical material and lighting into large rectangles: a 32×32 flat stone floor becomes **one quad** instead of 1,024.

The classic algorithm (Mikola Lysenko, 2012) works per axis, per slice: build a 2D mask of visible faces for the slice, then repeatedly find the top-left unmerged cell, extend it as far right as possible, then as far down as possible while every row matches, emit the quad, and clear the mask region.

Typical reduction: **another 5–10×** on top of face culling.

The catch is that merging requires *identical* attributes. If per-vertex ambient occlusion differs, quads can't merge — so aggressive AO and aggressive merging fight each other, and you tune the balance.

## Step 3: binary greedy meshing

The modern version replaces per-cell comparisons with **bitwise operations on 64-bit integers**.

Represent a 64³ chunk's occupancy as a 64×64 array of `u64`, one bit per voxel along an axis. Then:

- **Face visibility** for an entire 64-voxel column is `visible = solid & ~(solid >> 1)` — one instruction culls 64 faces.
- **Greedy merging** proceeds by finding runs of set bits with trailing-zero counts and mask operations, merging 64 faces at a time.

Published implementations mesh a chunk in roughly **50–200 µs single-threaded**. That is fast enough to re-mesh a chunk during gameplay without a visible hitch — which is the entire requirement for a destructible world.

This technique is recent, well-documented, and directly relevant to this job. Knowing it by name and being able to explain the `solid & ~(solid >> 1)` trick is a strong signal.

## Step 4: vertex packing and per-vertex AO

Pack a voxel vertex into a single `u32`:

```
bits 0–5   : x within chunk (0–63)
bits 6–11  : y
bits 12–17 : z
bits 18–20 : face normal index (0–5)
bits 21–22 : ambient occlusion level (0–3)
bits 23–31 : material / texture layer id
```

Four bytes per vertex. Unpack in the vertex shader with shifts and masks — free ALU on the right side of the roofline.

**Per-vertex ambient occlusion** is the technique that makes voxel scenes look solid rather than flat. For each vertex (a corner shared by up to 8 voxels), count how many of the 3 relevant neighbours are solid; that count, 0–3, becomes an AO level that darkens the corner. The rasterizer interpolates it across the quad for free.

One subtlety with real visual consequence: when a quad's two diagonal AO values are asymmetric, the default triangulation produces a visible incorrect gradient. Flipping the quad's triangulation based on which diagonal has more contrast fixes it. This is the "anisotropy fix" you'll see in every good voxel mesher, and it takes three lines.

AO computed at mesh time costs **zero** at render time and looks better than SSAO for this content. It is the highest quality-per-cost technique in voxel rendering.

## Where meshing runs out

- **Rebuild cost on edits.** Fast, but not free, and an explosion touching 50 chunks means 50 rebuilds plus 50 GPU uploads in one frame.
- **Geometry scales with surface area.** Detailed or noisy worlds mean enormous triangle counts.
- **Sub-voxel detail is impossible.** You're locked to the grid.
- **Small voxels are pathological.** As voxels approach pixel size, you hit the 2×2 quad overdraw problem from Module 04 and rasterization efficiency collapses.

That last point is why studios chasing very high voxel resolution abandon meshing.

---

# Part 2: Ray tracing

## DDA: the core algorithm

The **Amanatides–Woo** voxel traversal algorithm (1987) walks a ray through a uniform grid, visiting every cell it passes through, in order, with a few adds and comparisons per step.

The idea: track, for each axis, the parametric distance `tMax` at which the ray crosses the *next* grid plane on that axis, and the constant increment `tDelta` between planes. Each step, advance along whichever axis has the smallest `tMax`.

```wgsl
fn traverse(origin: vec3f, dir: vec3f, maxSteps: u32) -> Hit {
  var cell = vec3i(floor(origin));
  let step = vec3i(sign(dir));
  let tDelta = abs(1.0 / dir);                       // guard against dir == 0
  var tMax = (vec3f(cell) + max(vec3f(step), vec3f(0.0)) - origin) / dir;
  var normal = vec3i(0);

  for (var i = 0u; i < maxSteps; i++) {
    let v = sampleVoxel(cell);
    if (v != 0u) { return Hit(true, cell, normal, v); }

    // advance along the axis with the smallest tMax
    if (tMax.x < tMax.y && tMax.x < tMax.z) {
      cell.x += step.x; tMax.x += tDelta.x; normal = vec3i(-step.x, 0, 0);
    } else if (tMax.y < tMax.z) {
      cell.y += step.y; tMax.y += tDelta.y; normal = vec3i(0, -step.y, 0);
    } else {
      cell.z += step.z; tMax.z += tDelta.z; normal = vec3i(0, 0, -step.z);
    }
  }
  return Hit(false, cell, normal, 0u);
}
```

Note what falls out for free: the **exact surface normal** (the axis you last stepped along), the **exact hit distance** (`min(tMax)` before the step), and the exact voxel coordinate. No normal buffers, no interpolation, no bias.

Two details that matter in practice: precompute `1/dir` once (Module 02), and handle `dir` components of exactly zero by setting `tDelta` to infinity so that axis never wins the comparison.

## Hierarchical DDA: the actual technique

Plain DDA visits every cell along the ray. At 1024 voxels of view distance that's up to 1024 steps per pixel — hopeless.

The fix is to run **nested DDA loops over the brickmap from Module 07**:

1. Coarse DDA over the top-level index grid, where each cell spans 8³ voxels. Empty cells are skipped in one step, covering 8 voxels of distance per iteration.
2. When the coarse loop enters a non-empty cell, run a fine DDA *inside* that brick only.
3. Optionally test the brick's 64-byte occupancy bitmask first to reject bricks that are technically allocated but empty along this ray.
4. Exit the brick, resume the coarse loop.

This is "MultiDDA," and for primary rays it performs remarkably well relative to its simplicity — typically an order of magnitude fewer memory accesses than flat DDA. Add a third level for very large worlds.

For octrees and 64-trees, the analogous technique is a stack-based descend/ascend traversal; the tradeoffs are as described in Module 07 (fewer steps, more dependent reads, higher register pressure).

## Beam optimization

A classic trick worth knowing: render a **low-resolution depth prepass** (say, one ray per 8×8 pixel block, conservatively taking the nearest hit), then start each full-resolution ray at that depth instead of at the camera. You skip most of the empty space for a tiny fraction of the cost. Laine & Karras used this in the ESVO paper and it remains effective.

## What ray tracing buys you

- **Perfect hard shadows** — march toward the light; if you hit anything, you're in shadow. No shadow maps, no cascades, no bias, no acne. (Module 06 flagged this.)
- **Reflections and refraction** — just more rays.
- **Ambient occlusion and GI** — cast short rays into the hemisphere; accumulate.
- **Instant edits.** Change a voxel, change the texture. **No re-meshing at all.** For a destructible game this is the single biggest architectural advantage.
- **Cost independent of scene complexity.** A world with a billion voxels costs the same as one with a million, at the same view distance.
- **Sub-voxel effects**, volumetrics, and participating media come naturally.

## What it costs

- **Cost scales with resolution.** 4K is 4× the work of 1080p, always.
- **Incoherent rays are slow.** Primary rays are coherent (Module 03); bounce rays go everywhere, so subgroups diverge and cache hit rates collapse. This is why real-time GI needs low sample counts plus heavy denoising.
- **Noise.** Any stochastic sampling means noise, which means temporal accumulation (reprojection with motion vectors) and a denoiser (spatiotemporal filtering, à la SVGF). That's a substantial subsystem.
- **You lose Early-Z** and MSAA in the classic sense, since you're writing depth from a shader (Module 04).
- **No hardware ray tracing in WebGPU.** All of this is your code. For uniform grids that's acceptable — DDA is cheap and regular — but there is no falling back on the driver's BVH.

---

# Part 3: Hybrids, and what shipped games do

Real engines mix.

**Teardown** (a fully destructible voxel game) ray traces the voxel scene in a compute shader against a two-level grid, and combines it with a rasterized pass for non-voxel content, plus temporal accumulation and denoising. Dennis Gustafsson's public write-ups on it are the most valuable single source in this space.

**Minecraft-lineage engines** mesh everything, use per-vertex AO plus a flood-filled light propagation grid, and lean on the rasterizer.

**Aokana** and similar recent research frameworks are GPU-driven voxel renderers combining hierarchical structures with compute-based culling for open worlds.

A pragmatic hybrid architecture that fits WebGPU well:

1. **Raster pass** for meshed static chunks — cheap, benefits from Early-Z and MSAA, establishes depth.
2. **Compute ray-march pass** for effects that meshing can't do: shadows, reflections, AO, volumetrics — reading the depth buffer to start rays at the surface.
3. **Forward/additive pass** for particles and VFX.
4. **Post** — tonemap, dither, AA.

Or the inverse, if the game's look demands it: ray trace primary visibility, rasterize only characters and UI.

**How you choose** is exactly the kind of judgment the Engine JD is describing when it warns that *"simply copying common industry engine patterns won't always work."* The inputs to the decision:

- **How destructible is the world?** Heavy destruction pushes hard toward ray tracing (no re-meshing).
- **What voxel resolution?** Sub-decimeter voxels make meshing untenable.
- **What's the target hardware and resolution?** Ray tracing cost is resolution-driven; meshing cost is content-driven.
- **What does the art direction need?** Reflections, volumetrics, and soft GI favor ray tracing. Crisp, flat, stylized surfaces are perfectly served by meshing plus baked AO — at a fraction of the cost.

---

## Voxel-specific lighting notes

**Flood-fill light propagation** — the Minecraft approach — stores a light level per voxel and propagates it with a BFS through the grid, decrementing per step. Cheap, stable, editable incrementally, and gives the characteristic look of light spilling around corners. It runs on the CPU/worker at edit time, and is a completely reasonable choice.

**Voxel cone tracing** approximates GI by marching cones (progressively wider steps, sampling mip levels of the voxel structure) instead of rays. Cheaper than path tracing, softer than exact. It was a big idea in the 2010s and still fits voxel data naturally, since the mip hierarchy you already have *is* the cone-tracing structure.

**Temporal accumulation** is not optional for any stochastic technique. You need motion vectors (reprojecting last frame's position), a history buffer, and neighborhood clamping to reject stale samples. Budget real time for this; it is where "looks noisy" becomes "looks shipped."

---

## The interview answer

*"Would you mesh or ray trace voxels?"*

> "Depends on destruction, voxel scale, and art direction. Meshing with binary greedy meshing plus per-vertex AO is extremely fast — chunk rebuilds in tens of microseconds — and gets you Early-Z, MSAA, and all the normal raster tooling. It falls over when voxels approach pixel size or when large-scale destruction means constant re-meshing. Ray tracing with a hierarchical DDA over a brickmap gives exact normals and distances, free hard shadows, instant edits with no re-meshing, and cost that scales with resolution rather than scene complexity — but you pay for incoherent secondary rays and you're signing up for temporal accumulation and denoising. In WebGPU there's no hardware RT, so all traversal is my code, which is fine for uniform grids. Honestly I'd prototype both against the actual art direction and measure — and I'd expect to end up hybrid: raster for primary visibility, compute ray-march for shadows and reflections."

---

## Exercise — Voxelforge, Stage 8

1. Implement **greedy meshing** on top of your Stage 5 face-culled mesher. Report the triangle count reduction.
2. Implement **binary greedy meshing** with `u64` masks (JS: use `BigUint64Array`, or split into two `u32` halves and benchmark both — this is a genuinely interesting V8 exercise, see Module 13). Time it per chunk.
3. Add **per-vertex AO** and the diagonal-flip triangulation fix. Screenshot with and without; the difference will surprise you.
4. Write a **flat DDA** compute shader that ray-marches your brick pool, writing color and depth. Compare its frame time to the meshed path at 1080p.
5. Upgrade to **hierarchical DDA** over the index grid with occupancy-bitmask rejection. Measure the step count reduction per pixel by writing step count to a debug output and visualizing it as a heatmap — this heatmap is the single most useful debug view you will build.
6. Add **ray-marched hard shadows** from the sun in the same shader. Compare quality and cost to your Module 06 shadow map.
7. Optional: add a beam-optimization prepass at 1/8 resolution and measure again.

---

## Go deeper

- **Amanatides & Woo, "A Fast Voxel Traversal Algorithm for Ray Tracing" (1987)** — four pages, and you will implement it from them directly.
- **Mikola Lysenko, "Meshing in a Minecraft Game" (0fps.net, 2012)** parts 1 and 2 — greedy meshing and the AO discussion. Still the canonical reference.
- **`cgerikj/binary-greedy-meshing`** on GitHub — the modern bitwise mesher, with benchmarks. Read the source.
- **Dennis Gustafsson's Teardown write-ups** (blog.voxagon.se) and his GDC/talk recordings — shipped, destructible, ray-traced voxels. Highest signal per minute in this module.
- **Laine & Karras, ESVO (2010)** — for the traversal and the beam optimization.
- **"A guide to fast voxel ray tracing using sparse 64-trees"** — dubiousconst282.github.io.
- **SVGF (Schied et al., 2017), "Spatiotemporal Variance-Guided Filtering"** — the standard real-time denoiser; read it once you have noise to fix.
- **Sebastian Lague's voxel/ray-marching videos** — not a professional reference, but excellent for building intuition quickly.

---

**Next:** [Module 09 — GPU Performance Engineering](./09-gpu-performance.md)
