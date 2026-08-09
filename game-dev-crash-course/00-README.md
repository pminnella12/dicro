# Game Engine Programming: A Crash Course for Senior Engineers

### Fourteen modules taking a strong TypeScript engineer from zero graphics knowledge to credible engine-and-rendering candidate — built specifically against two Bitshift Entertainment job descriptions

*Course index · ~4 min read · Total course: ~14 modules, ~170 min of reading, plus a build-along project*

---

## What this is

You are a senior engineer with deep TypeScript and JavaScript expertise and no game development background. This course covers **only** the technical territory that is specific to games — real-time constraints, GPUs, rendering, voxels, engine architecture, and the performance discipline that ties them together.

It deliberately skips general software engineering. You already have that, and pretending otherwise would waste your time.

**Source material:** the two job descriptions this was built from —

- **Senior Software Engineer (General):** small studio, first title *Levers and Chests*, custom engine, TypeScript/JavaScript, gameplay systems, tools and content pipelines, real-time systems, graphics/rendering, performance optimization, close collaboration with artists and designers.
- **Core Engine and Rendering Programmer:** custom engine **Bakest**, written entirely in TypeScript, **WebGPU** rendering backend, purpose-built **voxel** renderer. Bonus areas: voxel rendering techniques (ray tracing, DDA, mesh generation, octrees/BVHs, 3D textures), 3D rendering, GPU optimization, tech art, asset processing, JS/TS/V8 performance realities, working with artists, art direction.

**Useful context:** Bitshift Entertainment is Markus "Notch" Persson's studio; *Levers and Chests* is a voxel-based first-person roguelike dungeon crawler, in development since 2024. Early public builds were JavaScript + WebGL with a custom voxel renderer and experimental ray tracing; the Engine JD's description of Bakest on WebGPU indicates that stack has moved forward. The studio is small and self-funded, which is why both JDs emphasize breadth, self-direction, and shipping over specialization.

**The T-shape the Engine JD asks for** — *"deep expertise in at least one area and a broad understanding of most game-related programming"* — is the shape of this course. Modules 07–09 are the deep spike (voxels and GPU performance). Everything else is the crossbar.

---

## The two tracks

You said: interview now, depth later. That maps onto two passes through the same material.

### Track A — Interview readiness (2–4 weeks)

Read for **vocabulary and mental models**. Do the "interview answer" section of each module until you can say it unprompted. Skip most exercises; do the starred ones.

**Week 1 — Foundations.** Modules 01, 02, 03. Non-negotiable. Every other conversation assumes these.
**Week 2 — Rendering core.** Modules 04, 05, 06. Do the Module 05 exercise at minimum — get a triangle and a cube on screen in WebGPU. Having *touched* the API changes how you talk about it.
**Week 3 — The spike.** Modules 07, 08, 09. This is where you differentiate. Implement a DDA loop; it's 40 lines and it's the single highest-value hour in the course.
**Week 4 — Breadth.** Modules 10, 11, 12, 13, 14. Read for the interview answers. Module 13 is where your existing expertise becomes an asset — spend real time there.

### Track B — Day-1 competence (2–3 months)

Same order, but do every exercise. By the end you will have built **Voxelforge**, a small TypeScript + WebGPU voxel engine, which is simultaneously the best possible portfolio artifact for this specific job.

---

## The modules

| # | Module | Read | Priority | Core question it answers |
|---|---|---|---|---|
| **Part I — Foundations** ||||
| 01 | [Real-Time Thinking](./01-real-time-thinking.md) | 10 min | 🔴 Critical | Why is a game loop not an event loop? |
| 02 | [3D Math and the Chain of Spaces](./02-3d-math-and-spaces.md) | 13 min | 🔴 Critical | How does a vertex become a pixel position? |
| 03 | [The GPU Mental Model](./03-gpu-mental-model.md) | 12 min | 🔴 Critical | Why does the hardware punish branches and reward batches? |
| **Part II — Rendering** ||||
| 04 | [The Rasterization Pipeline](./04-rasterization-pipeline.md) | 11 min | 🔴 Critical | What actually happens between a draw call and a pixel? |
| 05 | [WebGPU and WGSL](./05-webgpu-and-wgsl.md) | 14 min | 🔴 Critical | What is the API, and where is its ceiling vs. Vulkan/D3D12? |
| 06 | [Materials, Lighting, and Color](./06-materials-lighting-color.md) | 13 min | 🟡 High | How do you make it look like something? |
| **Part III — The Spike** ||||
| 07 | [Voxel Data Structures](./07-voxel-data-structures.md) | 12 min | 🔴 Critical | How do you store a world that doesn't fit in memory? |
| 08 | [Voxel Rendering Techniques](./08-voxel-rendering.md) | 13 min | 🔴 Critical | Mesh it or ray trace it? |
| 09 | [GPU Performance Engineering](./09-gpu-performance.md) | 13 min | 🔴 Critical | How do you find out what is actually slow? |
| **Part IV — Engine Breadth** ||||
| 10 | [Engine Architecture](./10-engine-architecture.md) | 13 min | 🟡 High | ECS, jobs, render graphs — and when not to use them |
| 11 | [Asset Pipelines and Artists](./11-asset-pipelines-and-artists.md) | 12 min | 🟡 High | What is the contract between authored content and runtime? |
| 12 | [Gameplay and Simulation](./12-gameplay-and-simulation.md) | 11 min | 🟢 Medium | Collision, character feel, procedural generation |
| 13 | [TypeScript and V8 Performance](./13-typescript-v8-performance.md) | 13 min | 🔴 Critical | Where your existing expertise becomes an edge |
| 14 | [Tech Art, VFX, and Art Direction](./14-tech-art-and-vfx.md) | 13 min | 🟢 Medium | Turning "it should feel more magical" into systems |

**Read them in order.** Each module assumes the ones before it. Module 08 is meaningless without 03 and 07; Module 09 is meaningless without all of Parts I–III.

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

Put it on GitHub with a README containing screenshots and your measured numbers. For this particular role, a working WebGPU voxel renderer in TypeScript is a stronger signal than any credential.

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

**Deliberately out of scope** (per your instruction to focus on game-dev technical areas only): project management, work estimation, stakeholder communication, shipping process, and general software practices. Note that the Engine JD lists several of these as bonuses — your 5+ years of senior experience already covers them, and you should say so explicitly in an interview rather than assuming it's read between the lines.

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

## How to read these

Each module follows the same structure:

- **A framing idea** — the one thing that reorganizes your understanding
- **The concepts**, building on each other, with code where code clarifies
- **"The interview answer"** — what to say, in the register that signals experience
- **An exercise** — the Voxelforge stage
- **"Go deeper"** — vetted primary sources, not link soup

Where a module makes a judgment call rather than stating a fact, it says so. Where the right answer is "measure it," it says that too — because that is very often the correct professional answer and saying it confidently is itself a signal.

---

## Currency note

Written August 2026. Fast-moving facts verified at that time:

- **WebGPU** ships by default in Chrome/Edge (since 113, 2023), Safari 26 (macOS Tahoe 26, iOS/iPadOS 26), and Firefox (141 on Windows, 145 on macOS/Apple Silicon). Full major-browser desktop coverage as of early 2026; Firefox Linux and Android still in progress.
- **Bindless resources, multi-draw indirect, and mesh shaders** are not yet in WebGPU. Bindless is the roadmap blocker for the other two.
- **Subgroups** landed in Chrome (131+) and are rolling out; always ship a fallback path.
- **No hardware ray tracing and no multiple queues** (so no async compute) in WebGPU today.

Check `github.com/gpuweb/gpuweb` issues and `developer.chrome.com/blog/new-in-webgpu-*` before an interview — being current on the platform's trajectory is itself a signal for an engine role on WebGPU.

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
