# Module 05 — WebGPU and WGSL

### The API you will actually write against: its object model, how it maps onto D3D12 / Vulkan / Metal, and exactly where its ceiling is

*~14 min read · Part II: Rendering · Prerequisites: Modules 01–04*

---

WebGPU is not "WebGL 3." WebGL was an OpenGL ES binding: a giant global state machine where you bound things to slots and hoped. WebGPU is modelled on the **explicit** native APIs of the 2015+ generation — D3D12, Vulkan, Metal — and inherits their central idea:

> Do expensive validation and state resolution **once, up front**, when you create immutable objects. Then, per frame, do almost nothing but record commands into a buffer and submit it.

If you understand that sentence, the API's shape becomes obvious rather than arbitrary.

As of 2026, WebGPU ships enabled by default in Chrome/Edge (since 2023), Safari 26 (macOS Tahoe, iOS/iPadOS 26), and Firefox (141 on Windows, 145 on macOS), with Linux and Android still filling in. Cross-browser desktop coverage arrived in early 2026.

---

## The object graph

```
navigator.gpu
  └─ Adapter          (a physical GPU + backend, with limits & features)
      └─ Device       (your logical connection; owns everything below)
          ├─ Queue                  ← the only thing that executes work
          ├─ Buffer / Texture / Sampler
          ├─ BindGroupLayout → BindGroup       (resource binding)
          ├─ ShaderModule (WGSL)
          ├─ PipelineLayout → Render/ComputePipeline  (all state, baked)
          └─ CommandEncoder → CommandBuffer    (recorded work)
```

Minimal setup:

```ts
const adapter = await navigator.gpu.requestAdapter({ powerPreference: 'high-performance' });
if (!adapter) throw new Error('WebGPU unavailable');

const device = await adapter.requestDevice({
  requiredFeatures: ['timestamp-query'],           // opt-in; fails if unsupported
  requiredLimits: { maxStorageBufferBindingSize: 512 * 1024 * 1024 },
});

const context = canvas.getContext('webgpu')!;
const format = navigator.gpu.getPreferredCanvasFormat();  // usually 'bgra8unorm'
context.configure({ device, format, alphaMode: 'opaque' });
```

Two things to notice immediately, because they're the design philosophy in miniature:

**Features and limits are negotiated.** You *ask* the adapter for what you need. If you don't request it, you don't get it — even if the hardware supports it. This makes portability explicit instead of accidental.

**Errors are asynchronous and scoped.** WebGPU does not throw on most invalid GPU work. It captures errors and surfaces them via `device.pushErrorScope('validation')` / `popErrorScope()`, plus `device.lost` and `device.onuncapturederror`. During development, wrap your initialization in error scopes and log everything, or you will spend hours on silent failures.

---

## Resources: buffers and textures

```ts
const vertexBuffer = device.createBuffer({
  size: data.byteLength,
  usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST,
});
device.queue.writeBuffer(vertexBuffer, 0, data);
```

**Usage flags are mandatory and immutable.** A buffer must declare every way it will be used at creation. This lets the driver choose memory placement once. Forgetting a flag is a top-three beginner error; the validation message is clear, so read it.

The main flags: `VERTEX`, `INDEX`, `UNIFORM`, `STORAGE`, `INDIRECT`, `COPY_SRC`, `COPY_DST`, `MAP_READ`, `MAP_WRITE`.

**Uniform vs storage buffers** is a decision you make constantly:

| | Uniform | Storage |
|---|---|---|
| Default max binding size | 64 KiB | 128 MiB |
| Access | read-only | read or read-write |
| Layout rules | strict std140-like padding | more relaxed |
| Speed | often faster (constant cache) | slightly slower, far more flexible |
| Use for | camera, per-frame constants | per-instance arrays, voxel data, compute I/O |

**The padding rules will bite you.** In WGSL, a `vec3<f32>` has size 12 but **alignment 16**. A struct's stride is rounded up to its largest member's alignment. So this:

```wgsl
struct Light { position: vec3f, intensity: f32 };  // 16 bytes — fine, packs nicely
struct Bad   { intensity: f32, position: vec3f };  // 32 bytes — 12 bytes of padding
```

Silent mismatches between your TypeScript `Float32Array` writes and WGSL's expected layout produce garbage that looks like a math bug. The fix that professionals use: **generate your buffer layouts from a single source of truth** rather than hand-writing offsets on both sides. Libraries like `webgpu-utils` or `typegpu` do this; so does a 100-line codegen script you write yourself. Do this early.

**Textures** carry format, dimension (`1d`/`2d`/`3d`), size, mip level count, sample count, and usage. For voxel work, `3d` textures with `r8uint` or `rgba8unorm` formats are your bread and butter — with the hard constraint that **`maxTextureDimension3D` defaults to 2048**, so a single 3D texture tops out around 2048³ (and that's 8 GB at 1 byte/voxel anyway). This limit is a genuine architectural driver: it's part of why brick/pool structures exist (Module 07).

---

## Bind groups: the part that feels weird

You don't bind resources individually. You group them.

```ts
const layout = device.createBindGroupLayout({
  entries: [
    { binding: 0, visibility: GPUShaderStage.VERTEX | GPUShaderStage.FRAGMENT,
      buffer: { type: 'uniform' } },
    { binding: 1, visibility: GPUShaderStage.FRAGMENT, texture: { sampleType: 'float' } },
    { binding: 2, visibility: GPUShaderStage.FRAGMENT, sampler: { type: 'filtering' } },
  ],
});

const bindGroup = device.createBindGroup({
  layout,
  entries: [
    { binding: 0, resource: { buffer: cameraBuffer } },
    { binding: 1, resource: albedoTexture.createView() },
    { binding: 2, resource: sampler },
  ],
});
```

The layout is the *shape*; the bind group is a pre-validated *instance* of that shape. At draw time, `setBindGroup(index, bindGroup)` swaps a whole set at once, with essentially no validation cost — it was all done at creation.

**Organize bind groups by update frequency.** This is the single most important architectural convention in modern graphics APIs, and it applies identically in Vulkan and D3D12:

- **Group 0:** per-frame (camera, time, global lighting) — set once per frame
- **Group 1:** per-pass (render targets as inputs, pass constants)
- **Group 2:** per-material (textures, material parameters)
- **Group 3:** per-object/per-draw (model matrix, instance offsets)

You get **4 bind groups by default** (`maxBindGroups`), which is not a coincidence — it's exactly enough for this idiom.

**Dynamic offsets** are the escape valve for per-object data: declare a buffer binding with `hasDynamicOffset: true`, pack all your per-object uniforms into one big buffer, and pass a byte offset at `setBindGroup` time. One bind group, thousands of objects, no per-object allocation. Offsets must be aligned to `minUniformBufferOffsetAlignment` (256 bytes by default), which is why you'll see uniform structs padded to 256.

---

## Pipelines: all state, baked

```ts
const pipeline = device.createRenderPipeline({
  layout: device.createPipelineLayout({ bindGroupLayouts: [layout] }),
  vertex: {
    module: shaderModule, entryPoint: 'vs',
    buffers: [{ arrayStride: 8, attributes: [{ shaderLocation: 0, offset: 0, format: 'uint32x2' }] }],
  },
  fragment: {
    module: shaderModule, entryPoint: 'fs',
    targets: [{ format }],
  },
  primitive: { topology: 'triangle-list', cullMode: 'back', frontFace: 'ccw' },
  depthStencil: { format: 'depth32float', depthWriteEnabled: true, depthCompare: 'less' },
  multisample: { count: 4 },
});
```

Everything — shaders, vertex layout, blend state, depth state, cull mode, MSAA count, target formats — is frozen into one immutable object. That's why binding a pipeline is fast: there is nothing left to resolve.

**The consequence is combinatorial.** Every unique combination of shader × blend mode × target format × cull mode is a separate pipeline object, and creating one triggers shader compilation, which can take **milliseconds to hundreds of milliseconds**. Compiling pipelines during gameplay is a guaranteed, extremely visible hitch — it is one of the most common causes of stutter in shipped games across the entire industry.

The professional answer: **compile every pipeline you will need during loading**, warm them, and never create one inside the frame loop. `createRenderPipelineAsync()` exists specifically so you can do this off the critical path. Building a **pipeline cache keyed by a state hash** with an explicit warm-up phase is a real engine feature, and a great thing to have opinions about in an interview.

---

## Commands and the queue

```ts
const encoder = device.createCommandEncoder();
const pass = encoder.beginRenderPass({
  colorAttachments: [{
    view: context.getCurrentTexture().createView(),
    clearValue: { r: 0.1, g: 0.1, b: 0.15, a: 1 },
    loadOp: 'clear', storeOp: 'store',
  }],
  depthStencilAttachment: {
    view: depthView, depthClearValue: 1.0,
    depthLoadOp: 'clear', depthStoreOp: 'discard',
  },
});

pass.setPipeline(pipeline);
pass.setBindGroup(0, frameBindGroup);
pass.setVertexBuffer(0, vertexBuffer);
pass.setIndexBuffer(indexBuffer, 'uint32');
pass.drawIndexed(indexCount, instanceCount);
pass.end();

device.queue.submit([encoder.finish()]);
```

Recording is CPU work with no GPU involvement. `submit` hands the buffer over. Nothing has rendered when `submit` returns — see Module 01.

**WebGPU inserts barriers automatically.** In Vulkan and D3D12 you must manually specify resource transitions and memory barriers, and getting them wrong causes corruption or hangs. WebGPU tracks usage and inserts them for you. This is a genuine ergonomic win and a small performance cost (the implementation is conservative). Know that this is happening — an interviewer asking "how does WebGPU differ from Vulkan" is often fishing for exactly this.

**Render bundles** (`createRenderBundleEncoder`) pre-record a sequence of draws that can be replayed across frames with `executeBundles`. They cut JavaScript-side overhead substantially for static geometry, which matters much more in a JS engine than in a C++ one. For a voxel world where chunk meshes change rarely, bundles are a natural fit.

---

## WGSL in ten minutes

Rust-flavoured syntax, strongly typed, no preprocessor, no pointers into arbitrary memory, designed for safe validation.

```wgsl
struct Camera {
  viewProj : mat4x4f,
  position : vec3f,
  time     : f32,
};

@group(0) @binding(0) var<uniform> camera : Camera;
@group(0) @binding(1) var<storage, read> instances : array<Instance>;
@group(1) @binding(0) var albedo : texture_2d<f32>;
@group(1) @binding(1) var samp : sampler;

struct VSOut {
  @builtin(position) clip : vec4f,
  @location(0) uv : vec2f,
  @location(1) normal : vec3f,
};

@vertex
fn vs(@location(0) packed : vec2u, @builtin(instance_index) i : u32) -> VSOut {
  let pos = unpackVoxelPosition(packed.x);
  var out : VSOut;
  out.clip = camera.viewProj * instances[i].model * vec4f(pos, 1.0);
  out.uv = unpackUV(packed.y);
  out.normal = FACE_NORMALS[packed.y >> 28u];
  return out;
}

@fragment
fn fs(in : VSOut) -> @location(0) vec4f {
  let base = textureSample(albedo, samp, in.uv);
  let ndotl = max(dot(normalize(in.normal), LIGHT_DIR), 0.0);
  return vec4f(base.rgb * (ndotl * 0.8 + 0.2), base.a);
}
```

Things that will trip you up coming from GLSL or TypeScript:

- **`let` is immutable, `var` is mutable.** Opposite of your JavaScript instincts in the `let` case.
- **Address spaces are explicit:** `var<uniform>`, `var<storage, read>`, `var<workgroup>`, `var<private>`, `var<function>`.
- **No implicit conversions.** `1` is `i32`, `1.0` is `f32`, `1u` is `u32`, and mixing them is a compile error. This catches real bugs but feels pedantic at first.
- **Uniformity analysis.** `textureSample` computes derivatives, so it is only legal in **uniform control flow** within a fragment shader. Sampling inside a divergent `if` is a *compile error*, not a warning. Use `textureSampleLevel` (explicit LOD) or hoist the sample out of the branch. Everyone hits this within their first week.
- **No preprocessor.** No `#ifdef`, no `#include`. Shader variants and includes are your build system's job — you write a small string-templating layer, or use `wgsl-preprocessor`-style tooling. Every engine ends up with one.
- **Built-in functions are the GLSL set** you'd expect: `dot`, `cross`, `mix`, `clamp`, `smoothstep`, `pow`, `select` (instead of ternary), `fract`, `sign`, `textureLoad`, `textureStore`, `atomicAdd`, `workgroupBarrier`.

A compute entry point:

```wgsl
@group(0) @binding(0) var<storage, read_write> counts : array<atomic<u32>>;
@group(0) @binding(1) var voxels : texture_3d<u32>;

@compute @workgroup_size(4, 4, 4)
fn main(@builtin(global_invocation_id) gid : vec3u) {
  let v = textureLoad(voxels, vec3i(gid), 0).r;
  if (v != 0u) { atomicAdd(&counts[v], 1u); }
}
```

Dispatch it with `pass.dispatchWorkgroups(x, y, z)` — note those are **workgroup counts, not thread counts**. Dividing wrong is the classic compute-shader bug: you either process 1/64th of your data or read out of bounds.

---

## How WebGPU maps to native APIs — and where it stops

The JD explicitly asks for this. Here's the shape of the answer.

**The mapping is close.** WebGPU concepts have direct native analogues:

| WebGPU | Vulkan | D3D12 | Metal |
|---|---|---|---|
| Device | VkDevice | ID3D12Device | MTLDevice |
| Queue | VkQueue | Command Queue | MTLCommandQueue |
| Bind group | Descriptor set | Descriptor table | Argument buffer |
| Bind group layout | Descriptor set layout | Root signature (part) | — |
| Render pipeline | VkPipeline | PSO | MTLRenderPipelineState |
| Command encoder | Command buffer | Command list | MTLCommandBuffer |
| Render pass | VkRenderPass / dynamic rendering | — | Render command encoder |

Chrome implements WebGPU via **Dawn**, Firefox via **wgpu**; both target D3D12 on Windows, Metal on macOS/iOS, and Vulkan on Linux/Android.

**What WebGPU deliberately does not give you**, and why it matters for engine work:

- **Bindless resources.** No unbounded descriptor arrays. This is the big one: bindless is the foundation of modern GPU-driven renderers, since it lets a shader index arbitrary textures/buffers from GPU data. It is on the roadmap but not shipped. Consequence: your material system must work through bounded bind groups, texture arrays, or atlases.
- **Multi-draw indirect.** You can do single indirect draws (`drawIndexedIndirect`), but not "execute N draws whose count also comes from GPU memory." Under investigation; not core. Consequence: full GPU-driven rendering is partially available — the GPU can decide *what* to draw within one draw, but the CPU still issues the draws.
- **Mesh shaders.** Blocked behind bindless. Not available.
- **Geometry shaders and tessellation.** Never coming; deliberately excluded as slow/deprecated.
- **Hardware ray tracing.** Not exposed. **This is the decisive fact for a WebGPU voxel engine: all ray traversal is software, written by you, in compute or fragment shaders.** Which, for uniform grids, is actually fine — DDA on a grid is competitive with hardware BVH traversal for voxel data, and that's part of why voxels and WebGPU are a reasonable pairing.
- **Multiple queues / async compute.** One queue only. No explicit overlapping of compute and graphics.
- **64-bit atomics, and much of subgroup functionality.** Subgroup operations landed in Chrome (131+) and are rolling out across implementations; check current status before designing around them, and always have a non-subgroup fallback.
- **Explicit memory control.** No suballocation from your own heaps, no aliasing, no manual barriers.

The honest summary you'd give in an interview:

> "WebGPU is roughly a portable subset of D3D12/Vulkan/Metal with automatic barrier management and mandatory bounds/uniformity validation. You get explicit pipelines, descriptor sets, and compute — the architecture of a modern renderer transfers directly. What you give up is the bleeding-edge GPU-driven feature set: bindless, multi-draw indirect, mesh shaders, hardware RT, async compute. So you design for a slightly older GPU-driven architecture, and you compensate with algorithmic choices — which for voxels is a mild constraint rather than a fatal one."

---

## Default limits worth memorizing

These are the guaranteed baselines. Exceeding them requires requesting higher limits, which some devices won't grant.

- `maxBindGroups`: **4**
- `maxUniformBufferBindingSize`: **64 KiB**
- `maxStorageBufferBindingSize`: **128 MiB**
- `maxTextureDimension2D`: **8192**
- `maxTextureDimension3D`: **2048**
- `maxComputeInvocationsPerWorkgroup`: **256**
- `maxComputeWorkgroupSizeX/Y`: **256**, `Z`: **64**
- `maxComputeWorkgroupsPerDimension`: **65535**
- `maxComputeWorkgroupStorageSize`: **16 KiB**
- `maxVertexBuffers`: **8**, `maxVertexAttributes`: **16**
- `maxColorAttachments`: **8**
- `minUniformBufferOffsetAlignment`: **256 bytes**

The 3D texture limit, the 128 MiB storage binding, and the 256-thread workgroup cap are the three that will shape your voxel architecture directly.

---

## Exercise — Voxelforge, Stage 5

Time to render.

1. Init device with an error scope around everything; log validation errors loudly. Handle `device.lost`.
2. Draw a triangle. Then a cube with your Module 02 matrices and a depth buffer. Confirm depth works by putting two cubes at different distances.
3. Build a **32×32×32 chunk** filled by a seeded noise function. Naively draw one instanced cube per solid voxel. Measure CPU and GPU frame time. **Record the number.**
4. Replace instanced cubes with a single merged mesh: generate only the faces adjacent to air (six-neighbour check), pack each vertex into 8 bytes, upload one vertex + index buffer per chunk. Measure again. The ratio between step 3 and step 4 is the lesson.
5. Scale to a 16×16 grid of chunks and find the point where you fall below 16.6 ms. Note which side — CPU or GPU — gives out first.
6. Add a `webgpu-utils`-style layout generator (or write one) so your camera struct offsets are never hand-maintained.

---

## Go deeper

- **webgpufundamentals.org** (Gregg Tavares) — the best free WebGPU course. Work through it end to end; it is worth the two days.
- **toji.dev — Brandon Jones's "WebGPU Best Practices"** — bind group organization, buffer uploads, and the profiling guides used in Module 09. Written by one of the spec editors.
- **W3C WebGPU and WGSL specs** (w3.org/TR/webgpu, w3.org/TR/WGSL) — genuinely readable. Keep the limits table open.
- **Élie Michel, "Learn WebGPU for C++"** — even in C++, the clearest explanation of the object model and its native mapping.
- **gpuweb/gpuweb GitHub issues and `proposals/`** — where bindless, multi-draw-indirect, and subgroups are being designed. Reading these is how you speak credibly about the platform's trajectory.
- **Dawn and wgpu source** — when you need to know what actually happens.

---

**Next:** [Module 06 — Materials, Lighting, and Color](./06-materials-lighting-color.md)
