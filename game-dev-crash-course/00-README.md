# Game Engine Programming: A Crash Course for Senior Engineers

### Fourteen modules taking a strong TypeScript engineer from zero graphics knowledge to credible engine-and-rendering candidate — built specifically against two Bitshift Entertainment job descriptions

*Course index · ~8 min read · Total course: 14 modules, ~7 hours of reading, plus a build-along project*

---

## What this is

You are a senior engineer with deep TypeScript and JavaScript expertise and **no game development background**. This course covers **only** the technical territory that is specific to games — real-time constraints, GPUs, rendering, voxels, engine architecture, and the performance discipline that ties them together.

It deliberately skips general software engineering. You already have that, and pretending otherwise would waste your time.

**What it does *not* assume:** any prior graphics knowledge. Every piece of jargon is unpacked the first time it appears — what a fragment is, why `w` exists, what occupancy means, what a barrier does. Where a term recurs across modules, the module that introduces it defines it and later modules point back. If you hit a word you don't know, check the **[term index](#where-each-term-is-explained)** at the bottom of this page to find where it's defined.

**What it does assume:** that you can read TypeScript, that you know what a cache is in general terms, and that you're comfortable with the idea of a build pipeline. Everything else is explained.

**Source material:** the two job descriptions this was built from —

- **Senior Software Engineer (General):** small studio, first title *Levers and Chests*, custom engine, TypeScript/JavaScript, gameplay systems, tools and content pipelines, real-time systems, graphics/rendering, performance optimization, close collaboration with artists and designers.
- **Core Engine and Rendering Programmer:** custom engine **Bakest**, written entirely in TypeScript, **WebGPU** rendering backend, purpose-built **voxel** renderer. Bonus areas: voxel rendering techniques (ray tracing, DDA, mesh generation, octrees/BVHs, 3D textures), 3D rendering, GPU optimization, tech art, asset processing, JS/TS/V8 performance realities, working with artists, art direction.

**Useful context:** Bitshift Entertainment is Markus "Notch" Persson's studio; *Levers and Chests* is a voxel-based first-person roguelike dungeon crawler, in development since 2024. Early public builds were JavaScript + WebGL with a custom voxel renderer and experimental ray tracing; the Engine JD's description of Bakest on WebGPU indicates that stack has moved forward. The studio is small and self-funded, which is why both JDs emphasize breadth, self-direction, and shipping over specialization.

**The T-shape the Engine JD asks for** — *"deep expertise in at least one area and a broad understanding of most game-related programming"* — is the shape of this course. Modules 07–09 are the deep spike (voxels and GPU performance). Everything else is the crossbar.

---

## The two tracks

You said: interview now, depth later. That maps onto two passes through the same material.

### Track A — Interview readiness (2–4 weeks)

Read for **vocabulary and mental models**. Do the "interview answer" section of each module until you can say it unprompted. Skip most exercises; do the ⭐ starred ones.

| Week | Modules | Focus |
|---|---|---|
| **1 — Foundations** | 01, 02, 03 | Non-negotiable. Every other conversation assumes these. |
| **2 — Rendering core** | 04, 05, 06 | Do the Module 05 exercise at minimum — get a triangle and a cube on screen in WebGPU. Having *touched* the API changes how you talk about it. |
| **3 — The spike** | 07, 08, 09 | Where you differentiate. Implement a DDA loop; it's 40 lines and it's the single highest-value hour in the course. |
| **4 — Breadth** | 10, 11, 12, 13, 14 | Read for the interview answers. **Module 13 is where your existing expertise becomes an asset** — spend real time there. |

### Track B — Day-1 competence (2–3 months)

Same order, but do every exercise. By the end you will have built **Voxelforge**, a small TypeScript + WebGPU voxel engine, which is simultaneously the best possible portfolio artifact for this specific job.

---

## The modules

| # | Module | Read | Priority | Core question it answers |
|---|---|---|---|---|
| **Part I — Foundations** ||||
| 01 | [Real-Time Thinking](./01-real-time-thinking.md) | 25 min | 🔴 Critical | Why is a game loop not an event loop? |
| 02 | [3D Math and the Chain of Spaces](./02-3d-math-and-spaces.md) | 30 min | 🔴 Critical | How does a vertex become a pixel position? |
| 03 | [The GPU Mental Model](./03-gpu-mental-model.md) | 30 min | 🔴 Critical | Why does the hardware punish branches and reward batches? |
| **Part II — Rendering** ||||
| 04 | [The Rasterization Pipeline](./04-rasterization-pipeline.md) | 28 min | 🔴 Critical | What actually happens between a draw call and a pixel? |
| 05 | [WebGPU and WGSL](./05-webgpu-and-wgsl.md) | 35 min | 🔴 Critical | What is the API, and where is its ceiling vs. Vulkan/D3D12? |
| 06 | [Materials, Lighting, and Color](./06-materials-lighting-color.md) | 30 min | 🟡 High | How do you make it look like something? |
| **Part III — The Spike** ||||
| 07 | [Voxel Data Structures](./07-voxel-data-structures.md) | 30 min | 🔴 Critical | How do you store a world that doesn't fit in memory? |
| 08 | [Voxel Rendering Techniques](./08-voxel-rendering.md) | 35 min | 🔴 Critical | Mesh it or ray trace it? |
| 09 | [GPU Performance Engineering](./09-gpu-performance.md) | 32 min | 🔴 Critical | How do you find out what is actually slow? |
| **Part IV — Engine Breadth** ||||
| 10 | [Engine Architecture](./10-engine-architecture.md) | 28 min | 🟡 High | ECS, jobs, render graphs — and when not to use them |
| 11 | [Asset Pipelines and Artists](./11-asset-pipelines-and-artists.md) | 26 min | 🟡 High | What is the contract between authored content and runtime? |
| 12 | [Gameplay and Simulation](./12-gameplay-and-simulation.md) | 26 min | 🟢 Medium | Collision, character feel, procedural generation |
| 13 | [TypeScript and V8 Performance](./13-typescript-v8-performance.md) | 28 min | 🔴 Critical | Where your existing expertise becomes an edge |
| 14 | [Tech Art, VFX, and Art Direction](./14-tech-art-and-vfx.md) | 26 min | 🟢 Medium | Turning "it should feel more magical" into systems |

**Read them in order.** Each module assumes the ones before it and refers back to them by number. Module 08 is meaningless without 03 and 07; Module 09 is meaningless without all of Parts I–III.

---

## How to read these

Every module follows the same structure. Knowing the shape lets you skim to what you need.

| Section | What it's for |
|---|---|
| **"Read this first"** | The framing idea — the one thing that reorganizes your understanding of the topic. Never skip this. |
| **The concepts** | Building on each other, with code where code clarifies, worked numeric examples where numbers convince, and every term defined at first use. |
| **"Common confusions"** | The mistakes people actually make. Read this even if you skim the rest — it's the fastest way to find out whether you understood. |
| **"The interview answer"** | What to say, in the register that signals experience. Often annotated with *why* each phrase lands. |
| **"Exercise"** | The Voxelforge stage. ⭐ marks the highest-value steps. |
| **"Go deeper"** | Vetted primary sources, not link soup. Usually 5–8 items, each with a one-line reason. |

A few conventions:

- **⭐ marks the highest-value item** in a list — the exercise step or reference that earns its time several times over.
- **Cross-references are by module number** (*"Module 03"*), so you can jump back when a term is used that you've forgotten.
- Where a module makes a **judgment call** rather than stating a fact, it says so. Where the right answer is **"measure it,"** it says that too — because that is very often the correct professional answer and saying it confidently is itself a signal.
- Where the **voxel-native technique beats the general one**, the module says so explicitly. That pattern recurs about eight times across the course and it's the single most useful thing to notice, because the Engine JD asks for exactly that judgment.

---

## The build-along project: Voxelforge

A TypeScript + WebGPU voxel renderer, built incrementally. Each module's exercise is one stage.

| Stage | Module | What you build |
|---|---|---|
| 1 | 01 | Fixed-timestep loop, interpolation, determinism test, frame time histogram |
| 2 | 02 | Allocation-free math library: Vec3, Mat4, quaternions, frustum, ray-AABB |
| 3 | 03 | Roofline arithmetic for your own GPU (paper exercise — do not skip) |
| 4 | 04 | Frame graph design, depth convention, packed vertex layout (paper) |
| 5 | 05 | First triangle → cube → 32³ chunk → face-culled merged mesh |
| 6 | 06 | Texture array atlas, mips, HDR + tonemap + dither, shadow map |
| 7 | 07 | Palette compression, GPU brickmap, brick pool allocator, worker streaming |
| 8 | 08 | Greedy meshing → binary greedy meshing → per-vertex AO → hierarchical DDA raymarcher |
| 9 | 09 | Timestamp query HUD, debug heatmaps, culling, indirect draw, frame captures |
| 10 | 10 | Layered architecture, worker pool, handle-based assets, hot reload, tests |
| 11 | 11 | `.vox` parser, binary runtime format, incremental bake CLI |
| 12 | 12 | Swept AABB collision, character controller, block break/place, procgen |
| 13 | 13 | Allocation profiling, SoA particles, megamorphism measurement, deopt tracing |
| 14 | 14 | GPU particles, voxel debris, fog and volumetrics, screen shake, hit stop |

**The five highest-value stages if you only do some:** 5 (get something on screen), 8 (DDA + greedy meshing — the heart of the job), 9 (the profiling discipline), 13 (your differentiator), 7 (the data structure everything rests on).

**Put it on GitHub with a README containing screenshots and your measured numbers.** Several exercises deliberately produce *numbers* — the meshing speedup ratio, the SoA vs AoS particle ratio, the megamorphic penalty on your own hardware, the four-step CPU-bound optimization sequence. Those numbers are what make a portfolio project credible, because nobody can fake having measured something.

For this particular role, a working WebGPU voxel renderer in TypeScript is a stronger signal than any credential.

---

## Coverage map: every JD bullet → module

**Engine JD "Bonus" list:**

| JD bullet | Covered in |
|---|---|
| Voxel rendering: ray-tracing, DDA/line tracing, voxel mesh generation | 08 |
| Acceleration and sparse structures: octrees, BVHs, 3D textures | 07 |
| 3D rendering: space transformations | 02 |
| Textures and sampling, lighting equations, light sources, shadow techniques | 06 |
| Post-processing, dithering, text rendering | 06 |
| Particle systems | 14 |
| GPU optimization: peak-performance-percentage, draw calls, batching, indirect draw, culling | 09 |
| Cache friendliness, bindless, occupancy, latency hiding, overdraw | 03, 09 |
| Overlapping pipelines, ALU vs bandwidth, async compute | 03, 09 |
| PIX, RenderDoc, Nsight/Radeon tools | 09 |
| Tech art: particles, fog, clouds, magic/spell VFX, decals, ailment indications | 14 |
| Asset processing: file parsers from spec, intermediate representations, serialization, compression, parallel processing | 11 |
| JavaScript/TypeScript, V8 performance realities | 13 |
| WebGPU, and how it maps to DX12/Vulkan/Metal and differs from WebGL | 05 |
| Working with artists | 11 |
| Art direction | 14 |
| Project management, shipping | *Not covered — see below* |

**General JD:**

| JD bullet | Covered in |
|---|---|
| Real-time systems / game engines | 01, 03, 10 |
| Gameplay systems, simulation systems | 12 |
| Graphics or rendering | 04, 05, 06, 08 |
| Internal tools and content pipelines | 10, 11 |
| Performance optimization | 09, 13 |
| Working closely with artists or designers | 11, 14 |

**Deliberately out of scope** (per your instruction to focus on game-dev technical areas only): project management, work estimation, stakeholder communication, shipping process, and general software practices. Note that the Engine JD lists several of these as bonuses — your 5+ years of senior experience already covers them, and **you should say so explicitly in an interview** rather than assuming it's read between the lines.

---

## The eight questions to be ready for

If you can answer these cold, you are interview-ready. Each maps to a module's "interview answer" section.

1. **Walk me through a game loop.** → 01
2. **How does a vertex get from a model file to a pixel?** → 02
3. **Why did adding a branch make my shader slower?** → 03
4. **What's early-Z, and how do you accidentally lose it?** → 04
5. **How does WebGPU map onto Vulkan/D3D12, and what can't it do?** → 05
6. **How would you store and render a large, destructible voxel world?** → 07 + 08
7. **The game is at 30 FPS. What do you do?** → 09
8. **What do you watch for writing performance-critical TypeScript?** → 13

Two more that are really about judgment, and matter more than they look:

9. **When would you *not* copy a standard engine pattern?** → 10 (the JD asks for this explicitly)
10. **An artist wants something that would hurt performance. What do you do?** → 11

---

## Where each term is explained

If you hit a word you don't know, this is where it's defined. Terms are defined at their first use; later modules assume them.

| Term | Defined in |
|---|---|
| VSync, front/back buffer, tearing, compositor, scan-out | 01 |
| Frame budget, frame pacing, p99 / 1% lows | 01 |
| Cache, cache line, cache miss | 01 |
| GC pause, minor vs major GC | 01, 13 |
| Command buffer, command encoder, `queue.submit` | 01, 05 |
| Uniform buffer, ring buffer / frames in flight | 01, 05 |
| Determinism, seeded PRNG, `SharedArrayBuffer` / COOP+COEP | 01, 10 |
| Coordinate space, handedness, homogeneous coordinates, the `w` component | 02 |
| Model / world / view / clip / NDC / screen / texture space | 02 |
| Perspective divide, clipping, viewport transform | 02 |
| Dot and cross product, normal matrix / inverse transpose | 02 |
| Column-major vs row-major, quaternion, gimbal lock, slerp | 02 |
| Depth precision, z-fighting, reversed-Z | 02 |
| AABB, slab test, frustum culling, barycentric coordinates | 02 |
| SIMT, warp / wave / subgroup, divergence, coherence | 03 |
| ALU, FLOP, occupancy, latency hiding, register pressure | 03 |
| Coalescing, SoA vs AoS, roofline, bandwidth- vs compute-bound | 03 |
| Swizzled/Morton texture layout, mipmap, anisotropic filtering | 03, 06 |
| Workgroup, workgroup shared memory, atomics, dispatch | 03, 05 |
| Draw call, instancing, indirect draw, state change cost hierarchy | 03, 09 |
| ROP, barrier, async compute | 03, 04 |
| Fragment vs pixel, varying, quad overdraw, screen-space derivatives | 04 |
| Winding order, backface culling, perspective-correct interpolation | 04 |
| Early-Z, Hi-Z, depth prepass, `discard` | 04 |
| Blending, premultiplied alpha, OIT, alpha testing | 04 |
| Render pass, attachment, load/store ops, tile-based rendering, MSAA | 04 |
| Forward / deferred / forward+ rendering, G-buffer | 04 |
| Adapter, device, bind group, pipeline, render bundle | 05 |
| WGSL alignment and padding rules, uniformity analysis | 05 |
| Bindless, multi-draw indirect, mesh shaders (and their absence) | 05, 09 |
| Texel, UV, sampler, address mode, atlas bleeding | 06 |
| Albedo, BRDF, microfacet, GGX, Fresnel, metallic/roughness | 06 |
| Ambient occlusion, IBL, hemisphere ambient | 06, 08 |
| Shadow map, acne, peter-panning, PCF, comparison sampler, CSM | 06 |
| sRGB vs linear, tonemapping, banding, dithering, blue noise | 06 |
| Bloom, LUT color grading, TAA, motion vectors | 06, 08 |
| SDF / MSDF text | 06 |
| Chunk, halo/apron, palette compression, Morton encoding | 07, 08 |
| Octree, SVO, SVDAG, brickmap, brick pool, occupancy bitmask, 64-tree, popcount | 07 |
| LOD cracks, skirts, streaming, eviction | 07 |
| Face culling, greedy meshing, binary greedy meshing | 08 |
| Per-vertex AO, diagonal-flip fix | 08 |
| DDA, hierarchical DDA, beam optimization | 08 |
| Flood-fill light propagation, voxel cone tracing, temporal accumulation, denoising | 08 |
| Timestamp query, CPU- vs GPU-bound, the six bottlenecks | 09 |
| Peak-percentage analysis, overdraw heatmap, ray step heatmap | 09 |
| Sort key, occlusion culling, Hi-Z two-pass, cave culling, portal visibility | 09 |
| ECS, archetype vs sparse set, generation counter, data-oriented design | 10 |
| RHI, render graph, DAG, transient resource aliasing | 10 |
| Worker pool, structured clone vs transferable | 10, 13 |
| Handle-based assets, hot reload | 10 |
| Source / intermediate / runtime format, bake, content hashing | 11 |
| `DataView`, RIFF chunks, endianness, alignment for zero-copy | 11 |
| Basis Universal / KTX2, LZ4, block compression | 11 |
| Swept AABB, per-axis resolution, tunneling, step-up | 12 |
| Coyote time, input buffering, variable jump height | 12 |
| Spatial hash, A*, hierarchical pathfinding, FSM vs behavior tree | 12 |
| fBm, octaves, seed derivation, chunk independence | 12 |
| Skeletal animation, skinning, procedural animation / IK | 12 |
| Hidden class / shape, inline cache, monomorphic / megamorphic | 13 |
| Smi, HeapNumber, element kinds, holey arrays | 13 |
| Destination-first API, object pool, scratch buffer | 13 |
| Deopt, `--trace-deopt`, `%GetOptimizationStatus` | 13 |
| Branded types, discriminated unions with exhaustiveness | 13 |
| Emitter, curve-over-lifetime, billboard, soft particles | 14 |
| Froxel grid, height fog, volumetric lighting | 14 |
| Screen-space decals, per-face voxel decals | 14 |
| Trauma-based screen shake, hit stop, per-entity material parameters | 14 |

---

## Currency note

Written August 2026. Fast-moving facts verified at that time:

- **WebGPU** ships by default in Chrome/Edge (since 113, 2023), Safari 26 (macOS Tahoe 26, iOS/iPadOS 26), and Firefox (141 on Windows, 145 on macOS/Apple Silicon). Full major-browser desktop coverage as of early 2026; Firefox Linux and Android still in progress.
- **Bindless resources, multi-draw indirect, and mesh shaders** are not yet in WebGPU. Bindless is the roadmap blocker for the other two.
- **Subgroups** landed in Chrome (131+) and are rolling out; always ship a fallback path.
- **No hardware ray tracing and no multiple queues** (so no async compute) in WebGPU today.

Check `github.com/gpuweb/gpuweb` issues and `developer.chrome.com/blog/new-in-webgpu-*` before an interview — **being current on the platform's trajectory is itself a signal** for an engine role on WebGPU.

---

## The five books, if you only buy five

1. **Jason Gregory, *Game Engine Architecture* (3rd ed.)** — the field's standard reference
2. **Akenine-Möller et al., *Real-Time Rendering* (4th ed.)** — the rendering reference
3. **Robert Nystrom, *Game Programming Patterns*** — free at gameprogrammingpatterns.com
4. **Fletcher Dunn & Ian Parberry, *3D Math Primer*** — the friendliest correct math book
5. **Christer Ericson, *Real-Time Collision Detection*** — when you need it, nothing else will do

And the four free resources that punch above every book: **webgpufundamentals.org**, **Fabian Giesen's "A trip through the Graphics Pipeline 2011"**, **Amit Patel's Red Blob Games**, and **Dennis Gustafsson's Teardown write-ups at blog.voxagon.se**.

---

**Start here:** [Module 01 — Real-Time Thinking](./01-real-time-thinking.md)
