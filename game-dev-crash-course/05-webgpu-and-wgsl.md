# Module 05 — WebGPU and WGSL

### The API you will actually write against: its object model, how it maps onto D3D12 / Vulkan / Metal, and exactly where its ceiling is

*~35 min read · Part II: Rendering · Prerequisites: Modules 01–04*

---

## Read this first

WebGPU is not "WebGL 3."

WebGL was an OpenGL ES binding: a giant **global state machine**. You bound a texture to a slot, bound a buffer to another slot, set a dozen pieces of global state, and called `drawArrays()`, and the driver had to validate the whole configuration on the spot, every draw. It worked, it was slow, and it made large renderers fragile — any function could change global state out from under you.

WebGPU is modelled on the **explicit** native APIs of the 2015+ generation — D3D12, Vulkan, Metal — and inherits their central idea:

> Do expensive validation and state resolution **once, up front**, when you create immutable objects. Then, per frame, do almost nothing but record commands into a buffer and submit it.

If you understand that sentence, the API's shape becomes obvious rather than arbitrary. Every object that looks like ceremonial boilerplate — bind group layouts, pipeline layouts, immutable pipelines — exists to move work out of the frame.

### Where it ships (as of August 2026)

| Browser | Status |
|---|---|
| Chrome / Edge | Enabled by default since 113 (2023) — the reference implementation |
| Safari | Safari 26 (macOS Tahoe 26, iOS/iPadOS 26) |
| Firefox | 141 on Windows, 145 on macOS/Apple Silicon |
| Firefox Linux / Android | Still in progress |

Cross-browser **desktop** coverage arrived in early 2026. Mobile is partial. Always feature-detect and have a story for "no WebGPU" — even if that story is a message telling the user to upgrade.

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

**Adapter** = a specific physical GPU as reached through a specific backend. A laptop with integrated and discrete GPUs exposes two adapters. The adapter tells you what the hardware *can* do (its limits and optional features) but can't execute anything.

**Device** = your logical, exclusive connection to that adapter, created with a specific set of limits and features you asked for. Everything else hangs off it. A device can be **lost** (driver crash, GPU reset, tab backgrounded too long, or a laptop switching GPUs) — at which point every object you made from it is dead and you must recreate everything. Real engines handle this; toy projects don't and then mysteriously stop working.

### Minimal setup

```ts
// 1. Is WebGPU even here?
if (!navigator.gpu) throw new Error('WebGPU not supported in this browser');

// 2. Pick a GPU. 'high-performance' asks for the discrete one on a dual-GPU laptop.
const adapter = await navigator.gpu.requestAdapter({ powerPreference: 'high-performance' });
if (!adapter) throw new Error('No adapter — GPU may be blocklisted; check chrome://gpu');

// 3. Create the device, negotiating what you need.
const device = await adapter.requestDevice({
  requiredFeatures: ['timestamp-query'],           // opt-in; REJECTS if unsupported
  requiredLimits: { maxStorageBufferBindingSize: 512 * 1024 * 1024 },
});

// 4. Wire up the canvas.
const context = canvas.getContext('webgpu')!;
const format = navigator.gpu.getPreferredCanvasFormat();  // usually 'bgra8unorm'
context.configure({ device, format, alphaMode: 'opaque' });
```

Three things to notice immediately, because they're the design philosophy in miniature.

**Features and limits are negotiated.** You *ask* the adapter for what you need. **If you don't request it, you don't get it — even if the hardware supports it.** This makes portability explicit instead of accidental: your code either works everywhere it claims to, or fails loudly at startup on a machine that can't run it. Compare to WebGL, where you'd discover a missing extension in the middle of a frame in production.

Note the difference between the two:
- `requiredFeatures` — all-or-nothing capabilities (`timestamp-query`, `depth32float-stencil8`, `texture-compression-bc`). Request one the adapter lacks and `requestDevice` **rejects**. Check `adapter.features.has(...)` first and degrade gracefully.
- `requiredLimits` — numeric ceilings you want raised above the guaranteed baseline. Same rejection behavior.

**`getPreferredCanvasFormat()` matters.** It returns whatever the platform composites fastest — `bgra8unorm` on most desktops, `rgba8unorm` elsewhere. Hardcoding one causes an extra format conversion on every frame on the other platform. Always call it.

**Errors are asynchronous and scoped.** WebGPU does not throw on most invalid GPU work — throwing would require synchronizing with the GPU, which defeats the whole design. Instead it captures errors and surfaces them through three channels:

```ts
// Channel 1: scoped capture around a block of setup
device.pushErrorScope('validation');
const pipeline = device.createRenderPipeline({ /* ... */ });
const err = await device.popErrorScope();
if (err) console.error('Pipeline creation failed:', err.message);

// Channel 2: catch-all for anything you didn't scope
device.addEventListener('uncapturederror', (e) => {
  console.error('WebGPU error:', (e as GPUUncapturedErrorEvent).error.message);
});

// Channel 3: the device died
device.lost.then((info) => {
  console.error(`Device lost: ${info.reason} — ${info.message}`);
  if (info.reason !== 'destroyed') recreateEverything();
});
```

**During development, set up all three on line one.** WebGPU's validation messages are genuinely excellent — they tell you which binding, which field, and what was expected — but only if you're listening. Silent failure is otherwise the default experience and you will lose hours.

---

## Resources: buffers and textures

### Buffers

```ts
const vertexBuffer = device.createBuffer({
  size: data.byteLength,                                    // must be a multiple of 4
  usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST,   // every use, declared up front
});
device.queue.writeBuffer(vertexBuffer, 0, data);
```

**Usage flags are mandatory and immutable.** A buffer must declare *every* way it will ever be used at creation time. This lets the driver choose the right memory placement once (device-local VRAM vs host-visible staging memory) instead of guessing or migrating.

Forgetting a flag is a top-three beginner error. The classic: creating a buffer with `VERTEX` but not `COPY_DST`, then calling `writeBuffer` on it. The validation message is clear — read it.

The main flags:

| Flag | Meaning |
|---|---|
| `VERTEX` | Bindable as a vertex buffer |
| `INDEX` | Bindable as an index buffer |
| `UNIFORM` | Bindable as a uniform buffer |
| `STORAGE` | Bindable as a storage buffer (read or read-write in shaders) |
| `INDIRECT` | Can supply draw/dispatch arguments |
| `COPY_SRC` / `COPY_DST` | Can be the source/destination of a copy, including `writeBuffer` |
| `MAP_READ` / `MAP_WRITE` | Can be mapped into CPU memory (readback/upload staging) |

`MAP_READ` and `MAP_WRITE` are mutually restrictive — a mappable buffer can only be combined with the matching COPY flag. That's deliberate: it forces you to use a separate staging buffer and an explicit copy, which is what native APIs make you do anyway.

### Uniform vs storage buffers

A decision you make constantly:

| | Uniform | Storage |
|---|---|---|
| Default max binding size | **64 KiB** | **128 MiB** |
| Access in shader | read-only | read, or read-write |
| Layout rules | strict, std140-like padding | more relaxed |
| Speed | often faster (goes through a dedicated constant cache) | slightly slower, far more flexible |
| Array sizing | fixed at compile time | can be runtime-sized (`array<T>`) |
| Use for | camera, per-frame constants, material params | per-instance arrays, voxel data, compute I/O |

Rule of thumb: **small and read by every invocation → uniform. Large or written → storage.**

### The padding rules will bite you

This is the number one source of "my shader gets garbage values and I can't see why."

In WGSL, alignment rules come from the underlying hardware and are stricter than you expect. The one that catches everyone: **a `vec3<f32>` has size 12 but alignment 16.**

```wgsl
struct Light { position: vec3f, intensity: f32 };
// size 16. The f32 slots into the 4 bytes of padding after the vec3. Perfect.

struct Bad   { intensity: f32, position: vec3f };
// size 32! The vec3 must start at offset 16, so 12 bytes are wasted after the f32,
// and the struct is padded to 32 to keep its own 16-byte alignment.
```

Now imagine your TypeScript writes 16 bytes and your shader reads at offset 16. Every value is wrong, in a way that looks exactly like a math bug, with no error message anywhere.

The general rules, for reference:

| Type | Align | Size |
|---|---|---|
| `f32`, `i32`, `u32` | 4 | 4 |
| `vec2<f32>` | 8 | 8 |
| `vec3<f32>` | **16** | 12 |
| `vec4<f32>` | 16 | 16 |
| `mat4x4<f32>` | 16 | 64 |
| `struct` | max of members' alignments | rounded up to that alignment |
| `array<T, N>` | align of T | N × (size of T, rounded up to its alignment) |

**Practical rules that avoid the whole problem:**

1. Put your `vec4`s and `mat4`s first, then `vec3`+`f32` pairs, then loose scalars.
2. Never end a struct with a `vec3` if you can help it.
3. **Generate your buffer layouts from a single source of truth** rather than hand-writing offsets on both sides.

That third one is what professionals actually do. Libraries like `webgpu-utils` (parses your WGSL and gives you correctly-offset typed-array views) or `typegpu` (types flowing the other direction) solve it, and so does a 100-line codegen script you write yourself. **Do this early** — before you have twenty structs — because retrofitting it is miserable.

### Textures

A texture carries a **format**, a **dimension** (`1d` / `2d` / `3d`), a **size**, a **mip level count**, a **sample count** (for MSAA), and usage flags.

```ts
const tex = device.createTexture({
  size: [width, height, layers],
  format: 'rgba8unorm',
  dimension: '2d',
  mipLevelCount: Math.floor(Math.log2(Math.max(width, height))) + 1,
  usage: GPUTextureUsage.TEXTURE_BINDING | GPUTextureUsage.COPY_DST
       | GPUTextureUsage.RENDER_ATTACHMENT,  // needed if you generate mips by rendering
});
```

Format naming decoded, since it looks like line noise at first:

| Suffix | Meaning |
|---|---|
| `unorm` | Unsigned integer in memory, presented to the shader as a float in **[0, 1]** |
| `snorm` | Signed, presented as **[−1, 1]** |
| `uint` / `sint` | Integer in memory, **integer** in the shader (no filtering allowed) |
| `float` | Actual floating point |
| `-srgb` | Stored in sRGB, hardware converts to/from linear on read/write (see Module 06) |

So `rgba8unorm` is four bytes per texel presented as four floats in [0,1]; `r8uint` is one byte presented as an integer.

**A `GPUTexture` is not directly bindable.** You bind a **`GPUTextureView`** — a description of *how to interpret* the texture (which mip range, which array layers, which dimension). `texture.createView()` gives you the whole thing; explicit views let you bind one mip level or one slice of an array. Creating views per frame is cheap but not free — cache them.

**Samplers** are separate objects describing *how to read*, not *what to read*:

```ts
const sampler = device.createSampler({
  magFilter: 'linear',      // when the texture is magnified (bigger than its texels)
  minFilter: 'linear',      // when minified
  mipmapFilter: 'linear',   // 'linear' here = trilinear (blend between mip levels)
  addressModeU: 'repeat',   // what happens outside [0,1]
  addressModeV: 'repeat',
  maxAnisotropy: 4,         // anisotropic filtering taps
});
```

Separating samplers from textures (as opposed to WebGL, where filtering was texture state) means you can read the same texture with different filtering in different places — e.g. `nearest` for a voxel atlas lookup and `linear` for a smooth gradient — without duplicating data.

**For voxel work**, `3d` textures with `r8uint` or `rgba8unorm` formats are your bread and butter — with the hard constraint that **`maxTextureDimension3D` defaults to 2048**, so a single 3D texture tops out around 2048³. (And 2048³ at 1 byte/voxel is 8 GB, so you'd hit memory long before you hit the limit.) **This limit is a genuine architectural driver**: it's part of why brick/pool structures exist rather than one giant volume (Module 07).

### Getting data in

```ts
device.queue.writeBuffer(buffer, offset, data);            // buffers
device.queue.writeTexture({ texture }, data, layout, size); // textures
```

These are the easy path and they're fine for most uploads. Under the hood the implementation stages the copy through driver-managed memory and versions it so you can safely overwrite a buffer the GPU might still be reading (the ring-buffer problem from Module 01, solved for you).

For large streaming uploads — voxel chunk meshes arriving from a worker every frame — you eventually want `mappedAtCreation: true` buffers or a pool of `MAP_WRITE` staging buffers, so you can write directly into GPU-visible memory without the intermediate copy. Start with `writeBuffer`, measure, and only escalate when it shows up in a profile.

---

## Bind groups: the part that feels weird

You don't bind resources individually. You group them, and the group's *shape* is declared and validated separately from its *contents*.

```ts
// The SHAPE: what kinds of things live at which binding points, visible to which stages.
const layout = device.createBindGroupLayout({
  entries: [
    { binding: 0, visibility: GPUShaderStage.VERTEX | GPUShaderStage.FRAGMENT,
      buffer: { type: 'uniform' } },
    { binding: 1, visibility: GPUShaderStage.FRAGMENT, texture: { sampleType: 'float' } },
    { binding: 2, visibility: GPUShaderStage.FRAGMENT, sampler: { type: 'filtering' } },
  ],
});

// The INSTANCE: actual resources conforming to that shape. Validated once, here.
const bindGroup = device.createBindGroup({
  layout,
  entries: [
    { binding: 0, resource: { buffer: cameraBuffer } },
    { binding: 1, resource: albedoTexture.createView() },
    { binding: 2, resource: sampler },
  ],
});
```

At draw time, `setBindGroup(index, bindGroup)` swaps a whole set at once with essentially **no validation cost** — it was all done at creation. That's the payoff for the ceremony. In WebGL, every `bindTexture` had to be checked against the current program; here, nothing is checked in the hot path.

### Organize bind groups by update frequency

**This is the single most important architectural convention in modern graphics APIs**, and it applies identically in Vulkan and D3D12. Get it right once and your renderer scales; get it wrong and you rebuild bind groups thousands of times per frame.

| Group | Contents | Set how often |
|---|---|---|
| **0** | Per-frame: camera, time, global lighting, shadow maps | once per frame |
| **1** | Per-pass: render targets as inputs, pass constants | once per pass |
| **2** | Per-material: albedo/normal/roughness textures, material params | when the material changes |
| **3** | Per-object/per-draw: model matrix, instance offsets | per draw |

You get **4 bind groups by default** (`maxBindGroups: 4`), which is not a coincidence — it's exactly enough for this idiom, and the spec chose the number to make it the obvious path.

Rebinding group 3 does *not* invalidate groups 0–2, so the cheap thing changes often and the expensive thing changes rarely. That's the whole design.

### Dynamic offsets

The escape valve for per-object data, and the technique that lets you avoid creating a bind group per object:

```ts
// Layout declares the binding as dynamically offset.
{ binding: 0, visibility: ..., buffer: { type: 'uniform', hasDynamicOffset: true } }

// One big buffer holds every object's uniforms, 256 bytes apart.
// At draw time you pass the byte offset:
pass.setBindGroup(3, objectBindGroup, [objectIndex * 256]);
```

One bind group, thousands of objects, zero per-object allocation. Offsets must be aligned to `minUniformBufferOffsetAlignment` (**256 bytes** by default), which is why you'll constantly see uniform structs padded out to 256 bytes even when they hold 80 bytes of data.

---

## Pipelines: all state, baked

```ts
const pipeline = device.createRenderPipeline({
  layout: device.createPipelineLayout({ bindGroupLayouts: [layout] }),
  vertex: {
    module: shaderModule, entryPoint: 'vs',
    buffers: [{ arrayStride: 8,
                attributes: [{ shaderLocation: 0, offset: 0, format: 'uint32x2' }] }],
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

Everything — shaders, vertex layout, blend state, depth state, cull mode, MSAA count, target formats — is frozen into one immutable object. **That's why binding a pipeline is fast: there is nothing left to resolve.** The driver has already compiled the shaders for exactly this configuration.

(`layout: 'auto'` also works and infers the bind group layouts from your shader. Convenient for learning; avoid it in a real engine, because auto-generated layouts aren't shareable between pipelines, so you can't reuse one bind group across two pipelines.)

### The consequence is combinatorial — and it's the industry's most common stutter

**Every unique combination of shader × blend mode × target format × cull mode × MSAA count is a separate pipeline object.** And creating one triggers shader compilation, which takes **milliseconds to hundreds of milliseconds**.

Compiling a pipeline during gameplay is a guaranteed, extremely visible hitch. This is the thing behind "shader compilation stutter," which has been a widely-covered problem in shipped PC games for years — you walk around a corner, a new material appears, and the game freezes for 200 ms.

**The professional answer:**

1. **Enumerate every pipeline you will need and compile them all during loading.** If a material variant can exist, create its pipeline before gameplay starts.
2. Use **`createRenderPipelineAsync()`** so compilation happens off the critical path and you can show a progress bar.
3. Build a **pipeline cache keyed by a state hash**, with an explicit warm-up phase that walks the cache.
4. **Never create a pipeline inside the frame loop.** Assert on it in debug builds if you have to.

This is a real engine feature, it's a great thing to have opinions about in an interview, and mentioning "shader compilation stutter" by name signals that you've paid attention to how games actually fail in the wild.

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
    depthLoadOp: 'clear', depthStoreOp: 'discard',   // don't need depth after the pass
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

Recording is pure CPU work with no GPU involvement. `submit` hands the buffer over. **Nothing has rendered when `submit` returns** — see Module 01 if that still feels strange.

Note `context.getCurrentTexture()` — you call it **once per frame**, inside the frame, and the view you get is only valid for that frame. Caching it across frames is a classic bug that produces a frozen image.

### WebGPU inserts barriers automatically

In Vulkan and D3D12, when one pass writes a texture and the next pass reads it, **you** must specify a resource transition and a memory barrier saying exactly which stages and which access types are involved. Getting it wrong causes corruption, hangs, or hardware-specific bugs that don't reproduce on your machine. It is widely considered the hardest part of those APIs.

WebGPU tracks resource usage across your command encoder and inserts the barriers for you. This is a genuine ergonomic win and a small performance cost, since the implementation must be conservative where a human would know better.

**Know that this is happening.** An interviewer asking "how does WebGPU differ from Vulkan?" is very often fishing for exactly this, and "automatic barrier and resource-state tracking, at a small conservatism cost" is the answer that lands.

### Render bundles

```ts
const bundleEncoder = device.createRenderBundleEncoder({ colorFormats: [format], depthStencilFormat: 'depth32float' });
// ... record setPipeline / setBindGroup / draw calls, once ...
const bundle = bundleEncoder.finish();

// Then every frame, in the render pass:
pass.executeBundles([bundle]);
```

A **render bundle** pre-records a sequence of draws that can be replayed across frames. Because the recording work — the JavaScript calls, the validation, the state tracking — happens once instead of every frame, bundles cut JS-side overhead substantially.

**This matters much more in a JS engine than in a C++ one.** In C++, `setBindGroup` is a cheap function call; in JS it's a call across the WebIDL boundary with argument marshalling. For a voxel world where chunk meshes change rarely and you're issuing hundreds of draws, bundles are a natural fit and can be worth several milliseconds of CPU time.

The constraint: a bundle is tied to specific attachment formats and cannot contain `setViewport`, `setScissorRect`, or anything that changes pass-level state.

---

## WGSL in twenty minutes

Rust-flavoured syntax, strongly typed, no preprocessor, no pointers into arbitrary memory. Designed so that a browser can *prove* a shader is safe before running it — no out-of-bounds access, no uninitialized reads, no infinite loops that hang the GPU.

```wgsl
struct Camera {
  viewProj : mat4x4f,
  position : vec3f,
  time     : f32,      // slots into the vec3's padding — deliberate
};

@group(0) @binding(0) var<uniform> camera : Camera;
@group(0) @binding(1) var<storage, read> instances : array<Instance>;
@group(1) @binding(0) var albedo : texture_2d<f32>;
@group(1) @binding(1) var samp : sampler;

struct VSOut {
  @builtin(position) clip : vec4f,   // required: clip-space position
  @location(0) uv : vec2f,           // varyings, interpolated by the rasterizer
  @location(1) normal : vec3f,
};

@vertex
fn vs(@location(0) packed : vec2u, @builtin(instance_index) i : u32) -> VSOut {
  let pos = unpackVoxelPosition(packed.x);
  var out : VSOut;
  out.clip   = camera.viewProj * instances[i].model * vec4f(pos, 1.0);
  out.uv     = unpackUV(packed.y);
  out.normal = FACE_NORMALS[packed.y >> 28u];
  return out;
}

@fragment
fn fs(in : VSOut) -> @location(0) vec4f {
  let base  = textureSample(albedo, samp, in.uv);
  let ndotl = max(dot(normalize(in.normal), LIGHT_DIR), 0.0);
  return vec4f(base.rgb * (ndotl * 0.8 + 0.2), base.a);
}
```

### The attribute syntax

Those `@` decorations are how WGSL connects shader code to API objects:

| Attribute | Meaning |
|---|---|
| `@group(n) @binding(m)` | Matches bind group index `n`, binding slot `m` in your layout |
| `@location(n)` | A vertex attribute slot (input) or an interpolated varying / color target (output) |
| `@builtin(position)` | The clip-space output the hardware requires from a vertex shader |
| `@builtin(instance_index)`, `@builtin(vertex_index)` | Provided by the hardware |
| `@builtin(global_invocation_id)` | Compute: this thread's 3D index across the whole dispatch |
| `@vertex` / `@fragment` / `@compute` | Marks an entry point |
| `@workgroup_size(x, y, z)` | Compute: threads per workgroup |
| `@interpolate(flat)` | Don't interpolate this varying (Module 04) |

### Things that will trip you up coming from GLSL or TypeScript

- **`let` is immutable, `var` is mutable.** The opposite of your JavaScript instinct for `let`. `let` in WGSL is closer to `const` in JS.

- **Address spaces are explicit:** `var<uniform>`, `var<storage, read>`, `var<storage, read_write>`, `var<workgroup>`, `var<private>`, `var<function>` (the default for locals). You cannot take a pointer across address spaces.

- **No implicit conversions.** `1` is `i32`, `1.0` is `f32`, `1u` is `u32`, and mixing them is a **compile error**, not a warning. `f32(myInt)` and `i32(myFloat)` are explicit. This catches real precision bugs and feels pedantic for the first week.

- **Uniformity analysis.** `textureSample` computes derivatives from neighbouring lanes (Module 04's 2×2 quads), so it is only legal in **uniform control flow** within a fragment shader. Sampling inside a divergent `if` is a *compile error*:

  ```wgsl
  if (someVaryingCondition) {
    let c = textureSample(tex, samp, uv);       // ❌ compile error
    let c = textureSampleLevel(tex, samp, uv, 0.0);  // ✅ explicit LOD, no derivatives
  }
  // ✅ Or hoist the sample above the branch.
  ```

  **Everyone hits this within their first week.** The fix is either `textureSampleLevel` with an explicit mip, or restructuring so the sample is unconditional.

- **No preprocessor.** No `#ifdef`, no `#include`, no `#define`. Shader variants and includes are **your build system's job** — you write a small string-templating layer, or use `wgsl-preprocessor`-style tooling, or generate WGSL from TypeScript. Every engine ends up with one; decide early what yours looks like rather than accumulating `.replace()` calls.

- **Built-in functions are largely the GLSL set:** `dot`, `cross`, `normalize`, `length`, `mix`, `clamp`, `saturate` (via `clamp`), `smoothstep`, `step`, `pow`, `exp`, `fract`, `floor`, `sign`, `abs`, `min`, `max`, `textureSample`, `textureLoad`, `textureStore`, `atomicAdd`, `workgroupBarrier`. One notable difference: **there is no ternary operator** — use `select(falseValue, trueValue, condition)`, and note the argument order is the reverse of what you'd guess.

- **Swizzling works as you'd hope:** `v.xyz`, `v.rgb`, `v.xxyy`, and you can assign to swizzles of a `var`.

### A compute entry point

```wgsl
@group(0) @binding(0) var<storage, read_write> counts : array<atomic<u32>>;
@group(0) @binding(1) var voxels : texture_3d<u32>;

@compute @workgroup_size(4, 4, 4)
fn main(@builtin(global_invocation_id) gid : vec3u) {
  let v = textureLoad(voxels, vec3i(gid), 0).r;
  if (v != 0u) { atomicAdd(&counts[v], 1u); }
}
```

Dispatch it with:

```ts
pass.dispatchWorkgroups(
  Math.ceil(sizeX / 4),   // NOT sizeX
  Math.ceil(sizeY / 4),
  Math.ceil(sizeZ / 4),
);
```

**Those are workgroup counts, not thread counts.** Dividing wrong is *the* classic compute-shader bug: pass `sizeX` and you dispatch 4× too many threads (reading out of bounds); forget to divide at all and you process 1/64th of your data and wonder why the result is mostly empty. Always write the `Math.ceil(n / WORKGROUP_SIZE)` and always bounds-check inside the shader, since `ceil` means the last workgroup runs past the end:

```wgsl
if (any(gid >= dims)) { return; }   // guard for the ragged edge
```

---

## How WebGPU maps to native APIs — and where it stops

The Engine JD explicitly asks for this. Here's the shape of the answer.

### The mapping is close

WebGPU concepts have direct native analogues:

| WebGPU | Vulkan | D3D12 | Metal |
|---|---|---|---|
| Device | `VkDevice` | `ID3D12Device` | `MTLDevice` |
| Queue | `VkQueue` | Command Queue | `MTLCommandQueue` |
| Buffer | `VkBuffer` | `ID3D12Resource` | `MTLBuffer` |
| Bind group | Descriptor set | Descriptor table | Argument buffer |
| Bind group layout | Descriptor set layout | Root signature (part) | — |
| Pipeline layout | Pipeline layout | Root signature | — |
| Render pipeline | `VkPipeline` | PSO (Pipeline State Object) | `MTLRenderPipelineState` |
| Command encoder | Command buffer | Command list | `MTLCommandBuffer` |
| Render pass | `VkRenderPass` / dynamic rendering | — (OMSetRenderTargets) | Render command encoder |
| WGSL | SPIR-V | DXIL / HLSL | MSL |

Chrome implements WebGPU via **Dawn** (C++), Firefox via **wgpu** (Rust). Both target D3D12 on Windows, Metal on macOS/iOS, and Vulkan on Linux/Android. Both are usable as standalone native libraries, which means **your WebGPU knowledge transfers directly to native development** — a genuinely good thing to point out when someone worries that "web graphics" isn't real graphics.

### What WebGPU deliberately does not give you

And why each one matters for engine work:

**Bindless resources.** No unbounded descriptor arrays — a shader can't index into "any texture in the scene" using an index from GPU memory. **This is the big one**: bindless is the foundation of modern GPU-driven renderers. It's on the roadmap but not shipped. *Consequence:* your material system must work through bounded bind groups, **texture arrays** (many same-sized textures in one object, indexed by layer), or **atlases** (many images packed into one texture). For voxels, a texture array of block faces is the standard workaround and works well.

**Multi-draw indirect.** You can do single indirect draws (`drawIndexedIndirect`), but not "execute N draws where N itself comes from GPU memory." Under investigation; not core. *Consequence:* full GPU-driven rendering is only partially available — the GPU can decide *what* and *how many instances* within one draw, but the CPU still issues the draw calls.

**Mesh shaders.** Blocked behind bindless. Not available.

**Geometry shaders and tessellation.** Never coming; deliberately excluded as slow and effectively deprecated on modern hardware.

**Hardware ray tracing.** Not exposed. **This is the decisive fact for a WebGPU voxel engine: all ray traversal is software, written by you, in compute or fragment shaders.** Which, for uniform grids, is actually fine — DDA on a grid is competitive with hardware BVH traversal for voxel data, because a grid needs no acceleration structure at all. **Voxels and WebGPU are a reasonable pairing precisely because of this**, and being able to explain that is worth a lot in this specific interview.

**Multiple queues / async compute.** One queue only. No explicit overlapping of independent compute and graphics work (Module 03).

**64-bit atomics**, and much of subgroup functionality. Subgroup operations landed in Chrome (131+) and are rolling out across implementations; check current status before designing around them, and **always ship a non-subgroup fallback path**.

**Explicit memory control.** No suballocation from your own heaps, no memory aliasing between resources, no manual barriers. The implementation decides.

### The honest summary for an interview

> "WebGPU is roughly a portable subset of D3D12/Vulkan/Metal with automatic barrier management and mandatory bounds and uniformity validation. You get explicit pipelines, descriptor sets, and compute — the *architecture* of a modern renderer transfers directly, and Dawn and wgpu are both usable natively so the knowledge isn't web-only. What you give up is the bleeding-edge GPU-driven feature set: bindless, multi-draw indirect, mesh shaders, hardware RT, async compute. So you design for a slightly older GPU-driven architecture and compensate with algorithmic choices — which for voxels is a mild constraint rather than a fatal one, since grid traversal doesn't need hardware RT anyway."

---

## Default limits worth memorizing

These are the **guaranteed baselines** — every conformant implementation supports at least these. Exceeding them requires requesting higher limits, which some devices won't grant, so designing within them means your engine runs everywhere.

| Limit | Default |
|---|---|
| `maxBindGroups` | **4** |
| `maxUniformBufferBindingSize` | **64 KiB** |
| `maxStorageBufferBindingSize` | **128 MiB** |
| `maxTextureDimension2D` | **8192** |
| `maxTextureDimension3D` | **2048** |
| `maxComputeInvocationsPerWorkgroup` | **256** |
| `maxComputeWorkgroupSizeX/Y` | **256**, `Z`: **64** |
| `maxComputeWorkgroupsPerDimension` | **65535** |
| `maxComputeWorkgroupStorageSize` | **16 KiB** |
| `maxVertexBuffers` | **8** |
| `maxVertexAttributes` | **16** |
| `maxColorAttachments` | **8** |
| `minUniformBufferOffsetAlignment` | **256 bytes** |

**The three that will shape your voxel architecture directly:** the 2048³ 3D texture limit, the 128 MiB storage binding size, and the 256-thread workgroup cap. Write them on a sticky note.

---

## Common confusions

**"`requestAdapter` returned null, my code is broken."** Usually not. It means no adapter was available: WebGPU disabled, the GPU is on a driver blocklist, or you're in an insecure context (WebGPU requires HTTPS or localhost). Check `chrome://gpu` first.

**"My uniform values are garbage."** Padding, 95% of the time. Print `sizeof` on both sides — write a tiny WGSL shader that outputs `sizeof(MyStruct)` if you have to. Then generate your layouts instead of hand-writing them.

**"I get a validation error about bind group layout mismatch and both look identical."** Layouts are compared structurally, but `layout: 'auto'` produces layouts that are *not* compatible across pipelines even if identical. Create explicit layouts and share the objects.

**"`writeBuffer` right before `submit` — is that safe?"** Yes. `writeBuffer` is ordered on the queue relative to submits, and the implementation handles staging. What's *not* safe is assuming the write is visible to the CPU or to a `mapAsync` in progress.

**"My compute shader does nothing."** Check the dispatch arithmetic (workgroups, not threads), check that the buffer has the `STORAGE` usage flag, check that the bind group visibility includes `GPUShaderStage.COMPUTE`, and check that you called `pass.end()` and submitted. In that order.

**"Everything renders black but there are no errors."** Go back to the Module 04 checklist. WebGPU validates *API usage*, not *whether your matrices make sense*. A perfectly valid frame can be entirely wrong.

---

## Exercise — Voxelforge, Stage 5

**Time to actually render something.** This is the highest-value stage in the course for interview purposes — having *touched* the API changes how you talk about it, and you can't fake that.

**1. Init, defensively.** Create the device with an error scope around everything and a loud `uncapturederror` handler. Handle `device.lost` by logging the reason. Write this once and reuse it in every future project.

**2. Draw a triangle.** Then a cube using your Module 02 matrices and a depth buffer. **Confirm depth actually works** by putting two cubes at different distances and checking the near one occludes the far one — a surprising number of first renderers have a broken depth setup that only shows up much later.

**3. Build a 32×32×32 chunk** filled by a seeded noise function (reuse your Module 01 PRNG). Naively draw **one instanced cube per solid voxel**. Measure CPU and GPU frame time. **Record the number** — you're going to beat it by an enormous margin and the comparison is the point.

**4. Replace instanced cubes with a single merged mesh.** Generate only the faces adjacent to air (a six-neighbour check per voxel), pack each vertex into 8 bytes using your Module 04 bit layout, and upload one vertex + index buffer per chunk. Measure again.

**⭐ The ratio between step 3 and step 4 is the lesson.** Expect somewhere between 10× and 100×. Write the number in your project README — it's exactly the kind of measured claim that makes a portfolio project credible.

**5. Scale to a 16×16 grid of chunks** and find the point where you fall below 16.6 ms. **Note which side — CPU or GPU — gives out first**, and how you determined that. (Hint: if adding GPU work doesn't change frame time, you're CPU-bound.)

**6. Add a layout generator.** Use `webgpu-utils` or write one, so your camera struct offsets are never hand-maintained. Do this now, at 3 structs, not later at 30.

**Stretch:** wrap your static chunk draws in a render bundle and measure the CPU-side saving. In a JS engine this is often larger than you expect.

---

## Go deeper

- **webgpufundamentals.org** (Gregg Tavares) — the best free WebGPU course by a wide margin. Work through it end to end; it is worth the two days and it will save you two weeks.
- **toji.dev — Brandon Jones's "WebGPU Best Practices"** — bind group organization, buffer upload strategies, and the profiling guides used in Module 09. Written by one of the spec editors.
- **W3C WebGPU and WGSL specs** (w3.org/TR/webgpu, w3.org/TR/WGSL) — genuinely readable, unusually so for specs. Keep the limits table open in a tab.
- **Élie Michel, "Learn WebGPU for C++"** — even if you never write C++, the clearest explanation of the object model and its native mapping.
- **gpuweb/gpuweb GitHub issues and `proposals/`** — where bindless, multi-draw-indirect, and subgroups are being designed. Reading these is how you speak credibly about the platform's trajectory in an interview.
- **`developer.chrome.com/blog/new-in-webgpu-*`** — the running changelog. Skim the last few before an interview.
- **Dawn and wgpu source** — when you need to know what actually happens underneath.

---

**Next:** [Module 06 — Materials, Lighting, and Color](./06-materials-lighting-color.md)
