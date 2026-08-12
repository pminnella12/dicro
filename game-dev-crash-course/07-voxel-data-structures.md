# Module 07 — Voxel Data Structures

### Why a naive voxel world is impossible, and the sparse hierarchies that make it possible

*~30 min read · Part III: Voxels & Performance · Prerequisites: Modules 02–05*

---

## Read this first

This is the start of **the spike** — the three modules where you go from "broadly competent" to "this person knows the specific thing we're hiring for." Modules 07, 08, and 09 are the differentiator for the Bakest engine role. Slow down here.

Start with the arithmetic, because it settles the argument before it begins.

| World size | Dense storage at 1 byte/voxel |
|---|---|
| 256³ | 16.8 MB — fine |
| 512³ | 134 MB — already over WebGPU's default storage binding limit |
| 1024³ | **1.07 GB** |
| 2048³ | **8.6 GB** |
| 4096³ | **68 GB** |

A 4096³ world is *modest* for a game with any real draw distance — that's 4 km across at 1 m voxels, or 40 m across at 1 cm voxels.

Meanwhile, WebGPU's defaults (Module 05): `maxTextureDimension3D` is **2048** and `maxStorageBufferBindingSize` is **128 MiB**.

> The entire field of voxel data structures exists to answer one question: *how do you represent an enormous volume when almost all of it is empty or uniform?*

The answer is always the same shape — **hierarchy plus sparsity** — and the design space is about *which* hierarchy, at *what* branching factor, with *what* memory layout.

### The one insight that makes it all work

A voxel world is mostly **not surface**. Underground is solid stone (uniform), above ground is air (uniform), and only a thin shell between them has any detail at all.

For a 1024³ terrain world, the interesting surface is on the order of 1024² × a few = a few million voxels, out of a billion. **The surface grows as N², the volume as N³.** Every structure below is a way of paying for the N² and not the N³.

---

## Level 0: chunks

Before any clever structure, every voxel engine partitions the world into fixed-size **chunks** — 16³, 32³, or 64³ blocks of voxels stored contiguously in one array.

This is not an optimization; it's the organizing principle. Chunks give you:

| Chunks are a… | Because |
|---|---|
| **Streaming unit** | Load, generate, and evict at chunk granularity |
| **Rebuild unit** | Edit one voxel → re-mesh one chunk (~35 µs), not the world |
| **Culling unit** | One frustum test per chunk (Module 02) instead of per voxel |
| **Cache unit** | A 32³ chunk at 1 byte is 32 KB — fits comfortably in L2 |
| **Draw unit** | One merged mesh, one draw call |
| **Threading unit** | Generate and mesh chunks on workers with no shared state |

Plus **cheap addressing**, with power-of-two sizes:

```ts
const chunkX = worldX >> 5;    // divide by 32, floors correctly for negatives
const localX = worldX & 31;    // modulo 32
```

Shifts and masks, not division. This happens millions of times per frame, so it matters (Module 02).

### Size selection is a real tradeoff

| | Smaller chunks (16³) | Larger chunks (64³) |
|---|---|---|
| Culling granularity | Finer — cull more precisely | Coarser — draw more invisible stuff |
| Rebuild cost on edit | Cheap | Expensive (64³ = 64× the voxels of 16³) |
| Draw call count | High — CPU-bound risk | Low |
| Per-chunk overhead (bookkeeping, buffers) | High | Low |
| Meshing efficiency (greedy merging) | Worse — merges stop at boundaries | Better |

**32³ is the common sweet spot.** 64³ is increasingly favored in GPU-driven designs where draw count matters more than rebuild cost. Minecraft famously uses 16×16×256 columns for historical reasons and now subdivides into 16³ sections.

Note that chunks don't have to be cubes. A 32×32×256 column is convenient for terrain generation (which is often 2D noise extruded vertically) and terrible for a fully 3D cave system. Match the shape to the content.

### Indexing order matters

```ts
idx = x + y*S + z*S*S     // x varies fastest
idx = y + z*S + x*S*S     // y varies fastest
```

Equally correct, not equally fast. **Match your dominant traversal order** (Module 02).

Many engines use **Morton encoding** (also called Z-order curve), which interleaves the bits of x, y, and z:

```
x = 5 = 101₂
y = 3 = 011₂
z = 6 = 110₂
Morton = z₂y₂x₂ z₁y₁x₁ z₀y₀x₀ = 1 0 1  1 1 0  0 1 1 = 101110011₂
```

Why bother: with a linear index, moving +1 in x is a step of 1 but moving +1 in z is a step of S². With Morton, **spatial locality in 3D becomes memory locality in 1D along *every* axis** — a 2×2×2 neighborhood is always 8 consecutive addresses. The cost is a slightly more expensive index computation (a few shifts and masks, or a small lookup table).

Bonus: **a Morton code *is* a path down an octree.** Each group of 3 bits is one child index at one level. That makes octree indexing nearly free if you're already storing Morton codes, which is why the two ideas travel together.

### Chunk contents compress well

Two techniques, both cheap, both large wins:

**1. Uniform chunk detection.** Most chunks are entirely air or entirely stone. Store a flag plus a single value and **don't allocate the array at all**:

```ts
interface Chunk {
  uniform: number | null;   // if non-null, every voxel is this value
  data: Uint8Array | null;  // only allocated for mixed chunks
}
```

In typical terrain this eliminates **70–90% of your memory** before any other technique. It's twenty lines of code and it's the highest-leverage thing in this module.

**2. Palette compression.** A chunk might contain 500 voxels of stone, 300 of dirt, 100 of grass, and 5 other types — 9 distinct values total. Instead of storing 8- or 16-bit IDs, store:
- A **palette**: a 9-entry array of the actual block IDs.
- **Indices**: 4 bits per voxel (enough for 16 palette entries), packed into a `Uint32Array`.

That's a 2× win over 8-bit IDs, 4× over 16-bit, for nearly free. The bit width grows as the palette does: 1 bit for 2 types, 2 for 4, 4 for 16, and so on. **This is what modern Minecraft does**, and it's the standard approach.

```ts
// Reading voxel i from a 4-bit palette-compressed chunk
const bitPos   = i * 4;
const wordIdx  = bitPos >>> 5;          // / 32
const shift    = bitPos & 31;           // % 32
const paletteIdx = (words[wordIdx] >>> shift) & 0xF;
const blockId  = palette[paletteIdx];
```

(Watch the edge case where a value straddles two 32-bit words — Minecraft's format deliberately wastes a few bits to avoid it, which is a reasonable trade for simpler, branch-free reads.)

---

## Level 1: sparse voxel octrees

An **octree** recursively subdivides space into 8 children — halving along each axis:

```
Level 0:  one node covering 1024³
Level 1:  8 children, each 512³
Level 2:  64 nodes, each 256³
...
Level 10: 1024³ nodes, each 1³  ← leaves
```

**Sparse** means you only allocate children for non-empty (or non-uniform) nodes. An all-air region stops at whatever level it becomes uniform, and its subtree simply doesn't exist.

The classic implementation is Laine & Karras's **Efficient Sparse Voxel Octrees** (NVIDIA, 2010), which packs each node into a compact descriptor holding a **child mask** (8 bits — which children exist) and a **relative pointer**, keeping traversal cache-friendly by placing children near their parents.

### What octrees buy

- **Empty space skipping.** A ray entering an empty node skips its entire volume in one step. This is the whole point — it's what turns "march through a billion voxels" into "march through a few hundred nodes."
- **Free LOD.** Interior nodes hold averaged or representative data, so distant geometry can just *stop descending*. **Level-of-detail is a property of the structure, not a separate system** — which is genuinely elegant and a real reason people love octrees.
- **Memory logarithmic in *surface*, not volume** — and voxel worlds are almost entirely surface.

### What they cost

- **Pointer chasing.** Each descent is a **dependent memory read**: you can't issue the next read until the previous one returns, because the previous one *contains the address*. That is the worst possible pattern for a GPU (Module 03) — there's nothing to prefetch and nothing to overlap. Depth 11 (for 2048³) means **up to 11 serialized cache misses per ray step**, each ~400 cycles.
- **Traversal state.** A correct ray-octree traversal needs a **stack** (you descend, and when you exit a node you must pop back to its parent). A stack costs registers, registers cost occupancy, occupancy costs latency hiding (Module 03). This is a compounding problem.
- **Expensive edits.** Modifying one voxel may require allocating nodes down a path and reallocating up the tree, plus potentially collapsing now-uniform subtrees. **Bad for a game where the player destroys terrain**, which is exactly your target genre.

### Sparse Voxel DAGs

**Sparse Voxel DAGs** (Kämpe et al., 2013) take the idea further: after building the octree, deduplicate **identical subtrees** into a directed acyclic graph, so every identical 16³ region of stone points at the same single node.

For structured or repetitive content the compression is spectacular — thousands of times smaller than the equivalent SVO, enough that people have fit 128K³ scenes in a few gigabytes.

**The catch is that DAGs are effectively read-only.** You cannot cheaply modify a shared subtree, because it's shared — changing one voxel means copying the whole path and breaking the sharing, and then your compression degrades as the player digs. **Excellent for static scenery, wrong for destructible worlds.**

Knowing *why* an SVDAG is wrong for this specific game is more valuable than knowing how to build one.

---

## Level 2: brickmaps — the pragmatic winner

Deep trees have too many levels. Flat grids use too much memory. The compromise that most modern voxel renderers converge on is a **shallow multi-level grid**, usually called a **brickmap**.

### The structure

```
Top-level index grid (dense, e.g. 128³ of u32)
   each cell covers an 8³ region
   value = index into the brick pool, or 0xFFFFFFFF for "empty"
        ↓
Brick pool (a big 3D texture or buffer)
   fixed-size 8³ voxel bricks, densely packed
   allocated from a free list
```

A 1024³ world becomes:
- A **128³ index grid** = 2 M entries × 4 bytes = **8 MB**, always resident.
- A **brick pool** holding only the occupied bricks. Typical terrain occupies maybe 5–15% of bricks: ~200K bricks × 512 bytes = **~100 MB**.

So ~110 MB instead of 1.07 GB, without any tree at all.

### Why this beats an octree in practice

**1. Two levels, not eleven.** Traversal is two nested DDA loops (Module 08): coarse steps through the index grid to skip empty space, fine steps within a brick. **No stack, few registers, high occupancy.** This is the single biggest reason.

**2. Random access is O(1).** `setVoxel` is two array lookups and a write:
```ts
const brickIdx = indexGrid[bx + by*128 + bz*128*128];
if (brickIdx === EMPTY) allocateBrick(...);
brickPool[brickIdx * 512 + (lx + ly*8 + lz*64)] = id;
```
**Perfect for a destructible game.** No reallocation, no tree rebalancing, no cascading updates.

**3. The brick pool can be a 3D texture**, so you get the swizzled cache layout and free hardware filtering discussed in Module 03. That's often worth 2× on traversal-heavy workloads.

**4. Streaming is natural.** Bricks are **fixed-size**, so allocation is a free-list pop and deallocation is a free-list push. **No fragmentation, ever** — the classic problem with variable-size allocation simply doesn't arise. Evicting a distant region returns its bricks to the pool and they're immediately reusable.

```ts
// The entire allocator
class BrickPool {
  private freeList: number[] = [];   // pre-filled with 0..N-1
  alloc(): number { return this.freeList.pop() ?? this.grow(); }
  free(i: number): void { this.freeList.push(i); }
}
```

### Refinements

**A third level** (a 32³ super-grid over the index grid) for very large worlds — same idea, one more coarse skip.

**Occupancy bitmasks.** Store, per brick, a 512-bit mask (8³ bits = 64 bytes) of which voxels are solid. Now a ray can ask "is this brick worth entering at all?" or even "is there anything along this row?" with a few 64-bit integer reads instead of texture fetches. Since GPUs have fast bit operations and the mask is 8× smaller than the brick, this is a big traversal win. It's also what makes binary greedy meshing possible (Module 08).

> **This "two-level grid with a brick pool" design is what you should reach for first**, and being able to explain *why* — high occupancy, no pointer chasing, O(1) edits, texture-cache-friendly, fragmentation-free streaming — is exactly the depth this role asks for.

---

## Level 3: 64-trees and bitmask hierarchies

A newer direction worth knowing, because it's actively where the field is moving and mentioning it signals you read current work rather than 2010 papers.

Instead of branching by 2 per axis (octree → 8 children), branch by **4 per axis → 64 children**. Each node's occupancy fits in **exactly one 64-bit integer**, one bit per child.

### Why that specific number is elegant

Modern GPUs have fast bit operations, so you can do all the tree bookkeeping in registers with no extra memory reads:

```wgsl
// Does child i exist?
let exists = (mask & (1ul << i)) != 0;

// Where is child i in the compacted child array?
// Count how many children before it exist. That's its offset. No pointer array at all.
let offset = countOneBits(mask & ((1ul << i) - 1ul));
let childIndex = baseIndex + offset;
```

`countOneBits` is **popcount** — count the set bits — a single hardware instruction. So a node is just **one 64-bit mask plus one base index**, and child addressing is pure ALU. **There is no separate child pointer array to read.** Compare to an octree, where each descent needs a pointer *fetched from memory*.

### The tradeoffs versus octrees

| | Octree (8-way) | 64-tree |
|---|---|---|
| Levels to cover 2³⁰ voxels | 10 | **5** |
| Dependent memory reads per traversal | ~10 | **~5** |
| Child addressing | Pointer read | popcount (ALU) |
| Sparsity granularity | Finer | Slightly coarser |
| Position on the roofline (Module 03) | Memory-bound | **Better balanced** |

Fewer levels and fewer dependent reads is the actual win — it directly attacks the thing GPUs cannot hide.

The same bitmask trick shows up everywhere in voxel work. **`OpenVDB` / `NanoVDB`** (used across film VFX, and NanoVDB specifically designed for GPUs) is essentially a shallow tree of bitmask-indexed tiles with branching factors like 32³ / 16³ / 8³. **If you've heard "VDB," this is what it is** — and knowing that it's the same idea at a different branching factor is a nice piece of connective tissue to have.

---

## The dimension people forget: GPU memory layout

Choosing the algorithm is half the job. The other half is how it sits in memory, and **this is where good voxel engineers separate from average ones.**

### Storage buffer vs 3D texture

| | Storage buffer | 3D texture |
|---|---|---|
| Layout | Linear | Swizzled (3D-coherent) |
| Filtering | None | Free bilinear/trilinear |
| Read-write in shader | Yes | Only via storage textures, limited formats |
| Atomics | Yes | No |
| Size limit | 128 MiB per binding (default) | 2048³ per dimension |
| Indexing | Arbitrary | `textureLoad` with bounds safety |

For a **brick pool that's read many times per ray with 3D-coherent access**, the texture usually wins on cache behavior alone (Module 03). For the **index grid**, which is read once per coarse step, a buffer is fine and lets you use atomics for GPU-side allocation.

**Benchmark both.** It's hardware-dependent, and *"I measured it on the target GPUs"* is the right answer to give — with the reasoning about swizzling as the hypothesis you were testing.

### Structure of arrays

If a voxel has a material ID, a light level, and flags, **do not interleave them.** A traversal that only needs "is this solid?" should not drag light levels through the cache. Split into parallel arrays and pack the hottest one as tightly as possible.

This is Module 03's coalescing rule applied to your own data. Concretely: an occupancy bitmask read is 64 bytes for 512 voxels; reading the same information from an interleaved 4-byte-per-voxel structure is 2 KB. **32× the bandwidth for the same answer.**

### Bit packing is nearly always worth it

Memory bandwidth is the bottleneck, ALU is cheap (Module 03). Unpacking a 4-bit field costs a shift and a mask and saves half your traffic.

A common voxel packing: **16 bits total** — 12 bits material ID (4096 types), 4 bits of light level or rotation state. Or 8 bits if 256 types is enough, which for most games it is.

### Alignment and the 128 MiB wall

WebGPU's default `maxStorageBufferBindingSize` of **128 MiB** means your brick pool may need to be **split across multiple bindings**, or you request a higher limit and accept reduced device compatibility.

**Design for the split.** It's a real constraint, not a hypothetical — and it's a good concrete example to raise when asked "what's different about building an engine for the web?"

### Uploads

- `queue.writeBuffer` for small updates — simple, correct, adequate for most things.
- A persistent staging buffer with `mapAsync` for large streaming.
- **Never upload a whole chunk when one voxel changed.** Upload the dirty brick (512 bytes) — or better, **apply edits in a compute shader from a small edit list** so the CPU never touches voxel data at all. Send `[{x, y, z, id}, ...]`, let a compute shader scatter them into the pool. That's the design that scales to a player firing an explosive.

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

**Generation belongs on Web Workers**, never the main thread. Terrain noise for a 32³ chunk is single-digit milliseconds — instantly fatal to your 16.6 ms budget if done inline, and you need dozens of chunks when the player moves.

**Amortize aggressively.** Cap uploads and mesh rebuilds per frame — e.g., a hard 2 ms budget, and whatever doesn't fit waits. **A visible pop-in is much better than a 40 ms hitch.** Players forgive geometry appearing; they do not forgive stutter (Module 01).

**Prioritize by need, not by distance alone.** In-frustum and close first, then near-but-behind (the player will turn around), then everything else. A simple priority = `distance - inFrustumBonus` works well.

**LOD by mip.** Averaged/downsampled bricks let distant regions use ⅛ the data per level.

- In a **raytraced** voxel renderer this is nearly free: just stop descending and sample the coarser level.
- In a **meshed** renderer it's genuinely hard, because two adjacent chunks at different LOD levels have mismatched vertex positions along their shared boundary, leaving visible **cracks** — holes you can see the sky through. The standard fixes are **skirts** (a downward-hanging rim of geometry at every chunk edge that plugs any crack, cheap and slightly wasteful) or **stitching** (generating transition geometry, correct and fiddly). Knowing that LOD cracks are the hard part of meshed LOD is itself a signal.

**Determinism.** Chunk contents must be a pure function of `(seed, chunkCoord)` plus a stored diff of player edits. This is Module 01's determinism discipline applied to world generation, and it's what **makes an effectively infinite world cost only what players have changed** — the foundation of the entire genre. A save file becomes a seed plus a sparse edit list, kilobytes instead of gigabytes.

---

## Choosing: a decision table

| Requirement | Structure |
|---|---|
| Small, fully loaded scene | Dense 3D texture |
| Editable game world, moderate size | Chunked dense + palette compression |
| **Large world, ray traced, destructible** | **Brickmap (two-level grid + brick pool)** |
| Very large world, mostly static | SVO or 64-tree |
| Huge, highly repetitive, read-only | SVDAG |
| Sparse volumetric data (smoke, clouds) | VDB-style bitmask tree |
| Meshed rendering | Chunked dense + greedy meshing (Module 08) |

**For a first-person voxel roguelike with destructible dungeons and a ray-traced look**, the honest recommendation is:

> Chunks of palette-compressed voxels on the CPU/worker side, mirrored into a GPU brickmap with per-brick occupancy bitmasks.

Everything else is a specialization of that.

---

## Common confusions

**"Sparse means less memory, so I should always use the sparsest structure."** Sparsity costs indirection, and indirection costs dependent memory reads, which is the thing GPUs handle worst. A denser structure with better access patterns frequently wins. This is the central tension of the whole module.

**"An octree gives me LOD for free, so it's strictly better."** A brickmap gives you LOD too — store mip levels of the brick pool, or a coarse occupancy summary per index-grid cell. You're not choosing between LOD and no LOD.

**"I'll just use a hash map for sparse voxels."** Hash lookups are a dependent read plus probing, they don't preserve spatial locality at all, and they're miserable on the GPU. Fine for a CPU-side edit journal, wrong for the render path.

**"Palette compression will slow down my access."** Reading is a shift and a mask — nanoseconds — and it halves or quarters your memory traffic. On the CPU side it's clearly a win. The complexity is in *writing* (you may need to grow the palette and repack), which is why you keep an uncompressed working copy for chunks currently being edited.

**"Bigger chunks are better because fewer draw calls."** Until one voxel edit costs a 64³ re-mesh in the middle of a frame. Measure your edit frequency; a mining/building game and a static-scenery game want different answers.

---

## The interview answer

***"How would you store a large voxel world?"***

> "Chunks as the streaming and edit unit — 32³ is a reasonable default — palette-compressed on the CPU side, since most chunks have very few distinct materials, plus a uniform-chunk flag so all-air and all-stone chunks allocate nothing at all. That alone is usually 70–90% of the memory.
>
> On the GPU, a two-level brickmap: a top-level index grid pointing into a pool of 8³ bricks held in a 3D texture, with per-brick occupancy bitmasks for cheap empty-space rejection.
>
> I'd pick that over a deep octree because octree traversal is a chain of dependent memory reads, which is exactly what GPUs can't hide, and because it needs a stack, which costs registers and therefore occupancy. And O(1) random access matters a lot when the world is destructible — a brickmap edit is two array lookups, an octree edit can cascade up the tree.
>
> If the content turned out to be largely static and repetitive I'd look at an SVDAG for the compression, though it's effectively read-only. Or a 64-tree if I wanted hierarchy with fewer levels and popcount-based child indexing instead of pointer chasing."

That answer demonstrates structure knowledge, hardware reasoning, and a design tradeoff tied to the actual game. **That's the target.** Notice it never says "X is better" — it says "X, because of this property, given this requirement."

---

## Exercise — Voxelforge, Stage 7

**1. Implement chunked storage with palette compression.** Measure the memory of a 512×128×512 terrain world before and after. **Report the ratio in your README.**

**2. Add uniform-chunk detection** (all-air / all-solid stores no array). Measure again. This one is twenty lines and usually the bigger win — note which of the two mattered more.

**3. Implement a GPU brickmap:**
   - A `u32` index grid in a storage buffer
   - An 8³ brick pool in an `r8uint` 3D texture
   - A free-list allocator on the CPU

   Upload a world and render it by *any* means — even just reading it back into your existing mesher. Getting the data structure right matters more right now than rendering from it.

**4. Add a 64-byte occupancy bitmask per brick** in a parallel buffer. You'll use it heavily in Module 08.

**5. Implement `setVoxel(x, y, z, id)`** that allocates a brick on demand, updates the bitmask, and marks a dirty region for upload. **Then verify a single voxel edit costs microseconds, not milliseconds.** If it doesn't, find out where the time went — that's the exercise.

**6. Move terrain generation to a Web Worker** with a fixed per-frame upload budget. **Sprint the camera across the world and check your frame time graph for hitches.** (You built that graph in Module 01. This is why.)

**⭐ Stretch:** implement the edits-as-a-compute-shader path — send a list of `{x,y,z,id}` to the GPU and scatter them in a compute pass, so the CPU never touches voxel data. Then simulate an explosion that changes 10,000 voxels in one frame and confirm the frame time doesn't move.

---

## Go deeper

- **Laine & Karras, "Efficient Sparse Voxel Octrees" (NVIDIA, 2010)** — the foundational paper. Read it for the node encoding and the traversal, even if you build a brickmap. Knowing what you rejected is half of a good design answer.
- **Kämpe, Sintorn & Assarsson, "High Resolution Sparse Voxel DAGs" (SIGGRAPH 2013)** — the deduplication idea, and one of the more surprising results in the field.
- **"A guide to fast voxel ray tracing using sparse 64-trees"** — dubiousconst282.github.io (2024). Modern, practical, benchmarked; pairs with the `VoxelRT` repo. This is the current-work citation.
- **Dennis Gustafsson's blog (blog.voxagon.se) on Teardown** — the best public writing on a *shipped*, fully destructible, ray-traced voxel game. Read everything. If you cite one practitioner in an interview, cite this one.
- **OpenVDB / NanoVDB documentation** — the bitmask-tile hierarchy in its most mature form.
- **`0fps.net` archives (Mikola Lysenko)** — foundational voxel engine writing, still worth the time despite its age.
- **John Lin's and Douglas Dwyer's voxel engine devlogs** — practitioners publishing real numbers on real structures.

---

**Next:** [Module 08 — Voxel Rendering Techniques](./08-voxel-rendering.md)
