# Module 08 — Voxel Rendering Techniques

### The two families — meshing and ray tracing — how each actually works, and how modern engines combine them

*~35 min read · Part III: Voxels & Performance · Prerequisites: Modules 04–07*

---

## Read this first

**This is the heart of the job.** If you only deeply learn one module, learn this one.

There are exactly two ways to get voxels on screen, and they are opposites.

**Meshing** converts voxels into triangles and feeds the rasterizer (Module 04). You use the hardware the way it was designed to be used. Cost scales with **surface area** and with **how often the world changes**.

**Ray tracing** skips triangles entirely. For each pixel, march a ray through the voxel structure until it hits something. Cost scales with **screen resolution** and **ray length**, and is almost independent of world complexity.

| | Meshing | Ray tracing |
|---|---|---|
| Cost scales with | Surface area, edit frequency | Resolution, ray length |
| Editing a voxel | Re-mesh the chunk | Change one texel. Done. |
| Hardware used | Rasterizer (Module 04) | Compute / fragment ALU |
| Early-Z, MSAA | Yes | No (you write depth) |
| Shadows | Shadow maps | March a ray. Exact. |
| Sub-voxel effects | No | Yes |
| Tiny voxels (≈pixel size) | Falls apart (quad overdraw) | Fine |
| Tooling maturity | Excellent | You write it |

Everything else — and every shipped voxel game — is a point on the spectrum between them or a hybrid of both.

---

# Part 1: Meshing

## The naive approach, and why it fails

Draw a cube per solid voxel.

A 32³ chunk that's half full is 16,384 cubes × 12 triangles = **~200,000 triangles** for a region you could represent with a few hundred. Multiply by a few hundred visible chunks and you're at 60 million triangles per frame before you've drawn anything else. You are finished before you start.

This is the measurement you took in Module 05, step 3. Here's how you beat it by 1000×.

## Step 1: cull hidden faces

**Only emit a face if the neighbouring voxel in that direction is air.**

```ts
for each solid voxel v at (x,y,z):
  if (!isSolid(x+1,y,z)) emitFace(+X);
  if (!isSolid(x-1,y,z)) emitFace(-X);
  if (!isSolid(x,y+1,z)) emitFace(+Y);
  ... etc for all 6
```

This alone typically removes **90–95%** of geometry, because the interior of solid rock contributes nothing — a face between two stone voxels can never be seen. Our half-full 32³ chunk drops from 200,000 triangles to roughly 10,000.

### The chunk boundary problem

To decide whether a face on the *edge* of chunk A is visible, you need to read a voxel in chunk B. Two options:

**Pass in neighbour references.** The mesher takes the chunk plus its 6 (or 26, for AO) neighbours and does bounds checks on every lookup. Saves memory, costs branches in the innermost loop, and creates a dependency — you can't mesh a chunk until its neighbours exist.

**Store a one-voxel halo (also called an apron or margin).** Each chunk keeps a copy of the shell of voxels immediately around it, so a 32³ chunk is stored as 34³. Every lookup is unconditional and in-bounds.

```
Memory cost: 34³ / 32³ = 1.20  →  ~20% more
Benefit:     branch-free meshing, no cross-chunk dependency, trivially parallel
```

**Most engines take the halo.** 20% memory for branch-free inner loops and independent parallel meshing is an easy trade, and it's exactly the kind of "spend memory to buy predictability" decision this field makes constantly.

## Step 2: greedy meshing

After face culling you have a lot of **coplanar unit quads** — a flat stone floor is 1,024 separate 1×1 quads all facing up, all the same material.

**Greedy meshing** merges adjacent quads with identical attributes into large rectangles. That 32×32 floor becomes **one quad**.

### The algorithm (Lysenko, 2012)

Work one axis at a time, one slice at a time:

1. For slice `z` along the +Z direction, build a 2D **mask**: for each (x, y), what face (if any) is visible here, and what are its attributes (material, AO, light)? Empty where there's no face.
2. Scan the mask for the first non-empty cell (top-left order).
3. **Extend right** as far as possible while cells match.
4. **Extend down** as far as possible while every cell in the candidate row matches.
5. Emit one quad covering that rectangle.
6. **Clear** that region of the mask.
7. Repeat from step 2 until the mask is empty.

```
Mask (S = stone face, D = dirt face, . = nothing):

S S S S D D          Pass 1: find (0,0), extend right to x=3
S S S S D D                  extend down to y=2 (row 3 differs)
S S S S . .                  emit 4×3 stone quad, clear it
. . . . . .
                     Pass 2: emit 2×2 dirt quad
                     Result: 2 quads instead of 18
```

**Typical reduction: another 5–10×** on top of face culling. Our chunk is now hundreds of triangles instead of 200,000.

### The catch

Merging requires **identical** attributes. If per-vertex ambient occlusion differs between two adjacent quads, they can't merge — and AO differs constantly, because it depends on the surrounding geometry.

**So aggressive AO and aggressive merging fight each other**, and you tune the balance. (One common compromise: merge only where AO is uniform, which naturally means large flat regions merge and detailed corners don't — which is exactly where you want the geometry anyway.)

## Step 3: binary greedy meshing

The modern version replaces per-cell comparisons with **bitwise operations on 64-bit integers**. This is recent, well-documented, directly relevant to this job, and one of the highest-value things in this course to be able to explain.

### The representation

Represent a 64³ chunk's occupancy as a 64×64 array of `u64` — one bit per voxel along one axis. So `columns[y][z]` is a 64-bit integer whose bit `x` says whether voxel (x,y,z) is solid.

### The trick

**Face visibility for an entire 64-voxel column is one instruction:**

```
solid    = 0b00111100   (voxels 2..5 are solid)
solid>>1 = 0b00011110
~(solid>>1)             = 0b11100001
solid & ~(solid >> 1)   = 0b00100000   ← only bit 5: the face at the +X end
```

In plain English: *a voxel has a visible +X face if it is solid **and** the voxel above it is not.* Shifting the whole column by one and ANDing computes that for all 64 voxels simultaneously.

Flip the shift direction for the −X face. Do it per axis. **One instruction culls 64 faces.**

**Greedy merging** then proceeds with bit operations too: find runs of set bits using `countTrailingZeros` to find the start of a run and `countTrailingZeros(~x)` to find its end, build a row mask, and AND it against subsequent rows to test whether the rectangle can extend downward — merging 64 faces at a time instead of one.

### Why it matters

Published implementations mesh a chunk in roughly **50–200 µs single-threaded.**

That number is the entire point. **It is fast enough to re-mesh a chunk during gameplay without a visible hitch** (Module 01: your whole frame is 16,670 µs), which is the requirement for a destructible world. It's what turns meshing from "static worlds only" into a viable choice for the game you're being interviewed about.

Knowing this technique by name and being able to explain the `solid & ~(solid >> 1)` trick on a whiteboard is a strong, specific signal.

> **JavaScript note:** JS has `BigInt` (arbitrary precision, heap-allocated, slow) and `BigUint64Array` (fixed 64-bit, but element access still boxes into `BigInt`). Neither is fast. **In practice you split each 64-bit column into two `u32` halves in a `Uint32Array`** and do the shifts with carry by hand — uglier, and typically several times faster. This is a genuinely interesting V8 exercise and Module 13 revisits it.

## Step 4: vertex packing and per-vertex AO

### Packing

Pack a voxel vertex into a single `u32` (Module 04's design exercise, now implemented):

```
bits  0–5  : x within chunk (0–63)
bits  6–11 : y
bits 12–17 : z
bits 18–20 : face normal index (0–5)
bits 21–22 : ambient occlusion level (0–3)
bits 23–31 : material / texture layer id (0–511)
```

Four bytes per vertex, down from 36. Unpack in the vertex shader with shifts and masks — free ALU on the right side of the roofline (Module 03).

```wgsl
@vertex
fn vs(@location(0) packed: u32) -> VSOut {
  let x   = f32( packed        & 63u);
  let y   = f32((packed >>  6u) & 63u);
  let z   = f32((packed >> 12u) & 63u);
  let face =    (packed >> 18u) &  7u;
  let ao  = f32((packed >> 21u) &  3u) / 3.0;
  let mat =     (packed >> 23u) & 511u;
  // ...
}
```

### Per-vertex ambient occlusion

**This is the technique that makes voxel scenes look solid rather than flat**, and it's essentially free.

Each vertex of a quad sits at a corner shared by up to 8 voxels. Of those, three are relevant to how occluded that corner is: the two **edge** neighbours (side1, side2) and the **corner** neighbour.

```
Looking at one corner of a face, from outside:

   corner   side2
      ┌───┬───┐
      │   │   │
      ├───┼───┤
   side1   [vertex is here]
```

```ts
function vertexAO(side1: boolean, side2: boolean, corner: boolean): 0|1|2|3 {
  if (side1 && side2) return 0;                    // fully occluded — both sides block
  return 3 - (Number(side1) + Number(side2) + Number(corner));
}
```

That gives 0–3, packed into 2 bits. **The rasterizer interpolates it across the quad for free** (Module 04's varyings), and you multiply it into the lighting in the fragment shader.

The `if (side1 && side2)` special case is important: when both edge neighbours are solid, the corner is completely enclosed regardless of what's diagonally behind it, so it should be maximally dark. Without that case, an inside corner looks wrong.

### The diagonal-flip fix

One subtlety with real visual consequence. A quad is drawn as two triangles, split along one of its two diagonals. When the quad's AO values are **asymmetric** across that diagonal, the interpolation produces a visible incorrect gradient — a diagonal seam that looks like a rendering bug.

The fix, three lines:

```ts
// Flip the triangulation to run along the axis of LESS contrast.
if (ao00 + ao11 > ao01 + ao10) {
  emitIndices(0, 1, 2, 0, 2, 3);   // default split
} else {
  emitIndices(1, 2, 3, 1, 3, 0);   // flipped split
}
```

**This is the "anisotropy fix" you'll see in every good voxel mesher.** It's in Lysenko's original article and it's a nice small detail to mention — it shows you've read the primary source, not a tutorial's summary of it.

### Why AO is the best deal in voxel rendering

AO computed at mesh time costs **zero** at render time — no SSAO pass, no depth sampling, no noise, no temporal instability — and looks better than SSAO for this content because it's *exact* rather than a screen-space approximation.

**It is the highest quality-per-cost technique in voxel rendering.** Module 06 made the general point; this is the implementation.

## Where meshing runs out

- **Rebuild cost on edits.** Fast, but not free. An explosion touching 50 chunks means 50 rebuilds plus 50 GPU uploads, potentially in one frame. You amortize (Module 07) but you can't make it free.
- **Geometry scales with surface area.** Detailed, noisy, or eroded worlds mean enormous triangle counts even after greedy merging, because greedy merging only helps where surfaces are *flat and uniform*.
- **Sub-voxel detail is impossible.** You're locked to the grid. No smooth surfaces, no partial voxels, no volumetric effects inside the geometry.
- **Small voxels are pathological.** As voxels approach pixel size you hit Module 04's 2×2 quad overdraw problem: every quad is sub-pixel, every fragment shader invocation wastes 75% of its lanes, and rasterization efficiency collapses. There is no fix within the rasterization model.

**That last point is why studios chasing very high voxel resolution abandon meshing.** It's not a tuning problem; it's a property of how the hardware generates fragments.

---

# Part 2: Ray tracing

## DDA: the core algorithm

**DDA** stands for Digital Differential Analyzer — a name inherited from line-drawing algorithms. In this context it means the **Amanatides–Woo voxel traversal algorithm** (1987), which walks a ray through a uniform grid, visiting **every cell it passes through, in order**, with a few adds and comparisons per step.

**This is the single highest-value hour in the course.** It's forty lines, it's the heart of the job, and implementing it will change how you think about the whole problem.

### The idea

Think of the grid as three sets of parallel planes — the x-planes, the y-planes, and the z-planes. A ray crosses each set at regular intervals. Every time it crosses *any* plane, it enters a new cell.

So: track, for each axis, the parametric distance `tMax` at which the ray will cross the **next** plane on that axis, plus the constant increment `tDelta` between consecutive planes on that axis. Each step, **advance along whichever axis has the smallest `tMax`** — that's the next plane the ray will hit.

```
Ray →  ·······╱·······
              ╱
    ─────────╱─────────  y-plane at tMax.y = 1.7
            ╱
    ───┬───╱┬───┬───     x-planes at tMax.x = 0.9, then 2.2, ...
       │  ╱ │   │
              ↑ smallest tMax wins → step in x, then recompute
```

### The code

```wgsl
struct Hit { hit: bool, cell: vec3i, normal: vec3i, value: u32, t: f32 };

fn traverse(origin: vec3f, dir: vec3f, maxSteps: u32) -> Hit {
  var cell   = vec3i(floor(origin));          // which cell are we in now
  let step   = vec3i(sign(dir));              // +1 or -1 per axis
  let tDelta = abs(1.0 / dir);                // distance between planes, per axis
                                              //  (inf when dir component is 0 — correct)

  // Distance to the FIRST plane crossing on each axis.
  var tMax = (vec3f(cell) + max(vec3f(step), vec3f(0.0)) - origin) / dir;

  var normal = vec3i(0);
  var t = 0.0;

  for (var i = 0u; i < maxSteps; i++) {
    let v = sampleVoxel(cell);
    if (v != 0u) { return Hit(true, cell, normal, v, t); }

    // Advance along the axis whose next plane is closest.
    if (tMax.x < tMax.y && tMax.x < tMax.z) {
      t = tMax.x;  cell.x += step.x;  tMax.x += tDelta.x;  normal = vec3i(-step.x, 0, 0);
    } else if (tMax.y < tMax.z) {
      t = tMax.y;  cell.y += step.y;  tMax.y += tDelta.y;  normal = vec3i(0, -step.y, 0);
    } else {
      t = tMax.z;  cell.z += step.z;  tMax.z += tDelta.z;  normal = vec3i(0, 0, -step.z);
    }
  }
  return Hit(false, cell, normal, 0u, t);
}
```

### What falls out for free

This is why the algorithm is beautiful, and what to point out in an interview:

- **The exact surface normal** — it's the axis you last stepped along, negated. No normal buffers, no per-vertex normals, no interpolation, no normalization.
- **The exact hit distance** — `t` at the moment of the hit. No depth reconstruction.
- **The exact voxel coordinate** — for material lookup, for editing, for picking.
- **No acceleration structure at all.** A uniform grid *is* its own acceleration structure. This is the reason WebGPU's lack of hardware ray tracing (Module 05) doesn't hurt voxels the way it would hurt triangle scenes.

### Two details that matter in practice

1. **Precompute `1/dir` once** (Module 02) — division is 10–40× a multiply and you're in a tight loop.
2. **Handle `dir` components of exactly zero.** `1.0/0.0` is `Infinity` in IEEE-754, and `tMax` for that axis becomes infinite, so that axis never wins the comparison. **That's the correct behavior** — a ray parallel to an axis never crosses that axis's planes. Don't add a special case; the float math already handles it. (Do check for `NaN` if `dir` could be all zeros.)

## Hierarchical DDA: the actual technique

Plain DDA visits **every** cell along the ray. At 1024 voxels of view distance that's up to 1024 steps per pixel × 2 million pixels = 2 billion steps per frame. Hopeless.

The fix is to run **nested DDA loops over the brickmap from Module 07**:

1. **Coarse DDA over the top-level index grid**, where each cell spans 8³ voxels. Empty cells are skipped in one step, covering 8 voxels of distance per iteration.
2. When the coarse loop enters a **non-empty** cell, run a **fine DDA inside that brick only** — at most 8 steps per axis, in a small volume that's cache-resident.
3. **Optionally test the brick's 64-byte occupancy bitmask first** to reject bricks that are allocated but empty along this particular ray. Bit tests are ALU, not memory.
4. Exit the brick, resume the coarse loop where it left off.

```
Flat DDA over 1024 voxels of empty air:     1024 steps
Two-level over the same distance:            128 coarse steps + a few fine
                                             ≈ 10× fewer memory accesses
```

This is often called **"MultiDDA."** For primary rays it performs remarkably well relative to its simplicity. **Add a third level for very large worlds** and the coarse loop gets cheaper again.

For octrees and 64-trees, the analogous technique is a **stack-based descend/ascend traversal** — descend while nodes are occupied, ascend when you exit one. The tradeoffs are as described in Module 07: fewer steps, but more dependent reads and higher register pressure, which costs occupancy.

## Beam optimization

A classic trick worth knowing by name.

Render a **low-resolution depth prepass** — say one ray per 8×8 pixel block, conservatively taking the *nearest* hit within that block's frustum — then start each full-resolution ray at that depth instead of at the camera.

You skip most of the empty space between the camera and the first surface, for 1/64th of the ray cost. Laine & Karras used this in the ESVO paper and it remains effective. It composes with hierarchical DDA rather than replacing it.

## What ray tracing buys you

- **Perfect hard shadows** — march toward the light; if you hit anything, you're in shadow. **No shadow maps, no cascades, no bias, no acne, no peter-panning, no resolution artifacts.** (Module 06 flagged this; here's why it's easy — you already wrote the traversal.)
- **Reflections and refraction** — just more rays, from the hit point.
- **Ambient occlusion and GI** — cast short rays into the hemisphere above the hit; count how many escape.
- **Instant edits.** Change a voxel, change one texel in the brick pool. **No re-meshing at all.** For a destructible game **this is the single biggest architectural advantage** and it's worth saying first when asked.
- **Cost independent of scene complexity.** A world with a billion voxels costs the same as one with a million, at the same view distance and resolution.
- **Sub-voxel effects**, volumetrics, participating media, and smooth density fields come naturally.

## What it costs

- **Cost scales with resolution.** 4K is exactly 4× the work of 1080p, always, with no content-dependent escape hatch.
- **Incoherent rays are slow.** Primary rays are coherent — neighbouring pixels take nearly parallel paths through the same bricks (Module 03). **Bounce rays go everywhere**, so subgroups diverge, cache hit rates collapse, and a secondary ray can cost 5–20× a primary one. This is why real-time GI means low sample counts plus heavy denoising, everywhere in the industry.
- **Noise.** Any stochastic sampling produces noise, which means **temporal accumulation** (reprojecting last frame's result using motion vectors) and a **denoiser** (spatiotemporal filtering, e.g. SVGF). That is a substantial subsystem — budget weeks, not days.
- **You lose Early-Z and MSAA** in the classic sense, since you're writing depth from a shader (Module 04).
- **No hardware ray tracing in WebGPU.** All of this is your code. For uniform grids that's acceptable — DDA is cheap and regular and needs no BVH — but there is no falling back on the driver's optimized traversal.

---

# Part 3: Hybrids, and what shipped games do

Real engines mix.

**Teardown** — a fully destructible voxel game — ray traces the voxel scene in a compute shader against a two-level grid, and combines it with a rasterized pass for non-voxel content, plus temporal accumulation and denoising. **Dennis Gustafsson's public write-ups on it are the most valuable single source in this space**, and citing them by name in an interview is a good move.

**Minecraft-lineage engines** mesh everything, use per-vertex AO plus a flood-filled light propagation grid, and lean entirely on the rasterizer. Proven at enormous scale on weak hardware.

**Aokana** and similar recent research frameworks are GPU-driven voxel renderers combining hierarchical structures with compute-based culling for open worlds.

### A pragmatic hybrid that fits WebGPU well

1. **Raster pass** for meshed static chunks — cheap, benefits from Early-Z and MSAA, establishes depth.
2. **Compute ray-march pass** for what meshing can't do: shadows, reflections, AO, volumetrics — **reading the depth buffer to start rays at the surface** rather than the camera (which is beam optimization for free).
3. **Forward/additive pass** for particles and VFX (Module 14).
4. **Post** — tonemap, dither, AA (Module 06).

Or the inverse, if the game's look demands it: ray trace primary visibility, rasterize only characters, particles, and UI.

### How you choose

**This is exactly the kind of judgment the Engine JD is describing** when it warns that *"simply copying common industry engine patterns won't always work."* Be ready to walk through the inputs:

| Input | Pushes toward |
|---|---|
| Heavy, constant destruction | **Ray tracing** — no re-meshing |
| Sub-decimeter voxel resolution | **Ray tracing** — meshing hits quad overdraw |
| High target resolution / weak GPUs | **Meshing** — RT cost is resolution-driven |
| Static or slowly-changing world | **Meshing** — pay once |
| Art direction wants reflections, volumetrics, soft GI | **Ray tracing** |
| Art direction is crisp, flat, stylized | **Meshing** + baked AO, at a fraction of the cost |
| Small team, need shipping tooling | **Meshing** — the raster ecosystem is mature |

---

## Voxel-specific lighting notes

**Flood-fill light propagation** — the Minecraft approach. Store a light level (0–15) per voxel; when a light source is placed or removed, run a **breadth-first search** through the grid, decrementing the level by 1 per step and stopping at solid voxels or level 0.

Cheap, stable, **editable incrementally** (only the affected region needs re-propagating), and it gives the characteristic look of light spilling around corners. Runs on the CPU/worker at edit time and costs nothing at render time. It's a completely reasonable choice and it scales to enormous worlds — don't dismiss it as primitive.

**Voxel cone tracing** approximates global illumination by marching **cones** — progressively wider steps that sample progressively coarser mip levels of the voxel structure — instead of many individual rays. One cone approximates thousands of rays. Cheaper than path tracing, softer than exact.

It was a big idea in the 2010s and it still fits voxel data naturally, because **the mip hierarchy you already have *is* the cone-tracing structure**. If you built LOD bricks in Module 07, you're most of the way there.

**Temporal accumulation is not optional** for any stochastic technique. You need:
- **Motion vectors** — for each pixel, where was this surface last frame? (Reproject its world position through last frame's view-projection matrix.)
- **A history buffer** — last frame's accumulated result.
- **Neighborhood clamping** — reject history that's too different from the current frame's local neighborhood, or you get smearing ghosts trailing every moving object.

Budget real time for this. **It is where "looks noisy" becomes "looks shipped,"** and it's often as much work as the ray tracing itself.

---

## Common confusions

**"Ray tracing is slower than rasterization."** For *triangle* scenes, usually. For voxel scenes at high voxel density, ray tracing wins, because rasterization cost scales with surface geometry and voxel surfaces are enormous. The crossover point is a real, measurable thing and finding it for your content is the job.

**"Greedy meshing means my chunk is one quad."** Only for perfectly flat, uniform regions. Real terrain with AO variation, multiple materials, and noise merges far less. Expect 5–10× on realistic content, not 1000×.

**"I'll ray trace, so I don't need chunks."** You still need chunks for streaming, generation, editing, and the coarse level of your hierarchical DDA. The brickmap *is* chunking, wearing a different hat.

**"DDA is O(1) per step, so it's cheap."** Each step is a memory read of a voxel, and memory is the expensive part (Module 03). The step *arithmetic* is trivial; the step *count* and the *cache behavior of the reads* are what you're optimizing. Which is exactly why hierarchical traversal and occupancy bitmasks matter.

**"Secondary rays cost the same as primary rays."** They're often 5–20× worse because of divergence and cache misses. Any budget that assumes uniform ray cost will be wrong.

**"AO from meshing is a hack; SSAO is the real thing."** SSAO is a screen-space *approximation* that's noisy, view-dependent, and temporally unstable. Voxel per-vertex AO is computed from the actual geometry and is exact. The meshing version is the *better* technique here, not the cheaper compromise.

---

## The interview answer

***"Would you mesh or ray trace voxels?"***

> "Depends on destruction, voxel scale, and art direction.
>
> Meshing with binary greedy meshing plus per-vertex AO is extremely fast — chunk rebuilds in tens of microseconds, so you can re-mesh during gameplay — and you get Early-Z, MSAA, and all the normal raster tooling. It falls over when voxels approach pixel size, because you hit quad overdraw and rasterization efficiency collapses, or when large-scale destruction means constant re-meshing across many chunks.
>
> Ray tracing with a hierarchical DDA over a brickmap gives you exact normals and distances for free, hard shadows by marching toward the light with no bias or cascades, instant edits with no re-meshing at all, and cost that scales with resolution rather than scene complexity. What you pay for is incoherent secondary rays — divergence and cache misses make a bounce ray many times more expensive than a primary — and you're signing up for temporal accumulation and denoising as a real subsystem.
>
> In WebGPU there's no hardware RT, so all traversal is my code — which is fine for uniform grids, since a grid is its own acceleration structure and DDA is cheap and regular.
>
> Honestly I'd prototype both against the actual art direction and measure. I'd expect to end up hybrid: raster for primary visibility since it's cheap and gives me depth, then a compute ray-march pass for shadows and reflections starting from that depth buffer."

That answer is long on purpose — this is the question you want them to ask, and the one where depth pays.

---

## Exercise — Voxelforge, Stage 8

**This is the most valuable stage in the course.** Budget real time for it.

**1. Implement greedy meshing** on top of your Stage 5 face-culled mesher. **Report the triangle count reduction** for a typical terrain chunk.

**2. Implement binary greedy meshing** with 64-bit masks. In JS, try `BigUint64Array` *and* the split-into-two-`u32` approach and **benchmark both** — the result will surprise you and it's a preview of Module 13. Time it per chunk and compare to step 1.

**3. Add per-vertex AO** and the diagonal-flip triangulation fix. **Screenshot with and without.** The difference will surprise you more than the triangle counts did.

**⭐ 4. Write a flat DDA compute shader** that ray-marches your brick pool, writing color and depth. Compare its frame time to the meshed path at 1080p. **This is the forty lines that matter most.**

**⭐ 5. Upgrade to hierarchical DDA** over the index grid with occupancy-bitmask rejection. Measure the step count reduction per pixel by **writing step count to a debug output and visualizing it as a heatmap** — red where you're taking hundreds of steps, blue where you're taking few.

**That heatmap is the single most useful debug view you will build.** It shows you instantly where your traversal is wasting time, it's how you'll validate every future optimization, and it's a great screenshot for your README.

**6. Add ray-marched hard shadows** from the sun in the same shader — march from the hit point toward the light. Compare quality and cost to your Module 06 shadow map. Write down which you'd ship.

**7. Optional: add a beam-optimization prepass** at 1/8 resolution and measure again.

---

## Go deeper

- **Amanatides & Woo, "A Fast Voxel Traversal Algorithm for Ray Tracing" (1987)** — four pages, and you will implement it directly from them. Read the actual paper; it's shorter than most blog posts about it.
- **Mikola Lysenko, "Meshing in a Minecraft Game" (0fps.net, 2012)**, parts 1 and 2 — greedy meshing and the AO discussion, including the diagonal-flip fix. Still the canonical reference after 14 years.
- **`cgerikj/binary-greedy-meshing`** on GitHub — the modern bitwise mesher, with benchmarks. Read the source; it's short.
- **Dennis Gustafsson's Teardown write-ups** (blog.voxagon.se) and his GDC talk recordings — shipped, destructible, ray-traced voxels from someone who did it. **Highest signal per minute in this module.**
- **Laine & Karras, ESVO (2010)** — for the traversal and the beam optimization.
- **"A guide to fast voxel ray tracing using sparse 64-trees"** — dubiousconst282.github.io. Current, benchmarked.
- **SVGF (Schied et al., 2017), "Spatiotemporal Variance-Guided Filtering"** — the standard real-time denoiser. Read it once you have noise to fix, not before.
- **Sebastian Lague's voxel and ray-marching videos** — not a professional reference, but excellent for building intuition fast if any of the above doesn't click.

---

**Next:** [Module 09 — GPU Performance Engineering](./09-gpu-performance.md)
