# Module 12 — Gameplay and Simulation Systems

### Collision against a grid, spatial queries, character control, procedural generation, and the systems that make a world feel like a game

*~11 min read · Part IV: Engine Breadth · Prerequisites: Modules 01, 02, 07, 10*

---

Rendering gets the glory; gameplay systems get the bug reports. Both JDs list gameplay and simulation as core territory, and a small studio will absolutely expect you to work across it.

The good news: this is the part of game programming that most resembles the software engineering you already do — state machines, event systems, data-driven configuration, careful edge cases. The part that doesn't transfer is that all of it must run inside a few milliseconds, deterministically, every frame.

---

## Collision in a voxel world

A general physics engine solves the hard problem: arbitrary convex hulls in arbitrary orientations. A voxel world hands you a much easier problem, and taking that gift is one of the clearest wins available.

**The whole world is an axis-aligned grid.** So:

- No broad-phase acceleration structure is needed for world collision — the grid *is* the acceleration structure, with O(1) lookup.
- No narrow-phase convex solver — it's AABB vs. AABB.
- No mesh collision data — the voxels are the collision geometry, always in sync, free after destruction.

**Swept AABB against the grid** is the core routine. The player is an AABB with a velocity; you need the first blocking contact along the movement.

The robust implementation, and the one you should reach for:

```
for each axis in (x, y, z):        # resolve one axis at a time
    move the AABB along that axis by velocity[axis] * dt
    compute the integer voxel range the AABB now overlaps
    if any voxel in that range is solid:
        snap the AABB to the boundary of the offending voxel
        velocity[axis] = 0
```

Axis-separated resolution looks crude and is in fact the standard technique, because it makes sliding along walls fall out automatically: blocking X doesn't stop Y or Z. Resolving all axes simultaneously produces the classic "player sticks on wall corners" bug.

**Order matters.** Resolving Y (gravity) before X and Z means you land on ground before attempting horizontal movement, which prevents catching on the lip of a block you're standing on. Most implementations converge on Y-then-X-then-Z or Y-last depending on the feel they want; try both.

**Tunneling** — moving fast enough to pass through a wall in one step — is prevented by either capping velocity to less than one voxel per fixed step, or by substepping the movement. Given a fixed timestep (Module 01), capping is simple and predictable.

**Step-up** (walking up single-voxel ledges without jumping) is a small, fiddly, high-impact feature: attempt the horizontal move; if blocked, try again raised by one voxel; if that succeeds and there's ground beneath, accept it and smooth the visual Y offset over a few frames so the camera doesn't jolt. Players never notice this working and immediately notice it missing.

**Player raycasting** — for targeting the block you're looking at — is your DDA from Module 08, run on the CPU. It returns the hit voxel *and* the face normal, which gives you both "which block to break" and "where to place the new one" (hit voxel + normal). Same algorithm, two uses.

**Entity-vs-entity collision** still needs a broad phase, but a **uniform spatial hash** (bucket entities by `floor(pos / cellSize)` into a hash map) is trivially simple and works extremely well for the entity densities in a typical game. Reach for a BVH or quadtree only when you've measured a need.

---

## Character control feels like nothing else

Movement is where a game is won or lost, and it is almost entirely a tuning problem informed by a few known techniques:

- **Acceleration and friction curves**, not instant velocity. Different values for ground and air.
- **Coyote time** — allow a jump for ~100 ms after walking off a ledge. Players believe they pressed jump in time; this makes them right.
- **Input buffering** — if the player presses jump within ~150 ms *before* landing, execute it on landing.
- **Variable jump height** — releasing the button early cuts upward velocity.
- **Separate camera and movement smoothing** — never smooth input latency, only visual position.

Every one of these is a lie told to make the game feel fair. Collectively they're the difference between "responsive" and "floaty," and none of them are in a physics textbook.

**Expose all of it as data.** Movement tuning values belong in a hot-reloadable config with an in-game debug UI, because designers will iterate on them a hundred times and every round trip through a code change is wasted.

---

## Spatial queries and AI

The queries gameplay actually needs:

- **Point/AABB overlap** — "what's in this explosion radius?" → spatial hash.
- **Raycast** — line of sight, targeting, projectile hits → DDA.
- **Nearest-N** — "closest enemies" → spatial hash with expanding ring search.
- **Pathfinding** — A* over the voxel grid.

**A\* on voxels** works directly: nodes are voxel positions, neighbours are the 6 (or 26, with diagonals) adjacent cells that an agent can occupy, cost includes jump/fall penalties. The concerns are practical:

- **Search space is enormous.** Cap the node budget and fail gracefully rather than searching forever.
- **Amortize across frames.** Pathfinding is a job for a worker; a request/response queue with results a few frames later is fine and invisible.
- **Hierarchical pathfinding** — path between chunk-level regions first, then refine within regions — is the standard scaling answer, and voxel chunks give you the hierarchy for free.
- **Cache and invalidate.** When the player destroys terrain, invalidate paths through the affected region rather than recomputing everything.

**AI behavior** at the scale of a dungeon crawler is well served by simple structures. **Finite state machines** for a handful of states (idle/patrol/chase/attack/flee) are readable, debuggable, and sufficient. **Behavior trees** compose better past roughly a dozen behaviors and are much friendlier to designer authoring. **Utility AI** — scoring actions by weighted considerations — produces more organic behavior at the cost of being harder to debug ("why did it do that?").

The most valuable thing you can build is not the AI itself but **the debug view**: draw each agent's current state, target, and path in the world. It converts hours of speculation into seconds of observation.

---

## Procedural generation for a roguelike

A roguelike's world is generated, and it must be generated **from a seed, deterministically** (Module 01), so a run can be reproduced, shared, and debugged.

**The layers, in typical order:**

1. **Noise** — value/Perlin/simplex/OpenSimplex for terrain shape. Fractal Brownian motion (summing octaves at doubling frequency, halving amplitude) is the workhorse. Use 3D noise for caves and overhangs, 2D heightmaps for surfaces.
2. **Structure placement** — rooms, corridors, prefabs. Common approaches: BSP subdivision, random room placement with corridor connection, cellular automata for organic caves, wave function collapse for constrained tile-based layouts.
3. **Connectivity guarantees** — the hardest part. A dungeon that generates an unreachable exit is a run-ending bug. Verify reachability with a flood fill *as part of generation*, and regenerate or repair when it fails.
4. **Decoration and loot** — placement rules, density budgets, pacing.
5. **Player edits** — stored as a sparse diff over the generated base, so an infinite world costs only what players changed.

**The rules that make this survivable:**

- **Determinism through seed derivation.** Never share one global PRNG stream across systems — the order systems run in would change results. Derive per-purpose seeds: `hash(worldSeed, chunkX, chunkZ, PURPOSE_CAVES)`. Then generating chunk (5,7) gives the same result whether it's generated first or thousandth, and adding a new system doesn't perturb existing ones. This is the single most important procgen engineering decision, and it's the one people most often get wrong.
- **Chunk independence.** A chunk's base content should depend only on its coordinates and the seed, never on its neighbours' generation state — otherwise generation order matters and parallelism dies. Features that span chunks (trees, buildings, corridors) are handled by having each chunk deterministically compute which features *originating in nearby chunks* overlap it.
- **Generation belongs on workers**, always.
- **Build a preview tool.** A CLI or in-editor view that generates and renders a level from a seed, without launching the game, turns a 60-second iteration into a 2-second one. Designers will use it constantly.

---

## Animation, briefly

Even a voxel game needs motion.

- **Skeletal animation** — a hierarchy of bones, a per-vertex weighting, poses sampled from keyframe tracks and blended, then skinned. The standard for characters.
- **Voxel-native alternatives** — many voxel games instead animate *rigid parts* (a Minecraft mob is a handful of boxes with independent transforms), which is far simpler, matches the aesthetic, and completely avoids skinning. If the art direction permits, take it.
- **Procedural animation** — inverse kinematics for foot placement, spring-damper systems for secondary motion, look-at for heads. Cheap and disproportionately effective for a stylized game.

**Animation samples at render rate, not simulation rate.** It's presentation, not simulation — Module 01's boundary applies.

---

## The systems around the systems

**Events.** Gameplay needs decoupling: "player took damage" should reach the HUD, the audio system, and the achievement tracker without the combat code knowing they exist. A simple typed event bus is enough. Two cautions: **avoid unbounded event chains** (an event handler emitting events that emit events makes ordering unknowable), and **process events at a defined point in the frame**, not whenever they're raised, or you'll mutate state mid-iteration.

**Save/load.** With determinism, a save is `{seed, playerState, sparse world diff, entity states, RNG stream positions}` — potentially kilobytes for a huge world. Version the format from day one. And test the migration path, because breaking saves after launch is one of the worst mistakes a game can make.

**Data-driven design.** Block types, item stats, enemy definitions, loot tables, and tuning constants belong in data files with schema validation (Zod or equivalent), hot reloadable, editable by designers without a build. The engineering payoff is that balance iteration stops consuming engineer time entirely.

**Time and pausing.** Keep separate clocks: simulation time (pausable, scalable for slow-motion), real time (for UI animation and networking), and unscaled time. Systems must be explicit about which they use, or pausing the game will pause your menus.

---

## The interview answer

*"How would you handle collision in a voxel game?"*

> "Swept AABB against the grid, resolved one axis at a time so sliding along walls falls out naturally and corners don't stick. Y first so you land before moving horizontally. Velocity capped to under a voxel per fixed step to prevent tunneling, plus step-up with a smoothed visual offset. No general physics engine — the grid is already a perfect broad phase with O(1) lookup, and using voxels directly as collision geometry means destruction stays in sync for free. For entity-vs-entity I'd start with a uniform spatial hash and only go further if measurement said to."

*"How do you keep procedural generation deterministic?"*

> "Derive a separate seed per chunk per system from a hash of the world seed and coordinates, rather than sharing one global stream — otherwise generation order changes results and you lose reproducibility, parallelism, and the ability to add a system without perturbing existing worlds. Chunks generate independently from coordinates alone, cross-chunk features are computed by having each chunk work out which nearby-origin features overlap it, and player edits live as a sparse diff over the generated base."

---

## Exercise — Voxelforge, Stage 12

1. Implement **swept AABB vs. grid** collision with per-axis resolution. Deliberately implement the all-axes-at-once version first and feel the corner-sticking bug.
2. Add a first-person controller with gravity, jump, acceleration/friction, **coyote time**, **input buffering**, and **variable jump height**. Put every constant in a hot-reloadable config with an on-screen tuning UI.
3. Add **step-up** with visual smoothing.
4. Implement **block break/place** using CPU DDA — hit voxel for break, hit voxel + face normal for place. Verify a single edit re-meshes only the affected chunk (and its neighbours, if the edit was on a boundary — that's the bug you'll hit).
5. Build **seed-derived procedural generation**: FBM terrain, 3D-noise caves, and BSP or cellular-automata rooms, with per-system seed derivation. Write a test asserting that generating chunks in a different order produces identical results.
6. Add a **flood-fill reachability check** and make generation retry when the exit is unreachable.
7. Add a **spatial hash** for entities and A* pathfinding on a worker, with a debug view drawing agent state and path.
8. Implement save/load as seed + sparse diff, with a version field. Save, quit, reload, and verify the world is bit-identical.

---

## Go deeper

- **Robert Nystrom, *Game Programming Patterns*** — State, Event Queue, Component, Spatial Partition, Service Locator. Free online, and directly applicable to every section above.
- **Glenn Fiedler's gafferongames.com physics series** — integration, collision response, and determinism, written with unusual clarity.
- **Christer Ericson, *Real-Time Collision Detection*** — the reference. You'll use a fraction of it, but the AABB and swept-test chapters are exactly what you need.
- **Amit Patel's Red Blob Games** — A*, pathfinding, hex grids, noise, and procedural generation, all interactive. The best explanatory site in game development.
- **Bob Nystrom, "Rooms and Mazes: A Procedural Dungeon Generator"** — journal.stuffwithstuff.com. Concrete and readable.
- **"Procedural Content Generation in Games"** (Shaker, Togelius, Nelson) — free at pcgbook.com.
- **Chris Simpson, "Behavior trees for AI: How they work"** — the clearest introduction.
- **GDC talks on game feel** — Steve Swink's *Game Feel* book, and Jan Willem Nijman's "The Art of Screenshake." Both explain why the lies in the character-control section work.

---

**Next:** [Module 13 — TypeScript and V8 Performance Realities](./13-typescript-v8-performance.md)
