# Module 01 — Real-Time Thinking

### Why a game loop is not an event loop, and why that single difference rewires everything you know about writing software

*~25 min read · Part I: Foundations · Prerequisites: none*

---

## Read this first

You have spent years writing software that **waits**. A request arrives, you do work, you respond. Idle is the default state; work is the exception. Latency budgets are measured in hundreds of milliseconds, and if something takes longer, you add a spinner.

Games invert every one of those assumptions.

> A game is a program that must produce a *complete, correct, novel* output image every 16.6 milliseconds, forever, with no exceptions, while a human watches.

Nothing waits. There is no idle. There is no spinner. If you miss the deadline, the user does not see a loading state — they see a **stutter**, and their hands feel it before their eyes register it.

Here is the same idea as a table, in the terms you already think in:

| | Web service you've built | Game |
|---|---|---|
| Default state | Idle, waiting for a request | Running flat out, always |
| Who sets the deadline | Your SLA (say, p99 < 300 ms) | The physical display, 16.67 ms, non-negotiable |
| Missing the deadline | A slow response; usually invisible | A visible stutter the player physically feels |
| Work arrives | When a client sends it | You generate it yourself, 60 times a second |
| Can you defer work? | Yes — queue it, batch it, cron it | Only across frames, and you must hide the seams |
| Is 99% good? | Yes, that's a great SLO | 1% of frames missed = a hitch every 1.7 seconds |

That last row is the one to sit with. **A 99% success rate, which would be an excellent SLO for a backend service, is a broken game.** At 60 FPS, one percent of frames is 36 dropped frames per minute. Players describe that build as "janky" and stop playing.

This module is about internalizing that constraint, because every other decision in an engine descends from it.

---

## First, what *is* a frame?

Before the budget makes sense, you need to know what physically happens, because the whole discipline is shaped by hardware that predates you by decades.

**Your monitor redraws itself on a fixed schedule.** A 60 Hz display refreshes 60 times per second. It does this whether or not you gave it anything new. Internally it reads a block of memory — the **front buffer**, a big array of pixel colors — from top-left to bottom-right and pushes those values out to the panel. That read-out process is called **scan-out**, and it takes most of the refresh interval.

Now the obvious problem: if your program overwrote that same memory while the display was halfway through reading it, the top half of the screen would show the old image and the bottom half the new one. That visible horizontal seam is called **tearing**.

The fix is **double buffering**: you keep two images. The display scans out the **front buffer** while you draw into the **back buffer**. When you're done, the two swap. The swap happens during the brief gap between refreshes — the **vertical blanking interval**, or "vblank" — so the display never catches you mid-write.

- **VSync** (vertical synchronization) is the rule that says "only swap during vblank." It eliminates tearing. Its cost is that if you're not finished when vblank arrives, you miss the swap entirely and the *old* frame gets shown again — so a frame that took 17 ms on a 60 Hz display doesn't cost you 0.4 ms of lateness, it costs you a full extra refresh. **You effectively drop to 30 FPS.** This cliff edge is why the budget is treated as hard.
- **Triple buffering** adds a third image so you can start the next frame instead of idling while waiting for the swap. It costs memory and a bit of latency and doesn't remove the cliff, it just softens the stall.

> **In the browser you do not control any of this directly.** The browser owns the swap chain. What you control is *whether you finish your work before the deadline*. Everything below is about that.

**The compositor.** Your `<canvas>` is one layer among several the browser has to combine — page content, video, overlays — into the final image. The piece of the browser that does that combining is the **compositor**. It runs on its own thread, on its own schedule, driven by the display's refresh. It's the compositor that decides when your frame gets shown, and it's the compositor that calls your `requestAnimationFrame` callback. So your game loop is not really "yours"; it is a callback the browser invites you to run, once per display refresh, right before it needs your pixels.

---

## The frame budget is the real spec

At 60 frames per second you have **16.67 ms** per frame (1000 ÷ 60). At 120 Hz, **8.33 ms**. At 144 Hz, **6.94 ms**.

That is not "16 ms of your code." That is 16 ms for *everything*:

| Who spends it | What it is |
|---|---|
| Input polling | Reading keyboard/mouse/gamepad state |
| Game simulation | Your actual game rules |
| Animation | Sampling and blending animation data |
| Physics | Integration, collision detection, collision response |
| Culling | Deciding what *not* to draw |
| Command building | Writing the GPU's to-do list (see below) |
| Browser overhead | Layout, GC, extension callbacks, compositing |
| GPU execution | The GPU actually drawing the thing |

Your gameplay logic might get 4 ms of that. Sometimes 2.

### Get the units into your body

Engineers coming from web work often have a fuzzy sense of the scale below one millisecond, because they've never needed it. You need it now.

```
1 second       = 1,000 milliseconds (ms)
1 millisecond  = 1,000 microseconds (µs)
1 microsecond  = 1,000 nanoseconds (ns)
```

So one frame at 60 Hz is **16,670 µs**, or **16,670,000 ns**. That sounds enormous until you count what you're doing 100,000 times.

| Operation | Rough cost | Share of a 60 Hz frame |
|---|---|---|
| L1 cache hit | ~1 ns | 0.00006% |
| L2 cache hit | ~4 ns | negligible |
| Main memory (RAM) miss | ~100 ns | 0.0006% |
| A single JS object allocation | ~20–50 ns | negligible *alone* |
| 100,000 allocations in a frame | ~2–5 ms | **~20% of budget** |
| A minor GC pause | 1–10 ms | **6–60%** |
| A major GC pause | 10–100+ ms | **catastrophic** |
| One WebGPU draw call (CPU side) | ~1–5 µs | 10,000 draws = *over budget* |

Two vocabulary items in that table that you may only half-know:

**Cache and "cache miss."** Your CPU cannot read RAM quickly. To hide that, it keeps small, fast copies of recently-used memory in caches: L1 (tiny, ~1 ns), L2 (small, ~4 ns), L3 (bigger, ~15 ns), then RAM (~100 ns). When the data you asked for isn't in cache, that's a **cache miss** and you eat the full RAM latency. A miss is roughly **100× slower** than a hit. Crucially, the CPU doesn't fetch one value — it fetches a **cache line**, typically 64 bytes. So if your data is laid out so that the *next* thing you need is in the same 64 bytes, you get it free. If your data is scattered across the heap in separate objects, every single access is a miss. This one fact is why game engines obsess over memory layout, and it's the seed of everything in Module 13.

**GC pause.** JavaScript's garbage collector reclaims memory you're no longer using. To do it safely it must, at some point, **stop your code from running** — a "stop-the-world" pause. V8's *minor* GC (the "scavenger," which sweeps the small nursery where new objects are born) is usually 1–10 ms. Its *major* GC (the full old-generation collection) is much bigger, though V8 does most of that work incrementally and concurrently these days. You cannot schedule it, cannot defer it, cannot opt out. **The only lever you have is not producing garbage in the first place.**

The last two rows of the table are the ones that kill web-based engines. In a web app, allocating 100,000 short-lived objects per second is invisible — nobody notices a 5 ms hitch on a page. In a game it is a visible hitch every few seconds, and players will describe your engine as "bad."

**The mental shift:** you stop asking "is this fast enough?" and start asking "what is this costing me out of 16.67 ms, and what am I giving up to pay for it?" Performance stops being an optimization phase and becomes a design constraint, like a type system.

---

## The loop

### Why `requestAnimationFrame` and not `setInterval`

You may reach for `setInterval(frame, 16)` out of habit. Don't:

- `setInterval` isn't aligned to the display refresh, so your frames land at arbitrary points in the refresh cycle and you get uneven pacing even when your code is fast.
- It doesn't know about 120 Hz or 144 Hz displays.
- It keeps firing in a background tab, burning battery on frames nobody sees.
- Timer callbacks get clamped and coalesced by the browser in ways you don't control.

`requestAnimationFrame` (rAF) hands your callback to the compositor, which runs it once per display refresh, just before it needs your pixels. It also gives you a high-resolution timestamp as an argument and stops firing when the tab is hidden. It is the only correct choice.

### The naive loop

```ts
let last = performance.now();

function frame(now: number) {
  const dt = (now - last) / 1000;  // delta time, in SECONDS
  last = now;

  input.poll();
  world.update(dt);
  renderer.render(world);

  requestAnimationFrame(frame);
}
requestAnimationFrame(frame);
```

`dt` ("delta time") is how much real time elapsed since the last frame. Dividing by 1000 converts ms to seconds so that velocities can be written in sane units — `metersPerSecond * dt` gives meters. This is the loop nearly every tutorial shows you.

It is wrong in interesting ways, and understanding *why* is a genuine interview topic.

### Problem 1: a variable `dt` makes your simulation non-deterministic

To move something you **integrate**: you know its velocity, and you want its new position. The simplest way is *explicit Euler integration*:

```ts
position += velocity * dt;
velocity += acceleration * dt;
```

That's not exact — it's an approximation that assumes velocity was constant across the whole step. It wasn't. The error you introduce grows with the size of `dt`.

Watch what happens to a ball falling under gravity (a = −9.8 m/s²) over 1 second of simulated time, starting at rest:

| Step size | Steps | Final position (explicit Euler) |
|---|---|---|
| Exact answer | — | −4.900 m |
| 1/144 s | 144 | −4.866 m |
| 1/60 s | 60 | −4.818 m |
| 1/30 s | 30 | −4.737 m |
| 1/10 s | 10 | −4.410 m |

Same physics, same starting state, five different answers. So on a 144 Hz desktop your character's jump clears a gap; on a 60 Hz laptop it doesn't. That's not a rounding detail — it's a **gameplay difference caused by the player's monitor**.

The pathological version is **tunneling**. Collision detection usually asks "is the object overlapping the wall *right now*?" With a big `dt`, a fast object can be in front of the wall on one step and behind it on the next, never overlapping on any frame you actually checked. It passes straight through. Every "I fell through the floor" bug you've heard about traces back to this.

### Problem 2: `dt` can spike enormously

Alt-tab away for ten seconds and come back, and `dt` is `10.0`. Now you integrate ten seconds of physics in one step. Every object moves 600× further than it should and your entire world teleports through the floor at once.

The same thing happens on a garbage collection pause, a shader compile, a long asset load, or a laptop waking from sleep.

### The fix: fixed timestep with an accumulator

```ts
const STEP = 1 / 60;           // simulation always advances by exactly this
const MAX_FRAME = 0.25;        // clamp: never simulate more than 250 ms of catch-up
let accumulator = 0;           // unspent real time, in seconds
let last = performance.now();

function frame(now: number) {
  // How much real time passed? Clamp it so a huge gap can't detonate the sim.
  let frameTime = Math.min((now - last) / 1000, MAX_FRAME);
  last = now;
  accumulator += frameTime;    // bank the elapsed time

  input.poll();

  // Spend the banked time in fixed-size chunks. May run 0, 1, or several times.
  while (accumulator >= STEP) {
    world.saveStateForInterpolation();  // remember where things were
    world.step(STEP);                   // always exactly STEP. deterministic.
    accumulator -= STEP;
  }

  // Whatever's left over is a fraction of a step: 0.0 .. 1.0
  const alpha = accumulator / STEP;
  renderer.render(world, alpha);        // draw BETWEEN the last two sim states
  requestAnimationFrame(frame);
}
```

#### Trace it by hand

Run that loop on a 100 Hz display (10 ms per frame) with a 60 Hz simulation (16.67 ms per step). Watch the accumulator:

| Frame | frameTime | accumulator before | steps run | accumulator after | alpha |
|---|---|---|---|---|---|
| 1 | 10 ms | 10 ms | 0 | 10.00 ms | 0.60 |
| 2 | 10 ms | 20 ms | 1 | 3.33 ms | 0.20 |
| 3 | 10 ms | 13.3 ms | 0 | 13.33 ms | 0.80 |
| 4 | 10 ms | 23.3 ms | 1 | 6.67 ms | 0.40 |
| 5 | 10 ms | 16.7 ms | 1 | 0.00 ms | 0.00 |

Notice: **some frames run zero simulation steps and some run one.** That is correct and expected. The simulation advances at exactly 60 Hz on average regardless of the display, and the leftover in the accumulator tells the renderer how far between two known states to draw.

If the display were 30 Hz instead, some frames would run *two* steps. Same code, no changes.

#### The three things that code is doing

1. **The fixed `STEP`** makes the simulation deterministic and numerically stable regardless of display refresh rate. Every step is identical in size, forever, on every machine.

2. **The clamp (`MAX_FRAME`)** prevents the **spiral of death**. Here's the failure it stops: a slow frame banks a lot of time, so the loop runs many catch-up steps, which makes that frame take even longer, which banks even more time, which means even more steps next frame. The loop never catches up and the game hangs. With the clamp, the worst case is that the game briefly runs in slow motion — the simulation clock falls behind the wall clock and never recovers that lost time. **That is the right trade.** A moment of slow motion is survivable; a hang is not.

3. **The `alpha` interpolation** decouples render rate from simulation rate. To render, you blend the previous and current simulation states:

   ```ts
   // "lerp" = linear interpolation: walk from a to b by fraction t
   const lerp = (a: number, b: number, t: number) => a + (b - a) * t;

   renderX = lerp(entity.previousX, entity.currentX, alpha);
   ```

   Rendering at 144 Hz off a 60 Hz simulation looks smooth because you draw *between* two states you have already computed, rather than extrapolating forward into a state you're guessing at. (Extrapolation is also a technique, but it can overshoot and then visibly snap back when it's wrong. Interpolation never overshoots. Its cost is one step of latency, since you're always drawing slightly in the past.)

> Rendering shows a lie — an interpolated state that never actually existed in the simulation. That lie is what makes motion look smooth. This is a foundational pattern, not a hack.

**Which values do you interpolate?** Positions and rotations, yes. Discrete state — "is this door open," "how much health" — no; just read the current value. And rotations must be interpolated as **quaternions with slerp/nlerp**, not as three separate Euler angles, or you get wobble. (Module 02 covers why.)

### Where systems go in the loop

Order matters and is a source of subtle bugs that are miserable to diagnose. A typical ordering:

```
poll input
  → fixed steps:
      apply input to intents      ← "player is holding forward" → "player wants to move +Z"
      AI / behavior               ← NPCs decide what they want, same shape as player intent
      physics integrate           ← apply velocities, gravity; move everything
      collision detect + resolve  ← find overlaps, push things apart
      gameplay reactions          ← damage, triggers, pickups, events fired by the above
  → animation sampling            ← at RENDER rate, not sim rate: it's presentation
  → camera update                 ← LAST, so it sees final positions
  → culling                       ← throw away what the camera can't see
  → build render commands         ← write the GPU's to-do list
  → submit                        ← hand it over
```

A few of these deserve explanation:

- **"Apply input to intents"** means translating raw device state into abstract desires *before* anything acts on them. The rest of your code reads `intent.moveForward`, never `keyboard.isDown('W')`. This is what lets you add gamepad support, replay a recorded input log, and let AI drive the same character controller — all without touching gameplay code. It's the same instinct as not scattering `req.body` reads through your service layer.

- **Animation at render rate** because animation is *presentation*, not simulation. It doesn't change gameplay outcomes, so it shouldn't be locked to the sim clock — and sampling it at display rate makes it smoother for free. (The exception is when animation drives gameplay, e.g. root motion or attack hitboxes coming from bone positions. Then it must be inside the fixed step, and you should know you've made that choice deliberately.)

- **Culling** is deciding what not to draw. The cheapest triangle is the one you never submit. Module 09 covers this properly.

- **Camera last** is not arbitrary. If the camera updates before the player moves, the camera is showing you a viewpoint computed from last frame's player position — it lags one frame behind, permanently. Players *feel* that as sluggishness even when they cannot name it. This is one of those bugs that generates the feedback "the controls feel bad" with no further detail, and it's a one-line fix once you know to look.

---

## Frame pacing: smoothness ≠ average FPS

A build that renders 60 frames in one second is not necessarily smooth. If those frames arrive at intervals of 5, 5, 5, 40, 5, 5 ms, the average is fine and the experience is terrible — the player sees a jolt every time that 40 ms frame lands.

**Frame time consistency matters more than frame time average.**

### Why FPS is a bad unit

FPS is the reciprocal of frame time, and reciprocals are nonlinear. That makes FPS deltas meaningless without context:

| Change | Frame time change | Actual work you must remove |
|---|---|---|
| 60 → 50 FPS | 16.7 → 20.0 ms | 3.3 ms |
| 30 → 25 FPS | 33.3 → 40.0 ms | 6.7 ms |
| 120 → 110 FPS | 8.3 → 9.1 ms | 0.8 ms |

Three identical-looking "10 FPS drops," three completely different amounts of work. Worse, the savings don't add: shaving 2 ms is 2 ms whether you're at 60 FPS or 144 FPS, but the FPS number it produces looks wildly different.

**Always think and measure in milliseconds, never in FPS.** "We're at 12 ms and the budget is 16.6" is a sentence an engine programmer says. "We're at 83 FPS" is a sentence a benchmark reviewer says. Interviewers notice which one you use.

### Percentiles, and "1% lows"

Because the bad frames are the whole problem, games profile with distributions rather than averages:

- **p50 / median frame time** — the typical frame.
- **p99 frame time** — the frame time that 99% of frames come in under. This is your hitchiness number. If p50 is 11 ms and p99 is 45 ms, you have a smoothness problem no average will reveal.
- **"1% lows"** — the same idea expressed as FPS by the PC gaming community: take your worst 1% of frames and report their average as an FPS number. Same measurement, worse unit.

A **frame time graph** — milliseconds on Y, frame index on X — is the single most useful visualization in the field. Spikes are visible instantly, and their *rhythm* tells you the cause: perfectly periodic spikes suggest a scheduled system (a save, a chunk rebuild, an occlusion pass); irregular ones suggest GC or asset streaming.

---

## CPU and GPU run in parallel, and this confuses everyone

Here is the single most common mental-model error for newcomers:

```
Your code:   [--- frame N build ---][--- frame N+1 build ---]
GPU:                                [--- frame N execute ---]
```

### What actually happens when you "draw"

In a modern graphics API you never say "draw this now." You **record** work into a list and then hand the whole list over. Concretely, in WebGPU:

```ts
const encoder = device.createCommandEncoder();      // start recording
const pass = encoder.beginRenderPass({ /* ... */ }); // "I'm drawing into this texture"
pass.setPipeline(pipeline);                          // which shaders + state
pass.setBindGroup(0, bindGroup);                     // which resources they can see
pass.draw(3);                                        // record: draw 3 vertices
pass.end();
const commands = encoder.finish();                   // bake the list
device.queue.submit([commands]);                     // hand it to the driver
```

- A **command encoder** is the recorder. Calling methods on it appends instructions to a list; nothing executes.
- A **command buffer** is the finished list — the baked output of `encoder.finish()`. It's the exact analogue of writing a shell script instead of typing commands interactively.
- `queue.submit()` hands that script to the GPU's driver, which will get to it. It returns immediately.

**When `submit` returns, nothing has rendered.** The GPU may not have started. It is very likely still finishing *last* frame's list while your CPU builds this frame's. That is intentional — it's how you keep two expensive processors busy at once. This design is called **pipelining**, and it's the reason a well-built engine is never waiting on itself.

### Four consequences you must know

**1. You cannot read GPU results back this frame without stalling.**
Suppose a compute shader computed something and you want the number on the CPU. To see it you must map the buffer into CPU-visible memory, which requires the GPU to have finished writing it — so you wait. That wait is a **sync point** (also called a "stall" or "bubble"): the CPU sits idle, the pipeline drains, and you can lose several frames. Real engines therefore read results back **2–3 frames late** and design systems that tolerate stale data. GPU-driven occlusion culling, for instance, culls this frame using *last* frame's visibility results, and accepts the occasional wrong answer.

**2. "Frame time" is really two numbers.**
There is CPU frame time (how long your code took to build the list) and GPU frame time (how long the GPU took to execute it). **You are bound by the larger one.** If you're GPU-bound at 20 ms and you spend a week optimizing CPU code from 8 ms to 4 ms, your frame rate does not move at all. Figuring out which one you're bound by, *before* optimizing, is the single most important diagnostic skill in graphics work. Module 09 is largely about this.

**3. Latency has a floor.**
The chain from a physical action to a photon is: input device polls → OS delivers the event → your loop reads it → simulation runs → commands are built → GPU executes → compositor composites → display scans it out. Even a perfect engine is **2–4 frames** from click to photon, which is 33–67 ms at 60 Hz. This is why competitive players buy 240 Hz monitors — not because they see 240 distinct images, but because every stage of that chain shrinks.

**4. Resources you write must not be in use.**
If the GPU is still reading a uniform buffer while executing frame N, you cannot overwrite it with frame N+1's data — you'd corrupt a frame that's mid-flight.

> **What's a uniform buffer?** A small block of GPU memory holding values that are *uniform* across a draw — the camera matrices, the current time, a light's color. Every shader invocation in that draw reads the same values. Contrast with a *vertex buffer*, where each vertex reads its own distinct element.

Engines solve the in-use problem with **ring buffers** (also called *n-buffering* or *frames in flight*): allocate 2 or 3 sets of per-frame resources and rotate through them. Frame N writes set 0, frame N+1 writes set 1, frame N+2 writes set 0 again — by which point the GPU has certainly finished with it.

```ts
const FRAMES_IN_FLIGHT = 3;
const uniformBuffers = [b0, b1, b2];
let frameIndex = 0;

function frame() {
  const buf = uniformBuffers[frameIndex % FRAMES_IN_FLIGHT];
  // ... write camera matrices into buf, use it this frame ...
  frameIndex++;
}
```

WebGPU hides some of this from you — `queue.writeBuffer()` does its own internal staging and versioning so a naive write is safe. But the concept resurfaces the moment you do anything advanced (persistently-mapped buffers, GPU-driven rendering, readback), and interviewers will expect you to know the shape of the problem even if the API papered over it.

---

## Determinism, and why it is worth money

A simulation is **deterministic** if the same initial state plus the same input sequence always yields exactly the same result — bit for bit, every time, on every machine.

That's a strong property, and it buys you a surprising amount:

- **Replays that are a list of inputs** (kilobytes) rather than recorded video (gigabytes). You re-run the simulation from the same seed with the same inputs and it reproduces itself.
- **Reproducible bug reports** — "here's the 4 KB input log" instead of "it happened once, I think I was jumping."
- **Cheap save systems and rewind mechanics** — you can re-simulate rather than store.
- **Lockstep networking**, where clients exchange only inputs and each simulates the identical world. (Not needed for a single-player roguelike, but it's the technique behind most RTS netcode, and knowing why it demands determinism is good interview material.)
- **Procedural generation that regenerates identically from a seed.** Directly relevant to a roguelike: a dungeon should be reconstructible from a seed rather than stored, so a save file is a seed plus a diff rather than a megabyte of level data.

### What breaks determinism

| Enemy | Why | Fix |
|---|---|---|
| Variable timestep | Different step sizes → different integration results | Fixed timestep (above) |
| Unseeded `Math.random()` | Different every run, and V8 doesn't let you seed it | Own your PRNG (below) |
| Wall-clock time in sim code | `Date.now()` inside a step makes the result time-dependent | Simulation gets a tick count, never a clock |
| Unstable iteration order | Different order → different float accumulation order → different results | Iterate arrays, not object keys |
| Transcendental functions | `Math.sin`, `Math.cos`, `Math.pow` are **not** specified to bit-exactness and genuinely differ between JS engines and versions | Use lookup tables or your own polynomial approximation if you need cross-engine determinism |
| Float32 vs Float64 mixing | JS numbers are float64; `Float32Array` stores float32; rounding differs | Be consistent; `Math.fround(x)` forces float32 rounding explicitly |
| Multithreaded accumulation | Workers finishing in a different order changes summation order, and float addition is not associative | Deterministic reduction order, or keep it single-threaded |

Two JS-specific notes that trip people up:

**Iteration order is actually fine in JS**, unlike C++. `Map` and `Set` iterate in insertion order by spec; arrays are arrays. Plain objects are *mostly* insertion-ordered too, with one gotcha: integer-like keys (`"0"`, `"42"`) always come first in ascending numeric order regardless of when you added them. So `{b: 1, 2: 2, a: 3}` iterates as `2, b, a`. If you're keying entities by numeric ID on a plain object, your iteration order silently depends on your ID values. Use an array or a `Map`.

**Floating point is deterministic *for the same operations in the same order*.** IEEE-754 `+`, `-`, `*`, `/`, and `sqrt` are exactly specified — same inputs give the same bits on any conforming machine. What's *not* specified is the transcendental library (`sin`, `cos`, `exp`, `pow`), and what breaks in practice is order of operations, because float addition isn't associative: `(a+b)+c` can differ from `a+(b+c)`. So determinism in JS is achievable, which is not true in every language, and that's a genuinely nice thing about your platform.

### A seeded PRNG you own

This is four lines and you should be able to write it from memory:

```ts
// mulberry32 — small, fast, statistically decent. Returns [0, 1).
function mulberry32(seed: number) {
  return function random(): number {
    seed |= 0;
    seed = (seed + 0x6D2B79F5) | 0;
    let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

const rng = mulberry32(12345);
rng(); // → 0.6789... same value, every run, every machine
```

(`Math.imul` is 32-bit integer multiplication — needed because normal JS `*` on large integers loses precision by going through float64. `>>> 0` coerces to unsigned 32-bit.)

Give each *system* its own stream seeded from the master seed — dungeon layout, loot rolls, and particle jitter should each have their own generator. Otherwise adding one particle effect changes how many times the shared generator was called, which shifts every subsequent roll, and your dungeon layout changes because you added a spark. This mistake is extremely common and extremely annoying to debug.

### The one architectural rule

**Never let render-only code write back into simulation state.** A hard wall between "simulation state" (authoritative, deterministic, stepped at fixed rate) and "presentation state" (interpolated, derived, thrown away every frame) is one of the highest-value architectural decisions in an engine. Once that wall has a hole in it — a particle system that nudges a position, a camera shake that moves the player — determinism is gone and you will not find out for weeks.

---

## What this means for a browser-based engine

Web-specific realities you will be expected to know for a WebGPU engine role:

- **`requestAnimationFrame` is driven by the compositor** and aligns to the display refresh (VSync). You do not choose your frame rate; the display does. There is no `setTargetFPS`.

- **The main thread is shared** with the browser's own work: layout, style recalculation, GC, extension callbacks, and event dispatch. Anything heavy belongs on a **Web Worker** — meshing, pathfinding, asset decoding, procedural generation.

- **`SharedArrayBuffer` requires cross-origin isolation.** `SharedArrayBuffer` is the only way to have a worker and the main thread read the *same* memory rather than copies. It's disabled by default (a Spectre mitigation) and re-enabled only if your server sends two headers:

  ```
  Cross-Origin-Opener-Policy: same-origin
  Cross-Origin-Embedder-Policy: require-corp
  ```

  Without it, workers can only exchange data by structured-clone copying, or by *transferring* an `ArrayBuffer` (which is zero-copy but hands over ownership — the sender's copy becomes detached and unusable). That changes your entire threading architecture: with `SharedArrayBuffer` you can have a worker write meshes directly into a shared pool; without it you're ping-ponging ownership of buffers. Also note that `require-corp` means every cross-origin resource you load must opt in via CORS or CORP headers, which can break third-party embeds and analytics. **This is a real, early, hard-to-reverse engine decision**, and it's a good thing to raise unprompted in an interview.

- **The tab can be throttled or suspended.** Background tabs may drop rAF to 1 Hz or stop it entirely. Another argument for the `MAX_FRAME` clamp — and a reason to handle the `visibilitychange` event explicitly rather than letting a 30-second gap hit your accumulator.

- **Garbage collection is not yours to schedule.** You cannot force it, defer it, or opt out. You can only avoid feeding it. This is why engines written in JS/TS end up allocation-averse to a degree that looks pathological to web developers — object pools, preallocated scratch vectors, typed arrays instead of object arrays. Module 13 covers the techniques; this module is just establishing *why*.

---

## Common confusions

**"I'll just cap the frame rate to 30 to be safe."** Capping is a legitimate tool, but it doesn't fix pacing — it just moves the deadline. A build with a 25 ms average and 60 ms spikes is still janky at 30 FPS. Fix the spikes first.

**"My frame rate is fine in dev."** You are running on a developer machine with a discrete GPU, a warm cache, and no other tabs. Integrated GPUs are roughly 5–10× slower and share bandwidth with the CPU. Test on the worst hardware you intend to support, early.

**"Chrome DevTools says my JS is only taking 3 ms, so I'm fine."** DevTools measures your JavaScript. It does not, by default, tell you the GPU took 22 ms. If your JS is fast and your frame rate is still bad, you are GPU-bound and looking at the wrong instrument entirely.

**"The accumulator loop runs the simulation faster on faster machines."** No — it runs the *same* number of simulation steps per second of wall-clock time on every machine. Faster machines just render more frames between steps, with more accurate interpolation. That's the entire point.

**"Interpolation adds input lag, so I'll extrapolate instead."** Interpolation does cost you up to one simulation step of latency (~16.7 ms). Extrapolation removes that but guesses at the future, and visibly snaps when the guess is wrong — most noticeably on collisions and direction changes, which is exactly where the player is looking. Nearly every shipped engine chooses interpolation. Choose it too, and know the trade-off so you can explain it.

---

## The interview answer

If asked *"walk me through a game loop"*, a strong answer hits these beats in about 90 seconds:

> "Fixed timestep for simulation with an accumulator, clamped so a long frame can't spiral. Render at display rate, interpolating between the last two simulation states with the leftover accumulator as alpha. That gives determinism in the sim and smoothness in presentation independently. Camera updates after simulation so it isn't a frame behind. And I'd keep in mind the CPU is building frame N+1 while the GPU executes N, so any readback is at least a couple of frames late and per-frame resources need to be ring-buffered."

Why each beat lands:

| Beat | What it signals |
|---|---|
| "Fixed timestep with an accumulator" | You know the standard solution, not just the naive loop |
| "Clamped so it can't spiral" | You've thought about the failure mode, not just the happy path |
| "Interpolating with alpha" | You understand sim/render decoupling — this is the part beginners miss |
| "Camera after simulation" | You've actually shipped something and felt this bug |
| "CPU builds N+1 while GPU executes N" | You understand the hardware, not just the API |
| "Readback is a couple frames late" | Real graphics experience; almost nobody says this unprompted |

If asked *"the game runs at 60 FPS but feels janky"*, the answer is: **average FPS is the wrong metric — show me the frame time graph and the p99.** Then diagnose whether the spikes correlate with GC, asset streaming, shader compilation, or a periodic system (a chunk rebuild, a save, an occlusion pass). Saying "show me the frame time graph" is itself the signal; it's what a graphics programmer reaches for first.

---

## Exercise — Voxelforge, Stage 1

Throughout this course you will build **Voxelforge**, a small TypeScript + WebGPU voxel renderer. Stage 1 has no graphics at all — it's the loop and the discipline.

**1. Build the loop.** Fixed 60 Hz accumulator, 250 ms clamp, alpha interpolation. Structure it so `world.step(dt)` cannot see the wall clock — pass it only the step size and a tick counter.

**2. Simulate 10,000 bouncing points** in a box, using your own seeded PRNG for initial positions and velocities. Deliberately store them as a plain array of `{x, y, z, vx, vy, vz}` objects — the naive layout. You'll fix this in Module 13 and want the "before" measurement.

**3. Render nothing.** Log a rolling frame time histogram to the console every second: min, mean, p50, p99, max. Write the percentile function yourself; it's five lines and you'll use it constantly.

**4. Add an artificial hitch.** Allocate 200,000 temporary `{x, y, z}` objects per frame and throw them away. Watch p99 explode while the *mean* barely moves — this is the whole lesson of the frame pacing section, demonstrated on your own machine. Then remove it by preallocating and reusing, and confirm p99 comes back down.

**5. Verify determinism.** Run the simulation for 600 fixed steps twice from the same seed, and assert the final positions are **bit-identical** (compare with `Object.is` or bit patterns via a `Float64Array`, not `Math.abs(a-b) < 0.001` — an approximate check will hide exactly the bug you're looking for).

**⭐ Step 5 teaches the most. If it fails, find out why before moving on.** The usual culprits: you touched `Math.random()` somewhere, you iterated a plain object with numeric keys, or a "harmless" bit of render code wrote back into simulation state.

**Stretch:** graph the frame times to a `<canvas>` instead of the console. You'll want a frame time graph in every project you ever build, and building it once means you always have it.

---

## Go deeper

- **Glenn Fiedler, "Fix Your Timestep!"** — gafferongames.com. The canonical article on this topic, and short. Read it twice; the second read lands differently.
- **Robert Nystrom, *Game Programming Patterns*** — free at gameprogrammingpatterns.com. Read the "Game Loop," "Update Method," and "Double Buffer" chapters now; the rest later.
- **Jason Gregory, *Game Engine Architecture* (3rd ed.)** — Chapter 8, "The Game Loop and Real-Time Simulation." The industry's standard reference.
- **Chrome DevTools Performance panel** — learn to read a flame chart with the frame track visible. You will live here. Turn on "Screenshots" and "Memory" and watch GC sawtooth against your frame spikes.
- **`chrome://tracing`** (or the newer Perfetto UI) when DevTools isn't enough — it shows the compositor and GPU process threads that DevTools hides.

---

**Next:** [Module 02 — 3D Math and the Chain of Spaces](./02-3d-math-and-spaces.md)
