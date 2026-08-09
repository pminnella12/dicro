# Module 04 — The Rasterization Pipeline

### The fixed-function machine your shaders plug into, and every stage where a frame can silently go wrong

*~11 min read · Part II: Rendering · Prerequisites: Modules 01–03*

---

A GPU is not a general-purpose parallel computer that happens to draw. It is a **purpose-built pipeline** with a few programmable slots cut into it. Understanding the fixed-function parts is what lets you predict cost and diagnose "nothing is on screen."

The pipeline, end to end:

```
Index/Vertex fetch → Vertex Shader → Primitive Assembly → Clipping →
Perspective Divide → Viewport Transform → Backface Cull → Rasterization →
Early-Z → Fragment Shader → Late-Z / Stencil → Blending (ROP) → Framebuffer
```

Two of those stages are yours: the **vertex shader** and the **fragment shader**. Everything else is silicon with a small number of knobs.

---

## Vertex fetch and the vertex shader

You supply buffers of vertex data plus a description of their layout: for each attribute, a location, a format (`float32x3`, `unorm8x4`, …), an offset, and a stride. The hardware fetches and converts, then runs your vertex shader **once per vertex**.

Two details with real consequences:

**Vertex formats are compression.** A position doesn't need three 32-bit floats if your voxels live on a 32³ grid — 5 bits per axis suffices. Normals are one of six directions, so 3 bits. Packing a voxel vertex from 32 bytes down to 8 or even 4 is standard practice, and since vertex fetch is bandwidth-bound, it's often a direct speedup. Every serious voxel engine packs vertices into a single `uint32`.

**Indexed drawing enables the post-transform cache.** With an index buffer, vertices shared between triangles are shaded once and reused, if the reuse happens within a small window (~16–32 entries). Ordering your indices for locality is what "vertex cache optimization" means. For voxel meshes made of quads, this is close to free: emit indices per quad in order and you naturally get good reuse.

The vertex shader's contract: consume attributes and uniforms, output a clip-space position (`@builtin(position)`) plus any **varyings** you want interpolated. It cannot see other vertices, cannot create geometry (there are no geometry shaders in WebGPU — a deliberate omission, since they were slow everywhere), and cannot know which triangle it belongs to.

---

## Primitive assembly, clipping, and culling

Vertices are grouped into triangles according to the topology (`triangle-list`, `triangle-strip`, …).

**Clipping** happens in homogeneous clip space, *before* the perspective divide, for the reason covered in Module 02: dividing by a negative `w` maps geometry behind the camera to nonsense. Triangles straddling a frustum plane get split into smaller triangles.

**Backface culling** removes triangles whose winding order (after projection) indicates they face away. You pick a front-face convention (`ccw` or `cw`) and a cull mode. This is a ~2× reduction in fragment work for closed meshes, for free.

It is also **the most common cause of "my mesh is invisible."** The diagnostic is trivial: set `cullMode: 'none'` and see if the geometry appears. If it does, your winding is inverted — usually from a mirrored transform (negative scale flips winding), a coordinate system mismatch during import, or emitting quad indices in the wrong order.

For voxel meshing specifically, you generate faces yourself, so you control winding directly — and you get an extra trick: since you know each face's normal at generation time, you can cull entire faces on the CPU before they ever become vertices.

---

## Rasterization

The rasterizer determines which pixels a triangle covers and generates **fragments** — candidate pixels with interpolated attributes.

Three facts that shape performance:

**Fragments are generated in 2×2 quads.** Always. Even a triangle covering one pixel produces a full quad, with three lanes masked off but still executing. This is not a quirk — it's required, because screen-space derivatives (`dpdx`/`dpdy`), which drive automatic mip level selection, are computed by differencing neighbors within the quad.

The implication is **quad overdraw**: thin or small triangles waste up to 75% of fragment shading. This is precisely why very dense meshes underperform their triangle count, and why techniques like Nanite's software rasterizer exist. For voxels, it's an argument in favor of merged quads over per-voxel cubes.

**Interpolation is perspective-correct.** Attributes are interpolated in a way that accounts for depth, by interpolating `attribute/w` and `1/w` linearly and dividing. Without this, textures on floors visibly warp — the classic PlayStation 1 look, which lacked it.

**Overdraw is the enemy.** If ten surfaces cover the same pixel and you shade all ten, you paid 10× for one visible result. Overdraw is a primary metric you will measure.

---

## Depth testing, and Early-Z

The **depth buffer** stores the closest depth seen so far per pixel; a fragment is kept only if it passes the comparison. That's the mechanism for correct occlusion without sorting.

The critical optimization is **Early-Z** (a.k.a. early depth test): the hardware tests depth *before* running the fragment shader, skipping shading entirely for occluded fragments. Combined with **hierarchical Z (Hi-Z)** — a low-resolution depth pyramid used to reject whole tiles at once — this is one of the largest performance wins in any renderer.

But **you can turn it off by accident.** Early-Z is disabled when the fragment shader:

- writes to `@builtin(frag_depth)`
- uses `discard` (in many cases, since coverage isn't known until the shader runs)
- has side effects like storage buffer/texture writes
- uses alpha-to-coverage

This is why alpha-tested foliage is disproportionately expensive, and why writing custom depth in a raymarching shader has a cost far beyond the instructions you added. If you write depth in a voxel raytracer — which you generally must, to composite with rasterized geometry — you should know you have given up Early-Z for that pass and plan around it.

**Depth prepass** is the standard countermeasure: render all opaque geometry depth-only (no fragment shader, so it's very cheap), then render the real pass with `depthCompare: 'equal'` and depth writes off. Every pixel is now shaded exactly once, at the cost of transforming geometry twice. Worth it when your fragment shaders are expensive; not worth it when you're vertex-bound.

**Ordering still matters even with Early-Z.** Drawing front-to-back lets Early-Z reject the maximum number of fragments. Drawing back-to-front defeats it entirely. Rough front-to-back sorting of opaque objects is standard.

---

## Blending and transparency

Blending happens in the **ROP** (raster operations) stage, fixed-function, after the fragment shader:

```
result = src * srcFactor  ⊕  dst * dstFactor
```

Standard alpha blending is `src * srcAlpha + dst * (1 - srcAlpha)`, and it is **order-dependent**. That single fact creates the entire transparency problem in real-time graphics:

- Opaque geometry: any order (depth buffer handles it), preferably front-to-back.
- Transparent geometry: must be sorted **back-to-front**, per-object, every frame, from the camera. Depth writes off, depth test on.
- Intersecting or concave transparent objects: **cannot be sorted correctly per-object**, period. There is no draw order that is right.

The escape hatches: **alpha testing** (`discard` below a threshold — makes it opaque again, but disables Early-Z and aliases badly), **additive blending** (order-independent because addition commutes — which is why particles, fire, and magic VFX are so often additive), **premultiplied alpha** (correct filtering and compositing; adopt it as the default convention), and **order-independent transparency** techniques like weighted blended OIT (approximate) or per-pixel linked lists (expensive).

For a voxel game this is mostly about water, glass, and effects. A common and pragmatic architecture: opaque voxels in one pass, then a separate sorted pass for transparent voxel types, then additive VFX last.

---

## Render passes, attachments, and why load/store ops matter

A **render pass** binds a set of output attachments (color targets, a depth/stencil target) and runs draws into them. In WebGPU you declare, per attachment, a `loadOp` and a `storeOp`.

This looks like boilerplate. It is not — it's a bandwidth control, and it comes straight from mobile/tiled GPU architectures:

- `loadOp: 'clear'` — start fresh. **Cheap.**
- `loadOp: 'load'` — read the existing contents into tile memory. **Costs a full-framebuffer read.**
- `storeOp: 'store'` — write results to memory.
- `storeOp: 'discard'` — throw them away. Free, and correct for a depth buffer you don't need after the pass.

On tiled/mobile GPUs (and Apple Silicon, which is tile-based), getting these wrong can cost more than your shaders. Always clear rather than load when you're going to overwrite everything, and always discard depth you won't reuse.

**MSAA** (multisample anti-aliasing) also lives here: the rasterizer computes coverage at multiple sample points per pixel but runs the fragment shader **once per pixel** (not per sample), then resolves. So MSAA costs bandwidth and memory, not shading — which makes it cheap for geometric edges and useless for shader aliasing (specular sparkle, alpha-tested foliage). Note that MSAA composes poorly with deferred rendering, which is why most modern engines use temporal antialiasing instead. For a hard-edged voxel game, MSAA is unusually attractive because *all* your aliasing is geometric.

---

## Forward vs deferred, in one page

**Forward rendering:** for each object, for each light, shade. Simple, MSAA-friendly, handles transparency and varied materials naturally. Cost explodes as `objects × lights`.

**Deferred rendering:** first pass writes surface properties (albedo, normal, roughness, depth) into a **G-buffer**; a second pass shades once per pixel, per light, reading the G-buffer. Decouples lighting cost from geometry cost, so hundreds of lights become tractable. Costs: heavy bandwidth (the G-buffer is written and read every frame), awkward transparency (needs a separate forward pass), and no cheap MSAA.

**Forward+ / clustered forward:** split the screen (and depth range) into tiles/clusters, use a compute pass to build a per-cluster list of affecting lights, then forward-shade reading only relevant lights. This is the modern default for most engines — most of deferred's light scalability, most of forward's flexibility.

For a voxel renderer the tradeoffs shift, because if you're **ray tracing** the voxels rather than rasterizing them, you're naturally producing a G-buffer-like result from a compute pass anyway, and the distinction partly dissolves. Understanding all three is still expected; being able to say *why the standard taxonomy doesn't map cleanly onto a voxel raytracer* is better.

---

## Debugging a black screen: the checklist

This will happen to you constantly. Work down the list rather than staring at shader code:

1. **Is anything being submitted?** Check the draw call actually executes and the vertex/index counts aren't zero.
2. **Is the clear color showing?** If not, the problem is pass setup, canvas configuration, or the pass never ran.
3. **Winding/culling** — set `cullMode: 'none'`.
4. **Depth** — is the depth buffer cleared to the right value (1.0 normally, 0.0 for reversed-Z), and is the compare function the matching direction?
5. **Is it off-screen or inside-out?** Output a constant color from the fragment shader; if you see a shape, it's a shading problem, not a geometry problem.
6. **Is it behind the near plane or beyond far?** Print the clip-space position of one known vertex on the CPU using the same matrices.
7. **Matrix convention** — try transposing. If the object appears, you have a row/column-major mismatch.
8. **Is alpha zero?** A fully transparent output over a black clear is indistinguishable from nothing.

Then, and only then, open a GPU debugger. Module 09 covers those tools.

---

## The interview answer

*"What's early-Z and how do you lose it?"*

> "Depth testing before the fragment shader so occluded fragments never get shaded, usually backed by a hierarchical Z pyramid that rejects whole tiles. You lose it if the shader writes depth, uses discard, or has side effects like storage writes — which is why alpha-tested foliage is expensive. A depth prepass gets the benefit back by establishing depth cheaply first and then shading with an equal test."

*"Why is transparency hard?"*

> "Alpha blending isn't commutative, so it's order-dependent. You can sort per-object back-to-front, but intersecting or concave transparent geometry has no correct draw order. Real options are alpha testing, additive blending where the math commutes, or an OIT technique — each with real costs."

---

## Exercise — Voxelforge, Stage 4 (design pass)

You will implement this in Module 05, but decide it now, on paper:

1. Sketch your frame's render passes: what attachments, what load/store ops, what order.
2. Decide your depth convention (standard or reversed-Z) and write down the clear value, compare function, and projection variant that go with it. Getting these three consistent is the entire trick.
3. Design a **packed voxel vertex** that fits in 8 bytes or fewer for a 32³ chunk: position, face normal index, texture/material ID, and a 2-bit ambient occlusion value. Write out the bit layout and the pack/unpack expressions.
4. Decide where transparent voxels (water, glass) go in your pass ordering, and what breaks if two water surfaces intersect.

---

## Go deeper

- **Fabian Giesen, "A trip through the Graphics Pipeline 2011"** — again; parts 6–9 cover rasterization, early-Z, and ROP in exactly this territory.
- **Real-Time Rendering, 4th ed.**, Chapters 2–5 — the reference text for this pipeline. Expensive book, worth it.
- **"Transparency (or Translucency) Rendering" — Nvidia/Cem Yuksel lectures** for the OIT landscape.
- **Nathan Reed, "Depth Precision Visualized"** and **"A Quick Overview of MSAA"** — reedbeta.com.
- **"Optimizing Triangles for a Modern GPU" / Nanite SIGGRAPH 2021 course notes (Brian Karis)** — for why small triangles are pathological.

---

**Next:** [Module 05 — WebGPU and WGSL](./05-webgpu-and-wgsl.md)
