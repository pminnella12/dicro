# Module 02 — 3D Math and the Chain of Spaces

### The seven coordinate systems every vertex passes through, and the small number of operations that move it between them

*~30 min read · Part I: Foundations · Prerequisites: Module 01*

---

## Read this first

Graphics math has a reputation for being hard. It isn't. It is *unforgiving* — one wrong sign or one transposed matrix produces a black screen with no error message and no stack trace — but the actual body of knowledge is small. You need roughly six operations and one pipeline of coordinate spaces.

The reason it feels hard is that nobody tells you the pipeline up front, so every tutorial looks like a pile of unmotivated matrix multiplications. So here it is first, and then the pieces.

> Every vertex in every frame travels the same road: **model → world → view → clip → NDC → screen**. Every "why is my object invisible / inverted / stretched / flickering" bug is a bug in one of those hops.

**You will not be asked to derive anything.** You will be asked to know what each step does, why it exists, and which step to suspect when something looks wrong. That's a memorizable amount of material, and this module is that material.

---

## Before the chain: three ideas you need

### 1. What a "coordinate space" actually is

A coordinate space is just an agreement about where zero is and which way the axes point. Nothing more.

"The chair is 2 meters from the door" and "the chair is at (5, 0, 3) in the building's floor plan" describe the same chair in two different spaces. Neither is more real. Converting between them means knowing where the door is in floor-plan coordinates and which way it faces.

Every space in the chain below is exactly that: a different agreement, useful for a different job. The whole of "3D math" is bookkeeping for converting between agreements.

### 2. Handedness, and why it matters

Point your right hand's index finger along +X and your middle finger along +Y. Your thumb points along +Z. That's a **right-handed** coordinate system. Do the same with your left hand and +Z points the other way — **left-handed**.

This is not a stylistic preference; it decides:
- Which way `cross(a, b)` points.
- Which direction the camera looks (down −Z or +Z).
- Which triangle winding (clockwise vs counter-clockwise vertex order) counts as "front-facing."

Common conventions you'll meet:

| System | Handedness | Up axis | Camera looks down |
|---|---|---|---|
| OpenGL / glTF / most math texts | Right-handed | +Y | −Z |
| Direct3D | Left-handed | +Y | +Z |
| Unity | Left-handed | +Y | +Z |
| Blender | Right-handed | **+Z** | −Y |
| WebGPU NDC | Left-handed after projection (z into screen) | +Y | +Z |

**Pick one convention for your engine, write it in a comment at the top of your math library, and never think about it again.** Most WebGPU engines use right-handed, Y-up, camera-looks-down−Z in world/view space, and let the projection matrix do the flip into WebGPU's clip space. Half of all "my model is inside out" bugs are a handedness mismatch between your exporter and your engine.

### 3. Why 4×4 matrices and the mysterious `w`

You'd think 3D needs 3×3 matrices. A 3×3 matrix can rotate, scale, and shear — but it *cannot translate*, because a matrix multiply always maps the origin to the origin. `M * (0,0,0)` is `(0,0,0)` no matter what `M` is.

The fix is a trick called **homogeneous coordinates**: add a fourth component `w` and work in 4D.

```
| 1 0 0 5 |   | x |   | x + 5 |
| 0 1 0 0 | * | y | = | y     |     ← translation now works,
| 0 0 1 0 |   | z |   | z     |       because w=1 feeds the 4th column
| 0 0 0 1 |   | 1 |   | 1     |
```

That gives you two rules that you will use constantly:

- **`w = 1` means "this is a position."** Translation applies.
- **`w = 0` means "this is a direction."** Translation does *not* apply, because the fourth column gets multiplied by zero. Normals, light directions, and ray directions all use `w = 0`.

Getting this wrong is a classic bug: translate a normal by accident and your lighting swings around as the object moves.

The second thing `w` buys you is perspective, which we'll get to in step 5.

---

## The chain of spaces

### 1. Model space (a.k.a. object space, local space)

The coordinates the artist authored, in a frame convenient for that object alone. A voxel chunk's corner is at `(0,0,0)` and it extends to `(32,32,32)`. A character's origin is usually between their feet, so that "put them on the ground at y=0" is trivial. A wheel's origin is at its hub so it rotates in place.

Nothing in model space knows about the world. That's the point: the same chunk mesh can be drawn at a thousand different places.

### 2. World space

Everything placed into one shared frame. You get there by multiplying by the **model matrix** (also called the *world matrix*), which is built as translate × rotate × scale.

This is the space you think in when you say "the player is at (120, 64, −30)" or "the sun points down and to the left." Physics, AI, and gameplay all live here.

### 3. View space (a.k.a. camera space, eye space)

The world re-expressed so that **the camera is at the origin, looking down an axis** (−Z, by the common convention). You get there by multiplying by the **view matrix**.

The view matrix is the **inverse** of the camera's world transform, and that inversion trips everyone. The intuition: there is no such thing as "moving the camera." There is only moving the entire world in the opposite direction and pretending the camera stayed still. If the camera walks 5 meters right, the world moves 5 meters left. If the camera turns 30° left, the world turns 30° right. That's what inverting the camera's transform does.

Why bother? Because once the camera is at the origin looking down a known axis, projection math becomes trivial — a fixed formula instead of one that has to account for an arbitrary camera position.

### 4. Clip space

Multiply by the **projection matrix**. You are now in a 4D homogeneous space where `w` is no longer 1 — for a perspective projection, `w` now holds the view-space distance from the camera.

Clip space exists so the GPU can cheaply answer "is this vertex inside the visible volume?" A point is visible if:

```
-w ≤ x ≤ w
-w ≤ y ≤ w
 0 ≤ z ≤ w        ← WebGPU / D3D / Metal convention
                    (OpenGL used -w ≤ z ≤ w; WebGPU does not)
```

Those are cheap comparisons with no division, which is exactly what you want in fixed-function hardware.

**Why clip here and not later?** Because clipping after the divide is mathematically broken for geometry behind the camera. A point behind the eye has negative `w`; dividing by a negative `w` flips its sign and it lands *inside* the visible box, on the wrong side. You'd draw a mirrored ghost of geometry behind you. The GPU clips triangles against the frustum planes *before* the divide, generating new vertices along the cut, and only then divides. You don't write this code, but you should know it happens — it's why a triangle straddling the near plane doesn't glitch.

### 5. NDC (normalized device coordinates)

Divide `x`, `y`, and `z` by `w`. This is the **perspective divide**, and it is the step that actually creates perspective.

Here's the entire trick, with numbers. Two points, same size, one twice as far:

| | View-space position | Clip space (after projection) | After ÷w (NDC) |
|---|---|---|---|
| Near cube corner | (1, 1, −5) | x=1.79, y=2.41, w=5 | x=0.357, y=0.482 |
| Far cube corner | (1, 1, −10) | x=1.79, y=2.41, w=10 | x=0.179, y=0.241 |

Identical `x` and `y` going in; the far one comes out at half the screen offset. **Distant things are smaller because you divided by a bigger `w`.** That is all perspective is. The projection matrix's real job is just copying `−z` into `w` so this division has something to work with.

In WebGPU, NDC is:
- `x ∈ [−1, 1]`, left to right
- `y ∈ [−1, 1]`, **bottom to top** (y is up here)
- `z ∈ [0, 1]`, near to far

### 6. Screen space (framebuffer coordinates)

The **viewport transform** maps NDC to actual pixels:

```
screenX = (ndcX * 0.5 + 0.5) * viewportWidth
screenY = (1.0 - (ndcY * 0.5 + 0.5)) * viewportHeight    ← note the flip
```

In WebGPU, `(0,0)` is the **top-left** and y increases downward — the opposite of NDC's y-up. That flip is the single most common source of "my texture is upside down" and "my mouse picking is mirrored vertically." When something is vertically mirrored, this is the first place to look.

### 7. Texture space (UV)

`[0,1]²` addressing into a texture. `(0,0)` is the **top-left** texel in WebGPU, matching screen space and D3D, and opposite to OpenGL's bottom-left. If you port a GL shader and your textures come out flipped, it's this.

### The chain in code

In practice the whole chain collapses into one matrix multiply on the CPU (view × projection, done once per frame) and one in the shader:

```wgsl
@vertex
fn vs(@location(0) position: vec3f) -> @builtin(position) vec4f {
  //           view-space & projection    model→world     w=1 → it's a position
  return camera.viewProj * model.matrix * vec4f(position, 1.0);
}
```

`@builtin(position)` is a special output the hardware understands as **clip space**. You return clip space; the GPU does the clipping, the divide, and the viewport transform for you. You never write those three steps yourself — you just have to know they happen, in that order, so you know what to blame.

---

## Vectors: the two products that matter

You need exactly two vector products. Both have a one-sentence intuition, and every use in graphics follows from that sentence.

### Dot product — "how aligned are these?"

```
a · b = ax*bx + ay*by + az*bz = |a| |b| cos θ
```

For **unit** vectors (length 1), the dot product *is* the cosine of the angle between them:

| Relationship | dot(a, b) |
|---|---|
| Pointing the same way | **1** |
| Perpendicular | **0** |
| Pointing opposite ways | **−1** |
| 60° apart | 0.5 |

Now every use case:

- **`dot(normal, lightDir)` → diffuse lighting.** A surface facing the light directly gets 1 (full brightness); a surface edge-on gets 0. This is Lambert's cosine law, and it's the entire basis of diffuse shading. Clamp at 0 — a negative value means the surface faces away, and unclamped it would make the surface glow with negative light.

- **`dot(v, v)` → squared length.** Because `cos 0° = 1`, a vector dotted with itself is just its length squared. **Compare squared distances instead of calling `sqrt`.** If `distA² < distB²` then `distA < distB`, so sorting and range checks never need the actual distance, and you skip a square root that would otherwise happen thousands of times per frame.

- **`dot(point, planeNormal) - d` → signed distance to a plane.** Positive means in front, negative means behind, and the magnitude is the actual distance (if the normal is unit length). This one operation is the entire basis of frustum culling — see below.

- **Sign of `dot(viewDir, faceNormal)` → backface test.** Is this triangle facing me or facing away?

- **Projection.** The component of `a` that lies along unit vector `b` is `dot(a, b) * b`. Used constantly in collision response to split a velocity into "sliding along the surface" and "pushing into the surface" — you keep the first and cancel the second, and that's how a character slides along a wall instead of sticking to it.

### Cross product — "give me a perpendicular"

```
a × b = (ay*bz - az*by,  az*bx - ax*bz,  ax*by - ay*bx)
```

Produces a vector **perpendicular to both** inputs, with length `|a| |b| sin θ`. Direction follows the right-hand rule in a right-handed system: point fingers along `a`, curl toward `b`, thumb is the result. Note `a × b = −(b × a)` — order matters, and swapping it flips your normals inside out.

- **Triangle normal:** `normalize(cross(b - a, c - a))` for a triangle with corners `a`, `b`, `c`. The **winding order** — whether the vertices are listed clockwise or counter-clockwise when viewed from the front — decides which way that normal points, which decides which side is "front," which decides whether backface culling makes your model vanish entirely. If your mesh renders as a hollow shell with the inside visible, you've got the winding backwards.

- **Building an orthonormal basis.** Given a forward direction and a rough "up," `right = normalize(cross(forward, up))` then `trueUp = cross(right, forward)`. This is how you build camera matrices and tangent frames for normal mapping.

- **Area.** `|a × b|` is twice the area of the triangle they span. Used for barycentric coordinates and for detecting degenerate (zero-area) triangles, which cause divide-by-zero in a lot of geometry code.

### Normals are not positions

They're **directions**, and they transform differently. Two rules:

1. **Use `w = 0`** so translation is ignored (see the homogeneous coordinates section above).

2. **If your model matrix has non-uniform scale, normals must be transformed by the inverse transpose of the upper-left 3×3** — not by the model matrix itself.

Why: imagine a sphere squashed to half height. A normal that pointed 45° up should now point *more steeply* up to stay perpendicular to the squashed surface. But naively scaling the normal by 0.5 in y tilts it the wrong direction — *less* steeply. The inverse transpose is the matrix that does the correct opposite adjustment.

**Uniform scale lets you skip this** (the inverse transpose of a uniform scale is just the same rotation, up to a length change you fix by normalizing). So many engines skip it — right up until an artist scales something 2× on one axis only, and the lighting goes subtly, maddeningly wrong. Voxel engines mostly dodge this entirely, since voxel geometry is axis-aligned and unscaled.

---

## Matrices: the parts you actually manipulate

### Reading a matrix like a data structure

A 4×4 transform matrix is a coordinate frame plus a translation, and you can read those pieces straight out of it:

```
| Rx Ux Fx Tx |     R = right axis      (column 0)
| Ry Uy Fy Ty |     U = up axis         (column 1)
| Rz Uz Fz Tz |     F = forward axis    (column 2)
|  0  0  0  1 |     T = translation     (column 3)
```

**Reading a matrix by pulling out those four columns is a debugging superpower.** When something is in the wrong place, print the translation column — it's the object's position in the parent space, in plain numbers. When something is sheared or skewed, check whether the three axis columns are still orthogonal (dot products ≈ 0) and unit length. When something is inside-out, check whether the determinant went negative (a mirroring transform sneaked in).

Write yourself a `debugPrintMatrix()` early. You will use it constantly.

### Order is not commutative

`A * B` is not `B * A`. For transforms, this is intuitive once you see it concretely:

- `T * R` = "rotate the object, then translate it." The object spins around its own center and then moves. **This is what you almost always want.**
- `R * T` = "translate it, then rotate the whole thing around the world origin." The object orbits, like a moon.

Both are useful; confusing them makes objects fly off into space. The standard object transform is:

```
M = T * R * S
```

Read **right to left**: scale first (in the object's own frame), then rotate, then translate. If you scale after rotating, non-uniform scale gets applied along world axes instead of object axes, which shears your model.

### Column-major vs row-major

This is the other classic trap, and it has two independent parts that people conflate.

**Storage order** — how the 16 numbers are laid out in the array:
- *Column-major:* `[Rx Ry Rz 0, Ux Uy Uz 0, Fx Fy Fz 0, Tx Ty Tz 1]` — the translation is at indices 12, 13, 14.
- *Row-major:* the transpose of that — translation at indices 3, 7, 11.

**Vector convention** — which side the vector goes on:
- *Column vectors:* `v' = M * v`. Transforms compose right-to-left: `M = T * R * S`.
- *Row vectors:* `v' = v * M`. Transforms compose left-to-right: `M = S * R * T`.

**WebGPU/WGSL uses column-major storage and column vectors (`M * v`).** gl-matrix matches this, as do most modern references. Older D3D-era literature and some math texts use row vectors, where *every multiplication order in every code sample is reversed* and matrices appear transposed.

The practical rule: **when adapting code from a tutorial, check the convention before you debug the math.** If a ported transform gives you nonsense, transposing it or reversing the multiply order is the first thing to try — and if translation is landing in the wrong components, you have a storage-order mismatch, not a math error.

### Inverses

You rarely need a general 4×4 inverse (which is slow and numerically fussy). For a **rigid transform** — rotation plus translation, no scale, which describes nearly every camera and nearly every bone — the inverse has a closed form:

```
inverse(R|T) = (Rᵀ | -Rᵀ * T)
```

In words: transpose the 3×3 rotation part (for a rotation matrix, the transpose *is* the inverse), then rotate the negated translation by it. That's a handful of multiplies, exact, with no numerical drift.

**That is exactly what a view matrix is.** `lookAt()` doesn't invert anything expensive; it builds the camera basis with cross products and then writes down this closed form directly.

---

## Rotations: why quaternions win

There are three ways to represent an orientation, and engines use all three — for different jobs.

### Euler angles (yaw / pitch / roll)

Three numbers: rotate this much around Y, then this much around X, then this much around Z. Intuitive, and perfect for a first-person camera's *input* ("mouse moved 5 pixels → yaw += 0.3°").

Terrible everywhere else, for three reasons:

1. **Gimbal lock.** Look straight up (pitch = 90°) and your yaw and roll axes become the same axis — you've lost a degree of freedom and there are orientations you literally cannot express by continuing to change these numbers. The classic demo is an aircraft in a vertical climb suddenly unable to distinguish turning from rolling.
2. **They interpolate badly.** Blending from (0°, 0°, 0°) to (90°, 90°, 90°) by lerping each number separately traces a wobbling, non-obvious path through orientation space, not the direct rotation you wanted.
3. **Order conventions.** XYZ, ZYX, YXZ, intrinsic vs extrinsic — there are 24 valid conventions and every tool picks a different one. Half of all "the model imported rotated 90° on the wrong axis" bugs are this.

### Rotation matrices

Nine numbers to express three degrees of freedom. Fine for *applying* a rotation (that's what the GPU wants), but bad for storing and composing: repeated multiplication accumulates floating-point error until the axes are no longer perpendicular or unit length ("drift"), and re-orthonormalizing is fiddly. Interpolating them naively produces garbage.

### Quaternions

Four numbers `(x, y, z, w)`. A **unit quaternion** encodes "rotate by angle θ around unit axis `a`" as:

```
q = ( a.x·sin(θ/2),  a.y·sin(θ/2),  a.z·sin(θ/2),  cos(θ/2) )
```

You don't need to understand *why* the halves are there or how quaternion multiplication is derived. You need to know what they buy you:

- **No gimbal lock.** Every orientation is representable, always.
- **Cheap composition.** Combining two rotations is one 16-multiply operation, versus 27 for matrices.
- **Correct interpolation** via `slerp` (spherical linear interpolation), which traces the shortest arc at constant angular speed — exactly what "blend from this pose to that pose" should mean.
- **Cheap correction of drift.** Just divide by the length. Compare to re-orthonormalizing a matrix.
- **Compact.** 4 floats instead of 9, which matters when you have 200 bones × 60 frames of animation.

### The policy every engine uses

- **Store** orientation as a quaternion.
- **Compose** with quaternion multiplication.
- **Interpolate** with `slerp`, or `nlerp` (normalize a plain lerp) for small deltas — cheaper, slightly non-constant angular velocity, and visually indistinguishable below ~30°.
- **Convert to a matrix once per frame**, at the point where you build the model matrix for the GPU.
- **Accept Euler angles at the boundary** — designer-facing tools and FPS camera input — and convert to a quaternion immediately, never storing the Euler form.

### The three facts to remember

1. `q` and `−q` represent the **same rotation** (they differ by a full 360° turn). Slerp must therefore check `dot(q1, q2)` and negate one of them if it's negative, or your character will take the long way around — 350° instead of 10° — and it looks unmistakably wrong.
2. **Renormalize after repeated composition**, or accumulated float error slowly turns your quaternion into a non-rotation that scales things.
3. Quaternion multiplication is **not commutative**, same as matrices, for the same reason.

---

## Projection, and the depth buffer's dirty secret

### What the projection matrix contains

Built from four inputs: vertical field of view, aspect ratio, and near/far plane distances. For WebGPU's `0..1` depth convention, right-handed, looking down −Z:

```
f = 1 / tan(fovY / 2)

| f/aspect   0        0             0       |
|    0       f        0             0       |
|    0       0    far/(near-far)  near*far/(near-far) |
|    0       0       -1             0       |    ← this row copies -z into w
```

Two jobs, no more:
1. **Scale x and y** by the focal length `f`, so a wider FOV squeezes more world into the same NDC box.
2. **Copy `−z` into `w`** (that bottom row), so the perspective divide has a distance to divide by.

The z row exists only to remap view-space depth into `[0, 1]` in a way that survives the divide.

### The near and far planes

- **Near plane:** the closest distance you can see. Geometry closer than this is clipped away. It can never be 0 (that would mean dividing by zero).
- **Far plane:** the furthest. Geometry beyond is clipped.

Together they define a truncated pyramid — the **view frustum** — that is exactly the region of the world that can appear on screen.

### Why your distant geometry flickers

This is the part worth understanding deeply, because it *will* happen to you and there's no error message.

After projection, depth is stored as a **nonlinear** function of view-space distance — roughly proportional to `1/z`. Precision is lavished on things close to the near plane and starved further away. Concretely, with `near = 0.01` and `far = 10000` in a 24-bit depth buffer:

| Distance from camera | Depth buffer values available in the next meter |
|---|---|
| 0.01 – 1 m | ~16.7 million (most of the buffer) |
| 10 m | ~16,000 |
| 100 m | ~168 |
| 1000 m | ~1.7 |
| 5000 m | **less than 1** |

Past a few thousand meters, two surfaces a meter apart map to the *same* depth value. The GPU then can't tell which is in front, and which one wins varies per pixel and per frame as tiny float differences tip the comparison. The result is **z-fighting**: flickering, stitched, shimmering surfaces. It's most visible on coplanar geometry (a decal on a wall, a road on terrain) and it looks like a rendering bug because it is one.

### The fixes, in order of preference

1. **Push the near plane out.** Going from `near = 0.01` to `near = 0.1` gives you an order of magnitude more precision everywhere else. It costs you nothing except the ability to put your face 1 cm from a wall. **Do this first, always.** It is the single highest-value one-line change in depth precision.

2. **Reversed-Z.** Map near → 1.0 and far → 0.0 (swap the near/far arguments in your projection), use a **float** depth format (`depth32float`), and flip the depth comparison to `greater` and the clear value to 0.

   Why this works: floating-point numbers have far more precision near zero than near one (the exponent gives you fine resolution as values shrink). The projection's `1/z` distribution wastes precision far away; float's distribution lavishes precision near zero. Put "far away" at zero and the two distributions cancel almost exactly, giving near-uniform precision across the entire range. You can then use `near = 0.01` and `far = 100000` and get no z-fighting anywhere.

   This is standard practice in modern engines and a strong thing to mention unprompted in an interview — it signals you've dealt with real depth problems rather than read about them.

3. **Logarithmic depth** — write a custom depth value in the fragment shader for extreme ranges (space sims, planetary scale). It costs you early-Z (Module 04 explains why that hurts), so it's a last resort.

**WebGPU's `0..1` clip-space z convention** (inherited from D3D/Metal rather than OpenGL's `−1..1`) is what makes reversed-Z clean to set up — with a `−1..1` range you'd waste half your float precision on values that never occur. It's one of several places where WebGPU's design shows its native-API lineage.

### Orthographic projection

No divide (`w` stays 1), so there's no perspective — parallel lines stay parallel and distant objects are the same size as near ones. Depth is linear, so all the precision problems above vanish.

You use it for:
- **UI and 2D**, where perspective would be wrong.
- **Shadow maps from directional lights** (the sun), where the light is effectively infinitely far away so its rays are parallel. This is the important one, and it's why you'll build an ortho projection even in a fully 3D game.

---

## The geometry you will actually write

Five routines cover the overwhelming majority of engine geometry work. Write them once, correctly, with tests, and you'll reuse them forever.

### 1. Ray–AABB (the slab test)

An **AABB** is an axis-aligned bounding box — a box whose faces are perpendicular to the world axes, describable by just two corners (`min` and `max`). They're everywhere in games because they're cheap to test and cheap to store.

The slab test: a box is the intersection of three "slabs" (the space between two parallel planes, one pair per axis). For each axis, compute the parametric `t` where the ray enters and exits that slab. The ray hits the box if the **latest entry** is before the **earliest exit**.

```ts
/**
 * @param o    ray origin
 * @param invD 1/direction, precomputed per ray (see note below)
 * @returns distance along the ray to the hit, or null
 */
function rayAABB(o: Vec3, invD: Vec3, min: Vec3, max: Vec3): number | null {
  let tmin = -Infinity;   // latest entry so far
  let tmax = Infinity;    // earliest exit so far

  for (let i = 0; i < 3; i++) {
    // Where does the ray cross this axis's two planes?
    const t1 = (min[i] - o[i]) * invD[i];
    const t2 = (max[i] - o[i]) * invD[i];
    // Don't assume t1 < t2 — a negative direction swaps them.
    tmin = Math.max(tmin, Math.min(t1, t2));
    tmax = Math.min(tmax, Math.max(t1, t2));
  }

  // Hit if the slabs overlap, and the overlap isn't entirely behind us.
  return tmax >= Math.max(tmin, 0) ? tmin : null;
}
```

About ten lines, no branches in the hot path, and it's the workhorse of voxel raycasting, mouse picking, and BVH traversal (Module 07).

**Precomputing `invD = 1/direction` matters.** Division is 10–40× more expensive than multiplication on most hardware. In a traversal loop you do this thousands of times per ray and millions of times per frame, so hoisting the reciprocal out of the loop is a real, measurable win. (Infinity from a zero component is fine here — IEEE-754 handles it correctly and the min/max logic still works.)

### 2. Frustum extraction and AABB-vs-frustum

**Frustum culling** is the highest-leverage optimization in any renderer: don't draw what the camera can't see.

Step one: pull six planes out of the view-projection matrix. Each plane is a sum or difference of two matrix rows — e.g. the left plane is `row3 + row0`, the right plane is `row3 − row0`. (This falls straight out of the clip-space test `−w ≤ x ≤ w`; you don't need to derive it, just look up the formula once and write a unit test.) Normalize each plane by the length of its normal so distances come out in world units.

Step two: for each of the six planes, test whether the box is entirely behind it. The efficient way is the **positive-vertex test**: of the box's 8 corners, only one can possibly be furthest along the plane's normal, and you can pick it by looking at the signs of the normal's components:

```ts
function aabbInFrustum(planes: Plane[], min: Vec3, max: Vec3): boolean {
  for (const p of planes) {
    // The corner furthest along this plane's normal.
    const px = p.nx >= 0 ? max[0] : min[0];
    const py = p.ny >= 0 ? max[1] : min[1];
    const pz = p.nz >= 0 ? max[2] : min[2];

    // If even the furthest corner is behind the plane, the whole box is out.
    if (p.nx * px + p.ny * py + p.nz * pz + p.d < 0) return false;
  }
  return true;   // may be a false positive (corner cases near edges) — that's fine
}
```

One dot product per plane instead of eight. Note that this test can return `true` for a box that's technically outside (near frustum corners); that's an acceptable false positive, because the cost is drawing something invisible, not drawing something wrong.

### 3. Sphere and AABB overlap tests

For broad-phase collision and spatial queries. Sphere-sphere is `distSquared < (r1+r2)²`. AABB-AABB is three interval overlap checks. Both are a few lines and both are used constantly.

### 4. Barycentric coordinates

A way of expressing a point inside a triangle as a weighted blend of its three corners, with weights summing to 1. The GPU does this automatically for you per-fragment (that's how vertex colors and UVs get interpolated across a triangle — Module 04), but you'll need it on the CPU for ray-triangle hits and for mouse picking against a mesh.

### 5. Plane–point signed distance

`dot(point, planeNormal) + d`. Half of everything above is built from this one line.

---

## Voxel-specific math notes

Voxel worlds simplify some math dramatically and complicate other parts. Since this is the spike area for the job, know these cold.

**Everything is axis-aligned.** There is no arbitrary triangle intersection in the world representation — just a grid of boxes. Collision becomes AABB-vs-grid, which is both much cheaper and much more *robust* than general mesh collision. No degenerate triangles, no thin-sliver numerical instability, no "which side of this polygon soup am I on." This is a large part of why voxel games can have fully destructible worlds while polygon games mostly can't.

**Integer coordinates are first-class.** A voxel is identified by `(ix, iy, iz)`. Converting between world floats and voxel integers happens constantly, and you must be deliberate:

```ts
const ix = Math.floor(worldX);   // ✅ correct
const ix = worldX | 0;           // ❌ truncates toward zero — WRONG for negatives
```

`Math.floor(-0.5)` is `-1`; `(-0.5 | 0)` is `0`. So with truncation, the voxels at x ∈ [−1, 0) and x ∈ [0, 1) both map to index 0 — a **double-width voxel at the origin** and a one-voxel seam through your world. This bug is nearly universal in first voxel engines and it's maddening to spot because everything looks fine until the player walks past x=0.

**Chunk/local decomposition.** Worlds are split into fixed-size chunks (say 32³). Given a global voxel coordinate, you need which chunk and where inside it:

```ts
const chunkX = ix >> 5;    // divide by 32 — and >> floors correctly for negatives
const localX = ix & 31;    // modulo 32
```

Power-of-two chunk sizes let you replace division and modulo with a shift and a mask. That matters when it happens millions of times per frame. (Note `>>` on negatives gives you floor division, which is what you want — unlike `/` followed by truncation.)

**Face normals are one of six constants.** `(±1,0,0)`, `(0,±1,0)`, `(0,0,±1)`. No normal maps needed for base geometry, no normal matrix, no tangent frames, no per-vertex normal storage — you can pack the face direction into 3 bits. Lighting becomes cheap and exact. This is a genuine architectural advantage of voxels and worth mentioning when asked about the format's strengths.

**Indexing order defines your memory layout.** These two are mathematically identical and behave completely differently:

```ts
index = x + y*S + z*S*S     // x varies fastest → walking +x is sequential in memory
index = z + y*S + x*S*S     // z varies fastest → walking +x jumps S² elements
```

If you index one way and iterate the other, every access is a cache miss (see Module 01) and you can be 10× slower for zero functional difference. Module 07 goes deep here; for now, note that **in a voxel engine, indexing arithmetic is a performance decision, not a style choice.** Write down your convention and iterate to match it.

---

## Common confusions

**"My object is invisible."** Walk the chain, in this order: (1) Is it inside the frustum? Print its world position and the camera's. (2) Is it behind the near plane or past the far plane? (3) Is it facing away — try disabling backface culling; if it appears, your winding order or handedness is wrong. (4) Is it black-on-black — try outputting a constant color from the fragment shader. (5) Is the depth test rejecting it — try `depthCompare: 'always'`. Each of these isolates one hop.

**"I inverted my view matrix and everything went crazy."** The view matrix *is* the inverse. Don't invert it again. If you have the camera's world transform, the view matrix is its inverse; if you have the view matrix and want the camera position, invert it (or read the translation out of the inverse rigid form).

**"Multiplying matrices in the order I read them left to right."** With column vectors, `M = T * R * S` applies S first. The matrix written *rightmost* is applied *first*. This inverts most people's reading instinct and causes an enormous number of transform bugs.

**"Normalize everything, just in case."** `normalize()` involves a square root and a division. In a shader that runs 8 million times a frame, gratuitous normalizes cost real milliseconds. Normalize where correctness requires it (interpolated normals, after accumulating rotations) and not otherwise.

**"Small angles are fine with lerp instead of slerp."** True, and it's the standard optimization — but only after the shortest-path sign flip. Skipping the sign check makes 10° rotations occasionally take the 350° route, which is far more visible than any interpolation-speed artifact.

**"The math library will handle conventions for me."** gl-matrix has both `perspective` (OpenGL `−1..1` depth) and `perspectiveZO` (`0..1`, "zero to one"). Using the wrong one gives you a picture that looks *almost* right with a broken near half. Check which one you're calling.

---

## The interview answer

***"How does a vertex get from a model file to a pixel?"***

> "Model space to world with the model matrix, world to view with the inverse of the camera transform, view to clip with the projection matrix. Clipping happens in homogeneous clip space *before* the divide, because clipping after the divide breaks for geometry behind the eye — negative w flips the sign and it lands inside the box. Then the perspective divide by w gives NDC, the viewport transform gives framebuffer coordinates, and the rasterizer interpolates the vertex attributes across the triangle with perspective-correct interpolation."

The two phrases that signal real experience there are "**before the divide, because negative w**" and "**perspective-correct interpolation**." Most candidates recite the chain; few explain why clipping happens where it does.

***"Why is my distant geometry flickering?"***

> "Z-fighting from depth precision. Depth is stored proportional to 1/z, so precision collapses far from the camera. First thing I'd do is push the near plane out — usually that alone fixes it and it's free. If the range is genuinely huge, reversed-Z with a float depth buffer: near maps to 1, far maps to 0, flip the compare to greater. Float precision near zero cancels the 1/z distribution and you get roughly uniform precision across the whole range."

***"Why quaternions?"***

> "No gimbal lock, cheap composition, correct shortest-arc interpolation with slerp, compact, and easy to renormalize when they drift. I store orientation as a quaternion, compose in quaternion space, and convert to a matrix once at render time. Euler angles only at the boundaries — camera input and designer-facing tools."

***"What's the difference between transforming a position and a normal?"***

> "Position uses w=1 so translation applies; a direction uses w=0 so it doesn't. And if there's non-uniform scale in the model matrix, normals need the inverse transpose of the upper-left 3×3, otherwise they stop being perpendicular to the surface and lighting goes wrong."

---

## Exercise — Voxelforge, Stage 2

Write your own tiny math library. **Do not reach for gl-matrix yet** — build it once so you know what's inside, *then* switch to a library for real work. The point is that when the screen is black at 2 a.m., you need to be able to rule the math out.

**1. `Vec3` and `Mat4` backed by `Float32Array`, with no allocation in hot operations.** Use destination-first APIs (`mul(out, a, b)`) rather than returning new objects. This is the single most important habit in JS numeric code and Module 13 explains why in depth — for now, just build it this way.

**2. `perspective(out, fovY, aspect, near, far)`** for WebGPU's `0..1` depth range, plus a reversed-Z variant. Assert that a point at the near plane comes out at NDC z = 0 (or 1.0 reversed) and a point at the far plane comes out at 1 (or 0).

**3. `lookAt(out, eye, target, up)`** built from cross products and the closed-form rigid inverse. Then verify it: build the camera's world matrix independently, invert it with a general inverse, and assert the two agree.

**4. A quaternion type** with `fromAxisAngle`, `multiply`, `slerp` (**with the shortest-path sign flip**), and `toMat4`. Test: slerp from 350° to 10° must travel 20°, not 340°.

**5. `extractFrustumPlanes(viewProj)` and `aabbInFrustum(planes, min, max)`** using the positive-vertex test. Test by placing boxes clearly inside, clearly outside each of the six planes, and straddling.

**6. `rayAABB`** with precomputed inverse direction. Test rays that hit, miss, start inside the box, and travel exactly parallel to a face (the zero-direction-component case — this is where naive implementations produce `NaN`).

**⭐ Test it properly.** Transform a known point through your full chain by hand on paper and assert the result. Assert `inverse(view) * view ≈ identity`. **These asserts will save you days later**, because when the screen is black you need to know the math is not the suspect. This is the least glamorous stage of the project and the one that pays off most.

**Stretch:** add a `debugPrintMatrix()` that labels the right/up/forward/translation columns and warns if the axes aren't orthonormal.

---

## Go deeper

- **Fletcher Dunn & Ian Parberry, *3D Math Primer for Graphics and Game Development*** — the best book for exactly this material, written for people who want to *use* it rather than prove things about it. If you buy one math book, this one.
- **immersivemath.com/ima** — free, fully interactive linear algebra book. Drag the vectors around. Excellent for building intuition about dot/cross/basis that no amount of reading provides.
- **Eric Lengyel, *Foundations of Game Engine Development, Vol. 1: Mathematics*** — terse, rigorous, engine-oriented. The reference you graduate to.
- **"Depth Precision Visualized"** — Nathan Reed, reedbeta.com. The definitive explanation of reversed-Z, with graphs that make the whole thing obvious in five minutes.
- **"Homogeneous Coordinates"** — Jim Blinn's *Jim Blinn's Corner* essays, if you want the `w` component to genuinely click.
- **gl-matrix source** — read it after you've written your own. It is a masterclass in allocation-free JS numeric code, which is a Module 13 topic in disguise.

---

**Next:** [Module 03 — The GPU Mental Model](./03-gpu-mental-model.md)
