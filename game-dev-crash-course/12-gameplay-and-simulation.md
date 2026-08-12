# Module 12 — Gameplay and Simulation Systems

### Collision against a grid, spatial queries, character control, procedural generation, and the systems that make a world feel like a game

*~26 min read · Part IV: Engine Breadth · Prerequisites: Modules 01, 02, 07, 10*

---

## Read this first

Rendering gets the glory; gameplay systems get the bug reports. Both JDs list gameplay and simulation as core territory, and a small studio will absolutely expect you to work across it.

**The good news:** this is the part of game programming that most resembles the software engineering you already do — state machines, event systems, data-driven configuration, careful edge cases, and a lot of "what happens if two of these overlap."

**The part that doesn't transfer:** all of it must run inside a few milliseconds, deterministically, every frame (Module 01), and much of it is judged by *feel* rather than correctness. A collision system can be mathematically perfect and feel terrible, and the feel is what ships.

---

## Collision in a voxel world

A general physics engine solves the hard problem: arbitrary convex hulls in arbitrary orientations, with rotation, restitution, friction, and stacking stability. A voxel world hands you a **much easier problem**, and taking that gift is one of the clearest wins available.

### Why the voxel case is easy

**The whole world is an axis-aligned grid.** Therefore:

- **No broad-phase acceleration structure** is needed for world collision — the grid *is* the acceleration structure, with O(1) lookup. (Broad phase = the "which pairs might be touching?" step that a general engine needs a BVH or sweep-and-prune for.)
- **No narrow-phase convex solver** — it's AABB vs. AABB, which is three interval overlap tests.
- **No mesh collision data** — the voxels *are* the collision geometry. Always in sync, free after destruction. In a triangle-mesh game, destructible geometry means regenerating collision meshes, which is a whole subsystem you simply don't have.

**This is the clearest example in the course of "don't copy the industry pattern."** Integrating Rapier or Havok into a voxel game costs you more than writing the 300 lines that do it better.

### Swept AABB against the grid

The core routine. The player is an AABB (axis-aligned box) with a velocity; you need the first blocking contact along the movement.

The robust implementation, and the one you should reach for:

```
for each axis in (y, x, z):        # resolve ONE AXIS AT A TIME
    move the AABB along that axis by velocity[axis] * dt
    compute the integer voxel range the AABB now overlaps
    if any voxel in that range is solid:
        snap the AABB to the boundary of the offending voxel
        velocity[axis] = 0
```

**Axis-separated resolution looks crude and is in fact the standard technique**, because it makes **sliding along walls fall out automatically**: blocking X doesn't stop Y or Z, so a player pressing diagonally into a wall keeps moving along it.

Resolving all axes simultaneously produces the classic **"player sticks on wall corners"** bug: you detect an overlap, you don't know which axis caused it, you push back along the shortest exit vector, and the player grinds to a halt against a flat wall. **Implement the wrong version first so you feel it** — the exercise says to, and it's worth it.

### Order matters

Resolving **Y (gravity) first** means you land on ground before attempting horizontal movement, which prevents catching on the lip of the block you're standing on.

Most implementations converge on Y-then-X-then-Z, or Y-last, depending on the feel they want. **Try both.** This is a feel decision, not a correctness one, and being comfortable saying "I'd try both and pick by feel" is the right instinct for gameplay code.

### Tunneling

Moving fast enough to pass through a wall in one step (Module 01). Two fixes:

1. **Cap velocity** to less than one voxel per fixed step. Simple and predictable, and given a fixed timestep you can compute the exact cap.
2. **Substep** the movement — break a large move into several small ones.

**Given a fixed timestep, capping is simple and predictable.** Use it unless you have a projectile that genuinely needs to move 50 voxels per step, in which case that projectile gets a DDA raycast instead of an AABB sweep.

### Step-up

Walking up single-voxel ledges without jumping. A small, fiddly, **high-impact** feature:

```
1. Attempt the horizontal move.
2. If blocked: try again with the AABB raised by one voxel.
3. If that succeeds AND there's ground beneath the new position: accept it.
4. Smooth the visual Y offset over ~100 ms so the camera doesn't jolt.
```

**Players never notice this working and immediately notice it missing.** Step 4 is the one people skip, and skipping it makes stairs feel like an earthquake.

### Player raycasting

For targeting the block you're looking at — **this is your DDA from Module 08, run on the CPU.**

It returns the hit voxel *and* the face normal, which gives you both:
- **"Which block to break"** → the hit voxel
- **"Where to place the new one"** → hit voxel + normal

Same algorithm, two uses, and the normal you got free from DDA is exactly the piece you need. This is a nice concrete example of the traversal code paying for itself twice.

### Entity-vs-entity collision

Still needs a broad phase, but a **uniform spatial hash** is trivially simple and works extremely well for typical game entity densities:

```ts
const key = (x: number, y: number, z: number) =>
  `${Math.floor(x / CELL)},${Math.floor(y / CELL)},${Math.floor(z / CELL)}`;
// (in a hot path, use a numeric hash instead of a string key — Module 13)

// Insert every entity into its cell; query by checking the 27 surrounding cells.
```

**Reach for a BVH or octree only when you've measured a need.** For a few hundred entities, a spatial hash with a cell size around the largest entity's diameter is both simpler and faster.

---

## Character control feels like nothing else

Movement is where a game is won or lost, and it is **almost entirely a tuning problem** informed by a few known techniques. None of these are in a physics textbook, and all of them are standard.

| Technique | What it does |
|---|---|
| **Acceleration and friction curves** | Not instant velocity. Different values for ground and air. |
| **Coyote time** | Allow a jump for ~100 ms *after* walking off a ledge |
| **Input buffering** | If jump is pressed within ~150 ms *before* landing, execute it on landing |
| **Variable jump height** | Releasing the button early cuts upward velocity |
| **Separate camera and movement smoothing** | Never smooth input latency, only visual position |

**Every one of these is a lie told to make the game feel fair.**

Coyote time exists because players genuinely believe they pressed jump before the edge — and at 60 Hz with human reaction variance, they were right by their own perception. Input buffering exists for the same reason at the other end. Together they make the game agree with the player's memory of what happened.

Collectively they're the difference between "responsive" and "floaty." A game with perfect physics and none of these feels broken; a game with sloppy physics and all of these feels great.

### Expose all of it as data

Movement tuning values belong in a **hot-reloadable config with an in-game debug UI**, because designers will iterate on them a hundred times and every round trip through a code change is wasted (Module 11's iteration-speed thesis, applied).

```ts
// Not constants in a file. A live, editable, hot-reloaded object.
export const movementTuning = {
  groundAccel: 60,  airAccel: 12,
  groundFriction: 14, airFriction: 1,
  jumpVelocity: 9.2, coyoteTimeMs: 100, jumpBufferMs: 150,
  variableJumpCutoff: 0.4, stepUpHeight: 1.0, stepSmoothMs: 100,
};
```

Put sliders on screen. Watch someone else use them. You'll learn more about your movement code in ten minutes than in a week of reading it.

---

## Spatial queries and AI

### The queries gameplay actually needs

| Query | "What's in this explosion radius?" | Use |
|---|---|---|
| **Point / AABB overlap** | ✓ | Spatial hash |
| **Raycast** | Line of sight, targeting, projectile hits | DDA (Module 08) |
| **Nearest-N** | "Closest enemies" | Spatial hash with expanding ring search |
| **Pathfinding** | "How do I get there?" | A* over the voxel grid |

### A* on voxels

Works directly: nodes are voxel positions, neighbours are the 6 (or 26, with diagonals) adjacent cells an agent can occupy, and the cost function includes jump and fall penalties.

> **A\* in one paragraph, if it's been a while:** a best-first search that expands the node minimizing `f = g + h`, where `g` is the actual cost from the start and `h` is a *heuristic* estimate of the remaining cost to the goal (for a grid, Manhattan or Euclidean distance). As long as `h` never *overestimates*, A* finds the optimal path while exploring far fewer nodes than Dijkstra.

The concerns are practical, not algorithmic:

- **Search space is enormous.** A 3D grid has vastly more nodes than a 2D one. **Cap the node budget and fail gracefully** rather than searching forever — an agent that gives up and wanders is better than a 200 ms frame spike.
- **Amortize across frames.** Pathfinding is a job for a worker (Module 10); a request/response queue with results a few frames later is fine and invisible to the player.
- **Hierarchical pathfinding** — path between chunk-level regions first, then refine within regions — is the standard scaling answer, and **voxel chunks give you the hierarchy for free.**
- **Cache and invalidate.** When the player destroys terrain, invalidate paths through the affected region rather than recomputing everything.

### AI behavior

At the scale of a dungeon crawler, simple structures serve well:

| Approach | Good for | Cost |
|---|---|---|
| **Finite state machine** | A handful of states (idle/patrol/chase/attack/flee) | Readable, debuggable, sufficient. Gets tangled past ~8 states |
| **Behavior tree** | A dozen-plus behaviors; designer-authorable | More machinery, composes much better |
| **Utility AI** | Organic-feeling decisions from weighted scoring | Hard to debug — *"why did it do that?"* |
| **GOAP / planners** | Emergent multi-step plans | Usually overkill; famously hard to tune |

**The most valuable thing you can build is not the AI itself but the debug view**: draw each agent's current state, its target, and its path in the world, with a label over its head.

**It converts hours of speculation into seconds of observation.** This is the same instinct as Module 09's debug heatmaps, applied to a different subsystem, and it's a theme worth noticing: *in games, you build the instrument before you build the thing.*

---

## Procedural generation for a roguelike

A roguelike's world is generated, and it must be generated **from a seed, deterministically** (Module 01), so a run can be reproduced, shared, and debugged.

### The layers, in typical order

**1. Noise** — value / Perlin / simplex / OpenSimplex for terrain shape.

> **Fractal Brownian motion (fBm)** is the workhorse: sum several **octaves** of noise, each at double the frequency and half the amplitude of the last. One octave is smooth rolling hills; six octaves is a mountain range with detail at every scale. Two parameters — *lacunarity* (frequency multiplier, usually 2) and *persistence* (amplitude multiplier, usually 0.5) — control the character.

Use **3D noise for caves and overhangs** (sample noise at (x,y,z), carve where it exceeds a threshold) and **2D heightmaps for surfaces** (cheaper, and can't produce overhangs).

**2. Structure placement** — rooms, corridors, prefabs. Common approaches:
- **BSP subdivision** — recursively split the space, put a room in each leaf, connect siblings. Produces orderly, architectural layouts.
- **Random room placement + corridor connection** — scatter non-overlapping rooms, connect with a minimum spanning tree plus a few extra edges for loops.
- **Cellular automata** — start with noise, repeatedly apply "become solid if ≥5 of my neighbours are solid." Produces organic cave systems in a dozen lines.
- **Wave function collapse** — constraint propagation over tile adjacency rules. Beautiful for constrained tile-based layouts, and slow.

**3. Connectivity guarantees** — **the hardest part.** A dungeon that generates an unreachable exit is a run-ending bug that will be reported as "the game is broken."

**Verify reachability with a flood fill as part of generation**, and regenerate or repair when it fails. Do not treat this as a test; treat it as a generation step. (Repair is usually better than regeneration — carve a corridor between the disconnected components rather than throwing away the whole level.)

**4. Decoration and loot** — placement rules, density budgets, pacing.

**5. Player edits** — stored as a sparse diff over the generated base, so an infinite world costs only what players changed (Module 07).

### The rules that make this survivable

**⭐ Determinism through seed derivation.** This is the single most important procgen engineering decision, and **it's the one people most often get wrong.**

```ts
// ❌ WRONG — one global stream shared by every system
const rng = mulberry32(worldSeed);
generateTerrain(rng);   // consumes N values
generateCaves(rng);     // starts wherever terrain left off
generateLoot(rng);      // depends on everything before it
// Add ONE more noise sample to terrain and every cave and every loot roll changes.

// ✅ RIGHT — derive an independent seed per chunk per system
const caveSeed = hash(worldSeed, chunkX, chunkY, chunkZ, PURPOSE_CAVES);
const rng = mulberry32(caveSeed);
```

With derivation, generating chunk (5,7) gives the **same result whether it's generated first or thousandth**, and **adding a new system doesn't perturb existing ones**. Without it, you can never change any generation code without invalidating every existing world — including the one in the bug report you're trying to reproduce.

**Chunk independence.** A chunk's base content should depend **only on its coordinates and the seed**, never on its neighbours' generation state — otherwise generation *order* matters and parallelism dies.

Features that span chunks (trees, buildings, corridors) are handled by inverting the question: instead of "chunk A places a tree that spills into chunk B," each chunk **deterministically computes which features originating in nearby chunks overlap it**. Chunk B checks its 8 neighbours' potential tree positions and renders the overlapping parts itself. Same answer, no ordering dependency.

**Generation belongs on workers**, always (Module 10).

**Build a preview tool.** A CLI or in-editor view that generates and renders a level from a seed, **without launching the game**, turns a 60-second iteration into a 2-second one. Designers will use it constantly, and it's the cheapest tool you'll ever build.

---

## Animation, briefly

Even a voxel game needs motion.

**Skeletal animation** — a hierarchy of bones, per-vertex weights, poses sampled from keyframe tracks and blended, then **skinned** (each vertex transformed by a weighted combination of its bones' matrices). The standard for characters, and it's a real subsystem.

**Voxel-native alternatives** — many voxel games instead animate **rigid parts**. A Minecraft mob is a handful of boxes with independent transforms; there's no skinning at all, just a small transform hierarchy. **Far simpler, matches the aesthetic, and completely avoids the skinning pipeline.** If the art direction permits, take it — and notice that this is another "purpose-built beats copied" case (Module 10).

**Procedural animation** — inverse kinematics for foot placement, spring-damper systems for secondary motion (a lantern swinging, a tail following), look-at for heads. **Cheap and disproportionately effective for a stylized game.** A character with procedural head-tracking and foot IK reads as far more alive than the animation budget suggests.

**Animation samples at render rate, not simulation rate.** It's presentation, not simulation — Module 01's boundary applies. (Unless animation drives gameplay via root motion or animation-driven hitboxes, in which case it must be inside the fixed step and you should know you chose that.)

---

## The systems around the systems

### Events

Gameplay needs decoupling: *"player took damage"* should reach the HUD, the audio system, and the achievement tracker **without the combat code knowing they exist.** A simple typed event bus is enough:

```ts
type GameEvents = {
  damage: { entity: number; amount: number; source: number };
  blockBroken: { x: number; y: number; z: number; id: number };
};
```

Two cautions from experience:

- **Avoid unbounded event chains.** An event handler emitting events that emit events makes ordering unknowable and stack traces useless. Cap the depth or forbid it outright.
- **Process events at a defined point in the frame**, not whenever they're raised — otherwise you'll mutate state mid-iteration and get the classic "collection modified during enumeration" family of bugs. Queue them; drain them at a known point in the fixed step (Module 01's ordering diagram).

### Save/load

With determinism, a save is:

```
{ seed, playerState, sparse world diff, entity states, RNG stream positions }
```

Potentially **kilobytes for a huge world.** That's the payoff for all the determinism discipline.

**Version the format from day one**, and **test the migration path** — breaking saves after launch is one of the worst mistakes a game can make, and it's entirely preventable.

Note the "RNG stream positions": if any system consumes randomness during play (loot rolls, enemy spawns), you must save where each stream is, or a reload diverges.

### Data-driven design

Block types, item stats, enemy definitions, loot tables, and tuning constants belong in **data files with schema validation** (Zod or equivalent), hot reloadable, editable by designers without a build.

**The engineering payoff is that balance iteration stops consuming engineer time entirely.** That's a real, measurable reclaiming of your week, and it's the same Module 11 thesis one more time.

### Time and pausing

Keep **separate clocks**:

| Clock | Pausable | Scalable | Used by |
|---|---|---|---|
| Simulation time | ✅ | ✅ (slow-motion) | Gameplay, physics |
| Real time | ❌ | ❌ | UI animation, networking, music |
| Unscaled time | ✅ | ❌ | Effects that should pause but not slow |

**Systems must be explicit about which they use**, or pausing the game will pause your menus, and slow-motion will make the UI crawl. This bug is universal, obvious in hindsight, and always shipped at least once.

---

## Common confusions

**"I'll use a physics engine so I don't have to write collision."** For a voxel world, the physics engine is *more* code (integration, conversion, sync after destruction) for a *worse* result (non-deterministic, harder to tune, another dependency). Write the 300 lines.

**"My collision is correct, so the movement should feel good."** Feel comes from coyote time, buffering, acceleration curves, and camera smoothing — none of which are correctness. A correct system with no feel work feels bad, reliably.

**"Determinism means I can't use randomness."** It means you can't use *unseeded* randomness, or share streams across systems. Derived per-purpose seeds give you as much randomness as you want, reproducibly.

**"I'll add the reachability check later."** It will surface as an unreproducible player report of a broken run. Make it part of generation, not a test.

**"A* is too slow for a 3D grid."** Unbudgeted A* on a 3D grid is too slow. Budgeted, hierarchical, worker-based, cached A* is fine. The fix is scheduling, not the algorithm.

**"The AI is broken."** Usually the AI is doing exactly what you told it and you can't see the state. Build the debug view first.

---

## The interview answer

***"How would you handle collision in a voxel game?"***

> "Swept AABB against the grid, resolved one axis at a time so sliding along walls falls out naturally and corners don't stick. Y first, so you land before moving horizontally and don't catch on the lip of the block you're standing on. Velocity capped to under a voxel per fixed step to prevent tunneling, plus step-up with a smoothed visual offset so stairs don't jolt the camera.
>
> No general physics engine — the grid is already a perfect broad phase with O(1) lookup, and using voxels directly as collision geometry means destruction stays in sync for free. That's a case where integrating Rapier or Havok would be more code for a worse, less deterministic result.
>
> For entity-vs-entity I'd start with a uniform spatial hash and only go further if measurement said to."

***"How do you keep procedural generation deterministic?"***

> "Derive a separate seed per chunk per system from a hash of the world seed, the chunk coordinates, and a purpose constant — rather than sharing one global stream. If you share a stream, generation order changes results, so you lose reproducibility, you lose parallelism, and adding one new system perturbs every existing world.
>
> Chunks generate independently from coordinates alone. Cross-chunk features like trees and corridors are handled by having each chunk deterministically work out which nearby-origin features overlap it, rather than by chunks writing into each other. And player edits live as a sparse diff over the generated base, so an infinite world costs only what players changed."

***"What makes a character controller feel good?"***

> "Almost none of it is physics. Acceleration and friction curves rather than instant velocity, with different values on ground and in air. Coyote time so a jump pressed just after leaving a ledge still works. Input buffering so a jump pressed just before landing fires on landing. Variable jump height on button release. And smoothing on the camera and visual position but never on input.
>
> Then all of it in a hot-reloadable config with on-screen sliders, because it's a tuning problem and every round trip through a code change is wasted."

---

## Exercise — Voxelforge, Stage 12

**1. Implement swept AABB vs. grid collision** with per-axis resolution. **Deliberately implement the all-axes-at-once version first and feel the corner-sticking bug** — five minutes of frustration teaches the lesson permanently.

**2. Add a first-person controller** with gravity, jump, acceleration/friction, **coyote time**, **input buffering**, and **variable jump height**. **Put every constant in a hot-reloadable config with an on-screen tuning UI.** Then spend twenty minutes just playing with the sliders.

**3. Add step-up** with visual smoothing.

**⭐ 4. Implement block break/place using CPU DDA** — hit voxel for break, hit voxel + face normal for place. **Verify a single edit re-meshes only the affected chunk** *(and its neighbours, if the edit was on a boundary — that's the bug you'll hit, and it's the halo from Module 08 asserting itself)*.

**5. Build seed-derived procedural generation:** fBm terrain, 3D-noise caves, and BSP or cellular-automata rooms, with **per-system seed derivation**. **Write a test asserting that generating chunks in a different order produces identical results.** That test is the whole section, made executable.

**6. Add a flood-fill reachability check** and make generation retry (or repair) when the exit is unreachable.

**7. Add a spatial hash for entities and A\* pathfinding on a worker**, with a debug view drawing agent state and path.

**8. Implement save/load as seed + sparse diff, with a version field.** Save, quit, reload, and verify the world is **bit-identical**.

**Stretch:** build the generation preview CLI — `voxelforge-preview --seed 12345 --out level.png` — and use it to iterate on your dungeon generator without launching the game.

---

## Go deeper

- **Robert Nystrom, *Game Programming Patterns*** — State, Event Queue, Component, Spatial Partition, Service Locator. Free online, and directly applicable to every section above.
- **Glenn Fiedler's gafferongames.com physics series** — integration, collision response, and determinism, written with unusual clarity.
- **Christer Ericson, *Real-Time Collision Detection*** — the reference. You'll use a fraction of it, but the AABB and swept-test chapters are exactly what you need.
- **Amit Patel's Red Blob Games** — A*, pathfinding, hex grids, noise, and procedural generation, all interactive. **The best explanatory site in game development**, full stop.
- **Bob Nystrom, "Rooms and Mazes: A Procedural Dungeon Generator"** — journal.stuffwithstuff.com. Concrete, readable, and directly applicable to a roguelike.
- **"Procedural Content Generation in Games"** (Shaker, Togelius, Nelson) — free at pcgbook.com.
- **Chris Simpson, "Behavior trees for AI: How they work"** — the clearest introduction.
- **GDC talks on game feel** — Steve Swink's *Game Feel* book, and Jan Willem Nijman's **"The Art of Screenshake."** Both explain why the lies in the character-control section work. Nijman's talk is 25 minutes and will change how you look at every game you play.

---

**Next:** [Module 13 — TypeScript and V8 Performance Realities](./13-typescript-v8-performance.md)
