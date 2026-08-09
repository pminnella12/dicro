# Module 07 — Voxel Data Structures

### Why a naive voxel world is impossible, and the sparse hierarchies that make it possible

*~12 min read · Part III: Voxels & Performance · Prerequisites: Modules 02–05*

---

Start with the arithmetic, because it settles the argument before it begins.

A dense 1024³ voxel grid at one byte per voxel is **1.07 GB**. At 2048³ it is **8.6 GB**. A 4096³ world — modest for a game with any draw distance — is **68 GB**.

Meanwhile, WebGPU's default `maxTextureDimension3D` is **2048** and `maxStorageBufferBindingSize` is **128 MiB**.

> The entire field of voxel data structures exists to answer one question: *how do you represent an enormous volume when almost all of it is empty or uniform?*

The answer is always the same shape — **hierarchy plus sparsity** — and the design space is about which hierarchy, at what branching factor, with what memory layout.

---

## Level 0: chunks

Before any clever structure, every voxel engine partitions the world into fixed-size **chunks** — 16³, 32³, or 64³ blocks of voxels stored contiguously.

Chunks give you:

- **A streaming unit.** Load, generate, and evict at chunk granularity.
- **A rebuild unit.** Edit one voxel, re-mesh one chunk (~35 µs), not the world.
- **A culling unit.** One frustum test per chunk instead of per voxel.
- **A cache unit.** A 32³ chunk at 1 byte is 32 KB — L2-resident.
- **Cheap addressing.** With power-of-two sizes: `chunkCoord = worldCoord >> 5`, `local = worldCoord & 31`. Shifts and masks, not division.

**Size selection is a real tradeoff.** Smaller chunks mean finer culling and cheaper rebuilds but more draw calls and more per-chunk overhead. Larger chunks mean fewer draws but expensive edits and coarser culling. 32³ is the common sweet spot; 64³ is increasingly favored in GPU-driven designs where draw count matters more than rebuild cost.

**Indexing order matters.** `idx = x + y*S + z*S*S` versus `idx = y + z*S + x*S*S` are equally correct and not equally fast. Match your dominant traversal order. Many engines use **Morton (Z-order) encoding** — interleaving the bits of x, y, z — so that spatial locality in 3D becomes memory locality in 1D along *every* axis, at the cost of slightly more expensive index computation. Morton codes also make octree indexing nearly free, since a Morton code *is* a path down an octree.

**Chunk contents compress well.** Most chunks are entirely air or entirely stone. Store a flag for uniform chunks and skip allocating the array at all — in typical terrain this eliminates 70–90% of your memory before any other technique. The next step is a **palette**: if a chunk contains only 9 distinct block types, store a 9-entry palette plus 4-bit indices instead of 8- or 16-bit IDs. This is what modern Minecraft does, and it's a 2–4× win for nearly free.

---

## Level 1: sparse voxel octrees

An **octree** recursively subdivides space into 8 children. **Sparse** means you only allocate children for non-empty (or non-uniform) nodes.

The classic implementation is Laine & Karras's **Efficient Sparse Voxel Octrees** (NVIDIA, 2010), which packs each node into a compact descriptor with a child mask and a relative pointer, keeping traversal cache-friendly.

**What octrees buy:**
- **Empty space skipping.** A ray hitting an empty node skips its entire volume in one step. This is the whole point.
- **Free LOD.** Interior nodes hold averaged/representative data, so distant geometry can stop descending. Level-of-detail is a property of the structure, not a separate system.
- **Logarithmic memory** in the amount of *surface*, not volume — and voxel worlds are almost entirely surface.

**What they cost:**
- **Pointer chasing.** Each descent is a dependent memory read — the worst possible pattern for a GPU, because you cannot prefetch and you cannot hide the latency. Depth 11 (2048³) means up to 11 serialized cache misses per ray step.
- **Traversal state.** A proper ray-octree traversal needs a stack, which costs registers, which costs occupancy (Module 03).
- **Expensive edits.** Modifying a voxel may require reallocating nodes up the tree. Bad for a game where the player destroys terrain.

**Sparse Voxel DAGs** (Kämpe et al., 2013) take this further: identical subtrees are deduplicated into a directed acyclic graph. For structured or repetitive content the compression is spectacular — thousands of times smaller than the equivalent SVO. The catch is that DAGs are effectively read-only; you cannot cheaply modify a shared subtree. Excellent for static scenery, wrong for destructible worlds.

---

## Level 2: brickmaps — the pragmatic winner

Deep trees have too many levels. Flat grids use too much memory. The compromise that most modern voxel renderers converge on is a **shallow multi-level grid**, usually called a **brickmap**.

The structure:

- A **top-level grid** of pointers/indices, e.g. 128³, where each cell covers an 8³ region.
- A **brick pool**: a big allocated buffer (or 3D texture) of fixed-size 8³ voxel bricks.
- Empty regions store a null/sentinel index and consume no brick.

A 1024³ world becomes a 128³ index grid (2 MB at 1 uint each) plus however many bricks are actually occupied. Typical terrain occupies maybe 5–15% of bricks, so you land in the tens of megabytes instead of a gigabyte.

**Why this beats an octree in practice:**

1. **Two levels, not eleven.** Traversal is two nested DDA loops — coarse steps through the index grid to skip empty space, fine steps within a brick. No stack, few registers, high occupancy.
2. **Random access is O(1).** Editing a voxel is two array lookups. Perfect for a destructible game.
3. **The brick pool can be a 3D texture**, so you get the swizzled cache layout and hardware filtering discussed in Module 03.
4. **Streaming is natural.** Bricks are fixed-size, so allocation is a free-list pop; evicting a distant region returns bricks to the pool with no fragmentation.

You can add a third level (a 32³ super-grid over the index grid) for very large worlds, or an **occupancy bitmask** per brick (8³ = 512 bits = 64 bytes) so a ray can test "is this brick even worth entering" with a few 64-bit reads instead of a texture fetch.

This "two-level grid with a brick pool" design is what you should reach for first, and being able to explain *why* — occupancy, no pointer chasing, O(1) edits, texture-cache-friendly, streaming-friendly — is exactly the depth this role asks for.

---

## Level 3: 64-trees and bitmask hierarchies

A newer direction worth knowing, because it's actively where the field is moving.

Instead of branching by 2 per axis (octree, 8 children), branch by **4 per axis: 64 children**. Each node's occupancy fits in exactly **one 64-bit integer**, one bit per child.

This is elegant for a specific reason: modern GPUs have fast bit operations, so you can test occupancy, count set bits (`countOneBits`, i.e. popcount) to compute a child's offset in a compacted array, and find the next set bit — all in a handful of ALU instructions, with **no separate child pointer array**. Node data becomes: one 64-bit mask plus a base index; child `i` lives at `base + popcount(mask & ((1<<i)-1))`.

The tradeoffs versus octrees:
- **Far fewer levels.** 64⁵ = 2³⁰ ≈ 1 billion voxels in five levels versus 30 in a binary-branching tree.
- **Fewer dependent memory reads**, which is the actual bottleneck.
- **Better ALU/memory balance**, which is the right side of the roofline (Module 03).
- Slightly coarser sparsity granularity than an octree.

The same bitmask trick shows up everywhere in voxel work: `NanoVDB`/`OpenVDB` (used across VFX) is essentially a shallow tree of bitmask-indexed tiles with branching factors like 32³/16³/8³. If you've heard "VDB," this is what it is.

---

## The dimension people forget: GPU memory layout

Choosing the algorithm is half the job. The other half is how it sits in memory, and this is where good voxel engineers separate from average ones.

**Storage buffer vs 3D texture.** Buffers are flexible and support read-write and atomics; 3D textures give you swizzled cache layout, hardware filtering, and bounds-safe `textureLoad`. For a brick pool that's read many times per ray with 3D-coherent access, the texture usually wins on cache behavior alone. Benchmark both — it's hardware-dependent, and "I measured it on the target GPUs" is the right answer.

**Structure of arrays.** If a voxel has a material ID, a light level, and flags, do not interleave them. A traversal that only needs "is this solid?" should not drag light levels through the cache. Split into parallel arrays, and pack the hottest one as tightly as possible.

**Bit packing is nearly always worth it.** Memory bandwidth is the bottleneck, ALU is cheap; unpacking a 4-bit field costs a shift and a mask and saves half your traffic. A common voxel packing: 16 bits total — 12 bits material ID (4096 types), 4 bits of light or rotation state.

**Alignment and 128 MiB.** WebGPU's default `maxStorageBufferBindingSize` of 128 MiB means your brick pool may need to be split across multiple bindings, or you request a higher limit and accept reduced device compatibility. Design for the split; it's a real constraint, not a hypothetical.

**Uploads.** `queue.writeBuffer` for small updates; a persistent staging buffer with `mapAsync` for large streaming. Never upload a whole chunk when one voxel changed — upload the dirty brick, or better, apply edits in a compute shader from a small edit list so the CPU never touches voxel data at all.

---

## Streaming, LOD, and the world beyond view distance

A world larger than memory needs a lifecycle:

```
generate/load (worker) → compress/palette → upload bricks (GPU) →
build acceleration data (occupancy masks, mesh) → render →
… player moves …
→ mark distant chunks cold → evict, return bricks to pool
```

Rules that make this survivable:

- **Generation belongs on Web Workers**, never the main thread. Terrain noise for a 32³ chunk is milliseconds — instantly fatal to your frame budget if done inline.
- **Amortize.** Cap uploads and mesh rebuilds per frame (e.g., 2 ms of budget). A visible pop-in is much better than a 40 ms hitch.
- **Prioritize by need**, not by distance alone: in-frustum and close first, then near-but-behind (the player will turn around).
- **LOD by mip.** Averaged/downsampled bricks let distant regions use one-eighth the data per level. In a raytraced voxel renderer this is nearly free — just stop descending. In a meshed renderer it's harder, because merging LOD levels creates cracks at chunk boundaries; the standard fixes are skirts (a downward-hanging rim of geometry at chunk edges) or stitching.
- **Determinism.** Chunk contents must be a pure function of `(seed, chunkCoord)` plus a stored diff of player edits. This makes an effectively infinite world cost only what players have changed — the foundation of the entire genre.

---

## Choosing: a decision table

| Requirement | Structure |
|---|---|
| Small, fully loaded scene | Dense 3D texture |
| Editable game world, moderate size | Chunked dense + palette compression |
| Large world, ray traced, destructible | **Brickmap (two-level grid + brick pool)** |
| Very large world, mostly static | SVO or 64-tree |
| Huge, highly repetitive, read-only | SVDAG |
| Sparse volumetric data (smoke, clouds) | VDB-style bitmask tree |
| Meshed rendering | Chunked dense + greedy meshing (Module 08) |

For a first-person voxel roguelike with destructible dungeons and a ray-traced look, the honest recommendation is: **chunks of palette-compressed voxels on the CPU/worker side, mirrored into a GPU brickmap with per-brick occupancy bitmasks.** Everything else is a specialization of that.

---

## The interview answer

*"How would you store a large voxel world?"*

> "Chunks as the streaming and edit unit, palette-compressed on the CPU side since most chunks have very few distinct materials. On the GPU, a two-level brickmap: a top-level index grid pointing into a pool of 8³ bricks held in a 3D texture, with per-brick occupancy bitmasks for cheap empty-space rejection. I'd pick that over a deep octree because octree traversal is a chain of dependent memory reads, which is exactly what GPUs can't hide, and because O(1) random access matters when the world is destructible. If the content turned out to be largely static and repetitive I'd look at an SVDAG for the compression, or a 64-tree if I wanted hierarchy with fewer levels and bitmask-based indexing."

That answer demonstrates structure knowledge, hardware reasoning, and a design tradeoff tied to the actual game. That's the target.

---

## Exercise — Voxelforge, Stage 7

1. Implement chunked storage with palette compression. Measure the memory of a 512×128×512 terrain world before and after. Report the ratio.
2. Add uniform-chunk detection (all-air / all-solid stores no array). Measure again.
3. Implement a GPU brickmap: a `u32` index grid in a storage buffer, an 8³ brick pool in an `r8uint` 3D texture, and a free-list allocator on the CPU. Upload a world and render it by *any* means (even just reading it in the existing mesher).
4. Add a 64-byte occupancy bitmask per brick in a parallel buffer.
5. Implement `setVoxel(x,y,z,id)` that allocates a brick on demand, updates the bitmask, and marks a dirty region for upload — then verify a single voxel edit costs microseconds, not milliseconds.
6. Move terrain generation to a Web Worker with a fixed per-frame upload budget. Sprint the camera across the world and check your frame time graph for hitches.

---

## Go deeper

- **Laine & Karras, "Efficient Sparse Voxel Octrees" (NVIDIA, 2010)** — the foundational paper. Read it for the node encoding and the traversal, even if you build a brickmap.
- **Kämpe, Sintorn & Assarsson, "High Resolution Sparse Voxel DAGs" (SIGGRAPH 2013)** — the deduplication idea.
- **"A guide to fast voxel ray tracing using sparse 64-trees"** — dubiousconst282.github.io (2024). Modern, practical, benchmarked; pairs with the `VoxelRT` repo.
- **Dennis Gustafsson's blog (blog.voxagon.se) on Teardown** — the best public writing on a shipped, fully destructible, ray-traced voxel game. Read everything.
- **OpenVDB / NanoVDB documentation** — for the bitmask-tile hierarchy in its most mature form.
- **`0fps.net` archives (Mikola Lysenko)** — foundational voxel engine writing, still worth the time.
- **John Lin's and Douglas Dwyer's voxel engine devlogs** — practitioners publishing real numbers on real structures.

---

**Next:** [Module 08 — Voxel Rendering Techniques](./08-voxel-rendering.md)
