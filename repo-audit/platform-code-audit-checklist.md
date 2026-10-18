# Code Audit Guide

**Round:** Refactoring LLM Assessment (first technical round, described as "the most important")
**Format:** 90 minutes · your own machine · pnpm TypeScript monorepo (full stack) · AI tools allowed
**Deliverable:** atomic commits, one per issue, each with a written justification
**Source:** *Platform Team – Interview Prep Guide*
**Revised 2026-10-16** after a gap review. Sources and confidence levels: 

> **What candidates report:** the repo is **large**, there are "several tasks", it's **proctored (camera + screen recording)**, and people run out of time ("two tasks, half done"). Plan for that: read with grep rather than file by file, keep AI verification time-boxed, and write findings down as you go.

---

## Contents

1. [What they are scoring and what each criterion looks like](#1-what-they-are-scoring)
2. [Setup checklist (do this 2+ days before)](#2-setup-checklist)
3. [The 90-minute game plan](#3-the-90-minute-game-plan)
4. [Using AI during the assessment](#4-using-ai-during-the-assessment)
5. [The audit checklist, by category](#5-the-audit-checklist)
   - 5.1 Security (+ Firebase, LLM/RAG) · 5.2 Performance (+ how to prove it) · 5.3 Reliability · 5.4 Developer tooling and CI · 5.5 Tests · 5.6 API design
6. [Migration patterns: fixes that can't ship in one step](#6-migration-patterns)
7. [Writing it up: commits, justifications, FINDINGS.md](#7-writing-it-up)
8. [Practice exercises (answers hidden)](#8-practice-exercises)
9. [Red flags to avoid](#9-red-flags-to-avoid)
10. [Prep schedule and day-of checklist](#10-prep-schedule-and-day-of-checklist)
11. [If the format differs: LRU cache and SSML parser drill](#11-if-the-format-differs)
12. [The feedback stage](#12-the-feedback-stage)

---

## 1. What they are scoring

The guide lists five criteria. Here is what each one looks like in practice.

| Criterion | What it means | What to do |
|---|---|---|
| **Diagnostic depth** | Find the root cause, not the symptom, and explain *why* it is a problem. | Every write-up says *what*, *why it matters*, and *how it gets triggered or exploited*. Trace the bug to the line where the bad decision is made, not the line where it blows up. |
| **Solution quality** | Small, targeted fixes. "Fix the problem, not the neighbourhood." | Keep diffs small. No renames, reformatting, or drive-by refactors. If you see something else, write it down; don't fix it in the same commit. |
| **Backwards compatibility** | Existing tests pass and existing behaviour is unchanged. | Run tests before your first change (baseline) and after every change. Keep API response shapes the same. |
| **Documentation** | Clear, short explanations of what you found and changed. | Commit messages with a fixed structure plus a `FINDINGS.md` that also lists issues you found but didn't fix. |
| **Systems thinking** | Thinking about scale, migration paths, and long-term maintenance. | For each fix, add one line on "at scale / in production this means…" and, where it applies, "rollout plan: …". |

**What this tells you:** they expect you *not* to fix everything ("a thorough fix on two issues beats a superficial pass on five"). They are scoring judgement and communication as much as code. A well-documented finding you didn't have time to fix still gets credit.

**The repo is planted.** It is described as having "a set of issues spanning security, performance, reliability, and developer tooling." Expect around 6–12 deliberate bugs, about 1–3 per category, plus a few red herrings. Graders usually have a rubric of the planted issues, so finding them reliably matters more than finding clever extra ones.

---

## 2. Setup checklist

Setup problems come out of your 90 minutes. Finish this 2+ days before.

- [ ] `node --version` shows **v22+** (use `nvm install 22 && nvm alias default 22` if needed)
- [ ] `npm install -g pnpm` then `pnpm --version`
- [ ] GitHub SSH works: `ssh -T git@github.com` prints "Hi <you>!"
- [ ] You can sign in to GitHub with the **same account you scheduled with**, and you can reach that account's email (for verification codes)
- [ ] Git identity: `git config --global user.name` / `user.email`
- [ ] **Dry run with a pnpm monorepo:** clone any public pnpm + TS monorepo (for example a Turborepo starter: `pnpm dlx create-turbo@latest`), then run `pnpm install`, `pnpm build`, `pnpm test`, `pnpm -r lint`. This catches node-gyp, Corepack, and permissions problems early.
- [ ] Chrome is installed and up to date
- [ ] AI tools are signed in and tested: Claude Code (`claude` in a terminal), and any others you plan to use
- [ ] **Clarify the autocomplete rule.** The environment section says *"ensure there is no predictive code completion enabled,"* but the assessment section says Copilot/Cursor are allowed. **Email the recruiter and ask.** Until they answer, assume chat or agent AI (Claude Code, ChatGPT) is fine and inline ghost-text completion (Copilot autocomplete, Cursor Tab) should be **off**. In VS Code: `"editor.inlineSuggest.enabled": false`, and disable the Copilot completions extension.
- [ ] Turn on notification silencing, close Slack, and charge your laptop
- [ ] **Proctoring:** candidates report **camera and screen recording**. Test your webcam, and grant your browser/OS screen-recording permission *ahead of time* (on macOS, System Settings → Privacy & Security → Screen Recording; a permission prompt mid-test costs minutes). **Ask the recruiter** whether a second monitor or a printed cheat sheet is allowed. Don't assume it is.
- [ ] **AI policy for *your* variant:** some candidates got a different first round (LRU cache + SSML parser) where **AI tools and external libraries were banned** (§11). Confirm with the recruiter, and on the day **read the README rules before anything else**.
- [ ] **pnpm 10 skips dependency install scripts by default.** If `pnpm install` warns about *ignored build scripts* and then esbuild/bcrypt/prisma/sharp fail, run `pnpm approve-builds` (or add the packages to `pnpm.onlyBuiltDependencies` in `package.json`). Practise this on the dry-run repo so you recognise the warning.

### Commands to memorise

```bash
pnpm install --frozen-lockfile        # install exactly what's in the lockfile
pnpm -r build                         # build every workspace package
pnpm -r test                          # test every package
pnpm --filter <pkg> test              # one package
pnpm --filter <pkg> exec vitest run path/to/file.test.ts   # one test file
pnpm -r exec tsc --noEmit             # typecheck everything
pnpm why <dep>                        # why a dependency is installed
pnpm audit                            # known CVEs in dependencies
pnpm approve-builds                   # allow blocked install scripts (pnpm 10+)
pnpm --filter <pkg> exec vitest related src/changed.ts --run   # only tests that import changed files
pnpm --filter <pkg> exec vitest run -t "rejects forged"        # filter by test name
git commit --fixup <sha>              # follow-up change that belongs to an earlier commit
GIT_SEQUENCE_EDITOR=: git rebase -i --autosquash <base>        # fold fixups in, no editor
git add -p                            # stage hunks, so each commit stays atomic
git commit                            # opens the editor for a multi-line message
git log --oneline                     # check the commit history at the end
```

---

## 3. The 90-minute game plan

The guide says to *"read the entire codebase before writing any code."* That's right, but in 90 minutes "read" has to mean a **structured skim**, not reading every line.

| Time | Phase | Output |
|---|---|---|
| **0–5** | **Rules, then boot.** Read the README/instructions *first*: AI policy, explicit task list, submission method. Then clone, `pnpm install`, run the tests, start the app. Record the **baseline**: which tests pass or fail *before you touch anything*. Pre-existing failures aren't yours; write them down so no one blames you. | `FINDINGS.md` created with a baseline section |
| **5–25** | **Map and hunt.** Start the AI audit in a second terminal *first* (see §4), then read the config files below and run the **grep sweep**. Log every suspicion in `FINDINGS.md` as you go. | Rough list of 10–20 suspicions |
| **25–30** | **Triage.** Score each item as severity × confidence × fix size. Choose the top 3–5. | Ranked fix list |
| **30–80** | **Fix loop** (about 10–12 minutes per issue): reproduce → fix → test → commit with justification → next. | 3–5 atomic commits |
| **80–90** | **Wrap up.** Finish `FINDINGS.md` (including unfixed items and rollout notes), run the full test suite once more, check `git log`, and push or submit. | Final deliverable |

**Hard rule:** at minute 80, stop fixing, even if you're in the middle of an issue. An unfinished fix that breaks tests is a negative signal. A documented, unfixed finding is a positive one.

**If the repo has an explicit task list** (TASKS.md, issues in the README): **that list is the rubric.** Triage against it first and add your own findings after. If it says "refactor X", the grading is about clean, behaviour-preserving changes to X, so tests first, then small steps.

**Check these before flagging anything** (they're the most common false positives):
- **Express version** (`package.json`). **Express 5** forwards rejected promises and thrown errors in async handlers to the error middleware automatically. "Missing asyncHandler" is only a bug on **Express 4**. Errors thrown inside *plain callbacks* still escape on both.
- **Auth mode:** cookies or `Authorization: Bearer`? CSRF only matters for cookie auth.
- **Frameworks and ORM:** Prisma/Knex/Mongoose/Firestore decide what "injection" and "N+1" look like.
- **Middleware order:** a route registered *before* `app.use(auth)` is public.

### The grep sweep (minutes ~10–20)

Candidates say the repo is too big to read every line. Read the configs and entry point (items 1–5 below), then **search for risky patterns** instead of reading files in order. `git grep` works everywhere:

```bash
git grep -nE 'query\(`|\.raw\(|\$where|\bexec\(|eval\('                    # injection
git grep -nE 'jwt\.decode|createHash\(.(md5|sha1|sha256)|=== *process\.env'  # auth/crypto
git grep -nE "\|\| *'(secret|changeme|dev)|_SECRET *\?\?"                     # fallback secrets
git grep -nE 'req\.params\.\w+|req\.body\)|\.\.\.req\.body'                   # authz / mass assignment
git grep -nE "origin: *(true|'\*')|trust proxy|credentials: *true"            # CORS / proxy
git grep -nE 'readFileSync|hashSync|Sync\(|forEach\(async'                    # blocking / floating promises
git grep -nE 'catch *\([^)]*\) *\{ *\}|catch *\{ *\}|console\.log\(req'       # swallowed errors / log leaks
git grep -nE "/debug|/test|/internal|/admin|/v1/"                             # leftover endpoints
git grep -nE 'innerHTML|dangerouslySetInnerHTML|localStorage\.setItem'        # XSS / token storage
git grep -nE 'similaritySearch|embedding|max_tokens|completions?\.'           # LLM / RAG
git grep -nE '\.only\(|\.skip\(|\|\| *true|continue-on-error|"strict": *false' # tests / CI / tsconfig
git grep -nE 'if true|request\.auth *!= *null'                                # Firestore rules
```
Every hit is a lead, not a finding. Open it, check the guards upstream, *then* log it.

### Reading order (minutes 5–25)

1. **Root `package.json`, `pnpm-workspace.yaml`, `turbo.json`/`nx.json`**: what packages exist, what the scripts do, and whether `test`/`lint`/`typecheck` are actually wired up.
2. **`tsconfig*.json`**: is `strict` off? Is `skipLibCheck` hiding errors? Do paths point at the right places?
3. **CI config** (`.github/workflows/*`): does it run tests, lint, and typecheck? Does it use `--frozen-lockfile`? Are there secrets in plain text?
4. **`.env*`, `config/`**: committed secrets, insecure defaults (`JWT_SECRET ?? 'secret'`), missing validation.
5. **Server entry point** (`server.ts`, `app.ts`, `main.ts`): middleware order, CORS, body limits, error handler, graceful shutdown.
6. **Auth module**: password hashing, token signing and verification, session handling, middleware coverage.
7. **Routes/controllers → services → data layer**: for each endpoint, check *who can call it* (authz), *what input it trusts* (validation), and *what queries it runs* (injection, N+1, unbounded).
8. **Frontend** (skim): `dangerouslySetInnerHTML`/`innerHTML`, tokens in `localStorage`, secrets in the client bundle, re-render storms, huge imports, `useEffect` fetches without cleanup.
9. **Tests**: `.skip`, `.only`, tests with no assertions, missing `await`, tests that mock away the thing under test.
10. **Infra files** (if present): `Dockerfile`, `.dockerignore`, `firestore.rules`, migrations folder.

**Hard cap:** by minute 25, stop reading and triage, even if you haven't seen everything. Note any unread areas in FINDINGS ("not reviewed: packages/x").

### Triage scoring (minutes 25–30)

Score each item from 1 to 3 on each axis and pick the highest totals:

- **Severity:** 3 = data breach, money loss, or outage · 2 = degraded performance or correctness · 1 = hygiene
- **Confidence:** 3 = can reproduce or prove it · 1 = hunch
- **Fix cost (inverted):** 3 = a few lines · 1 = needs a migration or redesign

Roughly: **security first** (it is usually the most heavily weighted), then **reliability/correctness**, then **performance**, then **tooling**. Exception: if a tooling issue blocks the tests (for example the test script is broken), fix it **first**, because you need working tests to show backwards compatibility for everything else.

### The fix loop (for each issue)

1. **Reproduce or prove it.** Write a failing test if it takes under about 3 minutes. Otherwise, cite the exact line and the input that triggers it. For performance, **measure** (§5.2 "Proving a performance issue"). Before touching risky, untested code, add a quick **characterisation test** that pins down current behaviour, so you can show you preserved it.
2. **Make the smallest fix** at the root cause.
3. **Run tests:** the affected package's tests, then the full suite.
4. **Stage only this fix** (`git add -p`, then `git diff --cached`; never `git commit -a`) and **commit** using the template in §7. If you later need to adjust an earlier fix, use `git commit --fixup <sha>` and autosquash before submitting, so it stays one commit per issue.
5. **Update `FINDINGS.md`.**

---

## 4. Using AI during the assessment

"The best candidates use AI to accelerate their diagnosis, not to replace their judgment." Treat the AI as a fast junior reviewer: it suggests leads, and you confirm them.

### Parallel audit setup

As soon as `pnpm install` finishes, open a **second terminal** running Claude Code and paste in an audit prompt. Let it run while you read the code yourself. Compare its list with yours at minute 25.

**Prompt 1: full audit**
```
Audit this repository as a senior engineer. It contains deliberately planted issues
in security, performance, reliability, developer tooling, and tests.
For each issue output: file:line, category, severity (critical/high/med/low),
root cause in one sentence, how it is triggered or exploited, and the minimal fix.
Do not modify any files. Also list anything in CI, tsconfig, package scripts,
or test files that looks wrong. Be skeptical; flag uncertainty explicitly.
```

**Prompt 2: architecture map** (useful if the repo is large)
```
Without modifying files, give me a 15-line map of this monorepo: packages, how they
depend on each other, the request path from frontend to DB for the main feature,
where auth is enforced, and how tests/CI are wired.
```

**Prompt 3: check a specific suspicion**
```
In <file>, I think <X> is vulnerable to <Y> because <Z>. Am I right? What input
triggers it? Is there any upstream middleware or validation that already prevents it?
```
The last question matters. The AI (and you) can flag something that is already handled upstream. Always check before claiming it's a bug.

**Prompt 4: review your diff**
```
Review my staged diff (git diff --cached). Does it fully fix the root cause?
Does it change any existing behaviour or API contract? Edge cases I missed?
```

### Time-box it

How fast AI *feels* is not how fast it is. In METR's 2025 randomised trial, experienced developers took **19% longer** with AI while believing they were ~20% faster. Candidates specifically report losing time checking AI output.
- The audit prompt runs **in the background**. Never sit waiting for it.
- **2-minute rule:** each AI claim gets at most 2 minutes to verify (open file:line, check upstream guards). If you can't confirm it, it goes in FINDINGS under **"Suspected, unverified"**, not in a commit.
- Prefer writing small fixes yourself: a 5-line diff is faster to write than to generate and review.
- Use AI for boilerplate: regression-test skeletons, first drafts of commit messages.

### Rules for yourself

- **Never commit AI-written code you haven't read line by line.** You may be asked about it in later rounds.
- **Write the justifications yourself** (or edit heavily). The documentation criterion rewards your reasoning.
- **Don't let the AI refactor.** Tell it: "minimal fix only, change as few lines as possible."
- **Check AI claims against the code.** False positives in your write-up suggest you don't understand the system.
- Mention in `FINDINGS.md` how you used AI (one line). They want to see good AI use, so show it.

---

## 5. The audit checklist

For each item: **what it is → how to spot it → the fix**. The ⚡ items are the ones most likely to be planted in a TypeScript/Node full-stack exercise.

### 5.1 Security

#### ⚡ Injection (SQL / NoSQL / command)
**What:** user input is glued into a query or shell command, so the attacker controls its structure.
**Spot:** template strings or `+` inside `query(...)`, `$where`, `raw(...)`, `exec(...)`; Mongo `find(req.body)` (an attacker sends `{"password": {"$ne": null}}`).
**Fix:** parameterised queries (`query('... WHERE id = $1', [id])`); validate types with zod so objects can't go where strings belong; `execFile` with an args array instead of `exec`.

#### ⚡ Broken authentication
- **Weak password hashing:** `md5`/`sha1`/`sha256` with no salt, or plaintext. **Fix:** `bcrypt`/`argon2`/`scrypt`, plus a **rehash-on-login migration** (§6).
- **JWT misuse:** `jwt.decode()` used where `jwt.verify()` should be (the signature is never checked); no `expiresIn`; hardcoded or fallback secret (`process.env.JWT_SECRET || 'dev'`); `algorithms` not pinned. **Fix:** `jwt.verify(token, secret, { algorithms: ['HS256'] })`, require the secret at startup (fail fast), and set an expiry.
- **Non-constant-time comparison** of API keys or HMACs (`a === b`): leaks information through timing. **Fix:** `crypto.timingSafeEqual` on equal-length buffers.
- **No rate limit on login or password reset:** allows brute force. **Fix:** `express-rate-limit` (per IP + per account).

#### ⚡ Broken authorization / IDOR
**What:** IDOR (Insecure Direct Object Reference): the endpoint checks *that* you're logged in, but not that the resource *belongs to you*.
**Spot:** `findById(req.params.id)` with no `userId` filter; admin routes missing role middleware; routes registered **before** the auth middleware.
**Fix:** scope the query (`WHERE id = $1 AND owner_id = $2`), return 404 (not 403) to avoid leaking that the resource exists, and add the missing middleware.

#### Mass assignment
**Spot:** `db.users.update(id, req.body)` or `{...req.body}`. A user can set `role: 'admin'` or `credits: 999999`.
**Fix:** whitelist fields with a zod schema (`.pick()` / `.strict()`).

#### XSS
**Spot:** `dangerouslySetInnerHTML`, `innerHTML =`, `v-html`, server-rendered HTML built with template strings, and user content in `href` (`javascript:` URLs).
**Fix:** render as text; sanitise with DOMPurify if HTML is really needed; validate the URL scheme.

#### Secrets and config
**Spot:** keys in source or `.env` committed to git, secrets in the frontend bundle (`NEXT_PUBLIC_*`, `VITE_*` holding server secrets), secrets logged.
**Fix:** move to env vars, validate env at startup (zod), and **note in the write-up that a leaked key must be rotated**. Removing it from the code is not enough, because it is still in git history. That is a good systems-thinking point.

#### Other common ones
- **CORS** `origin: '*'` with `credentials: true`, or reflecting any origin → use an allowlist.
- **Error handler leaks** stack traces or SQL errors to clients → generic message to the client, details to logs.
- **Path traversal:** `path.join(UPLOAD_DIR, req.query.file)` lets `../../etc/passwd` through → `path.resolve` and then check `startsWith(UPLOAD_DIR + path.sep)`.
- **SSRF:** server fetches a user-supplied URL (common in "import from URL" or RAG ingestion) → allowlist hosts and block private IP ranges.
- **Missing body size limit** → `express.json({ limit: '1mb' })`.
- **Prototype pollution:** deep-merging user JSON into objects → reject `__proto__`/`constructor` keys.
- **Webhook without signature verification** (payments!) → verify the HMAC over the **raw body** (the JSON parser must not consume it first).
- **Vulnerable dependencies:** `pnpm audit`. Mention it, but don't spend fix time on it unless it's clearly planted.

#### ⚡ Gaps from the OWASP API Top 10 (2023)
Use the OWASP names in write-ups: IDOR = **BOLA (API1)**.
- **Excessive data exposure (API3, read side):** responses that serialise whole rows (`passwordHash`, `stripeCustomerId`, other users' emails). Fix: select or DTO only the public fields. *Compatibility:* check that the frontend doesn't use a field before removing it.
- **Function-level authz (BFLA, API5):** admin route reachable by regular users; GET protected but DELETE on the same path isn't.
- **Business-flow abuse (API6):** unlimited free trials, replayable coupons or referral credits, unlimited "regenerate audio". Fix: per-account limits and uniqueness constraints.
- **Leftover endpoints (API9):** `/debug/*`, `/test/seed`, an old `/v1` route missing auth that `/v2` has. Grep route registrations and compare them against the auth middleware.
- **Unsafe consumption of APIs (API10):** trusting payment/TTS/LLM provider responses without validation or timeouts.
- **Cost limits (API4):** per-user quotas on expensive downstream calls (TTS synthesis, LLM tokens), not just request-rate limits.

#### CSRF and cookies
- Cookie auth needs `httpOnly`, `secure` and `sameSite: 'lax'|'strict'` (`'none'` requires `secure`) **plus** CSRF protection. `csurf` is **deprecated**; replacements are `csrf-csrf` (double-submit) and `csrf-sync` (session).
- **Bearer-token APIs aren't CSRF-vulnerable.** Flagging them is a false positive. The trade-off: a JWT in `localStorage` + any XSS = stolen token.
- Missing **helmet** (security headers) and `x-powered-by` exposed: low severity, a quick win.

#### ⚡ Rate limiting behind a proxy
- `app.set('trust proxy', true)` lets clients spoof `X-Forwarded-For` and **bypass** the limit. Not setting it at all puts every user in one bucket (the load balancer's IP). Fix: the exact hop count (`1`).
- The default store is **in-memory, per instance**: with N instances, the limit becomes N×. Fix: a shared store (`rate-limit-redis`). For login, key by account + IP.

#### Secrets and PII in logs
`console.log(req.body)` on auth or payment routes, or logging the `Authorization` header, puts credentials in the log pipeline. Fix: pino `redact` paths (`req.headers.authorization`, `*.password`, `*.token`).

#### Firebase / Firestore
- The server must call `admin.auth().verifyIdToken(token)`. Never trust a `uid` from the body or header. Use `checkRevoked: true` for sensitive operations (costs an extra network call).
- Rules: a bare `request.auth != null` means **any user can read and write everyone's data**. Pin ownership to the **path** (`match /users/{userId}/...` + `request.auth.uid == userId`), not to a field the client writes. Rules are **OR-combined**, so a broad parent rule can't be narrowed by a child. Look for `if true` left over from test mode.
- **The Admin SDK bypasses rules**, so server code must do its own authz.

#### ⚡ LLM / RAG (runs "various RAG systems")
- **Vector search without a tenant filter** (`similaritySearch(q)` with no `userId` metadata filter) returns other users' chunks. This is BOLA in a new place.
- **Prompt injection**, including *indirect* injection from uploaded or retrieved docs. Keep retrieved text clearly delimited as data, never give the model tool access beyond what the user is allowed, and don't put secrets in system prompts.
- **Output handling:** model output rendered as HTML is XSS; never pass it into SQL or a shell.
- **Unbounded consumption:** no `max_tokens`, no input length cap, no per-user quota, so cost becomes a DoS vector.

### 5.2 Performance

#### ⚡ N+1 queries
**What:** 1 query to get a list, then 1 query *per item*. 100 items = 101 round trips.
**Spot:** `await` inside a `for` loop or `.map` that hits the DB or an API.
**Fix:** one batched query (`WHERE id IN (...)` / `$in`), a JOIN, or a DataLoader.

#### ⚡ Sequential awaits that could run in parallel
**Spot:** `const a = await x(); const b = await y();` where `y` doesn't depend on `a`.
**Fix:** `const [a, b] = await Promise.all([x(), y()]);`. For a large list, limit concurrency (`p-limit`) so you don't flood the DB.

#### ⚡ Unbounded queries / missing pagination
**Spot:** `SELECT * FROM table` / `find({})` returned straight to the client.
**Fix:** `LIMIT` with cursor or offset pagination and a capped max page size. For **backwards compatibility**, keep the old response shape and add pagination params with a default.

#### Missing database indexes
**Spot:** filtering or sorting on a column with no index in the migrations/schema (look at `WHERE email = …`, `ORDER BY created_at`).
**Fix:** add an index in a **new migration** (never edit old ones). Mention `CREATE INDEX CONCURRENTLY` in Postgres to avoid locking a large table. That's a systems-thinking point.

#### Blocking the event loop
**Spot:** `fs.readFileSync` in request handlers, `bcrypt.hashSync`, `crypto.pbkdf2Sync`, `JSON.parse` of huge payloads, CPU-heavy loops, catastrophic-backtracking regex (ReDoS: nested quantifiers like `(a+)+`).
**Fix:** async versions; move CPU work to a worker or queue; rewrite the regex.

#### Memory leaks
**Spot:** a module-level `Map`/array cache that only grows; event listeners added per request (`emitter.on` inside a handler); `setInterval` never cleared; closures holding large buffers.
**Fix:** bounded LRU with TTL (`lru-cache`), `once` or remove listeners, clear timers on shutdown.

#### Wasted work
- Re-computing or re-fetching the same thing per request → cache with TTL (think about invalidation).
- Fetching whole rows or objects when you need one field → select only the needed columns.
- Loading the whole file into memory → stream it (`pipeline(readStream, res)`).
- Frontend: re-render storms (new object or function props each render, missing memoisation; Redux selectors returning a new object every call), huge imports (`import _ from 'lodash'`), fetching in a loop.

#### ⚡ Proving a performance issue
"Diagnostic depth" means showing *why*. A before/after number in the commit message beats "this looks slow". Cheapest first:
1. **Count queries.** Turn on query logging (Prisma `log: ['query']`, `DEBUG=knex:query`, `mongoose.set('debug', true)`). "N+1: 101 queries → 2" is proof in one line.
2. **Load test:** `npx autocannon -c 50 -d 10 http://localhost:3000/api/x` gives requests/sec and p99 latency, before and after.
3. **Query plan:** `EXPLAIN (ANALYZE, BUFFERS) <query>`. A **Seq Scan** whose filter discards most rows means a missing index. Re-run after the fix to show the Index Scan. Note that `ANALYZE` *executes* the query.
4. **Event-loop blocking:** `perf_hooks.monitorEventLoopDelay()`. A p99 above ~100 ms under load means synchronous work on the request path.
5. **CPU profile:** `node --cpu-prof dist/server.js`, exercise the endpoint, stop it, then open the `.cpuprofile` in Chrome DevTools → Performance. (`--heap-prof` and `--heapsnapshot-signal=SIGUSR2` for memory.) Usually too slow to set up mid-assessment; mention it as "how I'd confirm in production".

#### Cost-based limits
The expensive resources are **TTS synthesis and LLM tokens**, not CPU. Missing per-user quotas, missing caching of identical synthesis requests, or regenerating audio that already exists in storage are perf *and* cost bugs.

### 5.3 Reliability

#### ⚡ Unhandled or floating promises
**Spot:** an async call with no `await` and no `.catch`; `array.forEach(async …)` (forEach does **not** wait); async Express handlers without try/catch on **Express 4 only** (the rejection never reaches the error middleware and the request hangs). **Express 5 forwards these automatically**, so check the version before flagging. On both versions, errors thrown inside plain callbacks (`setTimeout`, event emitters, old callback-style APIs) still escape.
**Fix:** `await` it, `for…of` or `Promise.all(array.map(...))`, wrap handlers in `asyncHandler`. Tooling-level fix: the `@typescript-eslint/no-floating-promises` lint rule.

#### ⚡ Race conditions / lost updates
**Spot:** read → compute → write (`const u = await get(); u.credits -= 1; await save(u)`). Two concurrent requests both read 10 and both write 9.
**Fix:** an atomic DB operation (`UPDATE … SET credits = credits - 1 WHERE id = $1 AND credits >= 1`, Mongo `$inc` with a condition), a transaction with row locking, or optimistic concurrency (version column).

#### ⚡ Non-idempotent payments / retries
**What:** idempotent means doing it twice has the same effect as doing it once. Webhooks get delivered more than once and clients retry, so charges or credits get applied twice.
**Fix:** idempotency keys (unique constraint on `event_id` / `idempotency_key`, processed-events table), and check before applying.
**Client-facing too:** for `POST /charge` or `POST /subscribe`, accept an **`Idempotency-Key` header** (IETF HTTPAPI draft). Store key + request fingerprint + response; on a replay return the stored response; if the same key comes with a different body, return 422. When *calling* Stripe, pass an idempotency key outbound as well.

#### Money as floats
**Spot:** `price * 1.1`, `0.1 + 0.2` math on amounts, `parseFloat` on money.
**Fix:** integer minor units (cents). Migration note: store in a new column and backfill.

#### External calls with no timeout or retry
**Spot:** `fetch(url)` with no timeout (Node's fetch has **no default timeout**, so a hung upstream ties up the request forever); retry loops without backoff (thundering herd).
**Fix:** `fetch(url, { signal: AbortSignal.timeout(5000) })`; retry with exponential backoff + jitter, only for idempotent operations.

#### Swallowed errors
**Spot:** `catch (e) {}`, `catch (e) { return null }`, `console.log(e)` and then continue as if it succeeded.
**Fix:** rethrow or return an explicit error; log with context.

#### Resource leaks
**Spot:** a DB client or connection acquired but not released on the error path (`pool.connect()` without `finally { client.release() }`), a new DB client created **per request**, file handles not closed.
**Fix:** `try/finally`, one shared pool.

#### Startup and shutdown
- Missing env vars only discovered at runtime → validate at startup and fail fast.
- **No graceful shutdown.** On `SIGTERM`: `server.close()` (stop accepting connections) → `server.closeIdleConnections()` (Node 18.2+; otherwise idle keep-alive sockets keep `close()` waiting) → let in-flight requests finish → stop queue consumers → close DB pools → exit. **Cloud Run gives 10 s** between SIGTERM and SIGKILL; Kubernetes `terminationGracePeriodSeconds` defaults to **30 s**. Add a hard timeout below that window.
- **`process.on('SIGTERM', () => process.exit(0))`** looks like a shutdown handler but drops every in-flight request.
- **The signal never arrives:** shell-form `CMD npm start` in a Dockerfile means `node` isn't PID 1 and never gets SIGTERM. Fix: exec form `CMD ["node", "dist/server.js"]`, or `tini`.
- **Health checks:** *readiness* should check dependencies (DB reachable); *liveness* shouldn't, or the orchestrator restart-loops the app on every DB blip. A health check that always returns 200 is a finding too.

#### Unsafe consumption of third-party APIs
Payment, TTS and LLM provider responses are input too: validate their shape (zod), set timeouts, don't blindly follow redirects, and handle partial failures.

#### Missing input validation
Covers both reliability and security: `parseInt(req.query.limit)` → `NaN` → the query breaks or returns everything. **Fix:** zod schema with coercion, bounds, and defaults.

### 5.4 Developer tooling and CI

These are easy to miss because nothing visibly breaks. Look for them on purpose.

| Smell | Why it matters | Fix |
|---|---|---|
| `"strict": false` or `noImplicitAny: false` in tsconfig | Type errors hide real bugs | Turn it on **for one package**, or use `strict` with targeted `// @ts-expect-error` comments. Migration note: enable incrementally. |
| No `typecheck` script, or CI doesn't run `tsc --noEmit` | Type errors ship (bundlers like Vite/esbuild **strip types without checking**) | Add `"typecheck": "tsc --noEmit"` and a CI step |
| CI runs `pnpm install` without `--frozen-lockfile` | CI can install different versions from what was tested; non-reproducible builds | `pnpm install --frozen-lockfile` |
| Lockfile missing or not committed; `*` or `latest` version ranges | Non-reproducible installs | Commit the lockfile, pin ranges |
| CI test step has `|| true` or `continue-on-error: true` | Failing tests never block a merge | Remove it |
| Test script points at the wrong glob or package | Tests silently don't run ("0 tests passed") | Fix the glob; check the test count |
| Lint configured but not in CI, or rules disabled | Quality gates are fake | Wire it in |
| Wrong workspace dependency (`"pkg": "1.0.0"` instead of `"workspace:*"`) | Pulls the published version from npm instead of local code | `workspace:*` |
| Turbo/Nx `outputs` or `dependsOn` wrong (e.g., `test` doesn't depend on `^build`) | Stale cache, or tests run against an unbuilt dependency | Fix the pipeline config |
| Secrets in the workflow file | Leaked to anyone with repo access | `${{ secrets.X }}` |
| Unpinned GitHub Actions (`@main`) | Supply-chain risk | Pin to a version tag or SHA |
| `devDependencies` vs `dependencies` mixed up | Prod crash, or bloated image | Move them |
| No `packageManager` field in root `package.json` | No Corepack pin, so the pnpm version differs between machines and CI | `"packageManager": "pnpm@<version>"` |
| `pnpm.neverBuiltDependencies: []`, or git-hosted deps | Re-enables every install script; supply-chain risk (pnpm 10.0–10.25 git-dep CVE-2025-69264) | Use the `onlyBuiltDependencies` allowlist; pnpm ≥10.26 |
| No dependency or secret scanning in CI | Known CVEs and leaked keys go unnoticed | `pnpm audit --prod`, gitleaks. Mention it, low priority |
| `csurf` in dependencies | Deprecated/archived | `csrf-csrf` or `csrf-sync` |
| **Dockerfile:** `COPY . .` with no `.dockerignore` | `.env`, `.git`, `node_modules` baked into image layers, so **secrets ship in the image** | `.dockerignore` with `.env*`, `*.pem`, `.git`, `node_modules` |
| **Dockerfile:** no `USER` | Container runs as root | `USER node` |
| **Dockerfile:** `node:latest`, single stage, `npm install` | Unreproducible, bloated image shipping the build toolchain | Pin the base image, multi-stage build, `pnpm install --frozen-lockfile` / `--prod` in the runtime stage |
| **Dockerfile:** shell-form `CMD npm start` | Node never receives SIGTERM (see §5.3) | Exec form `CMD ["node", "dist/server.js"]` |

### 5.5 Tests

- **`.only` left in:** the rest of the suite silently skips. **`.skip`** hides a known break.
- **Missing `await` on an async assertion** (`expect(promise).rejects…` without `await`, or no `return`): the test passes no matter what.
- **Assertions that can't fail:** `expect(result).toBeDefined()` on something always defined; asserting on the mock itself.
- **Over-mocking:** the module under test is mocked, so the test only tests the mock.
- **Shared mutable state between tests** → order-dependent flakiness.
- **Real time or network in tests** → flaky; use fake timers and stubbed fetch.
- **Snapshot tests that were blindly updated** to match a bug.

When you fix a bug, **add a regression test** if it's cheap. It proves the root cause and protects backwards compatibility. One focused test beats three superficial ones. Name it after the bug (`rejects forged JWT`), so the test list reads like your findings.

### 5.6 API design

Lists "API design best practices" as a study area. Each item below can be planted, and each has a backwards-compatibility angle.

- **Inconsistent error format:** each endpoint returns a different error shape, or leaks internals. The standard is **RFC 9457 Problem Details** (`application/problem+json` with `type`, `title`, `status`, `detail`, `instance`; it obsoletes RFC 7807). Don't migrate every endpoint in 90 minutes. List it under "found, not fixed" with a rollout plan (new format behind a version or an `Accept` header).
- **Wrong status codes:** 200 with `{ error }` in the body, 500 for validation failures (should be 400/422), 401 vs 403 confusion (401 = not authenticated, 403 = authenticated but not allowed), 404 vs 403 for resources the caller doesn't own (prefer 404).
- **GET that mutates** (`GET /api/credits/claim`): crawlers, prefetchers and retries trigger it. Use POST, and make it idempotent.
- **Pagination:** offset pagination gets slow on deep pages and skips or duplicates rows when data changes underneath. **Cursor/keyset** pagination (`WHERE (created_at, id) < ($1, $2) ORDER BY created_at DESC, id DESC LIMIT $3`) is stable. Backwards-compatible rollout: add optional `cursor`/`limit` params with defaults, keep the old response shape, and add `nextCursor`.
- **Breaking changes in place:** renaming or removing response fields, or changing types, breaks clients. Only make additive changes within a version; deprecate with `Deprecation`/`Sunset` headers, then remove in `/v2`.
- **Input contract:** no schema validation (zod) at the boundary means `limit=abc` → `NaN` → unbounded query.

---

## 6. Migration patterns

The guide says: *"Some fixes (especially security-related ones) can't be applied in a single step."* Showing you know these patterns covers the **systems thinking** criterion. Even if you only implement step 1, write out the full plan.

### Weak password hashes → bcrypt/argon2 (rehash on login)
You can't re-hash without the plaintext, which you only see at login.
```ts
async function verifyPassword(user: User, plain: string): Promise<boolean> {
  if (user.passwordAlgo === 'bcrypt') {
    return bcrypt.compare(plain, user.passwordHash);
  }
  // Legacy path: sha256. Remove once all active users have migrated.
  const legacyOk = timingSafeEqualHex(sha256(plain), user.passwordHash);
  if (legacyOk) {
    await users.update(user.id, {
      passwordHash: await bcrypt.hash(plain, 12),
      passwordAlgo: 'bcrypt',
    });
  }
  return legacyOk;
}
```
**Rollout:** (1) new users get bcrypt; (2) upgrade legacy users on login; (3) after N months, force a reset for the rest; (4) delete the legacy path. An optional stronger option: wrap the legacy hashes now (`bcrypt(sha256(pw))`) so no weak hash stays at rest.

### Rotating a signing key or changing token format
Accept **old and new** for an overlap window (verify with the new key, fall back to the old one), issue only new tokens, and remove the old key after the max token lifetime.

### A secret committed to the repo
Removing it from the code is step 2, not the fix. The full answer for FINDINGS.md:
1. **Rotate or revoke first.** The secret counts as compromised from the moment it was pushed. Accept that sessions signed with it will be invalidated.
2. Move it to an env var or **GCP Secret Manager**, and validate it at startup (no fallback values).
3. *Optionally* rewrite history (`git filter-repo --replace-text`, or BFG). Every SHA changes, open PRs break, and clones, forks and CI caches still hold the secret, which is why rotation is the real fix.
4. Prevent it: a pre-commit hook (gitleaks) **plus** server-side push protection (GitHub secret scanning), because client hooks can be bypassed.

**Never rewrite history during the assessment.** It destroys your atomic commit trail.

### Tightening input validation or a breaking API change
Log or measure violations first ("would have rejected") → fix callers → enforce. Or version the endpoint (`/v2`) and deprecate v1 with a sunset header.

### Database schema changes: expand → migrate → contract
1. **Expand:** add the new column or index (nullable, `CREATE INDEX CONCURRENTLY`). A plain `CREATE INDEX` takes a lock that **blocks writes** for the whole build. `CONCURRENTLY` allows writes but **cannot run inside a transaction block**, and most migration runners wrap each file in one, so that migration must opt out (Knex: `export const config = { transaction: false }`). A failed concurrent build leaves an `INVALID` index behind, so use `IF NOT EXISTS` and drop or rebuild it on retry.
2. **Dual-write:** the code writes both old and new.
3. **Backfill** in batches.
4. **Switch reads** to the new column.
5. **Contract:** drop the old column in a later deploy.

### Enabling `strict` in a large codebase
Enable it per package, or use a separate `tsconfig.strict.json` that covers migrated files, and ratchet up over time. Don't flip it globally and add 400 `any`s.

### Adding a CI gate that currently fails
Fix the existing violations in the same PR, or add the gate in "warn" mode with a baseline and then enforce. Never add a gate that is red on day one.

**Feature flags** are the general tool: ship the fix behind a flag, roll out in stages, keep a quick kill switch.

---

## 7. Writing it up

### Commit message template

```
fix(security): verify JWT signature in auth middleware

Problem: requireAuth used jwt.decode(), which parses the token without
checking its signature. Anyone can forge a token with any userId and
act as that user (full account takeover).

Fix: use jwt.verify() with the configured secret and pinned algorithm
(HS256). Expired/invalid tokens now return 401.

Compatibility: legitimately-issued tokens are unaffected. Added a
regression test for a forged token.

Severity: Critical
```

**Prefixes:** `fix(security)`, `fix(perf)`, `fix(reliability)`, `fix(tooling)`, `fix(test)`, `docs(findings)`.
**One issue per commit.** If two issues are in the same file, use `git add -p` to split them.

### Severity rubric (use it consistently)

| Level | Meaning |
|---|---|
| **Critical** | Remote, unauthenticated, or trivial exploit leading to data breach, account takeover, or money loss |
| **High** | Exploitable by an authenticated user, or likely outage or data corruption |
| **Medium** | Degradation under load, or correctness bugs in edge cases |
| **Low** | Hygiene or maintainability; makes future bugs more likely |

### `FINDINGS.md` template (create at minute 0, fill in as you go)

```markdown
# Audit Findings

## Baseline
- `pnpm test`: 42 passed, 0 failed (before changes)
- App boots: yes / build: yes / typecheck: 3 errors (pre-existing)

## Fixed
| # | Severity | Area | Issue | Commit |
|---|---|---|---|---|
| 1 | Critical | Security | JWT signature not verified (`api/src/auth.ts:14`) | abc123 |

### 1. JWT signature not verified
- **What:** ...
- **Why it matters / how it's exploited:** ...
- **Fix:** ...
- **Rollout / at scale:** ...

## Found, not fixed (time)
| # | Severity | Area | Issue | Proposed fix |
|---|---|---|---|---|
| 5 | Medium | Perf | N+1 in `listDocuments` (`services/docs.ts:40`) | Batch with `IN (...)` |

## Suspected, unverified
- `packages/api/src/upload.ts:22`: possible path traversal; couldn't confirm whether
  the upstream `sanitizeName` covers `..` in time.

## Not reviewed
- `packages/admin-ui` (time)

## Notes
- Prioritised security > reliability > perf > tooling.
- AI usage: Claude Code for an initial parallel audit pass and diff review; all
  findings verified manually against the code.
```

### Writing tips
- **Lead with the impact:** "anyone can forge a token" beats "decode is used instead of verify."
- **Name the trigger:** a concrete request or input that causes it.
- **One line on scale or rollout** for every fix.
- Aim for **3–6 lines per finding**. They value short, clear writing over long prose.

---

## 8. Practice exercises

For each snippet: find the issue(s), name the root cause, write a minimal fix, and write a 3-line commit justification. Time yourself at **about 4 minutes each**. Answers are in the collapsible sections.

### Exercise 1
```ts
app.get('/api/documents', requireAuth, async (req, res) => {
  const { sort = 'created_at' } = req.query;
  const rows = await db.query(
    `SELECT * FROM documents WHERE owner_id = $1 ORDER BY ${sort}`,
    [req.user.id],
  );
  res.json(rows);
});
```
<details><summary>Answer</summary>

- **SQL injection via `ORDER BY`.** You can't parameterise identifiers, so the developer interpolated the value. `?sort=(SELECT CASE WHEN ...)` allows blind data extraction. **Fix:** an allowlist: `const SORTS = { created: 'created_at', title: 'title' } as const; const col = SORTS[sort] ?? 'created_at';`
- **Unbounded result** (`SELECT *`, no `LIMIT`): add pagination with a default and a cap, and select only the needed columns.
- Lesson: placeholders protect *values*. For *identifiers*, use an allowlist.
</details>

### Exercise 2
```ts
export function requireAuth(req: Request, res: Response, next: NextFunction) {
  const token = req.headers.authorization?.split(' ')[1];
  if (!token) return res.status(401).end();
  const payload = jwt.decode(token) as { userId: string };
  req.user = { id: payload.userId };
  next();
}
```
<details><summary>Answer</summary>

- **`jwt.decode` does not verify the signature**, so tokens can be forged and any account taken over (Critical). Also, a malformed token makes `decode` return `null` → `payload.userId` throws a TypeError → 500.
- **Fix:** `try { const p = jwt.verify(token, env.JWT_SECRET, { algorithms: ['HS256'] }) as JwtPayload; ... } catch { return res.status(401).end(); }`
- Compatibility: valid tokens still work. Add a forged-token regression test.
</details>

### Exercise 3
```ts
app.delete('/api/voices/:id', requireAuth, async (req, res) => {
  await Voice.deleteOne({ _id: req.params.id });
  res.status(204).end();
});
```
<details><summary>Answer</summary>

- **IDOR:** any logged-in user can delete any voice. **Fix:** `const r = await Voice.deleteOne({ _id: req.params.id, ownerId: req.user.id }); if (r.deletedCount === 0) return res.status(404).end();`
- 404 rather than 403 avoids revealing whether the resource exists.
- Also: validate that `id` is a valid ObjectId (zod), or Mongoose throws a CastError → 500.
</details>

### Exercise 4
```ts
async function getPlaylistWithAuthors(playlistId: string) {
  const items = await db.items.findMany({ where: { playlistId } });
  for (const item of items) {
    item.author = await db.users.findUnique({ where: { id: item.authorId } });
  }
  return items;
}
```
<details><summary>Answer</summary>

- **N+1:** 1 + N sequential queries. Latency grows linearly with playlist size.
- **Fix:** `const authors = await db.users.findMany({ where: { id: { in: [...new Set(items.map(i => i.authorId))] } } }); const byId = new Map(authors.map(a => [a.id, a])); return items.map(i => ({ ...i, author: byId.get(i.authorId) }));`, or use the ORM's `include`.
- Bonus: it also returns the full user row (password hash?) to the caller. Select only the public fields.
</details>

### Exercise 5
```ts
async function chargeAndCredit(userIds: string[]) {
  userIds.forEach(async (id) => {
    await payments.charge(id);
    await credits.add(id, 100);
  });
  return { ok: true };
}
```
<details><summary>Answer</summary>

- **`forEach(async …)` doesn't wait.** It returns `{ok: true}` immediately, rejections are unhandled (they can crash the process on Node 15+), and errors never reach the caller.
- **Fix:** `for (const id of userIds) { ... }` (sequential), or `await Promise.all(userIds.map(...))` with a concurrency limit.
- Systems thinking: charge-then-credit isn't atomic, and a retry double-charges. Use idempotency keys per user per operation.
</details>

### Exercise 6
```ts
app.post('/api/credits/spend', requireAuth, async (req, res) => {
  const user = await db.users.findById(req.user.id);
  if (user.credits < req.body.amount) return res.status(400).json({ error: 'insufficient' });
  user.credits -= req.body.amount;
  await user.save();
  res.json({ credits: user.credits });
});
```
<details><summary>Answer</summary>

- **Race condition (lost update / double spend):** concurrent requests both pass the check.
- **No validation on `amount`:** a negative amount *adds* credits; a string causes type coercion bugs.
- **Fix:** zod `z.object({ amount: z.number().int().positive() })` and an atomic conditional update: `UPDATE users SET credits = credits - $1 WHERE id = $2 AND credits >= $1 RETURNING credits`. Zero rows → 400.
</details>

### Exercise 7
```ts
app.use(express.json());
app.post('/webhooks/stripe', async (req, res) => {
  const event = req.body;
  if (event.type === 'invoice.paid') {
    await subscriptions.activate(event.data.object.customer);
  }
  res.sendStatus(200);
});
```
<details><summary>Answer</summary>

- **No signature verification:** anyone can POST a fake `invoice.paid` and get a free subscription (Critical).
- **Fix:** mount the raw body for this route *before* the global JSON parser: `app.post('/webhooks/stripe', express.raw({ type: 'application/json' }), handler)`, then `stripe.webhooks.constructEvent(req.body, sig, whSecret)`.
- **Not idempotent:** Stripe retries. Record `event.id` in a processed-events table with a unique constraint, and skip duplicates.
</details>

### Exercise 8
```ts
const cache = new Map<string, Buffer>();
export async function getAudio(textHash: string): Promise<Buffer> {
  if (cache.has(textHash)) return cache.get(textHash)!;
  const audio = await tts.synthesize(textHash);
  cache.set(textHash, audio);
  return audio;
}
```
<details><summary>Answer</summary>

- **Unbounded cache of large buffers → memory leak → OOM crash.** Every unique text adds audio forever.
- **Fix:** `new LRUCache<string, Buffer>({ maxSize: 200 * 1024 * 1024, sizeCalculation: b => b.length, ttl: 3600_000 })`.
- **Cache stampede:** concurrent misses for the same key all call TTS. Cache the in-flight *promise* (and evict it on rejection).
- Systems thinking: per-process caches don't share between instances. At scale, use a shared cache or object storage (S3/CDN) for audio.
</details>

### Exercise 9
```ts
export async function fetchTranscript(url: string) {
  for (let i = 0; i < 5; i++) {
    try {
      const r = await fetch(url);
      return await r.json();
    } catch {}
  }
}
```
<details><summary>Answer</summary>

- **No timeout:** one hung upstream ties up the request forever. **Fix:** `fetch(url, { signal: AbortSignal.timeout(5000) })`.
- **Retries without backoff** cause a thundering herd. Add exponential backoff with jitter.
- **Errors swallowed:** returns `undefined` after 5 failures; callers get `undefined` and crash later. Throw after the final attempt.
- **`r.ok` not checked:** a 500 HTML page → `json()` throws, or the error body is treated as data.
- If `url` is user-supplied: **SSRF**.
</details>

### Exercise 10
```ts
app.get('/files', requireAuth, (req, res) => {
  const file = path.join(__dirname, 'uploads', String(req.query.name));
  res.send(fs.readFileSync(file));
});
```
<details><summary>Answer</summary>

- **Path traversal:** `?name=../../.env` → reads secrets.
- **Sync read blocks the event loop**, and the whole file is loaded into memory.
- **Fix:** `const base = path.resolve(__dirname, 'uploads'); const p = path.resolve(base, name); if (!p.startsWith(base + path.sep)) return res.status(400).end();` then `res.sendFile(p)` or stream it. Also check ownership (an IDOR on files).
</details>

### Exercise 11
```ts
export async function hashPassword(pw: string) {
  return crypto.createHash('sha256').update(pw).digest('hex');
}
export async function checkApiKey(provided: string) {
  return provided === process.env.ADMIN_API_KEY;
}
```
<details><summary>Answer</summary>

- **Fast unsalted hash:** GPU brute force and rainbow tables. **Fix:** bcrypt or argon2 with **rehash-on-login migration** (§6). The DB already holds sha256 hashes, so you can't just swap the function.
- **Non-constant-time compare** leaks the key through timing. And if `ADMIN_API_KEY` is unset, `provided === undefined` could match when `provided` is also undefined. **Fix:** fail if the env var is missing; compare SHA-256 digests of both values with `crypto.timingSafeEqual` (equal length).
</details>

### Exercise 12
```jsonc
// tsconfig.base.json
{ "compilerOptions": { "strict": false, "skipLibCheck": true } }
// .github/workflows/ci.yml (excerpt)
- run: pnpm install
- run: pnpm build
- run: pnpm test || true
```
<details><summary>Answer</summary>

- **`pnpm test || true`:** CI can never fail on tests (the most severe here). Remove it.
- **`pnpm install` without `--frozen-lockfile`:** CI may resolve different versions from local. Add the flag.
- **No typecheck or lint step:** if the build uses esbuild/Vite/SWC, type errors are never caught. Add `pnpm -r typecheck`.
- **`strict: false`:** propose an incremental migration (per package). Don't flip it globally in the assessment unless the code already compiles strict.
- `skipLibCheck: true` is normal and fine. Don't "fix" it (and don't report false positives).
</details>

### Exercise 13
```ts
it('rejects invalid email', () => {
  expect(createUser({ email: 'nope' })).rejects.toThrow('invalid email');
});
it.only('creates a user', async () => {
  const u = await createUser({ email: 'a@b.co' });
  expect(u).toBeDefined();
});
```
<details><summary>Answer</summary>

- **`.only`:** every other test in the file (and possibly the suite) is skipped, so the green run is misleading.
- **Missing `await`** on `.rejects`: the test finishes before the promise settles, so it passes even if validation is broken. **Fix:** `await expect(...).rejects.toThrow(...)`.
- **Weak assertion:** `toBeDefined()` proves almost nothing. Assert on `u.email` and `u.id`.
</details>

### Exercise 14
```ts
app.use(cors({ origin: true, credentials: true }));
app.use((err: Error, _req: Request, res: Response, _next: NextFunction) => {
  res.status(500).json({ message: err.message, stack: err.stack });
});
const JWT_SECRET = process.env.JWT_SECRET || 'changeme';
```
<details><summary>Answer</summary>

- **CORS reflects any origin with credentials:** any website can make authenticated requests and read the responses. **Fix:** an allowlist from env.
- **Error handler leaks the stack** (paths, library versions, sometimes SQL). **Fix:** generic message plus a request ID to the client, full details to the logs.
- **Fallback secret:** if the env var is missing in prod, tokens are signed with `'changeme'`, so anyone can forge them. **Fix:** validate env at startup and crash if it's missing.
</details>

### Exercise 15
```
// firestore.rules
match /databases/{db}/documents {
  match /documents/{docId} {
    allow read, write: if request.auth != null;
  }
}
```
<details><summary>Answer</summary>

- **Any signed-in user can read, overwrite or delete everyone's documents.** This is BOLA at the rules layer (Critical).
- **Fix:** pin ownership, and stop it changing on update:
  ```
  allow read, delete: if request.auth.uid == resource.data.ownerId;
  allow create: if request.auth.uid == request.resource.data.ownerId
                && request.resource.data.keys().hasOnly(['ownerId', 'title', 'body']);
  allow update: if request.auth.uid == resource.data.ownerId
                && request.resource.data.ownerId == resource.data.ownerId;
  ```
  Better still, nest under `/users/{userId}/documents/{docId}` and check `request.auth.uid == userId` (ownership from the path, not from client-written data).
- Rollout: test with the emulator first. Clients that relied on cross-user reads (shared docs?) will break; check before you tighten.
</details>

### Exercise 16
```ts
app.post('/api/ask', requireAuth, async (req, res) => {
  const chunks = await vectorStore.similaritySearch(req.body.question, 5);
  const answer = await llm.complete(
    `${SYSTEM_PROMPT}\nContext:\n${chunks.map((c) => c.text).join('\n')}\nQ: ${req.body.question}`,
  );
  res.send(`<div class="answer">${answer}</div>`);
});
```
<details><summary>Answer</summary>

- **No tenant filter:** retrieval searches *every user's* documents, so one user's private content shows up in another's answer (Critical, BOLA). **Fix:** `similaritySearch(q, 5, { filter: { userId: req.user.id } })`.
- **XSS:** model output is interpolated into HTML. A prompt-injected document can make the model emit `<script>`. Return JSON and render it as text.
- **Prompt injection:** retrieved chunks and the question go straight into the prompt. Delimit them clearly as untrusted data, and keep secrets out of `SYSTEM_PROMPT`.
- **Unbounded consumption:** no length limit on `question`, no `max_tokens`, no per-user quota. Validate with zod and add quotas.
</details>

### Exercise 17
```ts
// deployed on Cloud Run, autoscaling 1–10 instances
app.set('trust proxy', true);
app.use('/api/login', rateLimit({ windowMs: 15 * 60 * 1000, limit: 5 }));
```
<details><summary>Answer</summary>

- **`trust proxy: true`** trusts the client-supplied `X-Forwarded-For`, so an attacker rotates fake IPs and gets unlimited login attempts. **Fix:** `app.set('trust proxy', 1)` (one Google front end hop).
- **In-memory store per instance:** the effective limit is 5 × instance count, and it resets on every scale event. **Fix:** a shared store (`rate-limit-redis`).
- Key by **account + IP** for login, so a botnet can't brute-force one account from many IPs.
</details>

### Exercise 18
```dockerfile
FROM node:latest
WORKDIR /app
COPY . .
RUN npm install
CMD npm start
```
```ts
process.on('SIGTERM', () => process.exit(0));
```
<details><summary>Answer</summary>

- `COPY . .` with no `.dockerignore` → **`.env` is baked into the image** (secret leak), plus `.git` and local `node_modules`.
- `node:latest` is unreproducible. `npm install` in a **pnpm** repo ignores the lockfile. It's single-stage (ships devDeps) and runs as root.
- **Shell-form `CMD npm start`:** node isn't PID 1 and **never receives SIGTERM**.
- **The SIGTERM handler exits immediately** and drops in-flight requests. **Fix:** `server.close()` + `closeIdleConnections()`, close pools, with a hard timeout under Cloud Run's 10 s.
- **Fix:** pinned base, multi-stage build, `corepack enable && pnpm install --frozen-lockfile`, `USER node`, `CMD ["node", "dist/server.js"]`, and a `.dockerignore`.
</details>

### Exercise 19
```ts
app.get('/api/users/:id', requireAuth, async (req, res) => {
  const user = await prisma.user.findUnique({ where: { id: req.params.id } });
  res.json(user);
});
```
<details><summary>Answer</summary>

- **Excessive data exposure (API3):** returns `passwordHash`, `email`, `stripeCustomerId`, and so on, for **any** user ID.
- **Fix:** `select: { id: true, displayName: true, avatarUrl: true }`. If it's the caller's own profile, use a separate `/api/me` that returns more.
- `null` → `res.json(null)` with 200; should be 404.
- **Compatibility:** grep the frontend for fields you remove before removing them, and mention that check in the commit.
</details>

### Exercise 20
```tsx
function VoiceSearch({ query }: { query: string }) {
  const [voices, setVoices] = useState<Voice[]>([]);
  useEffect(() => {
    fetch(`/api/voices?q=${query}`).then((r) => r.json()).then(setVoices);
  }, [query]);
  return <VoiceList voices={voices} />;
}
```
<details><summary>Answer</summary>

- **Race:** typing "ab" then "abc" fires two requests. If "ab" resolves last, stale results overwrite the fresh ones.
- **Fix:**
  ```tsx
  useEffect(() => {
    const controller = new AbortController();
    fetch(`/api/voices?q=${encodeURIComponent(query)}`, { signal: controller.signal })
      .then((r) => { if (!r.ok) throw new Error(`HTTP ${r.status}`); return r.json(); })
      .then(setVoices)
      .catch((e) => { if (e.name !== 'AbortError') setError(e); });
    return () => controller.abort();
  }, [query]);
  ```
- Also: `query` isn't URL-encoded (`&` breaks it), `r.ok` isn't checked, and there's no debounce (a request on every keystroke).
</details>

### Exercise 21
```ts
// migrations/20261001_add_users_email_index.ts  (users: 40M rows)
export async function up(knex: Knex) {
  await knex.raw('CREATE INDEX idx_users_email ON users (email)');
}
```
<details><summary>Answer</summary>

- A plain `CREATE INDEX` **blocks all writes to `users`** for the whole build. On 40M rows that's minutes of failed signups and logins.
- **Fix:** `CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_users_email ON users (email)` **and** `export const config = { transaction: false };`, because `CONCURRENTLY` can't run inside the transaction Knex wraps each migration in.
- If it fails midway it leaves an `INVALID` index: detect it in `pg_index` (`indisvalid = false`), then drop and retry.
- Proof: `EXPLAIN ANALYZE SELECT ... WHERE email = $1` before (Seq Scan) and after (Index Scan).
</details>

### Practice on real code
After the snippets, do **2–3 timed 90-minute mock runs** on unfamiliar repos, following the full game plan (FINDINGS.md, atomic commits):
- **OWASP NodeGoat** (`github.com/OWASP/NodeGoat`): deliberately vulnerable Node/Express app; good for security.
- **OWASP Juice Shop** (`github.com/juice-shop/juice-shop`): large TypeScript + Angular + Express app. Practise *scoping*: pick one area and audit it.
- **A "RealWorld" (Conduit) backend in TS** (`github.com/gothinkster/realworld`, choose a Node/TS implementation): realistic code with no planted bugs. Hunt for N+1s, missing authz, and pagination problems.
- Ask Claude to **plant bugs for you:** "Take this small TS Express+React repo and introduce 8 subtle issues across security, perf, reliability, tooling, and tests. Commit them without describing them in messages, and give me the answer key in a separate file I won't open." This is the closest thing to the real assessment.

---

## 9. Red flags to avoid

- ❌ Starting to change code before you've read the structure and run the baseline tests
- ❌ Large diffs: reformatting, renames, "while I'm here" refactors, upgrading libraries
- ❌ Breaking an existing test, or "fixing" a test to match your new behaviour without explaining why
- ❌ Changing API response shapes or status codes that clients depend on (unless that *is* the bug, and then say so)
- ❌ Multiple issues in one commit; commit messages like "fixes"
- ❌ Symptom fixes: wrapping a crash in try/catch instead of fixing the bad input, or bumping a timeout instead of fixing the N+1
- ❌ Reporting AI false positives without checking them
- ❌ Running out of time with nothing written down. Document as you go.
- ❌ Silently skipping the tooling and test categories. They are explicitly in scope.
- ❌ **Classic false positives:** "missing asyncHandler" on Express 5; CSRF on a bearer-token API; "`skipLibCheck` is insecure"; "SQL injection" on a parameterised query; IDOR where the query is already scoped by a middleware you didn't read.
- ❌ Using AI when your variant's README bans it.
- ❌ Rewriting git history (filter-repo, force-push) during the assessment.
- ❌ Waiting on the AI audit instead of reading in parallel.

---

## 10. Prep schedule and day-of checklist

### Suggested prep (adjust to your interview date)

| Day | Focus |
|---|---|
| 1 | Finish §2 setup (including the proctoring and AI-policy questions to the recruiter). Read §5 fully. Do Exercises 1–7. |
| 2 | Exercises 8–14. Read §6 and §7. Write `FINDINGS.md` entries for 3 of the exercises as practice. |
| 3 | Exercises 15–21. Practise the §3 grep sweep on an unfamiliar repo (25-min cap). Timed mock run #1 (NodeGoat or the Claude-planted repo). Review: what did you miss? Where did time go? |
| 4 | Skim the OWASP API Security Top 10 2023 and LLM Top 10 2025 (30 min). One "prove it" perf drill (§5.2: create an N+1, measure, fix, re-measure). Mock run #2. |
| 5 | §11 drill: LRU cache + SSML parser, no AI, 45 min. Re-read §3 (game plan) and §9 (red flags). Check that setup still works. |

**Mock-run success metrics:** triage finished by minute 30 · ≥3 atomic fix commits, all tests green · FINDINGS lists ≥3 unfixed items with rollout notes · zero unverified AI claims in commits.

### Day of

- [ ] Node 22, pnpm, git SSH, editor, Chrome, and AI tools all checked within the last 24h
- [ ] Inline autocomplete in the agreed state (see §2)
- [ ] Two terminals ready (one for you, one for AI)
- [ ] This guide's §3 timeline and §7 templates open on a second screen
- [ ] Water nearby, notifications off
- [ ] Minute 0: `FINDINGS.md` created and baseline tests recorded
- [ ] Minute 25: triage done
- [ ] Minute 80: **stop fixing**, start the wrap-up

---

## 11. If the format differs

Some candidates got a different first round: **implement an LRU cache, an SSML parser, and an SSML-to-text function**, with tests provided, in 90 minutes. That version was reported in Java, with **no AI tools and no external libraries** (including no XML parser). Your prep guide describes the audit format, but read the README rules on the day. This drill is also good practice for round 2 (live coding: "performance and clean code").

### LRU cache (target: 10 minutes)
```ts
export class LRUCache<K, V> {
  private readonly entries = new Map<K, V>();

  constructor(private readonly capacity: number) {
    if (!Number.isInteger(capacity) || capacity < 1) {
      throw new RangeError(`capacity must be a positive integer, got ${capacity}`);
    }
  }

  get(key: K): V | undefined {
    if (!this.entries.has(key)) return undefined;
    const value = this.entries.get(key) as V;
    this.entries.delete(key); // move to most-recently-used position
    this.entries.set(key, value);
    return value;
  }

  set(key: K, value: V): void {
    if (this.entries.has(key)) {
      this.entries.delete(key);
    } else if (this.entries.size >= this.capacity) {
      const leastRecent = this.entries.keys().next().value as K; // Map iterates in insertion order
      this.entries.delete(leastRecent);
    }
    this.entries.set(key, value);
  }
}
```
**Talking points:** O(1) `get`/`set` because a `Map` keeps insertion order. The classic alternative is hashmap + doubly linked list (be ready to write it if they ban the `Map`-order trick). Edge cases: capacity 0 or negative, updating an existing key also refreshes its recency, and `undefined` stored as a value (use `has`, not a truthy check).

### SSML parser (target: 30 minutes)
Spec to practise: parse `<speak>Hello <break time="500ms"/> <emphasis level="strong">world</emphasis> &amp; more</speak>` into a tree; reject malformed input; then flatten the tree to plain text.

```ts
export interface SSMLElement {
  name: string;
  attributes: Record<string, string>;
  children: SSMLNode[];
}
export type SSMLNode = SSMLElement | string;

const ENTITIES: Record<string, string> = { '&lt;': '<', '&gt;': '>', '&quot;': '"', '&apos;': "'", '&amp;': '&' };
const unescapeText = (raw: string): string => raw.replace(/&(lt|gt|quot|apos|amp);/g, (m) => ENTITIES[m]);

export function parseSSML(input: string): SSMLElement {
  let pos = 0;

  const parseText = (): string => {
    const next = input.indexOf('<', pos);
    const end = next === -1 ? input.length : next;
    const raw = input.slice(pos, end);
    pos = end;
    return unescapeText(raw);
  };

  const parseElement = (): SSMLElement => {
    const close = input.indexOf('>', pos);
    if (close === -1) throw new SyntaxError(`Unterminated tag at ${pos}`);
    const body = input.slice(pos + 1, close).trim();
    pos = close + 1;

    const selfClosing = body.endsWith('/');
    const tag = selfClosing ? body.slice(0, -1).trim() : body;
    const name = tag.match(/^[A-Za-z][\w:-]*/)?.[0];
    if (!name) throw new SyntaxError(`Invalid tag name near ${pos}`);

    const attributes: Record<string, string> = {};
    for (const [, key, value] of tag.slice(name.length).matchAll(/([\w:-]+)\s*=\s*"([^"]*)"/g)) {
      attributes[key] = unescapeText(value);
    }
    if (selfClosing) return { name, attributes, children: [] };

    const children: SSMLNode[] = [];
    while (!input.startsWith('</', pos)) {
      if (pos >= input.length) throw new SyntaxError(`Missing </${name}>`);
      children.push(input[pos] === '<' ? parseElement() : parseText());
    }
    const endClose = input.indexOf('>', pos);
    if (endClose === -1) throw new SyntaxError(`Unterminated closing tag for <${name}>`);
    const closingName = input.slice(pos + 2, endClose).trim();
    if (closingName !== name) throw new SyntaxError(`Expected </${name}>, got </${closingName}>`);
    pos = endClose + 1;
    return { name, attributes, children };
  };

  input = input.trim();
  if (!input.startsWith('<speak')) throw new SyntaxError('Root element must be <speak>');
  const root = parseElement();
  if (root.name !== 'speak') throw new SyntaxError('Root element must be <speak>');
  if (pos !== input.length) throw new SyntaxError(`Unexpected content after </speak> at ${pos}`);
  return root;
}

export function ssmlToText(node: SSMLNode): string {
  return typeof node === 'string' ? node : node.children.map(ssmlToText).join('');
}
```
**Talking points:** single pass, O(n); recursion depth equals nesting depth (an explicit stack if they worry about deep input); attribute values in single quotes aren't supported (say so, or add `'([^']*)'`); unknown entities pass through unchanged; `<break>` contributes no text. **Write the edge-case tests out loud:** mismatched tags, missing close, text after the root, attributes with `=` inside quotes, empty `<speak/>`.

---

## 12. The feedback stage

One Platform posting describes a **two-stage** assessment: you submit, **an engineer from the team gives real feedback**, and you get time to act on it. If you get that:

- **Answer every comment.** Reply "fixed in `<sha>`", "disagree because…" (with evidence), or "agreed, out of scope, added to FINDINGS". Never leave one silently.
- **Add follow-up commits; don't force-push** over reviewed history. The reviewer needs to see what changed since their review. If they ask for a squash, do it at the end.
- **Re-run the full test suite** after every follow-up commit, and say so.
- **Pushback is fine when you have evidence** ("Express 5 forwards async errors to the error middleware, see the router docs; the wrapper would be dead code"). Being able to disagree with evidence is a senior signal.
- Update FINDINGS.md so the final version stands on its own.
- Expect your submission to come up again in **round 2 and with the EM**. Be ready to explain every commit, including the AI-assisted ones, in about 3 minutes each.
