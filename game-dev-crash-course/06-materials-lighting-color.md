# Module 06 — Materials, Lighting, and Color

### Textures and sampling, the lighting equation, shadow techniques, and the color-space discipline that separates a professional image from an amateur one

*~13 min read · Part II: Rendering · Prerequisites: Modules 02–05*

---

You can now put geometry on screen. This module is about making it look like something.

The Engine JD lists this territory explicitly: *"space transformations, textures and sampling, lighting equations, shadow techniques, light sources, particle systems, post-processing, dithering, text rendering."* Space transformations were Module 02; particles are Module 14. Everything else is here.

---

## Textures and sampling

A texture is an array of texels; a **sampler** is the rule for turning a continuous UV coordinate into a color. They are separate objects in WebGPU precisely because the same texture is often sampled different ways.

**Filtering.**
- `nearest` — pick the closest texel. Blocky, exact. The correct choice for pixel-art and for any data texture where interpolating between values is meaningless (voxel material IDs, palette indices, tile maps).
- `linear` — blend the 4 nearest texels (bilinear), free in hardware.
- **Trilinear** — linear within a mip level, plus linear *between* two mip levels. Kills the visible seam where mip levels change.
- **Anisotropic** — multiple taps along the projected footprint. Fixes the blurring of surfaces viewed at a grazing angle (floors, walls receding into the distance). Cheap relative to its impact; `maxAnisotropy: 4–16` on world textures is standard.

**Address modes** — `repeat`, `clamp-to-edge`, `mirror-repeat` — decide what happens outside `[0,1]`. Getting this wrong on a texture atlas causes **bleeding**: at low mip levels, neighboring tiles blend into each other and you see a halo of the wrong material at block edges. This is *the* classic voxel-texturing bug.

The three real fixes, in increasing order of quality:
1. Add a padding gutter around each atlas tile (helps, doesn't fully solve — deep mips still bleed).
2. Use a **2D texture array** (`texture_2d_array`), one layer per material. Each layer mips independently, so no cross-material bleeding at all. This is the right answer for voxel games and what you should propose.
3. Clamp UVs manually in the shader against a per-tile rect (works, costs ALU and defeats hardware wrapping).

**Mipmaps** are not optional. Without them, minified textures alias horribly *and* thrash the texture cache, because neighboring pixels sample distant texels. WebGPU does not generate mips for you — you write a small compute or render-based downsample chain, or use a utility. Do it once, keep it in your engine.

**Format choice is a bandwidth decision.** `rgba8unorm` for color, `r8uint` for voxel IDs, `rg11b10ufloat` or `rgba16float` for HDR intermediates, `depth32float` for depth. Block-compressed formats (BC/ASTC/ETC) are 4–8× smaller and stay compressed in VRAM, so they save bandwidth *and* memory — but WebGPU exposes them as optional features (`texture-compression-bc`, `-etc2`, `-astc`), so you must query support and ship multiple variants. For a voxel game with small, stylized textures, uncompressed atlases in a texture array are often perfectly fine and much simpler.

---

## The lighting equation, built up

Start with the simplest model and understand what each term buys.

**Lambert (diffuse).** Light scattered equally in all directions. Brightness depends only on the angle between surface normal and light direction:

```wgsl
let ndotl = max(dot(N, L), 0.0);
let diffuse = albedo * lightColor * ndotl;
```

That `max(…, 0.0)` is not a detail — negative values mean the surface faces away, and letting them through produces black artifacts on the back side.

**Blinn-Phong (specular).** Adds a highlight using the half-vector between view and light:

```wgsl
let H = normalize(L + V);
let spec = pow(max(dot(N, H), 0.0), shininess);
```

Cheap, controllable, and completely adequate for a stylized game. Do not assume you need PBR.

**Physically based rendering (PBR)** parameterizes materials by **base color, metallic, roughness** and evaluates a microfacet BRDF (typically GGX/Trowbridge-Reitz distribution, Smith geometry term, Schlick Fresnel). Its virtues are consistency — materials look right under any lighting, and artists can author once — and energy conservation.

Know the terms and what they do. But also know the judgment call: **PBR is a means, not a goal.** For a hard-edged stylized voxel game, a well-tuned Lambert + rim light + strong ambient occlusion + a good tonemap will often beat a mediocre PBR implementation, and costs a fraction as much. Being able to say that — and to say *when* you'd choose PBR — is more valuable than reciting the GGX formula.

**Light types:**
- **Directional** — infinitely far, parallel rays, one direction. The sun. No attenuation.
- **Point** — position + radius, attenuates by roughly inverse-square. Use a smooth windowed falloff that reaches exactly zero at the radius, or lights will pop when culled.
- **Spot** — point light with a cone; add inner/outer angle smoothstep.
- **Area** — physically correct, expensive; approximated in real time (LTC, or just faked with multiple point lights).
- **Ambient / IBL** — everything else. The cheap version is a constant or a hemisphere gradient (sky color from above, ground bounce from below). The good version is an environment cubemap, prefiltered by roughness, plus a BRDF lookup table.

**Ambient is where cheap games get expensive-looking.** A flat ambient term makes everything look like a 1998 render. A hemisphere ambient plus **ambient occlusion** — darkening in creases and corners — does more for perceived quality than any specular model. For voxels this is a gift: AO can be computed *exactly and for free* at meshing time by checking the 3 neighbors around each vertex corner, packed into 2 bits per vertex. That technique is a large part of why Minecraft-lineage renderers look as good as they do at their cost. Module 08 covers it.

---

## Shadow techniques

Shadows are visibility queries: *is there anything between this point and the light?*

**Shadow mapping** is the standard answer. Render depth from the light's point of view into a texture; when shading, transform the point into light space and compare its depth against the stored value. Further away than what the light saw → in shadow.

The problems, all of which you must be able to name:

**Shadow acne.** Self-shadowing stripes from depth-comparison precision at grazing angles. Fixes: a constant + slope-scaled **depth bias**, and/or **front-face culling** during the shadow pass (render back faces into the shadow map so the bias error lands inside the object). Too much bias causes **peter-panning** — the shadow detaches from the object's feet. Every shadow implementation is a negotiation between these two artifacts. Normal-offset bias (offset the sample position along the surface normal rather than in depth) is the modern, better-behaved option.

**Hard, aliased edges.** The shadow map has finite resolution. **PCF** (percentage-closer filtering) takes multiple depth comparisons in a small kernel and averages the *results* (not the depths — averaging depths is wrong). WebGPU supports this directly via **comparison samplers** (`sampler_comparison` + `textureSampleCompare`), which do the compare-then-filter in hardware. Poisson-disk or rotated-kernel sampling reduces banding for larger kernels.

**Not enough resolution across a large view.** **Cascaded Shadow Maps (CSM)**: split the view frustum into 3–4 depth ranges and render a shadow map for each, so nearby geometry gets high resolution. Blend between cascades to hide the transition. This is the industry standard for directional sunlight and what you'd implement for an outdoor voxel world.

**Alternatives worth knowing:** variance/exponential shadow maps (softer, prone to light leaking), ray-traced shadows (exact, expensive), and — highly relevant here — **voxel ray-marched shadows**. If your world is already a voxel grid, you can march a ray from the surface toward the light through the grid and get an exact hard shadow with no shadow map, no bias, no cascades, and no resolution artifacts at all. That is a real architectural advantage of voxel worlds and exactly the kind of "purpose-built rather than copied" thinking the Engine JD asks for.

---

## Color: the discipline most people skip

This is where "why does my lighting look muddy and my edges look wrong" gets answered.

**sRGB is not linear.** Displays and image files encode color with roughly a 2.2 gamma curve, because human perception of brightness is nonlinear and this uses 8-bit precision efficiently. **Light math is only correct in linear space.** Adding two lights, multiplying by an occlusion factor, interpolating across a triangle — all of it must happen on linear values.

The correct pipeline:

1. Author textures in sRGB.
2. Sample them through an **sRGB texture format** (`rgba8unorm-srgb`) so the hardware converts to linear for free during sampling. Do *not* do `pow(color, 2.2)` in the shader — you lose the hardware's filtering correctness, because filtering must also happen in linear space.
3. Do all lighting math in linear space, in an HDR render target (`rgba16float`).
4. **Tonemap** HDR → LDR at the end.
5. Encode back to sRGB for display (again, via the format).

**Data textures must NOT be sRGB.** Normal maps, roughness maps, masks, voxel material IDs — these are data, not color. Tagging them sRGB silently corrupts their values. This mistake is extremely common and extremely hard to spot.

**Tonemapping** maps unbounded HDR to `[0,1]`. `color / (1 + color)` (Reinhard) is the one-liner; ACES or the AgX curve are the modern filmic choices, preserving saturation in highlights instead of blowing them to white. This one shader function has an enormous effect on whether your game looks like a game or a tech demo.

**Dithering** — which the JD calls out by name — is adding a small, structured noise pattern before quantizing to 8 bits. It converts visible **banding** (those ugly concentric rings in smooth gradients, skies, and fog) into imperceptible noise. It costs almost nothing:

```wgsl
// Ordered/bayer or blue-noise dither before the final 8-bit write
let dither = (bayer4x4(vec2u(fragCoord.xy)) - 0.5) / 255.0;
return vec4f(tonemapped + dither, 1.0);
```

Blue noise is better than a Bayer matrix (less structured, better under temporal accumulation). Dithering is also used for **stochastic alpha** (dissolve effects, LOD transitions, and hiding geometry near the camera) — you `discard` fragments based on a noise threshold instead of blending, which keeps everything in the opaque pass. For a stylized voxel game with a limited palette, deliberate dithering can also be an **art-direction choice**, not just an artifact fix.

---

## Post-processing

After the scene renders to an HDR target, you run full-screen passes:

- **Bloom** — extract bright pixels, downsample-blur through a mip chain, add back. The modern approach (Jimenez/COD) uses progressive down- and up-sampling with a tent filter, which is cheaper and more stable than a wide Gaussian.
- **Tonemap + color grading** — usually via a 3D LUT so artists can grade in Photoshop/Resolve and hand you a `.cube` file. Giving artists a LUT pipeline is one of the highest-value tools you can build.
- **Antialiasing** — MSAA (geometric edges only, great for voxels), FXAA (cheap post filter), or **TAA** (temporal: jitter the projection each frame and accumulate with reprojection). TAA is the modern default and also the enabler for stochastic techniques (it converts noise into detail over time), but it introduces ghosting and requires motion vectors, a history buffer, and neighborhood clamping.
- **Fog, depth of field, motion blur, vignette, chromatic aberration** — see Module 14.

**The performance rule for post-processing:** each pass is a full read and write of the framebuffer, so passes are **bandwidth**-bound, not ALU-bound. Merging several effects into one shader is usually a bigger win than optimizing any of them. And doing bloom's blur at quarter resolution costs 1/16th the bandwidth for a visually identical result.

---

## Text rendering

Deceptively hard, and specifically called out in the JD.

- **Bitmap fonts / texture atlases** — fastest, but blurry when scaled and needs a separate atlas per size.
- **SDF (signed distance field)** — store distance-to-glyph-edge in a texture; threshold it in the shader with `smoothstep`. One atlas scales to any size, and you get outlines, glows, and drop shadows nearly free by thresholding at different distances. Corners round off slightly.
- **MSDF (multi-channel SDF)** — encodes distances in RGB channels to preserve sharp corners. This is the current best practice for game UI text, via `msdfgen` / `msdf-atlas-gen`.
- **Vector/GPU curve rasterization** — highest quality, most complex.

Beyond the rendering itself, real text has requirements people forget until localization: shaping (HarfBuzz), bidi, complex scripts, CJK atlas sizes, kerning, line breaking. In a browser engine you *can* cheat by rendering text to a `<canvas2d>` and uploading it as a texture — a completely legitimate pragmatic choice for a small studio, and a good example of knowing when not to build something.

---

## The interview answer

*"Walk me through your color pipeline."*

> "Author in sRGB, sample through sRGB formats so the hardware linearizes before filtering, do all lighting in linear space in an HDR float target, tonemap with something filmic, dither before the 8-bit write to kill banding, and encode back to sRGB via the swapchain format. Data textures — normals, roughness, IDs — stay in non-sRGB formats. If lighting looks washed out or muddy, that pipeline is where I'd look first."

*"How would you do shadows in a voxel game?"*

> "Cascaded shadow maps are the default answer for a directional sun, with slope-scaled or normal-offset bias and hardware PCF via comparison samplers. But in a voxel world I'd seriously evaluate ray-marching the grid toward the light instead — the world is already an acceleration structure, so you get exact hard shadows with no bias tuning, no cascades, and no resolution artifacts, and you can reuse the same traversal code as the primary rays. Which one wins depends on world size, view distance, and whether we want soft shadows."

---

## Exercise — Voxelforge, Stage 6

1. Build a texture atlas as a **2D texture array**, one layer per block type, with a proper mip chain you generate yourself. Deliberately implement the naive single-atlas version first and photograph the bleeding artifact, so you recognize it forever.
2. Add a directional light with Lambert diffuse plus hemisphere ambient. Compare to flat ambient.
3. Convert the pipeline to render into an `rgba16float` HDR target, then add a tonemap + dither resolve pass. Turn dithering on and off over a gradient sky and look closely.
4. Implement a single shadow map with PCF using a comparison sampler. Tune bias until you've personally created both acne *and* peter-panning; then fix it with normal-offset bias.
5. Now implement shadows the second way: march a ray through your voxel grid toward the sun (you'll have the DDA loop after Module 08). Compare quality and cost. Write down which you'd ship and why.
6. Add MSAA 4× and observe how much more it helps here than it would in a non-voxel game.

---

## Go deeper

- **Real-Time Rendering, 4th ed.**, Chapters 5–9 — shading, lighting, shadows. The reference.
- **pbr-book.org** (Pharr, Jakob, Humphreys) — free online. Read the BRDF chapters for grounding, not implementation.
- **"Physically Based Rendering in Filament"** — google.github.io/filament. The single best practical PBR document in existence: complete, honest about approximations, and full of code.
- **Nathan Reed, "Rendering Fundamentals"** posts and **John Hable's filmicworlds.com** — tonemapping and color, from people who shipped it.
- **"Common Techniques to Improve Shadow Depth Maps"** — Microsoft Learn. Old, still the clearest treatment of bias and cascades.
- **Chris Green, "Improved Alpha-Tested Magnification for Vector Textures" (Valve, 2007)** — the SDF text paper. Then `Chlumsky/msdfgen` for the modern version.
- **Alan Wolfe's blog (blog.demofox.org)** on blue noise and dithering — genuinely deep and very readable.

---

**Next:** [Module 07 — Voxel Data Structures](./07-voxel-data-structures.md)
