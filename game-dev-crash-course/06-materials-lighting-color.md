# Module 06 — Materials, Lighting, and Color

### Textures and sampling, the lighting equation, shadow techniques, and the color-space discipline that separates a professional image from an amateur one

*~30 min read · Part II: Rendering · Prerequisites: Modules 02–05*

---

## Read this first

You can now put geometry on screen. This module is about making it look like *something*.

The Engine JD lists this territory explicitly: *"space transformations, textures and sampling, lighting equations, shadow techniques, light sources, particle systems, post-processing, dithering, text rendering."* Space transformations were Module 02; particles are Module 14. Everything else is here.

A framing that will help: **almost everything in this module is an approximation of physics, chosen for its cost.** There is no "correct" — there's a ladder of models from "one dot product" to "path-traced ground truth," and engineering judgment is knowing which rung the game needs. An interviewer asking about lighting is usually testing whether you understand that ladder, not whether you've memorized the GGX distribution function.

---

## Textures and sampling

### The vocabulary

- A **texel** is one element of a texture. (Analogous to a pixel, but in texture space. The distinction matters: one pixel on screen may cover many texels, or one texel may cover many pixels, and *that ratio* is what filtering and mipmapping are about.)
- **UV** coordinates address a texture in `[0,1]²`, independent of its resolution. `(0.5, 0.5)` is the middle of the texture whether it's 16×16 or 4096×4096.
- A **sampler** is the rule for turning a continuous UV coordinate into a color.

Textures and samplers are separate objects in WebGPU (Module 05) precisely because the same texture is often read different ways.

### Filtering

The problem: your UV lands at (0.5013, 0.2288), which is between texels. What color is that?

| Mode | What it does | When to use |
|---|---|---|
| `nearest` | Pick the closest texel | Pixel art, and **any data texture** where blending values is meaningless: voxel material IDs, palette indices, tile maps |
| `linear` (bilinear) | Blend the 4 nearest texels, weighted by position | Almost everything else. Free in hardware |
| **Trilinear** | Bilinear within a mip level, *plus* blending between two mip levels | Kills the visible seam where mip levels change. Set `mipmapFilter: 'linear'` |
| **Anisotropic** | Multiple taps along the projected footprint | Surfaces at grazing angles: floors, walls receding into the distance |

**Anisotropic filtering** deserves a sentence of explanation because it's the one that isn't obvious. When you look at a floor at a shallow angle, one screen pixel covers a long, thin sliver of the texture — wide in one direction, narrow in the other. Standard mipmapping has to pick a single mip level for that pixel, so it picks one blurry enough for the *long* axis, and the floor turns to mush. Anisotropic filtering takes several samples spread along the long axis instead, keeping the detail. It's cheap relative to its visual impact; **`maxAnisotropy: 4–16` on world textures is standard** and one of the best quality-per-cost settings that exists.

### Address modes and the atlas bleeding bug

**Address modes** — `repeat`, `clamp-to-edge`, `mirror-repeat` — decide what happens outside `[0,1]`.

Getting this wrong on a **texture atlas** (many small images packed into one big texture) causes **bleeding**: at low mip levels, neighbouring tiles blend into each other and you see a halo of the wrong material at block edges. Grass with a stripe of dirt around it. Stone with a rim of wood.

Why it happens: a mip level is the average of a 2×2 block from the level above. Do that four times and one texel of mip 4 is the average of a 16×16 region — which, on a 16×16 atlas tile, is *the entire tile plus its neighbours*. Bilinear filtering then reads across the boundary too.

**This is *the* classic voxel-texturing bug.** The three real fixes, in increasing order of quality:

1. **Add a padding gutter** around each atlas tile (duplicate the edge texels outward). Helps at shallow mips, doesn't fully solve it — deep mips still bleed.
2. **Use a 2D texture array** (`texture_2d_array`), one layer per material. Each layer mips **independently**, so there is no cross-material bleeding *at all*, at any mip level. **This is the right answer for voxel games and what you should propose in an interview.** It's also what WebGPU makes easy, since you can't do bindless.
3. **Clamp UVs manually in the shader** against a per-tile rect. Works, costs ALU, and defeats hardware wrapping for tiling textures.

### Mipmaps are not optional

Covered in Module 03, restated here because it's a rendering decision: without mips, minified textures **alias horribly** (shimmer and crawl as the camera moves) *and* **thrash the texture cache**, because neighbouring pixels sample distant texels.

**WebGPU does not generate mips for you.** Unlike WebGL's `generateMipmap()`, you write a small downsample chain yourself — either a compute shader, or a series of render passes each sampling the previous level with a linear sampler. Write it once, keep it in your engine, and never think about it again. (`webgpu-utils` has one if you want to start from working code.)

### Format choice is a bandwidth decision

| Use | Format |
|---|---|
| Color textures | `rgba8unorm-srgb` (note the srgb — see the color section) |
| Voxel IDs / data | `r8uint` or `r16uint` — **never** srgb |
| HDR intermediates | `rgba16float`, or `rg11b10ufloat` if you don't need alpha |
| Depth | `depth32float` (or `depth24plus` if you don't need reversed-Z precision) |

**Block-compressed formats** (BC on desktop, ASTC on mobile, ETC2 on Android) are 4–8× smaller and — crucially — **stay compressed in VRAM and in cache**, so they save bandwidth *and* memory. WebGPU exposes them as optional features (`texture-compression-bc`, `-etc2`, `-astc`), so you must query support and ship multiple asset variants. That's real pipeline work (Module 11).

**For a voxel game with small, stylized textures, uncompressed atlases in a texture array are often perfectly fine and much simpler.** A 16×16 tile at `rgba8unorm` is 1 KB; 256 block types is 256 KB. That's nothing. Knowing when *not* to build the compression pipeline is worth saying out loud.

---

## The lighting equation, built up

Start with the simplest model and understand what each term buys you.

### Lambert (diffuse)

Light that hits a rough surface scatters equally in all directions. Its brightness depends only on the angle between the surface normal and the light direction:

```wgsl
let ndotl   = max(dot(N, L), 0.0);          // N = surface normal, L = toward the light
let diffuse = albedo * lightColor * ndotl;
```

> **Albedo** is the surface's base color — the fraction of each wavelength it reflects. A red brick has albedo around (0.5, 0.15, 0.1). It's what people mean by "the texture" before lighting.

That `max(…, 0.0)` is not a detail. Negative values mean the surface faces away from the light; letting them through produces black artifacts and, worse, *negative light* that subtracts from other lights.

This is one dot product and it gets you most of the way to a readable image. Everything below is refinement.

### Blinn-Phong (specular)

Adds a highlight — the bright spot where the surface reflects the light toward your eye:

```wgsl
let H    = normalize(L + V);                 // V = toward the viewer
let spec = pow(max(dot(N, H), 0.0), shininess);
```

`H` is the **half-vector**: the direction that would need to be the surface normal for the light to reflect exactly into your eye. The closer the actual normal is to `H`, the brighter the highlight. `shininess` (typically 8–256) controls how tight it is.

Cheap, controllable, and **completely adequate for a stylized game.** Do not assume you need PBR.

### Physically based rendering (PBR)

PBR parameterizes materials by **base color, metallic, roughness** and evaluates a **microfacet BRDF**.

Decoding that:

- **BRDF** — Bidirectional Reflectance Distribution Function. A function answering "given light arriving from direction A, how much leaves toward direction B?" That's all a material model *is*.
- **Microfacet** — the model assumes the surface is made of countless tiny perfect mirrors at varying angles. Roughness describes how varied those angles are. Smooth = mirrors mostly aligned = tight highlight. Rough = scattered = broad, dim highlight.
- The standard implementation is **GGX / Trowbridge-Reitz** for the distribution of those facet angles, a **Smith** geometry term for facets shadowing each other, and **Schlick's approximation of Fresnel** for the fact that all surfaces become mirror-like at grazing angles (look along a sheet of paper — it's shiny).
- **Metallic** is a binary-ish switch: metals tint their reflections and have no diffuse component; non-metals ("dielectrics") have white-ish reflections and a diffuse component.

Its virtues are **consistency** (materials look right under *any* lighting, so artists author once and it works in the cave and in the sunlight) and **energy conservation** (a surface never reflects more light than hits it, which is what stops things looking like glowing plastic).

Know the terms and what they do. **But also know the judgment call: PBR is a means, not a goal.** For a hard-edged stylized voxel game, a well-tuned Lambert + rim light + strong ambient occlusion + a good tonemap will often beat a mediocre PBR implementation and cost a fraction as much. Being able to say that — and to say *when* you'd choose PBR (many varied materials, dynamic lighting conditions, an art team that wants predictability) — is more valuable than reciting the GGX formula.

### Light types

| Type | What it is | Cost notes |
|---|---|---|
| **Directional** | Infinitely far, parallel rays, one direction. The sun. | No attenuation, no position. Cheapest. |
| **Point** | A position + radius, attenuating with distance | Use a **smooth windowed falloff** that reaches exactly zero at the radius. Pure inverse-square never reaches zero, so lights visibly **pop** when you cull them. |
| **Spot** | A point light restricted to a cone | Add an inner/outer angle `smoothstep` for a soft edge |
| **Area** | A light with actual size (a window, a strip) | Physically correct, expensive. Approximated in real time (LTC), or faked with several point lights. |
| **Ambient / IBL** | Everything else — bounced light from the environment | See below. This is the important one. |

### Ambient is where cheap games get expensive-looking

Direct lighting is only part of what you see. In reality, light bounces off everything and fills in the shadows. Simulating that properly is global illumination and it's expensive; approximating it is where enormous amounts of perceived quality live.

The ladder:

1. **Flat constant ambient** — `color += albedo * 0.2`. Makes everything look like a 1998 render: flat, dead, plastic.
2. **Hemisphere ambient** — blend between a sky color from above and a ground-bounce color from below, based on `N.y`. Two lines of code, transforms the image.
   ```wgsl
   let ambient = mix(groundColor, skyColor, N.y * 0.5 + 0.5);
   ```
3. **Environment cubemap / IBL** (image-based lighting) — a captured or authored 360° image of the surroundings, prefiltered by roughness, plus a BRDF lookup table. The good version.
4. **Ambient occlusion** — darkening in creases and corners, where less of the sky is visible.

**Ambient occlusion does more for perceived quality than any specular model.** And for voxels it's a gift: **AO can be computed exactly and for free at meshing time** by checking the 3 neighbouring voxels around each vertex corner, packed into 2 bits per vertex. No screen-space AO pass, no noise, no temporal artifacts, exact by construction.

That technique is a large part of why Minecraft-lineage renderers look as good as they do at their cost, and it's a great concrete answer to "what's an optimization that voxels make possible?" Module 08 covers the implementation.

---

## Shadow techniques

Shadows are visibility queries: *is there anything between this point and the light?*

### Shadow mapping

The standard answer, and the one you must be able to explain.

1. **Shadow pass:** render the scene depth-only *from the light's point of view* into a texture. For a directional light, use an **orthographic** projection (Module 02) since the rays are parallel. You now have "for each direction from the light, how far away is the nearest surface."
2. **Main pass:** for each shaded point, transform it into the light's clip space, look up the stored depth at that position, and compare. If the point is **further** from the light than what the light saw, something is in between — it's in shadow.

Elegant, general, and full of artifacts. All of which you must be able to name.

### Problem 1: shadow acne

Self-shadowing stripes across surfaces that should be lit. Cause: the shadow map stores one depth per texel, but the surface within that texel is sloped, so half the surface is "behind" the recorded depth and shadows itself. Worse at grazing angles.

Fixes:
- **Depth bias** — push the comparison depth slightly toward the light. A constant bias plus a **slope-scaled** term (more bias where the surface is steeper relative to the light).
- **Front-face culling during the shadow pass** — render *back* faces into the shadow map, so the depth error lands inside the object where nobody sees it. Free, and effective for closed geometry. Breaks for thin single-sided geometry (leaves, flags).
- **Normal-offset bias** — offset the *sample position* along the surface normal rather than biasing depth. Modern, better-behaved, and what you should reach for first.

Too much bias causes **peter-panning** — the shadow detaches from the object's feet and it looks like the object is floating. **Every shadow implementation is a negotiation between acne and peter-panning**, and saying that sentence signals you've actually tuned one.

### Problem 2: hard, aliased edges

The shadow map has finite resolution, so its edges are jagged blocks.

**PCF** (percentage-closer filtering) takes multiple depth comparisons in a small kernel and averages **the results** — not the depths. That distinction is critical: averaging depths and then comparing gives you a wrong answer at depth discontinuities (you'd compare against a depth that exists nowhere in the scene). Averaging comparison *results* gives you "60% of my samples were lit," which is exactly what a soft edge means.

WebGPU supports this directly via **comparison samplers**:

```wgsl
@group(0) @binding(2) var shadowMap  : texture_depth_2d;
@group(0) @binding(3) var shadowSamp : sampler_comparison;

// Does compare-then-filter in hardware — one instruction, 4 filtered comparisons.
let lit = textureSampleCompare(shadowMap, shadowSamp, shadowUV, currentDepth);
```

For larger kernels, Poisson-disk or rotated sample patterns reduce the visible banding that a regular grid produces.

### Problem 3: not enough resolution across a large view

A single shadow map covering a 1 km view distance at 2048² gives you half-meter shadow texels. Fine for distant hills, useless for the character's feet.

**Cascaded Shadow Maps (CSM):** split the view frustum into 3–4 depth ranges and render a shadow map for each, sized to its range. Nearby geometry gets high resolution; distant geometry gets low. Blend between cascades over a small overlap to hide the transition.

This is the industry standard for directional sunlight and what you'd implement for an outdoor voxel world. Its complexity is mostly in the bookkeeping: fitting each cascade's ortho projection tightly around its frustum slice, and stabilizing it (snapping to texel boundaries) so shadows don't shimmer when the camera moves.

### Alternatives worth knowing

- **Variance / exponential shadow maps** — store statistical moments instead of raw depth so you can blur the map itself. Softer, cheaper filtering; prone to **light leaking** where a bright surface bleeds through a thin occluder.
- **Ray-traced shadows** — exact, expensive, and not hardware-accelerated in WebGPU.
- **Voxel ray-marched shadows** — highly relevant here. **If your world is already a voxel grid, you can march a ray from the surface toward the light through the grid** and get an exact hard shadow with **no shadow map, no bias, no cascades, and no resolution artifacts at all**.

That last one is a real architectural advantage of voxel worlds and exactly the kind of *"purpose-built rather than copied"* thinking the Engine JD asks for by name. It reuses the same DDA traversal you already wrote for primary rays (Module 08), so it's less code, not more. Its cost is per-pixel ray marching (which diverges — Module 03), and it gives you hard shadows only unless you march multiple rays or cone-trace.

---

## Color: the discipline most people skip

This is where "why does my lighting look muddy and my edges look wrong" gets answered. It's also the section most likely to immediately improve a project you already have.

### sRGB is not linear

Displays and image files encode color with roughly a 2.2 **gamma** curve. The reason is perceptual: human brightness perception is nonlinear (we distinguish dark tones much better than bright ones), so spending 8-bit codes evenly across *perceptual* brightness uses them far more efficiently than spending them evenly across *physical* light.

The consequence: **a texel value of 0.5 in an sRGB image is not half as much light as 1.0. It's about 21%.**

**Light math is only correct in linear space.** Adding two lights, multiplying by an occlusion factor, interpolating across a triangle, averaging texels during mipmap generation — all of it must happen on linear values. Do it on sRGB-encoded values and everything is subtly wrong: two lights that should sum to full brightness come out dim, shadows look crushed, and blends look muddy.

### The correct pipeline

1. **Author textures in sRGB.** (That's what Photoshop and every PNG produces. Nothing to do.)
2. **Sample them through an sRGB texture format** (`rgba8unorm-srgb`) so the *hardware* converts to linear during sampling.
3. **Do all lighting math in linear space**, in an HDR render target (`rgba16float`).
4. **Tonemap** HDR → LDR at the end.
5. **Encode back to sRGB for display** — again, via the format (an sRGB swapchain view, or an explicit encode in the final shader).

**Step 2 is the one people get wrong.** Do *not* do `pow(color, 2.2)` in the shader. Two reasons:

- The hardware conversion is free; `pow` is not.
- More importantly, **filtering must happen in linear space.** The texture unit linearizes *before* bilinear blending; a manual `pow` after sampling blends sRGB values and then linearizes, which is mathematically wrong and visibly darkens edges. Mipmap generation has the same requirement.

### Data textures must NOT be sRGB

Normal maps, roughness maps, metallic maps, masks, voxel material IDs, height maps — **these are data, not color.** Tagging them sRGB applies a gamma curve to numbers that aren't brightness, silently corrupting every value.

This mistake is extremely common and extremely hard to spot, because the result looks *plausible* — just wrong. Roughness maps come out too smooth, normal maps come out too flat. If a material looks subtly off and you can't say why, check the formats.

### Tonemapping

Your HDR render target holds unbounded values — the sun might be 10,000, a lamp 50, a shadow 0.01. Your display accepts `[0,1]`. **Tonemapping** is the curve that maps one to the other.

| Approach | Character |
|---|---|
| Clamp | Everything bright becomes flat white. Terrible. |
| Reinhard: `c / (1 + c)` | The one-liner. Works, looks a bit washed out. |
| **ACES** | Filmic, contrasty, industry standard. Preserves saturation in highlights instead of blowing them to white. |
| **AgX** | Newer, gentler highlight rolloff, increasingly the default (Blender adopted it). |

**This one shader function has an enormous effect on whether your game looks like a game or a tech demo.** Try swapping Reinhard for ACES on a scene you've built and the difference is startling for a five-line change. It's the single highest ratio of visual improvement to effort in this entire module.

### Dithering

Called out by name in the JD, and worth understanding properly.

**Banding** is the ugly concentric rings you see in smooth gradients — skies, fog, soft lighting falloff. Cause: the gradient changes by less than 1/255 per pixel, so many adjacent pixels quantize to the same 8-bit value, and the eye latches onto the resulting hard steps (an effect amplified by our vision's edge-detection).

**Dithering** adds a small, structured noise pattern *before* quantizing. Pixels near a boundary randomly round up or down instead of all rounding the same way, converting a visible step into imperceptible noise. It costs almost nothing:

```wgsl
// Ordered/Bayer or blue-noise dither before the final 8-bit write
let dither = (bayer4x4(vec2u(fragCoord.xy)) - 0.5) / 255.0;
return vec4f(tonemapped + dither, 1.0);
```

**Blue noise** is better than a Bayer matrix — it has no low-frequency structure, so it reads as film grain rather than a visible crosshatch, and it accumulates better under TAA. Precompute a 64×64 blue noise texture and tile it.

Dithering is also used for **stochastic alpha**: instead of blending a semi-transparent surface, you `discard` fragments based on a noise threshold. Fifty percent alpha becomes "randomly keep half the pixels." This keeps everything in the opaque pass (no sorting, no order dependence, depth writes intact) and TAA smooths the noise back into apparent transparency. It's how modern engines do LOD dissolve transitions and how they fade geometry that's too close to the camera.

**For a stylized voxel game with a limited palette, deliberate dithering can also be an art-direction choice**, not just an artifact fix — think of the deliberate ordered dither in retro-styled games. Worth mentioning that you'd expose it as a knob for the art team rather than burying it.

---

## Post-processing

After the scene renders to an HDR target, you run full-screen passes over it.

**Bloom** — extract bright pixels, blur them widely, add back. Simulates light scattering in the eye and camera lenses; it's what makes bright things *read* as bright rather than just white. The modern approach (Jimenez / Call of Duty) uses progressive down-sampling through a mip chain and up-sampling with a tent filter, which is both cheaper and more stable under motion than one wide Gaussian.

**Tonemap + color grading** — usually via a **3D LUT** (lookup table): a 32³ cube mapping input color to output color. Artists grade a screenshot in Photoshop or DaVinci Resolve, export a `.cube` file, and you sample it in the shader. **Giving artists a LUT pipeline is one of the highest-value tools you can build** — it turns "make it feel colder" from an engineering ticket into something the art director does in ten minutes. That's a Module 11 theme showing up early.

**Antialiasing:**
- **MSAA** — geometric edges only (Module 04). Great for voxels, since all your aliasing is geometric.
- **FXAA** — a cheap post filter that finds edges in the final image and blurs across them. Fast, slightly mushy.
- **TAA** (temporal antialiasing) — jitter the projection matrix by a sub-pixel offset each frame, and accumulate frames over time with **reprojection** (using motion vectors to find where each pixel was last frame). Effectively free supersampling, and the modern default. Its costs are real: it needs motion vectors, a history buffer, and neighborhood clamping to reject bad history, and it produces **ghosting** on fast motion and blurring of fine detail. It's also the enabler for every stochastic technique in the engine, since it converts noise into detail over time.

**Fog, depth of field, motion blur, vignette, chromatic aberration** — see Module 14.

### The performance rule for post-processing

**Each pass is a full read and write of the framebuffer, so post passes are bandwidth-bound, not ALU-bound.** At 1080p with `rgba16float`, one pass is ~16 MB of traffic. Ten passes is 160 MB per frame — 9.6 GB/s at 60 Hz just moving pixels around.

Two consequences:
- **Merging several effects into one shader is usually a bigger win than optimizing any of them.** Tonemap + grade + vignette + dither should be one pass, not four.
- **Do expensive blurs at reduced resolution.** Bloom's blur at quarter resolution costs 1/16th the bandwidth for a visually identical result, because you're blurring anyway.

---

## Text rendering

Deceptively hard, and specifically called out in the JD.

| Technique | Quality | Notes |
|---|---|---|
| **Bitmap font atlas** | Sharp at one size | Fastest. Blurry when scaled; needs a separate atlas per size |
| **SDF** (signed distance field) | Good | One atlas scales to any size |
| **MSDF** (multi-channel SDF) | Best practical | Preserves sharp corners |
| **Vector / GPU curve rasterization** | Highest | Most complex by far |

**SDF**, since it's the interesting idea: instead of storing "is this texel inside the glyph," store *the distance to the nearest glyph edge*. Distance fields interpolate beautifully, so a low-resolution SDF texture scaled up 10× still has a crisp edge — you just `smoothstep` around the 0.5 threshold in the shader.

And because you have a distance, you get **outlines, glows, and drop shadows nearly free** by thresholding at different distances. The one weakness: sharp corners round off slightly, because a single distance value can't represent two edges meeting.

**MSDF** fixes exactly that by encoding distances to different edges in the R, G, and B channels and taking the median. It's the current best practice for game UI text — use `msdfgen` / `msdf-atlas-gen` to generate the atlases.

Beyond the rendering itself, real text has requirements people forget until localization lands: **shaping** (HarfBuzz — combining glyphs correctly for ligatures and complex scripts), **bidi** (mixing right-to-left and left-to-right), CJK atlas sizes (thousands of glyphs, so you need dynamic atlas allocation), kerning, and line breaking.

**In a browser engine you *can* cheat** by rendering text to a `<canvas2d>` and uploading the result as a texture. The browser has a world-class text stack already; using it is a completely legitimate pragmatic choice for a small studio, and it's a good example of **knowing when not to build something.** Say that in an interview — "I'd use MSDF for anything that scales or moves, and canvas2d upload for static UI, because the browser's shaping and bidi are better than anything I'd write" — and you sound like someone who ships.

---

## Common confusions

**"I'll just `pow(color, 2.2)` to linearize."** That's an approximation of the sRGB curve (which has a small linear segment near black), and more importantly it happens *after* filtering, which is the wrong order. Use the format.

**"My HDR target is `rgba16float`, so I'm doing HDR."** You're doing *high dynamic range storage*. You're doing HDR rendering when your light intensities are physically-scaled and your tonemap is doing real work. A scene where nothing exceeds 1.0 gains nothing from a float target.

**"PBR will make my game look better."** PBR makes materials *consistent*. If your art direction is stylized and your lighting is one directional light, PBR mostly adds cost. Good ambient, good AO, and a good tonemap will move the needle far more.

**"More shadow map resolution will fix my shadow quality."** Only if you're resolution-limited. If the problem is acne, bias tuning fixes it; if it's hard aliased edges, PCF fixes it; if it's shadows shimmering as the camera moves, cascade stabilization (texel snapping) fixes it. Doubling resolution costs 4× memory and fixes exactly one of those three.

**"Ambient occlusion is a post-process."** SSAO is. For voxels it doesn't have to be — you can compute it exactly at mesh generation time, which is cheaper, noise-free, and temporally stable. Reaching for the screen-space version by reflex is exactly the "copied a standard engine pattern" failure the JD warns about.

**"Dithering adds noise, that's bad."** Quantization error is *also* noise — just correlated noise, which the eye reads as structured banding. Dithering trades correlated error for uncorrelated error, and uncorrelated error is invisible. It's strictly better.

---

## The interview answer

***"Walk me through your color pipeline."***

> "Author in sRGB, sample through sRGB formats so the hardware linearizes before filtering — not a `pow` in the shader, because filtering has to happen in linear space. Do all lighting in linear in an HDR float target. Tonemap with something filmic like ACES or AgX. Dither before the 8-bit write to kill banding in skies and fog. Encode back to sRGB via the swapchain format. And data textures — normals, roughness, IDs — stay in non-sRGB formats, because a gamma curve on non-color data silently corrupts it. If lighting looks washed out or muddy, that pipeline is the first place I'd look."

***"How would you do shadows in a voxel game?"***

> "Cascaded shadow maps are the default answer for a directional sun — 3 or 4 cascades, normal-offset bias, hardware PCF through a comparison sampler, and texel snapping so they don't shimmer when the camera moves. But in a voxel world I'd seriously evaluate ray-marching the grid toward the light instead. The world is already an acceleration structure, so you get exact hard shadows with no bias tuning, no cascades, and no resolution artifacts, and you reuse the same DDA traversal as the primary rays — so it's less code, not more. The trade is per-pixel marching cost and divergence, and you only get hard shadows unless you cone-trace. Which one wins depends on world size, view distance, and whether the art direction wants soft shadows."

***"What's the cheapest thing you could do to make a scene look better?"***

> "Fix the tonemap and add ambient occlusion. A filmic curve instead of a clamp is five lines, and for voxels AO is free at meshing time. Those two together do more than any specular model, and both are cheaper than one extra light."

---

## Exercise — Voxelforge, Stage 6

**1. Build a texture atlas as a 2D texture array**, one layer per block type, with a proper mip chain you generate yourself. **Deliberately implement the naive single-atlas version first and screenshot the bleeding artifact** — walk away from the wall until the mips kick in. Once you've seen it, you'll recognize it in someone else's game forever.

**2. Add a directional light** with Lambert diffuse plus **hemisphere** ambient. Then swap the hemisphere for a flat constant and look at both. Note how much of "looks good" is coming from the ambient term.

**3. Convert the pipeline to render into an `rgba16float` HDR target**, then add a tonemap + dither resolve pass. Turn dithering on and off over a gradient sky and look closely at the transitions. Then swap Reinhard for ACES.

**4. Implement a single shadow map with PCF** using a comparison sampler. **Tune the bias until you've personally created both acne *and* peter-panning** — deliberately overshoot in each direction — then fix it properly with normal-offset bias. You cannot understand this trade-off from reading about it.

**5. Now implement shadows the second way:** march a ray through your voxel grid toward the sun. (You'll have the DDA loop after Module 08 — come back to this then.) Compare quality and cost, and **write down which you'd ship and why.** That written justification is exactly the interview answer above, in your own words, backed by your own numbers.

**6. Add MSAA 4×** and observe how much more it helps here than it would in a non-voxel game.

**⭐ Stretch:** implement a 3D LUT color grading pass and grade a screenshot externally. It takes an afternoon and it's the moment you understand why tools work is leverage.

---

## Go deeper

- **Real-Time Rendering, 4th ed.**, Chapters 5–9 — shading, lighting, shadows. The reference.
- **"Physically Based Rendering in Filament"** — google.github.io/filament. **The single best practical PBR document in existence**: complete, honest about its approximations, and full of code you can read. Free.
- **pbr-book.org** (Pharr, Jakob, Humphreys) — free online. Read the BRDF chapters for grounding, not for implementation; it's an offline renderer.
- **John Hable's filmicworlds.com** — tonemapping from the person who popularized filmic curves in games.
- **"Common Techniques to Improve Shadow Depth Maps"** — Microsoft Learn. Old, still the clearest treatment of bias and cascades anywhere.
- **Chris Green, "Improved Alpha-Tested Magnification for Vector Textures" (Valve, 2007)** — the SDF text paper, short and readable. Then `Chlumsky/msdfgen` for the modern version.
- **Alan Wolfe's blog (blog.demofox.org)** on blue noise and dithering — genuinely deep, very readable, and full of runnable code.
- **Jorge Jimenez, "Next Generation Post Processing in Call of Duty: Advanced Warfare"** (SIGGRAPH) — the bloom approach everyone now uses.

---

**Next:** [Module 07 — Voxel Data Structures](./07-voxel-data-structures.md)
