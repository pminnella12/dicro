# Module 02 — 3D Math and the Chain of Spaces

### The seven coordinate systems every vertex passes through, and the small number of operations that move it between them

*~13 min read · Part I: Foundations · Prerequisites: Module 01*

---

Graphics math has a reputation for being hard. It isn't. It is *unforgiving* — one wrong sign or one transposed matrix produces a black screen with no error message — but the actual body of knowledge is small. You need roughly six operations and one pipeline of coordinate spaces.

The reason it feels hard is that nobody tells you the pipeline up front. So here it is first, and then the pieces.

> Every vertex in every frame travels the same road: **model → world → view → clip → NDC → screen**. Every "why is my object invisible / inverted / stretched" bug is a bug in one of those hops.

---

## The chain of spaces

**1. Model space (object space).** The coordinates the artist authored. A voxel chunk's corner is at `(0,0,0)`; a character's origin is between their feet. Nothing here knows about the world.

**2. World space.** Everything placed into one shared frame. Multiply by the **model matrix** (translate × rotate × scale). This is the space you think in when you say "the player is at (120, 64, -30)."

**3. View space (camera space / eye space).** The world re-expressed with the camera at the origin looking down an axis. Multiply by the **view matrix**, which is the *inverse* of the camera's world transform. That inversion trips everyone: moving the camera right is mathematically identical to moving the entire world left.

**4. Clip space.** Multiply by the **projection matrix**. This is a 4D homogeneous space where the GPU can cheaply test whether a vertex is inside the visible volume: a point is visible if `-w ≤ x,y ≤ w` and (in WebGPU/D3D/Metal convention) `0 ≤ z ≤ w`. The GPU clips triangles here, before the divide, because clipping after the divide is mathematically broken for points behind the camera.

**5. NDC (normalized device coordinates).** Divide `xyz` by `w` — the **perspective divide**. This is the step that actually creates perspective: distant things have larger `w`, so dividing shrinks them. In WebGPU, NDC is `x ∈ [-1,1]`, `y ∈ [-1,1]` (y up), `z ∈ [0,1]`.

**6. Screen space (framebuffer coordinates).** The viewport transform maps NDC to pixels. In WebGPU, `(0,0)` is the **top-left** and y increases downward — the opposite of NDC's y-up. This flip is the single most common source of "my texture/image is upside down."

**7. Texture space (UV).** `[0,1]²` addressing into a texture, with `(0,0)` at the top-left in WebGPU.

The whole chain is usually collapsed into one matrix multiply on the CPU and one in the shader:

```wgsl
@vertex
fn vs(@location(0) position: vec3f) -> @builtin(position) vec4f {
  return camera.viewProj * model.matrix * vec4f(position, 1.0);
}
```

`@builtin(position)` is clip space. The hardware does the divide and viewport transform for you.

---

## Vectors: the two products that matter

### Dot product — "how aligned are these?"

```
a · b = ax*bx + ay*by + az*bz = |a| |b| cos θ
```

For **unit** vectors, the dot product *is* the cosine of the angle between them. Every use follows from that:

- `dot(normal, lightDir)` → diffuse lighting term (Lambert's cosine law). Clamp at 0; negative means facing away.
- `dot(v, v)` → squared length. **Compare squared distances** instead of calling `sqrt` — sorting and range checks never need the actual distance.
- `dot(pointToPlane, planeNormal) - d` → **signed distance to a plane**. This one operation is the entire basis of frustum culling.
- Sign of `dot(viewDir, faceNormal)` → backface test.
- Projection of `a` onto unit `b` is `dot(a,b) * b` — used constantly in collision resolution to split a velocity into "along the surface" and "into the surface."

### Cross product — "give me a perpendicular"

```
a × b = (ay*bz - az*by,  az*bx - ax*bz,  ax*by - ay*bx)
```

- Produces a vector perpendicular to both, with length `|a||b| sin θ`.
- **Triangle normal:** `normalize(cross(b - a, c - a))`. Winding order decides which way it points, which decides which side is "front," which decides whether backface culling makes your model vanish.
- Building an orthonormal basis (camera right/up, tangent frames for normal mapping).
- Its length is twice the triangle's area — used for barycentric coordinates and degenerate-triangle detection.

**Normals are not positions.** They are directions, and they transform differently: a position uses `w = 1`, a direction uses `w = 0` (so translation is ignored). And if your model matrix has non-uniform scale, normals must be transformed by the **inverse transpose** of the upper-left 3×3, or they stop being perpendicular to the surface and your lighting goes subtly wrong. Uniform scale lets you skip this; the moment an artist scales something 2× on one axis only, you need it.

---

## Matrices: the parts you actually manipulate

A 4×4 matrix is a coordinate frame plus a translation:

```
| Rx Ux Fx Tx |     R = right axis      T = translation
| Ry Uy Fy Ty |     U = up axis
| Rz Uz Fz Tz |     F = forward axis
|  0  0  0  1 |
```

Reading a matrix by pulling out those four columns is a debugging superpower. If an object is in the wrong place, print the translation column. If it's sheared, look at whether the axes are still orthogonal and unit-length.

**Order is not commutative.** `T * R` (rotate then translate — orbit around your own origin, then move) is not `R * T` (move, then rotate around the world origin — orbiting a point). Standard object transform is `M = T * R * S`: scale first, then rotate, then translate.

**Column-major vs row-major** is the other classic trap. WebGPU/WGSL uses column-major storage and column-vector convention (`M * v`). Most JS math libraries (gl-matrix) match this. Some references and D3D-era literature use row vectors (`v * M`), where all the multiplication orders reverse and matrices appear transposed. When adapting code from a tutorial, check the convention before you debug the math.

**Inverses.** You rarely need a general 4×4 inverse. For a rigid transform (rotation + translation, no scale), the inverse is: transpose the 3×3 rotation, and negate-then-rotate the translation. That is what a view matrix is — cheap, exact, and no numerical drift.

---

## Rotations: why quaternions win

Three ways to represent orientation:

**Euler angles** (yaw/pitch/roll) — intuitive, great for a first-person camera's input, terrible everywhere else. They suffer **gimbal lock** (at pitch = ±90° you lose a degree of freedom), interpolate badly, and require you to pin down an order convention that everyone gets wrong.

**Rotation matrices** — 9 numbers for 3 degrees of freedom, drift out of orthonormality when you accumulate them, expensive to interpolate.

**Quaternions** — 4 numbers (`x, y, z, w`), no gimbal lock, compose with a single multiply, interpolate correctly via `slerp`, and renormalize cheaply. A unit quaternion encodes "rotate by angle θ around unit axis `a`" as `(a * sin(θ/2), cos(θ/2))`.

Practical policy used by nearly every engine:

- **Store** orientation as a quaternion.
- **Compose** with quaternion multiplication.
- **Interpolate** with `slerp` (or `nlerp` for small deltas — cheaper, non-constant angular velocity, usually fine).
- **Convert to a matrix** once per frame at the point you build the model matrix.
- **Accept Euler angles at the boundary** — designer-facing tools and FPS camera input — and convert immediately.

You do not need to derive quaternion multiplication. You need to know why they're used, that `q` and `-q` represent the same rotation (so slerp must pick the shorter path by flipping sign when the dot product is negative), and that you must renormalize after repeated composition.

---

## Projection, and the depth buffer's dirty secret

The **perspective projection matrix** is built from vertical FOV, aspect ratio, and near/far planes. It does two things: it scales x and y by the focal length, and it copies `-z` into `w` so the perspective divide happens.

The part worth understanding deeply is **z precision**.

After projection, depth is stored as a nonlinear function of view-space distance — roughly proportional to `1/z`. That means precision is lavished on things close to the near plane and starved far away. Concretely, with a near plane of 0.01 and a far plane of 10,000, more than 90% of your depth buffer's precision is spent in the first few meters, and distant geometry gets **z-fighting**: flickering, stitched surfaces where two coplanar polygons alternate winning the depth test.

The fixes, in order of preference:

1. **Push the near plane out.** Going from 0.01 to 0.1 buys you an order of magnitude of precision. It is free. Do this first, always.
2. **Reversed-Z.** Map near→1.0 and far→0.0, use a float depth format, and flip the depth compare to `greater`. The floating-point format's exponent precision near zero cancels the projection's `1/z` distribution almost exactly, giving near-uniform precision across the whole range. This is standard practice in modern engines and is a strong thing to mention in an interview.
3. **Logarithmic depth** — for extreme ranges (space/planetary scale), at some cost.

WebGPU's `0..1` clip-space z convention (inherited from D3D/Metal rather than OpenGL's `-1..1`) is what makes reversed-Z clean to set up — one of several places where WebGPU's design shows its native-API lineage.

**Orthographic projection** has no divide (`w` stays 1), so depth is linear and parallel lines stay parallel. You use it for UI, for 2D, and — importantly — for **shadow maps from directional lights**, where the light is effectively infinitely far away.

---

## The geometry you will actually write

Five routines cover the overwhelming majority of engine geometry work:

**Ray–AABB (slab test).** For each axis, compute the parametric entry and exit `t` of the ray through that axis's pair of planes; the box is hit if the largest entry is less than the smallest exit. About ten lines, branchless, and the workhorse of voxel raycasting, picking, and BVH traversal.

```ts
function rayAABB(o: Vec3, invD: Vec3, min: Vec3, max: Vec3): number | null {
  let tmin = -Infinity, tmax = Infinity;
  for (let i = 0; i < 3; i++) {
    const t1 = (min[i] - o[i]) * invD[i];
    const t2 = (max[i] - o[i]) * invD[i];
    tmin = Math.max(tmin, Math.min(t1, t2));
    tmax = Math.min(tmax, Math.max(t1, t2));
  }
  return tmax >= Math.max(tmin, 0) ? tmin : null;
}
```

Precomputing `invD = 1/direction` matters: division is far more expensive than multiplication, and in a traversal loop you do this thousands of times per ray.

**Frustum extraction and AABB-vs-frustum.** Pull six planes out of the view-projection matrix (each plane is a sum or difference of two matrix rows). Then, for each plane, test the box's "positive vertex" — the corner furthest along the plane normal. If that corner is behind the plane, the whole box is outside and you reject. This is your frustum culling, and it is one of the highest-leverage optimizations in any renderer.

**Sphere and AABB overlap tests** for broad-phase collision and spatial queries.

**Barycentric coordinates** for interpolating anything across a triangle — the GPU does this for you per-fragment, but you'll need it on the CPU for ray-triangle hits and picking.

**Plane–point signed distance** — the primitive that half of the above is built from.

---

## Voxel-specific math notes

Voxel worlds simplify some math and complicate other parts:

- **Everything is axis-aligned.** No arbitrary triangle intersection in the world representation — just grids. Collision becomes AABB-vs-grid, which is much cheaper and much more robust than general mesh collision.
- **Integer coordinates are first-class.** A voxel is identified by `(ix, iy, iz)`. Converting between world floats and voxel integers is constant work, and you must be deliberate about it: use `Math.floor`, never truncation, because truncation is wrong for negative coordinates and you will get a one-voxel seam at the origin. This bug is nearly universal in first voxel engines.
- **Chunk/local decomposition.** With 32³ chunks, `chunkX = ix >> 5` and `localX = ix & 31`. Power-of-two chunk sizes let you replace division and modulo with shifts and masks — which matters when it happens millions of times per frame.
- **Face normals are one of six constants.** No normal maps needed for base geometry, no normal matrix, no tangent frames. Lighting becomes cheap and exact.
- **Indexing order defines your memory layout.** `index = x + y*S + z*S*S` versus `index = z + y*S + x*S*S` produces identical results and very different cache behavior depending on your traversal order. Module 07 goes deep here; for now, note that in a voxel engine, *indexing arithmetic is a performance decision, not a style choice.*

---

## The interview answer

*"How does a vertex get from a model file to a pixel?"*

> "Model space to world with the model matrix, world to view with the inverse of the camera transform, view to clip with the projection matrix. Clipping happens in homogeneous clip space before the divide, because clipping after the divide breaks for geometry behind the eye. Then perspective divide by w gives NDC, viewport transform gives framebuffer coordinates, and the rasterizer interpolates the vertex attributes across the triangle with perspective-correct interpolation."

*"Why is my distant geometry flickering?"* → Z-fighting from depth precision. Move the near plane out; consider reversed-Z with a float depth buffer.

*"Why quaternions?"* → No gimbal lock, cheap composition, correct interpolation, compact and renormalizable. Convert to matrix once at render time.

---

## Exercise — Voxelforge, Stage 2

Write your own tiny math library. Do not reach for gl-matrix yet — build it once so you know what's inside, *then* use a library.

1. `Vec3` and `Mat4` backed by `Float32Array`, with **no allocation** in hot operations (destination-first APIs: `mul(out, a, b)`).
2. `perspective(out, fovY, aspect, near, far)` for WebGPU's `0..1` depth range, plus a reversed-Z variant.
3. `lookAt(out, eye, target, up)` and verify it equals the inverse of the camera's world matrix.
4. A quaternion type with `fromAxisAngle`, `multiply`, `slerp` (with the shortest-path sign flip), and `toMat4`.
5. `extractFrustumPlanes(viewProj)` and `aabbInFrustum(planes, min, max)` using the positive-vertex test.
6. `rayAABB` with precomputed inverse direction.

**Test it properly:** transform a known point through your full chain by hand, and assert. Then assert that `inverse(view) * view ≈ identity` and that a point at the near plane lands at NDC z = 0 (or 1.0 reversed). These asserts will save you days later, because when the screen is black you need to know the math is not the suspect.

---

## Go deeper

- **Fletcher Dunn & Ian Parberry, *3D Math Primer for Graphics and Game Development*** — the best book for exactly this material, written for people who want to *use* it.
- **immersivemath.com/ima** — free, fully interactive linear algebra book. Excellent for building intuition about dot/cross/basis.
- **Eric Lengyel, *Foundations of Game Engine Development, Vol. 1: Mathematics*** — terse, rigorous, engine-oriented.
- **"Depth Precision Visualized"** — Nathan Reed, reedbeta.com. The definitive explanation of reversed-Z with pictures.
- **gl-matrix source** — read it. It is a masterclass in allocation-free JS numeric code, which is a Module 13 topic in disguise.

---

**Next:** [Module 03 — The GPU Mental Model](./03-gpu-mental-model.md)
