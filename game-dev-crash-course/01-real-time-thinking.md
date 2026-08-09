# Module 01 — Real-Time Thinking

### Why a game loop is not an event loop, and why that single difference rewires everything you know about writing software

*~10 min read · Part I: Foundations · Prerequisites: none*

---

You have spent years writing software that waits. A request arrives, you do work, you respond. Idle is the default state; work is the exception. Latency budgets are measured in hundreds of milliseconds, and if something takes longer, you add a spinner.

Games invert every one of those assumptions.

> A game is a program that must produce a *complete, correct, novel* output image every 16.6 milliseconds, forever, with no exceptions, while a human watches.

Nothing waits. There is no idle. There is no spinner. If you miss the deadline, the user does not see a loading state — they see a **stutter**, and their hands feel it before their eyes register it. This module is about internalizing that constraint, because every other decision in an engine descends from it.

---

## The frame budget is the real spec

At 60 frames per second you have **16.67 ms** per frame. At 120 Hz, **8.33 ms**. At 144 Hz, **6.94 ms**.

That is not "16 ms of your code." That is 16 ms for *everything*: input polling, game simulation, animation, physics, culling, building command buffers, the browser compositing your canvas, and the GPU finishing its own work. Your gameplay logic might get 4 ms of it.

Internalize the scale:

| Operation | Rough cost | Frames at 60 Hz |
|---|---|---|
| L1 cache hit | ~1 ns | 0.00006% |
| Main memory (RAM) miss | ~100 ns | 0.0006% |
| A single JS object allocation | ~20–50 ns | negligible *alone* |
| 100,000 allocations in a frame | ~2–5 ms | **~20% of budget** |
| A minor GC pause | 1–10 ms | **6–60%** |
| One WebGPU draw call (CPU side) | ~1–5 µs | 10,000 draws = *over budget* |

The last two rows are the ones that kill web-based engines. In a web app, allocating 100,000 short-lived objects per second is invisible. In a game, it is a visible hitch every few seconds when the garbage collector runs.

**The mental shift:** you stop asking "is this fast enough?" and start asking "what is this costing me out of 16.67 ms, and what am I giving up to pay for it?" Performance stops being an optimization phase and becomes a design constraint, like a type system.

---

## The loop

The simplest game loop looks like this:

```ts
function frame(now: number) {
  const dt = (now - last) / 1000;
  last = now;

  input.poll();
  world.update(dt);
  renderer.render(world);

  requestAnimationFrame(frame);
}
requestAnimationFrame(frame);
```

This is wrong in interesting ways, and understanding *why* is a genuine interview topic.

### Problem 1: variable `dt` makes simulation non-deterministic

If physics advances by whatever `dt` happened to be, then the same inputs produce different outcomes on a 60 Hz laptop and a 144 Hz desktop. A jump that clears a gap on one machine misses it on another. Numerical integration error scales with step size, so large frames produce *different physics*, not just slower physics — tunneling through walls is the classic symptom.

### Problem 2: `dt` can spike

Alt-tab away for ten seconds and `dt` is 10.0. Now you integrate ten seconds of physics in one step and every object in your world teleports through the floor.

### The fix: fixed timestep with an accumulator

```ts
const STEP = 1 / 60;           // simulation runs at a fixed rate
const MAX_FRAME = 0.25;        // clamp: never simulate more than 250ms of catch-up
let accumulator = 0;

function frame(now: number) {
  let frameTime = Math.min((now - last) / 1000, MAX_FRAME);
  last = now;
  accumulator += frameTime;

  input.poll();

  while (accumulator >= STEP) {
    world.saveStateForInterpolation();
    world.step(STEP);          // always exactly STEP. deterministic.
    accumulator -= STEP;
  }

  const alpha = accumulator / STEP;
  renderer.render(world, alpha); // interpolate between previous and current state
  requestAnimationFrame(frame);
}
```

Three things are happening here, and each solves a distinct problem:

1. **The fixed `STEP`** makes simulation deterministic and stable regardless of display refresh rate.
2. **The clamp (`MAX_FRAME`)** prevents the "spiral of death," where a slow frame causes more catch-up steps, which makes the next frame slower, which causes more steps. Better to run in slow motion for a moment than to lock up.
3. **The `alpha` interpolation** decouples render rate from simulation rate. Rendering at 144 Hz off a 60 Hz simulation looks smooth because you draw *between* two known-good simulation states rather than extrapolating into an unknown one.

> Rendering shows a lie — an interpolated state that never actually existed in the simulation. That lie is what makes motion look smooth. This is a foundational pattern, not a hack.

### Where systems go in the loop

Order matters and is a source of subtle bugs. A typical ordering:

```
poll input
  → fixed steps:
      apply input to intents
      AI / behavior
      physics integrate
      collision detect + resolve
      gameplay reactions (damage, triggers, events)
  → animation sampling (render rate, not sim rate)
  → camera update (last, so it sees final positions)
  → culling
  → build render commands
  → submit
```

Camera last is not arbitrary: if the camera updates before the player moves, the camera lags one frame behind, and players *feel* that as sluggishness even when they cannot name it.

---

## Frame pacing: smoothness ≠ average FPS

A build that renders 60 frames in one second is not necessarily smooth. If those frames arrive at intervals of 5, 5, 5, 40, 5, 5 ms, the average is fine and the experience is terrible.

**Frame time consistency matters more than frame time average.** This is why profiling in games uses frame time graphs and percentiles (99th percentile frame time, "1% lows") rather than mean FPS. FPS is a nonlinear, misleading unit: going 60→50 FPS costs 3.3 ms, while 30→25 FPS costs 6.7 ms — the same "10 FPS" drop, twice the damage.

**Always think and measure in milliseconds, never in FPS.** Saying "we're at 12 ms and the budget is 16.6" is a sentence an engine programmer says. "We're at 83 FPS" is a sentence a benchmark reviewer says.

---

## CPU and GPU run in parallel, and this confuses everyone

Here is the single most common mental-model error for newcomers:

```
Your code:   [--- frame N build ---][--- frame N+1 build ---]
GPU:                                [--- frame N execute ---]
```

When you call `queue.submit(commandBuffer)`, **nothing renders**. You have handed a to-do list to a device that will get to it. The GPU is typically working on the *previous* frame while the CPU builds the current one. That is intentional — it's how you keep both processors busy.

Consequences you must know:

- **You cannot read back GPU results this frame without stalling.** Mapping a buffer to read it forces a sync point that can cost you multiple frames of latency. Real engines read results back 2–3 frames late and design around it.
- **"Frame time" is really two numbers.** CPU frame time and GPU frame time. You are bound by the larger one. Optimizing the wrong one changes nothing — a critical diagnostic skill covered in Module 09.
- **Latency has a floor.** Input → simulation → command building → GPU execution → compositor → display scan-out. Even a perfect engine is 2–4 frames from click to photon.
- **Resources you write must not be in use.** If the GPU is still reading a uniform buffer from frame N, you cannot overwrite it for N+1. Engines solve this with **ring buffers** (typically 2–3 sets of per-frame resources, rotated). WebGPU hides some of this from you via `writeBuffer` staging, but the concept resurfaces the moment you do anything advanced.

---

## Determinism, and why it is worth money

A simulation is **deterministic** if the same initial state plus the same input sequence always yields the same result. Determinism buys you:

- **Replays** that are a list of inputs (kilobytes) rather than recorded video (gigabytes)
- **Reproducible bug reports** — "here's the input log" instead of "it happened once"
- **Cheap save systems** and rewind mechanics
- **Lockstep networking** if you ever need it
- **Procedural generation that regenerates identically** from a seed — directly relevant to a roguelike, where a dungeon must be reconstructible from a seed rather than stored

The enemies of determinism: variable timesteps, iteration order over hash maps, `Math.random()` without a seeded PRNG, floating-point differences across platforms, and reading wall-clock time inside simulation code.

Practical rules: use a **seeded PRNG** you own (xorshift/PCG are a few lines), keep simulation order stable and explicit, and never let render-only code write back into simulation state. That last rule — a hard wall between "simulation state" and "presentation state" — is one of the highest-value architectural decisions in an engine.

---

## What this means for a browser-based engine

A few web-specific realities you will be expected to know:

- **`requestAnimationFrame` is driven by the compositor**, and it aligns with display refresh (VSync). You do not choose your frame rate; the display does.
- **The main thread is shared** with the browser's own work: layout, GC, extension callbacks. Heavy work belongs on **Web Workers**.
- **`SharedArrayBuffer` requires cross-origin isolation** (`COOP`/`COEP` headers). Without it, workers can only exchange messages by copying or transferring, which changes your entire threading architecture. Getting these headers right is a real, early engine decision.
- **The tab can be throttled or suspended.** Background tabs may drop to 1 Hz or stop entirely — another argument for the `MAX_FRAME` clamp.
- **Garbage collection is not yours to schedule.** You cannot force it, you cannot defer it, you can only avoid feeding it. This is why engines written in JS/TS end up allocation-averse to a degree that looks pathological to web developers (covered in depth in Module 13).

---

## The interview answer

If asked *"walk me through a game loop"*, a strong answer hits these beats in about 90 seconds:

> "Fixed timestep for simulation with an accumulator, clamped so a long frame can't spiral. Render at display rate, interpolating between the last two simulation states with the leftover accumulator as alpha. That gives determinism in the sim and smoothness in presentation independently. Camera updates after simulation so it isn't a frame behind. And I'd keep in mind the CPU is building frame N+1 while the GPU executes N, so any readback is at least a couple of frames late and per-frame resources need to be ring-buffered."

If asked *"the game runs at 60 FPS but feels janky"*, the answer is: **average FPS is the wrong metric — show me the frame time graph and the 99th percentile.** Then diagnose whether spikes correlate with GC, asset streaming, shader compilation, or a periodic system (a chunk rebuild, a save, an occlusion pass).

---

## Exercise — Voxelforge, Stage 1

Throughout this course you will build **Voxelforge**, a small TypeScript + WebGPU voxel renderer. Stage 1 has no graphics at all.

1. Build a loop with a fixed 60 Hz accumulator, a 250 ms clamp, and alpha interpolation.
2. Simulate 10,000 bouncing points in a box using a seeded PRNG for initial positions. Store them in a plain array of objects.
3. Render nothing — just log a rolling frame time histogram to the console (min / mean / p99).
4. Now add an artificial hitch: allocate 200,000 temporary `{x,y,z}` objects per frame. Watch p99 explode. Remove it by pre-allocating and reusing.
5. Verify determinism: run the sim for 600 fixed steps twice from the same seed and assert the final positions are bit-identical.

That last step is the one that teaches the most. If it fails, find out why.

---

## Go deeper

- **Glenn Fiedler, "Fix Your Timestep!"** — gafferongames.com. The canonical article on this topic. Read it twice.
- **Robert Nystrom, *Game Programming Patterns*** — free at gameprogrammingpatterns.com. Read the "Game Loop," "Update Method," and "Double Buffer" chapters now; the rest later.
- **Jason Gregory, *Game Engine Architecture* (3rd ed.)** — Chapter 8, "The Game Loop and Real-Time Simulation." The industry's standard reference.
- **Chrome DevTools Performance panel** — learn to read a flame chart with the frame track visible. You will live here.

---

**Next:** [Module 02 — 3D Math and the Chain of Spaces](./02-3d-math-and-spaces.md)
