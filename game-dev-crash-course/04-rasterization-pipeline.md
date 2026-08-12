# Module 04 — The Rasterization Pipeline

### The fixed-function machine your shaders plug into, and every stage where a frame can silently go wrong

*~28 min read · Part II: Rendering · Prerequisites: Modules 01–03*

---

## Read this first

A GPU is not a general-purpose parallel computer that happens to draw. It is a **purpose-built pipeline** with a few programmable slots cut into it. Understanding the fixed-function parts is what lets you predict cost and diagnose "nothing is on screen."

"Fixed-function" means *implemented in dedicated silicon, not software*. You cannot change what those stages do — you can only configure them through a handful of flags in your pipeline description. They're free in the sense that you don't pay per instruction, and constraining in the sense that you cannot make them behave differently.

The pipeline, end to end:

```
Index/Vertex fetch → Vertex Shader → Primitive Assembly → Clipping →
Perspective Divide → Viewport Transform → Backface Cull → Rasterization →
Early-Z → Fragment Shader → Late-Z / Stencil → Blending (ROP) → Framebuffer
```

**Two of those stages are yours:** the **vertex shader** and the **fragment shader**. Everything else is silicon with a small number of knobs.

One vocabulary note up front, because it's used inconsistently everywhere:

- A **pixel** is a location on the screen with a final color.
- A **fragment** is a *candidate* contribution to a pixel — the output of rasterizing one triangle at one pixel location. Ten overlapping triangles produce ten fragments for one pixel. Some get thrown away by depth testing; the survivors get blended into the pixel.

The distinction matters because "fragment shader invocations" and "pixels on screen" can differ by 10× and the gap *is* your overdraw problem.

---

## Vertex fetch and the vertex shader

### What you supply

You supply buffers of vertex data plus a description of their layout: for each attribute, a **shader location**, a **format** (`float32x3`, `unorm8x4`, …), an **offset** into the vertex, and the **stride** between vertices.

```ts
buffers: [{
  arrayStride: 8,                     // bytes per vertex
  attributes: [
    { shaderLocation: 0, offset: 0, format: 'uint32'  },   // packed position+normal
    { shaderLocation: 1, offset: 4, format: 'uint32'  },   // packed material+AO
  ],
}]
```

The hardware fetches those bytes, converts them to the shader's declared types, and runs your vertex shader **once per vertex**.

A note on formats: `unorm8x4` means "four 8-bit unsigned integers, presented to the shader as floats in [0,1]." `snorm` is the signed [-1,1] version. That conversion is free in hardware, which is why storing colors and normals as bytes rather than floats is pure win.

### Two details with real consequences

**Vertex formats are compression, and compression is speed.**

Vertex fetch is bandwidth-bound (Module 03), so the size of a vertex directly sets your throughput. A position doesn't need three 32-bit floats if your voxels live on a 32³ grid — 5 bits per axis suffices (values 0–31), or 6 bits if you need the 33 boundary positions for a quad's far edge. Normals are one of six axis directions, so 3 bits. Ambient occlusion at 4 levels is 2 bits.

```
Naive voxel vertex:                          Packed voxel vertex:
  position: 3 × f32       = 12 bytes           one u32:
  normal:   3 × f32       = 12 bytes             bits  0-5   : x (0..32)
  uv:       2 × f32       =  8 bytes             bits  6-11  : y
  ao:       1 × f32       =  4 bytes             bits 12-17  : z
                          ─────────             bits 18-20  : face index (0..5)
                          36 bytes              bits 21-22  : AO level
                                                bits 23-31  : material ID
                                              ───────────────
                                                4 bytes  → 9× less bandwidth
```

**Every serious voxel engine packs vertices into one or two `uint32`s.** Unpacking costs a few shifts and masks in the vertex shader, which is nearly free (Module 03: arithmetic is cheap, memory is not). This is the single clearest example in the course of "compress and unpack" being a straight speedup.

**Indexed drawing enables the post-transform cache.**

Without an index buffer, a cube's 12 triangles need 36 vertices, and the 8 corners get shaded 4–6 times each. With an index buffer, you store 8 unique vertices and 36 indices pointing into them.

The GPU keeps a small **post-transform vertex cache** (~16–32 entries) of recently shaded vertices. If an index refers to a vertex still in that cache, the shader is skipped and the result reused. The catch: the cache is small, so reuse only helps **if the reuse happens within a short window of indices**. Ordering your indices so that triangles sharing vertices are adjacent in the buffer is what "vertex cache optimization" means (Tom Forsyth's algorithm is the classic).

For voxel meshes made of quads, this is close to free: emit each quad's 6 indices (`0,1,2, 0,2,3`) in order and you naturally get 4 shaded vertices for 2 triangles instead of 6.

### The vertex shader's contract

Consume attributes and uniforms; output a clip-space position (`@builtin(position)`) plus any **varyings** you want interpolated.

> **What's a "varying"?** Any value the vertex shader outputs *other* than position. The rasterizer interpolates it smoothly across the triangle's surface and hands the interpolated value to the fragment shader. UV coordinates, normals, and vertex colors are all varyings. The name is historical (GLSL called them that) but everyone still uses it.

What the vertex shader **cannot** do:

- See other vertices. Each invocation is independent.
- Create or destroy geometry. There are **no geometry shaders in WebGPU** — a deliberate omission, since they were slow on every hardware vendor and are widely regarded as a design mistake. (Their use cases are covered by instancing, compute shaders, or mesh shaders, the latter not yet in WebGPU.)
- Know which triangle it belongs to. It only knows its own index.

---

## Primitive assembly, clipping, and culling

**Primitive assembly** groups vertices into triangles according to the **topology** you declared:

- `triangle-list` — every 3 indices is one triangle. Simple, most common, what you'll use.
- `triangle-strip` — each new index makes a triangle with the previous two. Fewer indices, but awkward for disconnected geometry and it forces alternating winding.
- Also `point-list`, `line-list`, `line-strip`.

**Clipping** happens in homogeneous clip space, *before* the perspective divide, for the reason covered in Module 02: dividing by a negative `w` maps geometry behind the camera to nonsense positions inside the visible box. Triangles straddling a frustum plane get split into smaller triangles along the cut.

You never write this code, but knowing it happens explains why a triangle spanning the near plane doesn't glitch, and why extremely large triangles (a full-screen quad, a giant ground plane) can be marginally more expensive than you'd expect.

### Backface culling

Removes triangles whose **winding order** — the order its vertices appear in, after projection to screen — indicates it faces away from the camera. You configure it in the pipeline:

```ts
primitive: {
  topology: 'triangle-list',
  frontFace: 'ccw',      // counter-clockwise winding = front
  cullMode: 'back',      // discard back-facing triangles
}
```

For a **closed** mesh (a solid object with no holes), exactly half the triangles face away and are invisible. Culling them is a **free ~2× reduction in fragment work**, and it happens before rasterization so you don't even pay to generate their fragments.

It is also **the single most common cause of "my mesh is invisible."** The diagnostic is trivial and takes 10 seconds:

```ts
cullMode: 'none'    // if the geometry appears, your winding is inverted
```

Common causes of inverted winding:
- A **negative scale** anywhere in the transform chain — mirroring flips winding.
- A **coordinate system / handedness mismatch** during model import (Module 02).
- Emitting quad indices in the wrong order in your own mesh generator.
- Copying a shader or projection matrix from an OpenGL tutorial into a WebGPU project.

For voxel meshing specifically, you generate faces yourself, so you control winding directly — write a unit test that generates one face and asserts the resulting normal points outward. And you get an extra trick: since you know each face's normal at generation time, you can **cull entire faces on the CPU before they ever become vertices** (a face between two solid voxels is never visible and shouldn't be emitted at all). That's Module 08's territory and it's much better than letting the GPU cull them.

---

## Rasterization

The rasterizer determines which pixels a triangle covers and generates **fragments** — candidate pixels with interpolated attributes. A pixel is covered if the triangle overlaps its center point (plus tie-breaking rules so adjacent triangles don't double-cover or leave gaps along shared edges).

Three facts that shape performance.

### Fact 1: Fragments are generated in 2×2 quads. Always.

Even a triangle covering a single pixel produces a full 2×2 quad of fragment shader invocations, with three lanes masked off but **still executing** (Module 03's divergence, in hardware form).

This is not a quirk — it's *required*. **Screen-space derivatives** (`dpdx`/`dpdy` in WGSL) are computed by differencing values between neighbors within the quad. Those derivatives drive automatic mip level selection: the hardware compares the UV of this pixel to the UV of its neighbor, sees how fast the texture is being traversed, and picks a mip level accordingly. Without the quad, `textureSample()` couldn't work.

(This is also why `textureSample` is illegal inside non-uniform control flow in WGSL — if some lanes in the quad took a different branch, their derivative is meaningless. You use `textureSampleLevel` with an explicit mip when you need to sample inside a branch.)

The implication is **quad overdraw**: thin or small triangles waste up to **75%** of fragment shading on masked-off lanes.

```
A triangle 1 pixel wide across 4 pixels:
  actual pixels covered:            4
  fragment shader invocations:     16   ← 4 quads, 1 useful lane each
  efficiency:                      25%
```

This is precisely why very dense meshes underperform their triangle count — a model with more triangles than pixels is shading each pixel many times over, and no amount of GPU power fixes it. It's the reason Unreal's Nanite includes a *software* rasterizer for tiny triangles: below a certain size the fixed-function hardware is worse than doing it in a compute shader.

**For voxels, this is a strong argument in favor of merged quads over per-voxel cubes.** A greedy-meshed wall is a handful of huge triangles with near-100% quad efficiency; a wall of individual cubes at distance is thousands of sub-pixel triangles at 25%.

### Fact 2: Interpolation is perspective-correct

Attributes are not interpolated linearly in screen space. The hardware interpolates `attribute/w` and `1/w` linearly and then divides, which correctly accounts for depth.

Why it matters: without it, a textured floor receding into the distance visibly warps and swims as the camera moves — the texture appears to slide across the surface. That is the classic **PlayStation 1 look**; the PS1's rasterizer had no perspective correction and no depth buffer, and both artifacts are instantly recognizable.

You get this for free. WGSL lets you opt out per-varying with `@interpolate(linear)` (screen-space linear) or `@interpolate(flat)` (no interpolation — take the value from the triangle's provoking vertex). **`flat` is genuinely useful for voxels**: material IDs and face indices are constant across a quad, so interpolating them is meaningless and `flat` saves the interpolation work and avoids float precision issues on integer-ish values.

### Fact 3: Overdraw is the enemy

If ten surfaces cover the same pixel and you shade all ten, you paid 10× for one visible result. **Overdraw** is a primary metric you will measure (Module 09 shows how to visualize it as a heatmap).

Typical overdraw factors: a well-sorted opaque scene should be near 1.0–1.5 with early-Z working. Particles and foliage routinely hit 5–20× and are the usual culprit when a scene tanks.

---

## Depth testing, and Early-Z

### The depth buffer

A screen-sized buffer storing the closest depth value seen so far at each pixel. When a fragment arrives, the hardware compares its depth against the stored value; if the fragment is closer it's kept and the buffer updated, otherwise it's discarded.

That's the mechanism for correct occlusion **without sorting your geometry**, and it is one of the great ideas in computer graphics. Before depth buffers you had to sort polygons back-to-front (the "painter's algorithm") which is slow and gets intersecting geometry wrong.

Configured in WebGPU as:

```ts
depthStencil: {
  format: 'depth24plus',
  depthWriteEnabled: true,
  depthCompare: 'less',      // keep fragments closer than what's stored
}
```

### Early-Z: the critical optimization

Naively, depth testing happens *after* the fragment shader — you have to shade a fragment to know its color before you can decide whether to keep it. That's wasteful: you shaded something invisible.

**Early-Z** (early depth test) is the hardware testing depth *before* running the fragment shader, skipping shading entirely for occluded fragments. Combined with **hierarchical Z (Hi-Z)** — a low-resolution depth pyramid where each texel stores the max depth of a tile, letting the hardware reject an entire 8×8 tile with one comparison — this is one of the largest performance wins in any renderer, often several times over.

It is entirely automatic. You do nothing to enable it.

### But you can turn it off by accident

Early-Z requires the hardware to know a fragment's depth and coverage *before* the shader runs. Anything that makes the shader's output unpredictable forces the hardware to fall back to **Late-Z** (test after shading). This happens when your fragment shader:

| Does this | Why it breaks Early-Z |
|---|---|
| Writes `@builtin(frag_depth)` | The depth isn't known until the shader runs |
| Uses `discard` | Coverage isn't known until the shader runs |
| Writes to a storage buffer or storage texture | The write must happen even for a fragment that would be depth-rejected, so the shader must run |
| Uses alpha-to-coverage | Coverage depends on the shaded alpha |

**This is why alpha-tested foliage is disproportionately expensive.** A leaf card is mostly transparent, so it uses `discard`, so it loses Early-Z, so every leaf behind every other leaf gets fully shaded. A tree can cost more than a character.

**And it is why writing custom depth in a raymarching shader has a cost far beyond the instructions you added.** If you write depth in a voxel raytracer — which you generally must, in order to composite correctly with rasterized geometry like UI, particles, or characters — you should know **you have given up Early-Z for that pass** and plan around it (e.g. by making that pass render into its own target and doing the composite separately, or by ensuring it runs first so there's nothing to reject anyway).

### Depth prepass

The standard countermeasure when your fragment shaders are expensive:

1. **Pass 1:** render all opaque geometry depth-only — no color attachment, no fragment shader at all (or a trivial one). This is very cheap; you're only paying vertex processing and rasterization.
2. **Pass 2:** render the real pass with `depthCompare: 'equal'` and `depthWriteEnabled: false`.

Now every pixel is shaded **exactly once**, because only the fragment whose depth exactly matches the prepass survives. Overdraw becomes 1.0 by construction.

The cost: you transform all your geometry twice. **Worth it when your fragment shaders are expensive; not worth it when you're vertex-bound** (which a voxel engine with millions of quads can easily be). This is a measure-it decision, and saying so is the right answer.

One gotcha: the two passes must compute *bit-identical* vertex positions, or the `equal` test fails and geometry vanishes. Use the same shader code path and don't let the compiler optimize the two differently.

### Ordering still matters even with Early-Z

Early-Z can only reject a fragment if something *closer* has already been drawn. So:

- **Front-to-back** ordering lets Early-Z reject the maximum number of fragments.
- **Back-to-front** ordering defeats it entirely — every fragment passes the test when drawn, then gets overwritten.

**Rough front-to-back sorting of opaque objects is standard practice.** "Rough" is the operative word: you sort by distance bucket, not exactly, because exact sorting costs more than it saves and conflicts with sorting by pipeline state (Module 03). The usual compromise is a sort key that packs pipeline ID in the high bits and a coarse depth in the low bits.

---

## Blending and transparency

### How blending works

Blending happens in the **ROP** (Raster Operations) stage — fixed-function hardware after the fragment shader that combines the new fragment's color with what's already in the framebuffer:

```
result = src * srcFactor  ⊕  dst * dstFactor
```

where `src` is your fragment's output, `dst` is what's already there, `⊕` is usually addition, and the factors are chosen from a fixed menu (`one`, `zero`, `src-alpha`, `one-minus-src-alpha`, …).

Standard alpha blending is:

```
result = src * srcAlpha + dst * (1 - srcAlpha)
```

### Why transparency is genuinely hard

**Alpha blending is order-dependent.** Blend red over blue and you get a different color than blue over red. Mathematically, the operation isn't commutative. That single fact creates the entire transparency problem in real-time graphics:

| Geometry type | Requirement |
|---|---|
| Opaque | Any order works (depth buffer handles it). Prefer front-to-back for Early-Z. |
| Transparent | Must be sorted **back-to-front**, per-object, every frame, from the camera. Depth writes **off**, depth test **on**. |
| Intersecting or concave transparent objects | **Cannot be sorted correctly per-object. Period.** There is no draw order that is right. |

That last row is the crux. Two intersecting glass panes need pane A in front for some pixels and pane B in front for others. Per-object sorting can only pick one. You are choosing which artifact to accept.

(Depth writes off, depth test on: transparent surfaces should be *occluded* by opaque geometry in front of them, but shouldn't *occlude* the transparent surfaces behind them, since you need those to blend through.)

### The escape hatches

**Alpha testing** — `discard` fragments below an alpha threshold, making the surface effectively opaque again. Order-independent and depth-correct. Costs: disables Early-Z (above), and aliases badly at the cut edges since there's no partial coverage. Used for foliage, chain-link fences, and voxel textures with holes.

**Additive blending** — `result = src + dst`. **Order-independent, because addition commutes.** This is why particles, fire, sparks, magic VFX, and glows are so often additive: you get to skip the entire sorting problem. It only works for things that add light and never occlude, which is exactly what those effects are. Module 14 leans on this heavily.

**Premultiplied alpha** — store colors with RGB already multiplied by alpha, then blend with `src * 1 + dst * (1 - srcAlpha)`. This is not an optimization; it's *correct* where straight alpha is wrong. Filtering and mipmapping straight-alpha textures blends color values from fully-transparent texels into visible ones, producing dark or colored fringes around cutouts. Premultiplied alpha doesn't have this problem, and it also lets one blend mode express both alpha and additive blending (alpha=0 with nonzero RGB is additive). **Adopt it as your default convention** and you'll avoid a category of bug that's very hard to diagnose from a screenshot.

**Order-independent transparency (OIT)** — real solutions at real cost:
- *Weighted blended OIT* — an approximation that weights contributions by depth. Cheap, order-independent, visually acceptable for many cases, wrong in ways you can see if you look.
- *Per-pixel linked lists / depth peeling* — correct, expensive, unbounded memory.

### For a voxel game

This is mostly about water, glass, and effects. A common and pragmatic architecture:

1. Opaque voxels — one pass, front-to-back-ish, Early-Z doing its job.
2. Transparent voxel types (water, glass, ice) — a separate sorted pass. Voxels help here: they're on a grid, so sorting is cheap and exact per-face, and the classic Minecraft-style trick of not rendering faces between two water voxels removes most of the intersecting-surface problem by construction.
3. Additive VFX last, unsorted.

---

## Render passes, attachments, and why load/store ops matter

A **render pass** binds a set of output **attachments** (one or more color targets, optionally a depth/stencil target) and runs draws into them. Everything drawn in a pass writes to the same attachments with the same size and sample count.

In WebGPU you declare, per attachment, a `loadOp` and a `storeOp`:

```ts
colorAttachments: [{
  view: canvasView,
  loadOp: 'clear',              // what to do with existing contents at pass start
  clearValue: { r: 0, g: 0, b: 0, a: 1 },
  storeOp: 'store',             // what to do with results at pass end
}]
```

This looks like boilerplate. **It is not — it's a bandwidth control**, and it comes straight from mobile and tile-based GPU architectures.

> **Tile-based rendering**, in one paragraph: mobile GPUs (and Apple Silicon, and Qualcomm/ARM/Imagination designs) don't have the bandwidth for a full-screen framebuffer in main memory. Instead they split the screen into small tiles (say 32×32), keep one tile's worth of color and depth in ultra-fast on-chip memory, render everything touching that tile, then write the finished tile out once. Depth and intermediate blending never leave the chip. It's enormously more bandwidth-efficient — and it makes `loadOp`/`storeOp` semantically important rather than cosmetic.

| Op | Meaning | Cost |
|---|---|---|
| `loadOp: 'clear'` | Start fresh with the clear value | **Cheap** — nothing is read |
| `loadOp: 'load'` | Read the existing contents into tile memory | **A full-framebuffer read** |
| `storeOp: 'store'` | Write results out to memory | A full-framebuffer write |
| `storeOp: 'discard'` | Throw them away | **Free** |

**Practical rules:**
- Always `clear` rather than `load` when you're going to overwrite everything anyway.
- Always `discard` a depth buffer you won't reuse after the pass. Depth is often the largest attachment and it's usually pure scratch.
- On tiled GPUs, getting these wrong can cost more than your shaders do. On a desktop discrete GPU it matters less, but there's no reason to get it wrong.

### MSAA

**Multisample anti-aliasing** also lives at this stage. The rasterizer computes coverage at multiple sample points per pixel (4× is standard) but runs the **fragment shader once per pixel**, then writes the result to whichever samples were covered. At the end of the pass, a **resolve** averages the samples down to one color per pixel.

The key consequence: **MSAA costs bandwidth and memory, not shading.** A 4× MSAA target is 4× the memory and roughly 4× the ROP traffic, but your expensive fragment shader still runs once per pixel.

That makes MSAA:
- **Cheap and excellent for geometric edges** — the jagged staircase on a triangle boundary. That's what it's for.
- **Useless for shader aliasing** — specular sparkle, high-frequency normal maps, alpha-tested foliage edges. The shader ran once; averaging its single result four ways changes nothing. (Alpha-to-coverage partially addresses the foliage case.)
- **Awkward with deferred rendering** — you'd need a multisampled G-buffer and per-sample lighting, which destroys the bandwidth argument. This is a big reason most modern engines use **temporal antialiasing (TAA)** instead, which reuses previous frames' samples.

**For a hard-edged voxel game, MSAA is unusually attractive** because *all* your aliasing is geometric — voxel edges are perfectly straight, high-contrast boundaries, which is precisely the case MSAA nails. That's a genuinely good, specific point to make in an interview about this role.

---

## Forward vs deferred, in one page

You will be asked about this. Know all three, and know the voxel wrinkle.

### Forward rendering

For each object, for each light affecting it, shade. All lighting happens in one pass, in the fragment shader, at the moment the surface is rasterized.

- ✅ Simple, MSAA-friendly, handles transparency naturally, supports wildly varied material models.
- ❌ Cost scales as `objects × lights`. Also, you shade fragments that later get overdrawn (unless you do a depth prepass).

### Deferred rendering

Split into two passes:
1. **G-buffer pass** — rasterize geometry, but instead of computing lighting, write out surface *properties*: albedo, world normal, roughness, metalness, depth. That set of render targets is the **G-buffer** ("geometry buffer"), typically 2–4 textures.
2. **Lighting pass** — a full-screen pass (or per-light volumes) that reads the G-buffer and computes lighting once per pixel per light.

- ✅ Decouples lighting cost from geometry cost. Hundreds of dynamic lights become tractable. Zero overdraw in the lighting pass by construction.
- ❌ Heavy bandwidth (the G-buffer is written and read every frame — often 20+ MB per frame at 1080p). Transparency doesn't work at all and needs a separate forward pass. MSAA is expensive. All surfaces must fit one material model.

### Forward+ / clustered forward

Split the screen (and often the depth range) into tiles or 3D clusters. A compute pass builds a per-cluster list of which lights affect it. Then forward-shade, reading only the relevant lights for your cluster.

- ✅ Most of deferred's light scalability, most of forward's flexibility (transparency, MSAA, varied materials).
- ❌ More moving parts; needs a depth prepass for the tightest cluster bounds.

**This is the modern default for most engines**, and it's the right answer to "which would you pick" for a general renderer in 2026.

### The voxel wrinkle

For a voxel renderer the tradeoffs shift, because **if you're ray tracing the voxels rather than rasterizing them, you're naturally producing a G-buffer-like result from a compute pass anyway** — the raymarch gives you a hit position, a normal, and a material ID per pixel, which *is* a G-buffer. The rasterize-vs-deferred taxonomy partly dissolves.

Understanding all three is still expected. **Being able to say *why the standard taxonomy doesn't map cleanly onto a voxel raytracer* is better**, and it's the kind of answer that distinguishes someone who has thought about this domain from someone who has memorized a rendering textbook.

---

## Debugging a black screen: the checklist

This will happen to you constantly, and the instinct to stare at shader code is almost always wrong. Work down this list instead — each step isolates one stage of the pipeline.

1. **Is anything being submitted?** Check the draw call actually executes and the vertex/index counts aren't zero. Add a `console.log` in the submit path. (Sounds insulting; catches it maybe 20% of the time.)
2. **Is the clear color showing?** Set it to hot pink. If you don't see pink, the problem is pass setup, canvas configuration, or the pass never ran — nothing downstream matters yet.
3. **Winding / culling** — set `cullMode: 'none'`. Geometry appears? Your winding is inverted.
4. **Depth** — is the depth buffer cleared to the right value (1.0 normally, 0.0 for reversed-Z), and is `depthCompare` the matching direction (`less` vs `greater`)? Try `depthCompare: 'always'`. Geometry appears? It's a depth problem.
5. **Is it off-screen or inside-out?** Output a constant color from the fragment shader. If you see a *shape*, it's a shading problem, not a geometry problem — and that halves your search space immediately.
6. **Is it behind the near plane or beyond far?** Compute the clip-space position of one known vertex on the CPU using the exact same matrices, and print it. Check `−w ≤ x,y ≤ w` and `0 ≤ z ≤ w`.
7. **Matrix convention** — try transposing the matrix you upload. If the object appears, you have a row/column-major mismatch (Module 02).
8. **Is alpha zero?** A fully transparent output over a black clear is indistinguishable from nothing. Force `a = 1.0`.
9. **Are the bind group and pipeline layouts actually matching what the shader declares?** WebGPU validation errors go to the console — make sure you're capturing `device.onuncapturederror` and pushing/popping error scopes around setup.

**Then, and only then, open a GPU debugger.** Module 09 covers those tools.

---

## Common confusions

**"Overdraw and quad overdraw are the same thing."** Overdraw is multiple triangles covering the same pixel. Quad overdraw is one triangle covering less than a full 2×2 block, wasting masked lanes. Both waste fragment shading; they have different causes and different fixes (sorting/culling vs. bigger triangles).

**"A depth prepass always helps."** It helps when you're fragment-bound. It's a pure loss when you're vertex-bound, which a voxel renderer with millions of quads easily is. Measure both.

**"`discard` is free, it's just an early exit."** It's the opposite: it costs you Early-Z for the whole pipeline state, meaning fragments *behind* the discarding geometry get shaded too. The cost lands on other draws, which makes it very hard to attribute in a profiler.

**"MSAA will fix my aliasing."** Only geometric edge aliasing. If your shimmer comes from specular highlights, thin normal-mapped detail, or alpha-test edges, MSAA does nothing and you need mips, filtering, or TAA.

**"I'll just sort my transparent objects properly."** For non-intersecting convex objects, fine. For intersecting geometry, there is no correct order and you need OIT or a design change. Recognizing which situation you're in is the actual skill.

**"The depth buffer stores distance from the camera."** It stores a nonlinearly-remapped depth (Module 02). If you need real view-space distance in a shader, you must reconstruct it — don't read the depth value and treat it as meters.

---

## The interview answer

***"What's early-Z and how do you lose it?"***

> "Depth testing before the fragment shader so occluded fragments never get shaded, usually backed by a hierarchical Z pyramid that rejects whole tiles at once. You lose it if the shader writes depth, uses discard, or has side effects like storage writes — which is why alpha-tested foliage is disproportionately expensive. A depth prepass gets the benefit back: establish depth cheaply first, then shade with an equal test and depth writes off, so every pixel is shaded exactly once. It's a win when you're fragment-bound and a loss when you're vertex-bound, so I'd measure."

***"Why is transparency hard?"***

> "Alpha blending isn't commutative, so it's order-dependent. You can sort per-object back-to-front, but intersecting or concave transparent geometry has no correct draw order at object granularity. The real options are alpha testing — which makes it opaque again but costs early-Z and aliases — additive blending where the math commutes, or an OIT technique like weighted blended, which is approximate but cheap. And I'd use premultiplied alpha throughout, because straight alpha filters incorrectly and gives you fringing."

***"Walk me through what happens between a draw call and a pixel."***

> "Vertex fetch pulls attributes per your layout, the vertex shader runs once per vertex and outputs clip space, primitives get assembled, clipped against the frustum in homogeneous space, perspective-divided into NDC, viewport-transformed, backface-culled by winding. The rasterizer generates fragments in 2×2 quads with perspective-correct interpolated varyings. Early-Z rejects occluded fragments if the shader allows it. The fragment shader runs, then late depth and stencil, then the ROP blends into the framebuffer."

---

## Exercise — Voxelforge, Stage 4 (design pass)

You will implement this in Module 05, but **decide it now, on paper.** Making these decisions before you write code is exactly the discipline the job is asking for, and revisiting them after Module 09 is how you'll see what you got wrong.

**1. Sketch your frame's render passes.** What attachments does each have, what `loadOp`/`storeOp`, and in what order? Even for a first triangle this should be written down, because you'll be adding passes to it for the rest of the course.

**2. Decide your depth convention** — standard or reversed-Z — and write down the three things that must agree:
- clear value (1.0 or 0.0)
- `depthCompare` (`less` or `greater`)
- which projection matrix variant you build

**Getting these three consistent is the entire trick.** Get one wrong and you get a fully black or fully unoccluded scene with no error message.

**3. Design a packed voxel vertex** that fits in 8 bytes or fewer for a 32³ chunk: position, face normal index, texture/material ID, and a 2-bit ambient occlusion value. Write out the **bit layout** and the pack (TypeScript) and unpack (WGSL) expressions. Check your bit budget adds up. This is the artifact you'll actually implement in Module 05, and getting the layout right on paper first will save you a debugging session.

**4. Decide where transparent voxels (water, glass) go in your pass ordering**, and write down what visibly breaks when two water surfaces intersect. Then decide whether you care.

**⭐ Stretch:** write down what you expect your overdraw factor to be for a typical view of a voxel scene, and why. In Module 09 you'll build a heatmap and find out if you were right.

---

## Go deeper

- **Fabian Giesen, "A trip through the Graphics Pipeline 2011"** — again; parts 6–9 cover rasterization, early-Z, and ROP in exactly this territory, with more hardware detail than anywhere else public.
- **Real-Time Rendering, 4th ed.**, Chapters 2–5 — the reference text for this pipeline. Expensive book, worth it.
- **Nathan Reed, "A Quick Overview of MSAA"** and **"Depth Precision Visualized"** — reedbeta.com. Short and clarifying.
- **Morgan McGuire & Louis Bavoil, "Weighted Blended Order-Independent Transparency"** — the paper behind the most-used practical OIT technique.
- **Brian Karis, Nanite SIGGRAPH 2021 course notes** — for why small triangles are pathological, from the people who rebuilt rasterization to fix it.
- **ARM and Imagination's tile-based rendering guides** — the clearest explanations of why load/store ops exist, even if you never ship on mobile.

---

**Next:** [Module 05 — WebGPU and WGSL](./05-webgpu-and-wgsl.md)
