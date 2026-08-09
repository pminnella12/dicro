# Module 14 — Tech Art, VFX, and Art Direction

### Particles, fog, decals, spell effects, and the discipline of turning "it should feel more magical" into shipped systems

*~13 min read · Part IV: Engine Breadth · Prerequisites: Modules 04–06*

---

The Engine JD lists **tech art** as a bonus area — *"particle effects, fog, clouds, magic/spell vfx, decals, ailment indications"* — and separately lists **art direction**: *"Bitshift is crafting the art direction simultaneously with its technical direction. A good eye for what looks good and how to translate that into technical systems is a big bonus."*

Tech art is the seam between the two disciplines. It's where an engineer stops asking "is this correct?" and starts asking "does this read?" — and where a huge amount of a game's perceived quality actually lives.

> Players do not notice your BRDF. They notice that the fireball has weight, that the hit landed, and that the cave feels deep.

---

## Particle systems

The workhorse of game VFX: many small quads, spawned by emitters, animated over their lifetime.

**The data model.** An emitter defines spawn rate, initial velocity distribution, lifetime, and curves for size/color/rotation/velocity over normalized age. A particle carries position, velocity, age, and per-particle random seeds. Everything else is derived from age via curves — which is what lets artists tune behavior without touching code.

**CPU vs GPU simulation:**

- **CPU** — flexible, easy to make interact with gameplay (collisions, targeting, spawn-on-event), but costs main-thread time and requires uploading positions every frame. Fine to a few thousand particles.
- **GPU** — simulate in a compute shader against a particle buffer; the CPU only spawns. Scales to hundreds of thousands. Interaction with gameplay gets harder, and you need indirect draw or a fixed max count since the CPU doesn't know how many are alive.

For a WebGPU engine, the natural design is: CPU-side emitters push spawn requests into a GPU buffer, a compute shader integrates and compacts the live set, and rendering uses an indirect draw with the count written by that compute pass. Note the Module 09 caveat — no multi-draw indirect — so keep particles in a small number of large batches rather than one draw per emitter.

**Rendering:**

- **Billboards** (camera-facing quads) are the default. Velocity-aligned stretching sells speed; axis-aligned billboards suit beams and trails.
- **Additive blending** for light-emitting effects — fire, sparks, magic — because addition is commutative and therefore **order-independent**, which sidesteps the entire sorting problem from Module 04.
- **Alpha blending** for smoke and dust, which *does* require back-to-front sorting.
- **Soft particles** — fade the particle where it intersects opaque geometry, by comparing the particle's depth against the depth buffer. This removes the hard intersection line that instantly reads as "cheap," and costs about four lines of shader.
- **Overdraw is the killer.** A screenful of large, overlapping, alpha-blended particles can cost more than your entire scene. The mitigations: render particles at half resolution and upsample, cap on-screen particle area, use fewer/larger particles with better textures rather than many small ones, and keep the shader trivially cheap since it runs on an enormous number of fragments.

**Voxel-native particles** deserve mention: instead of textured billboards, spawn actual small cubes. It costs more geometry but matches the aesthetic perfectly and gives you free rotation and lighting consistency. For destruction debris in a voxel game it's often the right answer — the chunks flying off a wall are literally the voxels that were there.

---

## Fog, volumetrics, and depth

Fog does more work for atmosphere and readability than almost anything else, and the cheap versions are very cheap.

**Distance fog** — `mix(sceneColor, fogColor, f(depth))`, with exponential or exponential-squared falloff. Two lines. Beyond the atmosphere it buys, it hides your far clip plane and lets you reduce view distance without it reading as pop-in.

**Height fog** — density varies with world Y. Pools in valleys, fills dungeon floors, sells verticality. Still nearly free.

**Volumetric lighting (god rays)** — light scattering in participating media. The good implementation is a **froxel** (frustum-voxel) grid: a 3D texture aligned to the view frustum, populated by a compute pass with density and in-scattered light per cell, integrated front-to-back along each ray, then applied. Typically run at a low resolution (e.g. 160×90×64) and temporally jittered plus accumulated, because the noise from low sample counts is far cheaper to filter than to eliminate.

**A voxel engine has an unusual advantage here**: you already have a 3D grid and a ray-marcher. Volumetrics can reuse the same traversal code and the same acceleration structure. Recognizing that kind of reuse — where the game's core data structure makes a normally-expensive feature cheap — is precisely the "purpose-built tech" thinking the JD is describing.

**Clouds** are volumetric rendering at sky scale: ray-march a noise field (typically Worley + Perlin at multiple scales), with a cheap single-scattering approximation and heavy temporal reprojection. Expensive, beautiful, and — for a first-person dungeon crawler — probably not where your budget should go. Knowing that *and saying so* is better than knowing the technique.

---

## Decals

Decals project imagery onto existing geometry: scorch marks, blood, bullet holes, graffiti, puddles.

**Mesh decals** — generate geometry clipped to the receiving surface. Precise, but needs the receiver's geometry and creates z-fighting you fix with a depth bias or polygon offset.

**Deferred/screen-space decals** — render a box volume; for each covered pixel, reconstruct the world position from the depth buffer, transform it into the decal's local space, reject if outside the unit cube, and blend the decal's texture into the G-buffer. No geometry clipping, works on anything, and is the standard modern approach. Needs a G-buffer, and struggles with normals at grazing angles unless you reject by comparing the surface normal to the decal's projection axis.

**In a voxel world there's a third option that's often better:** since the world is a grid of discrete cells with known faces, a "decal" can just be per-face state — a scorch flag on a voxel face, looked up during shading. Exact, persistent, free at render time, and it survives destruction correctly. This is another case where the general solution is more machinery than the voxel-native one.

---

## Spell and ability VFX: the craft

The Engine JD says *"magic/spell vfx"* and *"ailment indications,"* which is the language of a game with abilities and status effects. The engineering questions this raises:

**VFX must be data-driven and hot-reloadable.** An effect is a small graph: emitters, curves, sub-effects, timing offsets, sounds, camera shake. If changing a fireball's color requires a rebuild, the fireball will stay mediocre. If an artist can tweak and see it in two seconds, it will be iterated fifty times. **The tool is the feature.**

**Effects need lifecycle and attachment.** Attach to an entity, a bone, a world position, or a moving projectile. Follow, or spawn-and-detach. Handle the source dying mid-effect — an orphaned effect that follows a deleted entity to the origin is a bug every VFX system has shipped at least once.

**Budgets and LOD.** Cap concurrent effects; degrade gracefully by distance (fewer particles, no sub-emitters, no lights) and by importance (the player's own abilities never degrade; a distant enemy's do). Without this, a busy fight tanks the frame rate exactly when responsiveness matters most.

**The anatomy of an effect that reads well** — worth knowing, because it's what an art director will ask you to support:

- **Anticipation** — a windup before the payoff. Without it, effects feel like they came from nowhere.
- **Impact** — a sharp, bright, brief peak. Screen shake, a hit-stop of 2–4 frames, a flash.
- **Decay** — a longer, softer falloff. Smoke lingers after the fire.
- **Silhouette and readability** — in a busy fight, the shape must be recognizable at a glance and distinguishable from other effects. This is a gameplay requirement, not an aesthetic one.
- **Color language** — consistent meaning across the game (this hue means "damage to you," that one means "you did damage"). Enforce it in the tool.

**Ailment indications** are pure readability engineering: the player must know their status at a glance, without reading text. The typical toolkit is a shader-level overlay on the affected entity (a tint, a pattern, a fresnel rim), a persistent particle or aura, a screen-edge vignette for effects on the player, and a UI icon as backup. The engine requirement this generates is a **per-entity material parameter channel** — a small set of values (tint, effect mask, intensity) that gameplay can set and shaders read. Design that channel early; retrofitting "make this entity glow purple" into a renderer that has no per-entity parameters is painful.

---

## Screen-space effects and camera feel

Cheap, high-impact, and mostly not about rendering at all:

- **Screen shake** — trauma-based (accumulate a `trauma` value, shake by `trauma²` with smooth noise, decay over time) rather than random jitter. Squared falloff is what makes it feel physical rather than buzzy.
- **Hit stop / frame freeze** — pause simulation for 2–5 frames on impact. Almost nothing sells weight better, and it costs one boolean.
- **Chromatic aberration and radial blur** on a strong hit or dash.
- **Damage vignette** — a red screen edge that also communicates direction.
- **Camera kick and FOV punch** on impacts and speed.
- **Flash frames** — one or two frames of a solid bright color on a big impact.

These are largely gameplay features implemented in rendering. They live in the same territory as coyote time and input buffering from Module 12: small, cheap lies that make the game feel good. Being the engineer who suggests them — and who builds them as tunable, data-driven systems — is disproportionately valuable at a small studio.

---

## Translating art direction into systems

This is the skill the JD is naming when it asks for "a good eye for what looks good and how to translate that into technical systems."

**The pattern:** an art director says something impressionistic — "the dungeons should feel oppressive," "magic should feel dangerous, not pretty," "I want it to look like a painting, not a render." Your job is to decompose that into knobs.

"Oppressive dungeons" might decompose into: low ambient light with strong local light falloff; height fog that pools at the floor; a desaturated palette with a single accent hue; heavy per-vertex AO in corners; a tighter FOV; and a limited view distance that the fog makes feel intentional. Each is a system with parameters, and each parameter is something the art team can then own.

**The three questions that make this concrete:**

1. **What is the visual reference?** Get images. Ambiguity dies on contact with a mood board.
2. **What are the two or three parameters that produce most of the effect?** Build those first, expose them, iterate.
3. **What does it cost, and what does it cost to change later?** A choice baked into the asset pipeline is expensive to reverse; a runtime parameter is cheap.

**Prototype fast and ugly.** A hardcoded, hacked-in version that an art director can look at *today* is worth more than a well-architected system next week — because the answer to "is this the right direction?" is usually no, and you want to find out cheaply. Then build the real system once the direction is settled. This is a genuine cultural expectation in game development, and it's often the biggest adjustment for engineers arriving from product software, where the reverse discipline is correct.

**A useful reframing for the whole discipline:** in product engineering, the spec is the input and the implementation is the deliverable. In tech art, **the implementation is how the spec gets discovered.** The loop is the point.

---

## The interview answer

*"How would you approach VFX for a voxel game?"*

> "Data-driven effect definitions with hot reload, because the tool is what determines quality — an artist iterating in seconds will produce something ten times better than one waiting on builds. GPU-simulated particles in a compute shader with indirect draw for scale, additive blending for light-emitting effects so ordering isn't a problem, soft particles against the depth buffer, and a hard budget with distance-based degradation so a busy fight doesn't tank the frame. For a voxel aesthetic I'd seriously look at actual voxel-cube particles for debris rather than billboards — it matches the art and it's literally the material that was destroyed. And I'd design a per-entity material parameter channel early, because ailment indications and hit flashes all need it and retrofitting that is painful."

*"An art director says the caves should feel deeper. What do you do?"*

> "Ask for reference images first. Then decompose it into the two or three parameters that carry most of the effect — probably light falloff, height fog density, and ambient occlusion strength — hack them in hardcoded the same day so we can look at it together, and only build the real system once we agree on the direction."

---

## Exercise — Voxelforge, Stage 14

1. Build a **GPU particle system**: compute-shader simulation, compaction, indirect draw. Support additive and alpha modes, and curves for size/color over lifetime.
2. Add **soft particles** using the depth buffer. Toggle it on and off against a floor and observe the difference — this is a 4-line change with an outsized effect.
3. Add **voxel-cube debris**: when a block breaks, spawn small cubes that inherit the block's material, arc under gravity, and collide with the world using Module 12's grid collision.
4. Implement **exponential distance fog and height fog**, exposed as hot-reloadable parameters. Then implement a low-resolution **froxel volumetric pass** that reuses your voxel ray-marcher, and compare the cost.
5. Implement **screen shake** with trauma-squared falloff and **hit stop**. Tune them until a block break feels good. Notice how much of "feel" is these two things.
6. Add a **per-entity material parameter channel** (tint, effect mask, intensity) and use it to build a damage flash and a poison overlay.
7. Pick a mood — "oppressive," "ethereal," whatever — and tune your fog, ambient, palette, and AO parameters to hit it. Screenshot before and after. **This exercise is the actual job.**

---

## Go deeper

- **Simon Trümpler's simonschreibt.de** — "Game Art Tricks." The best resource for seeing how shipped effects actually work, reverse-engineered with pictures.
- **Jan Willem Nijman, "The Art of Screenshake" (2013)** — 25 minutes, and it will change how you think about feel. Watch it today.
- **Steve Swink, *Game Feel*** — the book-length treatment.
- **Sébastien Hillaire, "Physically Based Sky, Atmosphere and Cloud Rendering in Frostbite" (SIGGRAPH 2016)** — the reference for volumetrics and froxel grids.
- **Bart Wronski's blog (bartwronski.com)** — volumetric fog, temporal techniques, and unusually rigorous thinking about sampling.
- **"Real-Time VFX" community (realtimevfx.com)** — where VFX artists talk shop. Read it to learn what artists actually need from you.
- **Inigo Quilez (iquilezles.org)** — SDFs, noise, procedural patterns, and shader craft. Endlessly useful.
- **Unreal Niagara and Unity VFX Graph documentation** — not to copy, but to see what a mature, artist-facing VFX tool exposes. It's a specification for your own tool's feature set.

---

**Next:** [Back to the course index](./00-README.md) — or, if you've done the exercises, you have a voxel engine. Go make it look good.
