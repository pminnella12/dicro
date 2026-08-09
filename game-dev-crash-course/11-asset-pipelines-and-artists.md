# Module 11 — Asset Pipelines and Working with Artists

### File parsers, intermediate representations, bake systems, and the human protocol that decides whether your engine helps or blocks the art team

*~12 min read · Part IV: Engine Breadth · Prerequisites: Module 10*

---

Both job descriptions weight this heavily. The Engine JD lists *"Asset processing: writing a file parser using a spec, designing intermediate data representations, serialization, compression, parallel processing"* and dedicates an entire responsibility bullet to *"Integrate Art: collaborate with 3D voxel artists to figure out what their tooling needs are, communicate necessary engine limitations back to artists, and find the balance between giving artists control and establishing realistic engine restrictions."*

That second sentence is not a soft skill in disguise. It is a technical design problem: **what is the contract between authored content and the runtime?**

---

## The three-format model

Every mature pipeline separates three representations, and confusing them is the source of most pipeline pain.

**1. Source format** — what the artist edits. MagicaVoxel `.vox`, Blender `.blend`, Aseprite, a Photoshop file, a spreadsheet. Lossless, editable, version-controlled, never loaded by the game.

**2. Intermediate format** — a normalized, engine-agnostic representation produced by importers. All source types converge here. This is where you put validation, unit conversion, coordinate-system fixes, and naming normalization.

**3. Runtime format** — what the game loads. Binary, aligned for direct upload, pre-compressed, pre-baked, with all decisions already made. Ideally memory-mappable or near-zero-parse: you read the bytes and hand them to the GPU.

Why the middle layer earns its keep:

- **New source formats** need only a new importer, not a new runtime path.
- **Runtime format changes** (a new vertex packing, a new compression scheme) re-bake from intermediates without artists touching anything.
- **Validation happens once**, in a place with good error messages, not scattered through loaders.
- **Platform variants** (compressed texture formats per device, LOD levels, quality tiers) are bake-time outputs from one intermediate.

The cost is one more stage and one more format to maintain. For a very small project you can skip it — and then you will add it around month six, at higher cost. Knowing that tradeoff, and being able to argue either side, is the point.

---

## Writing a parser from a spec

This is a concrete, testable skill the JD names directly. The workflow:

**Read the spec completely before writing code.** Specifically note: endianness, alignment/padding rules, versioning, and what's optional.

**Use `DataView` for structured binary reads in TypeScript** — it handles endianness explicitly:

```ts
class Reader {
  private view: DataView;
  private off = 0;
  constructor(buf: ArrayBuffer) { this.view = new DataView(buf); }

  u32(): number { const v = this.view.getUint32(this.off, true); this.off += 4; return v; }
  i32(): number { const v = this.view.getInt32(this.off, true);  this.off += 4; return v; }
  f32(): number { const v = this.view.getFloat32(this.off, true); this.off += 4; return v; }
  bytes(n: number): Uint8Array {
    const v = new Uint8Array(this.view.buffer, this.view.byteOffset + this.off, n);
    this.off += n; return v;   // a view, not a copy
  }
  fourcc(): string { return String.fromCharCode(...this.bytes(4)); }
}
```

`true` is little-endian. Most game formats are little-endian because x86 and ARM are; network formats are usually big-endian. Getting this wrong produces values off by factors of 16 million, which is at least an obvious symptom.

**Chunked formats** (RIFF-style: a four-character code, a size, then content) are extremely common — `.vox`, `.wav`, PNG, glTF's GLB container. The right shape is a loop that reads a chunk header, dispatches on the tag, and **skips unknown chunks using the size field**. Skipping unknown chunks is what makes a parser forward-compatible.

**MagicaVoxel `.vox`** is the format you'll most likely meet in a voxel studio. It is RIFF-like with `MAIN`, `SIZE`, `XYZI` (voxel positions + palette indices), `RGBA` (a 256-color palette), plus scene-graph and material chunks in later versions. Two things trip everyone: **it's Z-up while most engines are Y-up**, and the default palette has an off-by-one indexing quirk. Write the importer, write a round-trip test, and put the coordinate conversion in exactly one function.

**Rules that separate a good parser from a fragile one:**

- **Never trust the input.** Validate magic numbers, versions, sizes, and bounds. A malformed asset should produce a clear error naming the file and the offset — not a crash and not silent corruption.
- **Report errors with context.** `"chunk 'XYZI' at offset 0x1A4C claims 40000 voxels but only 12 bytes remain"` saves an artist an hour.
- **Test with real files**, including files exported by every tool version the artists actually use.
- **Fuzz it** if it will ever load untrusted content.
- **Keep parsing allocation-light** — read into pre-sized typed arrays rather than building object graphs you'll immediately discard.

---

## Designing the runtime format

Your own format is where you get to make good decisions.

**Header first:** magic number, version, endianness marker, table of contents with offsets and sizes. Version from day one — you *will* change the format, and a version field turns a mysterious crash into a clear "asset is v3, engine expects v5; re-bake."

**Align data for direct upload.** If a blob will become a GPU buffer, align its offset (4, 16, or 256 bytes as needed) so you can create a typed-array view over the file bytes with no copy. This is the difference between "load 200 MB in 400 ms" and "load it in 4 seconds."

**Structure of arrays, again.** Store all positions, then all normals, then all UVs — not interleaved records — unless the GPU wants them interleaved (vertex buffers usually do). Match the consumer.

**Choose compression per data type:**
- **Generic**: `DEFLATE` via `DecompressionStream` (built into browsers, free), or Zstandard/LZ4 via WASM when decode speed matters more than ratio. LZ4 decompresses at GB/s and is usually the right choice for runtime assets.
- **Textures**: block-compressed formats (BC/ASTC/ETC2) — these stay compressed in VRAM, so they save bandwidth, not just disk. Ship per-platform variants, or use a universal format like **Basis Universal / KTX2**, which transcodes to the device's native format at load.
- **Voxel data**: palette + RLE + generic compression on top. Voxel volumes compress spectacularly — often 50–100× — because they're so repetitive.
- **Audio**: Opus.

**Measure decode time, not just size.** A format that's 30% smaller but 5× slower to decode is a worse format for a game that must stream while running.

---

## The bake system

The offline step that turns intermediates into runtime assets. What it must do:

- **Be deterministic.** Same inputs → byte-identical outputs. Non-determinism destroys caching and makes "did my change break this?" unanswerable.
- **Be incremental.** Hash inputs (content + importer version + settings) and skip unchanged work. A full re-bake should be rare.
- **Track dependencies.** A texture change should re-bake the material that references it, and nothing else.
- **Parallelize.** Assets are embarrassingly parallel; use every core.
- **Cache shared results.** A team-wide bake cache keyed by content hash means the first person to bake an asset pays, and everyone else downloads. This is a large, real quality-of-life win once the team is more than three people.
- **Fail loudly with actionable messages**, naming the source file and the artist who can fix it.

**Bake vs. runtime is a recurring judgment call.** Bake when the input is static and the computation is expensive: mesh generation for static props, lightmaps, mip chains, texture compression, navmeshes, palette optimization. Compute at runtime when the input is dynamic: anything the player destroys, procedural terrain from a seed, dynamic lighting. For a procedurally generated roguelike, most world content is *generated at runtime from a seed* — which shifts the emphasis from baking to making generation fast and deterministic (Modules 07 and 12).

---

## Working with artists: the actual job

The Engine JD asks for someone who can *"find the balance between giving artists control and establishing realistic engine restrictions."* Here is how that plays out in practice.

**Constraints must be discovered by artists at authoring time, not by engineers at integration time.** The worst possible workflow is: artist spends three days on an asset, hands it over, engineer says "this has 40 materials and we support 8." The fix is always the same — **push validation into the tools**. A `.vox` importer that warns "this model uses 312 distinct colors; the palette budget is 256" the moment they save is worth more than any amount of documentation.

**Give budgets, not prohibitions.** "Keep it under 64³ per prop and 16 materials" is actionable. "Don't make it too complex" is not. Numbers let artists make their own tradeoffs, which is what they want.

**Explain the *why* once, in their terms.** Not "we're bandwidth-bound on the brick pool" but "every extra material means another texture layer, and we've got room for about 30 before the game gets slow on the target machine — here's the counter in the corner of the viewport showing where we are." Artists make excellent technical decisions when given accurate feedback loops. They make bad ones when the loop is a Slack message three days later.

**Build the feedback loop into the tool.** In-editor stat overlays, a validation panel, colored warnings, and a "test in game" button. This is the highest-leverage tooling work an engine programmer does at a small studio.

**Iteration speed is the deliverable.** If an artist can change a model and see it in game in 5 seconds, they will try twenty variations and the game will look better. If it takes 5 minutes, they will try two. Hot reload (Module 10) is an *art quality* feature, not a programmer convenience.

**Say yes with a number.** When an artist asks for something the engine doesn't support, the useful responses are "yes, that's about two days" or "yes, but it costs 2 ms per frame and we have 4 ms of headroom — is it worth it?" The unhelpful response is "the engine doesn't work that way." Most requests have a cheaper 80% version, and finding it is the job.

**Learn enough of their tools to have real conversations.** Spend an afternoon in MagicaVoxel. Build something bad. You will immediately understand three constraints that would otherwise take months of complaints to surface.

**Where artists most need control in a voxel game**, in rough priority order: the palette and color ramps; material properties (emissive, transparent, metallic-ish); animation/rig hooks; per-model pivot and orientation; VFX parameters; and the ability to preview lighting as it will actually appear. Give them these, and hold the line on things that break the engine's core assumptions — grid alignment, budget ceilings, and anything that would force a per-object code path.

---

## The interview answer

*"How would you set up an asset pipeline?"*

> "Three formats: artist-facing source, an engine-agnostic intermediate produced by importers where validation and coordinate normalization live, and a binary runtime format aligned for direct GPU upload with a version header. The bake is deterministic, content-hash-incremental, parallel, and shares a cache across the team. The most important part is that constraints surface in the artist's tool at save time — budget warnings and validation errors with the file name and a number — rather than being discovered by an engineer at integration. And I'd invest in hot reload early, because iteration speed is what actually determines how good the art ends up."

*"An artist wants a feature that would hurt performance. What do you do?"*

> "Find out what they're actually trying to achieve — the request is usually a proposed solution, not the goal. Then price the real options: 'the full version costs 3 ms, this 80% version costs 0.4 ms, here's what each looks like.' Then decide together against the frame budget, and make the cost visible in the tool so the next decision doesn't need me."

---

## Exercise — Voxelforge, Stage 11

1. Write a **`.vox` parser** from the MagicaVoxel spec, using `DataView` and a chunk loop that skips unknown chunks. Handle the palette, and convert Z-up to your engine's convention in exactly one place.
2. Write a **round-trip test**: parse a `.vox`, serialize to your own format, load it back, and assert the voxel data is identical.
3. Design your **runtime format**: magic, version, TOC, aligned blobs for voxel data / palette / metadata. Load it by creating typed-array views over the file bytes with zero copies. Measure load time against a JSON version of the same data — the ratio will be instructive.
4. Add **palette + RLE compression** and report the ratio on real content.
5. Build a **bake CLI**: `voxelforge-bake src/ out/` with content hashing, incremental skipping, and parallel processing across workers. Verify determinism by baking twice and diffing.
6. Add a **validation pass** that fails the bake with a clear message when a model exceeds your budgets, naming the file and the number.
7. Hand a friend (or yourself, in a fresh terminal) the tool with no instructions. Everything that confuses them is a bug.

---

## Go deeper

- **MagicaVoxel `.vox` format spec** (`ephtracy/voxel-model` on GitHub) — your first parser target.
- **glTF 2.0 / GLB specification** — the industry's interchange format, and an excellent example of a well-designed spec with a binary container. Worth reading even if you never use it.
- **KTX2 / Basis Universal** (Binomial, now Khronos) — transcodable compressed textures; the right answer for cross-platform texture shipping.
- **"Zen of Asset Pipelines" / Unreal's Derived Data Cache and Unity's Asset Import Pipeline docs** — read how the big engines structure bake caching; the ideas transfer.
- **Jason Gregory, *Game Engine Architecture*, Chapter 7** — the resource/asset chapter.
- **Casey Muratori's and Jonathan Blow's talks on iteration speed** — polarizing, but the core argument about compile/iteration times shaping what gets built is correct and directly relevant.

---

**Next:** [Module 12 — Gameplay and Simulation Systems](./12-gameplay-and-simulation.md)
