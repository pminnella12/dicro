# Module 14 — Tech Art, VFX, and Art Direction

### Particles, fog, decals, spell effects, and the discipline of turning "it should feel more magical" into shipped systems

*~26 min read · Part IV: Engine Breadth · Prerequisites: Modules 04–06*

---

## Read this first

The Engine JD lists **tech art** as a bonus area — *"particle effects, fog, clouds, magic/spell vfx, decals, ailment indications"* — and separately lists **art direction**:

> *"Bitshift is crafting the art direction simultaneously with its technical direction. A good eye for what looks good and how to translate that into technical systems is a big bonus."*

**Tech art is the seam between the two disciplines.** It's where an engineer stops asking *"is this correct?"* and starts asking *"does this read?"* — and where a huge amount of a game's perceived quality actually lives.

> Players do not notice your BRDF. They notice that the fireball has weight, that the hit landed, and that the cave feels deep.

This module is also the one most likely to feel foreign coming from product engineering, because **the deliverable is a feeling, discovered iteratively**, not a spec implemented correctly. The last section says more about that; keep it in mind while reading the rest.

---

## Particle systems

The workhorse of game VFX: many small quads, spawned by emitters, animated over their lifetime.

### The data model

An **emitter** defines:
- Spawn rate (or a burst count)
- Initial velocity distribution (a cone, a sphere, a direction plus randomness)
- Lifetime
- **Curves** for size, color, rotation, and velocity over *normalized age* (0 → 1)

A **particle** carries position, velocity, age, and per-particle random seeds. **Everything else is derived from age via curves** — which is what lets artists tune behavior without touching code.

That last sentence is the design insight. If size is a curve, an artist changes a graph. If size is a formula in a shader, an artist files a ticket.

### CPU vs GPU simulation

| | CPU | GPU |
|---|---|---|
| Flexibility | High — easy gameplay interaction | Lower — gameplay coupling is hard |
| Collision with world | Easy | Hard (needs the world on the GPU — which you have!) |
| Spawn on event | Trivial | Needs a spawn buffer |
| Scale | A few thousand | Hundreds of thousands |
| Per-frame cost | Main-thread time + upload every frame | Compute dispatch only |
| Knowing the live count | CPU knows | Needs indirect draw or a fixed max |

**For a WebGPU engine, the natural design is:**

1. CPU-side emitters push **spawn requests** into a GPU buffer.
2. A compute shader integrates the live particles and **compacts** the live set (using atomics — Module 03).
3. Rendering uses an **indirect draw** with the instance count written by that compute pass.

Note the Module 09 caveat — **no multi-draw indirect in WebGPU** — so keep particles in a small number of large batches rather than one draw per emitter. That's a design constraint worth stating up front rather than discovering.

### Rendering

**Billboards** (camera-facing quads) are the default. Two variants worth knowing:
- **Velocity-aligned stretching** — stretch the quad along the velocity vector. Sells speed for sparks and bullets, and it's a couple of lines in the vertex shader.
- **Axis-aligned billboards** — rotate around one fixed axis only. Right for beams, trails, and anything that should stay vertical.

**Additive blending** for light-emitting effects — fire, sparks, magic. **Addition is commutative, therefore order-independent**, which sidesteps the entire sorting problem from Module 04. This is why so much VFX is additive: not aesthetics, but the fact that you get to skip a hard problem.

**Alpha blending** for smoke and dust, which *does* require back-to-front sorting.

**Soft particles** — fade the particle where it intersects opaque geometry:

```wgsl
let sceneDepth    = textureLoad(depthTex, vec2i(pos.xy), 0);
let sceneLinear   = linearizeDepth(sceneDepth);
let particleLinear = linearizeDepth(pos.z);
let fade = saturate((sceneLinear - particleLinear) / SOFT_DISTANCE);
color.a *= fade;
```

**This removes the hard intersection line that instantly reads as "cheap," and costs about four lines of shader.** It is the single best quality-per-line change in this module. A smoke plume with a razor-sharp line where it meets the floor looks like 2004; the same plume with soft particles looks contemporary.

### Overdraw is the killer

**A screenful of large, overlapping, alpha-blended particles can cost more than your entire scene.** (Module 04: transparency loses Early-Z, so every layer is fully shaded.) A single explosion filling the screen with 40 overlapping smoke sprites is 40× the fragment cost of the whole background.

The mitigations, in order of effectiveness:
- **Render particles at half resolution and upsample** (with a depth-aware bilateral upsample so edges don't bleed). ¼ the fragments.
- **Cap on-screen particle area**, not just particle count. Ten huge particles cost more than a thousand tiny ones.
- **Use fewer, larger particles with better textures** rather than many small ones. This is also usually better-looking.
- **Keep the shader trivially cheap.** It runs on an enormous number of fragments; every instruction is multiplied by millions.

### Voxel-native particles

Instead of textured billboards, **spawn actual small cubes.**

It costs more geometry but **matches the aesthetic perfectly** and gives you free rotation and lighting consistency — a cube lit by your normal lighting path looks like it belongs, where a billboard has to fake it.

**For destruction debris in a voxel game it's often the right answer** — the chunks flying off a wall are *literally the voxels that were there*, with the same material and color. That's both cheaper (no VFX authoring) and more correct (destroy a red wall, get red debris, automatically).

This is another instance of the recurring theme: **the voxel-native version of a standard technique is frequently simpler and better.**

---

## Fog, volumetrics, and depth

Fog does more work for atmosphere and readability than almost anything else, and the cheap versions are **very** cheap.

**Distance fog** — `mix(sceneColor, fogColor, f(depth))`, with exponential or exponential-squared falloff:

```wgsl
let fogFactor = 1.0 - exp(-density * depth * depth);   // exp2 falloff
color = mix(color, fogColor, fogFactor);
```

Two lines. Beyond the atmosphere it buys, **it hides your far clip plane and lets you reduce view distance without it reading as pop-in** — which is a direct performance win disguised as an art feature. Reducing draw distance by 30% is free if fog makes the boundary invisible.

**Height fog** — density varies with world Y. Pools in valleys, fills dungeon floors, sells verticality. Still nearly free, and for a dungeon crawler it's probably your highest-value atmospheric feature.

**Volumetric lighting (god rays)** — light scattering in participating media, so you see the *beam* and not just what it illuminates.

The good implementation is a **froxel grid** ("frustum voxel"): a 3D texture aligned to the view frustum — X and Y across the screen, Z along depth — populated by a compute pass with density and in-scattered light per cell, then integrated front-to-back along each ray and applied to the scene.

Typically run at a **low resolution** (e.g. 160×90×64) and **temporally jittered plus accumulated**, because the noise from low sample counts is far cheaper to filter than to eliminate. (Module 06's TAA is the enabler again.)

> **A voxel engine has an unusual advantage here:** you already have a 3D grid and a ray-marcher. Volumetrics can reuse the same traversal code and the same acceleration structure.

**Recognizing that kind of reuse — where the game's core data structure makes a normally-expensive feature cheap — is precisely the "purpose-built tech" thinking the JD is describing.** It's the same observation as ray-marched shadows (Module 06) and it's worth having both examples ready.

**Clouds** are volumetric rendering at sky scale: ray-march a noise field (typically Worley + Perlin at multiple scales), with a cheap single-scattering approximation and heavy temporal reprojection. Expensive, beautiful, and — **for a first-person dungeon crawler — probably not where your budget should go.**

**Knowing that *and saying so* is better than knowing the technique.** Being the engineer who says "that's a two-week feature and we're underground" is more valuable than being the one who builds it.

---

## Decals

Decals project imagery onto existing geometry: scorch marks, blood, bullet holes, graffiti, puddles.

**Mesh decals** — generate geometry clipped to the receiving surface, offset slightly toward the camera. Precise, but needs access to the receiver's geometry and creates z-fighting you fix with a depth bias or polygon offset (Module 02's depth precision, again).

**Deferred / screen-space decals** — the standard modern approach:

1. Render a box volume (a unit cube transformed to the decal's placement).
2. For each covered pixel, **reconstruct the world position from the depth buffer**.
3. Transform that position into the decal's local space.
4. **Reject if outside the unit cube.**
5. Blend the decal's texture into the G-buffer.

No geometry clipping, works on anything, and doesn't care what it lands on. Needs a G-buffer (Module 04), and struggles with grazing angles — a decal box intersecting a floor will smear down an adjacent wall unless you **reject by comparing the surface normal against the decal's projection axis.**

**In a voxel world there's a third option that's often better.** Since the world is a grid of discrete cells with known faces, a "decal" can just be **per-face state** — a scorch flag on a voxel face, looked up during shading.

- Exact (no projection artifacts, no grazing-angle smear)
- Persistent (it's world data, not a transient effect)
- **Free at render time** (it's a bit you were already reading)
- **Survives destruction correctly** (break the block, the scorch goes with it)

**Another case where the general solution is more machinery than the voxel-native one.** You're accumulating a pattern here worth naming explicitly in an interview.

---

## Spell and ability VFX: the craft

The Engine JD says *"magic/spell vfx"* and *"ailment indications,"* which is the language of a game with abilities and status effects. Here are the engineering questions that raises.

### VFX must be data-driven and hot-reloadable

An effect is a small graph: emitters, curves, sub-effects, timing offsets, sounds, camera shake.

**If changing a fireball's color requires a rebuild, the fireball will stay mediocre. If an artist can tweak and see it in two seconds, it will be iterated fifty times.**

> **The tool is the feature.**

This is Module 11's thesis, and it applies more sharply here than anywhere else, because VFX quality is almost entirely a function of iteration count.

### Effects need lifecycle and attachment

Attach to an entity, a bone, a world position, or a moving projectile. Follow, or spawn-and-detach.

**Handle the source dying mid-effect.** An orphaned effect that follows a deleted entity to the world origin — so every explosion in the game briefly appears at (0,0,0) — is a bug **every VFX system has shipped at least once.** Design the detach semantics up front.

### Budgets and LOD

Cap concurrent effects. Degrade gracefully:
- **By distance** — fewer particles, no sub-emitters, no dynamic lights
- **By importance** — the player's own abilities never degrade; a distant enemy's do

**Without this, a busy fight tanks the frame rate exactly when responsiveness matters most** — which is the worst possible correlation between load and need.

### The anatomy of an effect that reads well

Worth knowing, because it's what an art director will ask you to support:

| Beat | What it does |
|---|---|
| **Anticipation** | A windup before the payoff. Without it, effects feel like they came from nowhere. |
| **Impact** | A sharp, bright, **brief** peak. Screen shake, hit-stop of 2–4 frames, a flash. |
| **Decay** | A longer, softer falloff. Smoke lingers after the fire. |
| **Silhouette / readability** | The shape must be recognizable at a glance in a busy fight, and distinguishable from other effects. **This is a gameplay requirement, not an aesthetic one.** |
| **Color language** | Consistent meaning across the game (this hue means "damage to you," that one means "you did damage"). **Enforce it in the tool.** |

The "brief" in Impact is doing real work. Beginners make the peak too long, which reads as mushy. The peak should be almost too short.

### Ailment indications

Pure readability engineering: **the player must know their status at a glance, without reading text.** The typical toolkit:

- A **shader-level overlay** on the affected entity — a tint, a pattern, a fresnel rim glow
- A persistent **particle or aura**
- A **screen-edge vignette** for effects on the player
- A **UI icon** as backup, never as the primary channel

**The engine requirement this generates is a per-entity material parameter channel** — a small set of values (tint color, effect mask ID, intensity) that gameplay can set and shaders read.

**Design that channel early.** Retrofitting *"make this entity glow purple"* into a renderer that has no per-entity parameters is painful — it touches your bind group layout, your instance buffer, and every shader. Add three `vec4`s to your per-instance data on day one and you'll never think about it again.

---

## Screen-space effects and camera feel

Cheap, high-impact, and **mostly not about rendering at all.**

**Screen shake** — trauma-based, not random jitter:

```ts
trauma = Math.min(1, trauma + impactMagnitude);   // accumulate on hits
const shake = trauma * trauma;                     // ← the squared falloff is the trick
camera.offset.x = shake * MAX_OFFSET * noise(t * FREQ, seed0);
camera.offset.y = shake * MAX_OFFSET * noise(t * FREQ, seed1);
camera.roll     = shake * MAX_ROLL   * noise(t * FREQ, seed2);
trauma = Math.max(0, trauma - DECAY * dt);
```

**Squared falloff is what makes it feel physical rather than buzzy** — a big hit shakes hard and settles fast, small hits barely register. Using smooth noise rather than random per-frame values is the other half; random jitter reads as a rendering bug.

**Hit stop / frame freeze** — pause simulation for 2–5 frames on impact. **Almost nothing sells weight better, and it costs one boolean.** (Use your Module 12 separate clocks so the UI keeps animating.)

**Chromatic aberration and radial blur** on a strong hit or dash.

**Damage vignette** — a red screen edge that also **communicates direction**, which turns a cosmetic effect into a gameplay affordance.

**Camera kick and FOV punch** on impacts and speed. A few degrees of FOV increase while sprinting is one of the strongest speed cues available.

**Flash frames** — one or two frames of a solid bright color on a big impact.

These are largely **gameplay features implemented in rendering.** They live in the same territory as coyote time and input buffering from Module 12: **small, cheap lies that make the game feel good.**

**Being the engineer who suggests them — and who builds them as tunable, data-driven systems — is disproportionately valuable at a small studio**, because there may be nobody else whose job it is to notice.

---

## Translating art direction into systems

This is the skill the JD is naming when it asks for *"a good eye for what looks good and how to translate that into technical systems."*

### The pattern

An art director says something impressionistic:

- *"The dungeons should feel oppressive."*
- *"Magic should feel dangerous, not pretty."*
- *"I want it to look like a painting, not a render."*

**Your job is to decompose that into knobs.**

*"Oppressive dungeons"* might decompose into:

| Knob | Setting |
|---|---|
| Ambient light | Low, with strong local light falloff |
| Height fog | Pools at the floor, moderate density |
| Palette | Desaturated with a single accent hue |
| Per-vertex AO | Heavy in corners (Module 08 — free) |
| FOV | Tighter |
| View distance | Limited, made intentional by the fog |

**Each is a system with parameters, and each parameter is something the art team can then own.** That last clause is the real deliverable: you're not producing "oppressive," you're producing the *controls* for oppressive, so they can find it themselves.

### The three questions that make this concrete

1. **What is the visual reference?** Get images. **Ambiguity dies on contact with a mood board.**
2. **What are the two or three parameters that produce most of the effect?** Build those first, expose them, iterate.
3. **What does it cost, and what does it cost to change later?** A choice baked into the asset pipeline is expensive to reverse; a runtime parameter is cheap. Prefer runtime parameters until the direction is settled.

### Prototype fast and ugly

**A hardcoded, hacked-in version that an art director can look at *today* is worth more than a well-architected system next week** — because the answer to *"is this the right direction?"* is usually **no**, and you want to find that out cheaply.

Then build the real system once the direction is settled.

**This is a genuine cultural expectation in game development, and it's often the biggest adjustment for engineers arriving from product software**, where the reverse discipline is correct. In a backend service, hacking something in to see if it works is how you accumulate tech debt. In tech art, it's how you avoid building the wrong thing carefully.

**The reframing for the whole discipline:**

> In product engineering, the spec is the input and the implementation is the deliverable.
> In tech art, **the implementation is how the spec gets discovered.** The loop is the point.

If you say a version of that in an interview, it demonstrates you've understood something about game development culture that most incoming engineers haven't.

---

## Common confusions

**"VFX is an artist's job."** The *authoring* is. The *systems, budgets, tooling, and performance* are yours, and at a small studio you'll do a fair amount of the authoring too.

**"I'll make the particle system general and let artists figure it out."** Generality without curated defaults produces a tool nobody uses. Ship five good preset effects alongside the system.

**"Screen shake is just random offset."** Random per-frame offset reads as a bug. Trauma accumulation, squared falloff, and *smooth* noise are what make it read as impact.

**"I'll add more particles to make it look better."** More particles usually means more overdraw and a worse-looking, slower effect. Fewer, larger, better-textured particles with good curves win almost every time.

**"This effect looks great."** In isolation, at 100% scale, with nothing else on screen. Check it in a busy fight, at distance, on the target hardware, against every background color in the game. Readability under load is the actual bar.

**"The art director keeps changing their mind."** They're *discovering* the direction, which is the intended process. Build for cheap iteration and the changes stop being expensive.

---

## The interview answer

***"How would you approach VFX for a voxel game?"***

> "Data-driven effect definitions with hot reload, because the tool is what determines quality — an artist iterating in seconds will produce something ten times better than one waiting on builds.
>
> GPU-simulated particles in a compute shader with indirect draw for scale, additive blending for light-emitting effects so ordering isn't a problem, soft particles against the depth buffer because that four-line change is the difference between looking current and looking cheap, and a hard budget with distance-and-importance-based degradation so a busy fight doesn't tank the frame exactly when responsiveness matters most.
>
> For a voxel aesthetic I'd seriously look at actual voxel-cube particles for debris rather than billboards — it matches the art, it's free to author, and it's literally the material that was destroyed.
>
> And I'd design a per-entity material parameter channel early, because ailment indications and hit flashes all need it and retrofitting that touches every shader."

***"An art director says the caves should feel deeper. What do you do?"***

> "Ask for reference images first — ambiguity dies on contact with a mood board. Then decompose it into the two or three parameters that carry most of the effect, probably light falloff, height fog density, and ambient occlusion strength. Hack those in hardcoded the same day so we can look at it together, and only build the real system once we agree on the direction. The prototype is how the spec gets discovered."

***"What's cheap and makes a game feel dramatically better?"***

> "Hit stop and trauma-based screen shake. Two or three frames of frozen simulation on impact and a squared-falloff shake, both tunable. It's maybe fifty lines and it does more for perceived impact than any amount of shader work. Same family as coyote time — small lies that make the game feel right."

---

## Exercise — Voxelforge, Stage 14

**1. Build a GPU particle system:** compute-shader simulation, compaction with atomics, indirect draw. Support additive and alpha modes, and curves for size/color over lifetime.

**⭐ 2. Add soft particles** using the depth buffer. **Toggle it on and off against a floor and observe the difference** — this is a 4-line change with an outsized effect, and seeing it once will make you always do it.

**3. Add voxel-cube debris:** when a block breaks, spawn small cubes that inherit the block's material, arc under gravity, and collide with the world using Module 12's grid collision.

**4. Implement exponential distance fog and height fog**, exposed as hot-reloadable parameters. Then implement a low-resolution **froxel volumetric pass that reuses your voxel ray-marcher**, and compare the cost.

**⭐ 5. Implement screen shake** with trauma-squared falloff **and hit stop.** Tune them until a block break feels good. **Notice how much of "feel" is these two things** — most people are startled by the ratio.

**6. Add a per-entity material parameter channel** (tint, effect mask, intensity) and use it to build a damage flash and a poison overlay.

**⭐ 7. Pick a mood** — "oppressive," "ethereal," whatever — and tune your fog, ambient, palette, and AO parameters to hit it. **Screenshot before and after.**

**This exercise is the actual job.** It's also the best possible pair of images for your portfolio README, because it shows the same engine producing two different feelings from parameters — which is exactly what "translate art direction into technical systems" means.

---

## Go deeper

- **Simon Trümpler's simonschreibt.de** — "Game Art Tricks." **The best resource for seeing how shipped effects actually work**, reverse-engineered with pictures. Start here.
- **Jan Willem Nijman, "The Art of Screenshake" (2013)** — 25 minutes, and it will change how you think about feel. **Watch it today.**
- **Steve Swink, *Game Feel*** — the book-length treatment.
- **Sébastien Hillaire, "Physically Based Sky, Atmosphere and Cloud Rendering in Frostbite" (SIGGRAPH 2016)** — the reference for volumetrics and froxel grids.
- **Bart Wronski's blog (bartwronski.com)** — volumetric fog, temporal techniques, and unusually rigorous thinking about sampling.
- **"Real-Time VFX" community (realtimevfx.com)** — where VFX artists talk shop. **Read it to learn what artists actually need from you**, which is the highest-leverage thing in this module.
- **Inigo Quilez (iquilezles.org)** — SDFs, noise, procedural patterns, and shader craft. Endlessly useful, and the source of half the techniques you'll see in shader demos.
- **Unreal Niagara and Unity VFX Graph documentation** — not to copy, but to see what a mature, artist-facing VFX tool exposes. **It's a specification for your own tool's feature set.**

---

**Next:** [Back to the course index](./00-README.md) — or, if you've done the exercises, you have a voxel engine. Go make it look good.
