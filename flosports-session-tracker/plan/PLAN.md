# FloSports Session Tracker — Implementation Plan

## Context
FloSports needs a real-time watch session service to replace their hourly batch pipeline. The goal is to track concurrent viewers and session patterns with ~10-15s latency. This plan implements the full service on top of the existing Express/TypeScript starter template (health check, Swagger, Vitest already wired up).

---

## Architecture
`routes → controllers → services → repositories`

- **Storage**: In-memory Map (v1 per requirements)
- **Session lifecycle**: driven by SDK events (`start`, `heartbeat`, `pause`, `resume`, `seek`, `quality_change`, `buffer_start`, `buffer_end`, `end`)
- **Active session heuristic**: last event received < 90s ago (3× heartbeat interval of 30s)
- **Validation**: Zod schemas at the controller boundary

---

## Milestone 1 — Types & In-Memory Repository
**Goal**: Define the domain model and storage layer.

### Files to create
- `src/types/session.ts` — all domain types (branded types, discriminated unions)
- `src/repositories/session.repository.ts` — in-memory Map CRUD for sessions

### Key types
```ts
type EventType = 'start' | 'heartbeat' | 'pause' | 'resume' | 'seek' | 'quality_change' | 'buffer_start' | 'buffer_end' | 'end'

type SessionState = 'active' | 'paused' | 'buffering' | 'ended'

interface WatchEvent {
  eventId: string
  sessionId: string
  userId: string
  eventType: EventType
  eventTimestamp: string
  receivedAt: string
  payload: { position: number; quality: string }
}

interface WatchSession {
  sessionId: string
  userId: string
  eventId: string          // the content/stream being watched
  state: SessionState
  startedAt: string
  lastEventAt: string
  endedAt?: string
  events: WatchEvent[]
}
```

### Repository methods
- `upsertSession(session)`, `getSession(sessionId)`, `getAllSessions()`, `getSessionsByEventId(eventId)`

**Status**: [x] Complete — 2026-03-16

### Completion notes
- `src/types/session.ts` — branded types (`SessionId`, `UserId`, `EventId`) plus `EventType`, `SessionState`, `WatchEvent`, `WatchSession` interfaces
- `src/repositories/session.repository.ts` — singleton `sessionRepository` object wrapping a module-level `Map<SessionId, WatchSession>`; exposes `upsertSession`, `getSession`, `getAllSessions`, `getSessionsByEventId`, and `_clear` (test helper)
- No external dependencies added; pure TypeScript

---

## Milestone 2 — Event Ingestion Endpoint
**Goal**: Accept and process player SDK events, maintain session state.

### Files to create/modify
- `src/routes/events.routes.ts`
- `src/controllers/events.controller.ts`
- `src/services/session.service.ts`
- `src/middleware/errorHandler.ts` (centralized error middleware)
- `src/app.ts` — wire in events router and error handler

### Endpoint
`POST /api/events`

#### Zod schema
```ts
const WatchEventSchema = z.object({
  sessionId: z.string().uuid(),
  userId: z.string(),
  eventId: z.string(),
  eventType: z.enum(['start','heartbeat','pause','resume','seek','quality_change','buffer_start','buffer_end','end']),
  eventTimestamp: z.string().datetime(),
  receivedAt: z.string().datetime(),
  payload: z.object({ position: z.number(), quality: z.string() })
})
```

#### Session state transitions (in `session.service.ts`)
| Event type | New session state |
|---|---|
| start | active |
| heartbeat, resume, seek, quality_change | active |
| pause | paused |
| buffer_start | buffering |
| buffer_end | active |
| end | ended |

#### Response
- `202 Accepted` with `{ sessionId, state }`

**Status**: [x] Complete — 2026-03-16

### Completion notes
- `zod@4.3.6` added as a dependency (`pnpm add zod`)
- `src/middleware/errorHandler.ts` — catches `ZodError` → 400 with `issues`; all other errors → 500
- `src/services/session.service.ts` — `sessionService.processEvent(event)`: `start` event (or no existing session) creates a fresh session; all other events upsert state via `nextState()` switch; `endedAt` set only when transitioning to `ended`
- `src/controllers/events.controller.ts` — `WatchEventSchema` (Zod) validates request body; branded type casts applied at controller boundary; responds `202 { sessionId, state }`
- `src/routes/events.routes.ts` — `POST /api/events` → `ingestEvent`
- `src/app.ts` — wired in `eventsRoutes` and `errorHandler` (error handler registered last)
- `tsc --noEmit` passes with zero errors

---

## Milestone 3 — Query Endpoints
**Goal**: Expose concurrent viewer count and session detail.

### Files to create/modify
- `src/routes/sessions.routes.ts`
- `src/controllers/sessions.controller.ts`
- `src/services/session.service.ts` — add query methods

### Endpoints

#### `GET /api/streams/:eventId/viewers`
Returns concurrent viewer count for a given content event.

**Active session filter**: `state !== 'ended'` AND `lastEventAt > now - 90s`

Response: `{ eventId, activeViewers: number, timestamp: string }`

#### `GET /api/sessions/:sessionId`
Returns full session detail.

Response:
```json
{
  "sessionId": "...",
  "userId": "...",
  "eventId": "...",
  "state": "active",
  "durationMs": 120000,
  "startedAt": "...",
  "lastEventAt": "...",
  "eventCount": 5,
  "events": [...]
}
```

`durationMs` = `(lastEventAt - startedAt)` for in-progress sessions; `(endedAt - startedAt)` for ended.

**Status**: [x] Complete — 2026-03-16

### Completion notes
- `src/services/session.service.ts` — added `getActiveViewerCount(eventId)` (filters `state !== 'ended'` AND `lastEventAt > now - 90s`) and `getSessionDetail(sessionId)` (returns session + computed `durationMs` and `eventCount`; uses `endedAt` when present, else `lastEventAt`)
- `src/controllers/sessions.controller.ts` — `getViewers` → 200 `{ eventId, activeViewers, timestamp }`; `getSession` → 200 full detail or 404 `{ error }`
- `src/routes/sessions.routes.ts` — `GET /api/streams/:eventId/viewers` and `GET /api/sessions/:sessionId`
- `src/app.ts` — wired in `sessionsRoutes` before `errorHandler`
- `tsc --noEmit` passes with zero errors

---

## Milestone 4 — Tests
**Goal**: Meaningful test coverage of core logic and API surface.

### Files to create
- `src/__tests__/session.service.test.ts` — unit tests for state machine and duration
- `src/__tests__/events.test.ts` — integration tests for POST /api/events
- `src/__tests__/sessions.test.ts` — integration tests for GET endpoints

### Key test cases
- `start` event creates a new session in `active` state
- `pause` transitions active → paused
- `end` transitions any state → ended
- Duplicate `sessionId` with `heartbeat` updates `lastEventAt`, doesn't create new session
- Viewer count excludes ended sessions
- Viewer count excludes sessions with `lastEventAt` > 90s ago
- 400 returned for malformed event payload
- 404 returned for unknown sessionId

**Status**: [x] Complete — 2026-03-16

### Completion notes
- `src/__tests__/session.service.test.ts` — 14 unit tests covering the state machine (`start`, `pause`, `buffer_start`, `buffer_end`, `end`), the duplicate `start` reset, `heartbeat` updating `lastEventAt`, `getActiveViewerCount` (excludes ended and stale sessions), and `getSessionDetail` (`durationMs` using `lastEventAt` vs `endedAt`)
- `src/__tests__/events.test.ts` — 8 integration tests for `POST /api/events`: 202 on valid payloads, 400 for missing `sessionId`, non-UUID `sessionId`, invalid `eventType`, missing payload field, invalid `eventTimestamp`, plus state transitions via heartbeat and pause
- `src/__tests__/sessions.test.ts` — 6 integration tests for `GET /api/streams/:eventId/viewers` (viewer count, excludes ended sessions, unknown eventId) and `GET /api/sessions/:sessionId` (full detail, 404 unknown, `durationMs` from `endedAt`)
- **Gotcha**: Zod v4 (`4.3.6`) enforces strict RFC 4122 UUID validation (version nibble must be `[1-8]`); test UUIDs must be valid (e.g. `123e4567-e89b-12d3-a456-426614174000`)
- **Gotcha**: viewer-count tests must use `new Date().toISOString()` for event timestamps so sessions fall within the 90s active window; fixed past timestamps are fine for duration/state tests only
- All 29 tests pass (`pnpm test`)

---

## Milestone 5 — README & Polish
**Goal**: Satisfy acceptance criteria and document the project.

### Files to create/modify
- `README.md`

### README sections
1. **Quick start** — single command to start, single command to test
2. **API overview** — endpoint list with example curl commands
3. **Assumptions** — in-memory storage, 90s activity window, UTC timestamps
4. **Tools & AI used** — Claude Code for scaffolding and implementation
5. **Trade-offs** — no persistence, no deduplication, no auth, no horizontal scaling

**Status**: [x] Complete — 2026-03-16

### Completion notes
- `README.md` created at repo root with all five required sections
- **Quick start**: `pnpm install`, `pnpm dev`, `pnpm test`
- **API overview**: all three domain endpoints (`POST /api/events`, `GET /api/streams/:eventId/viewers`, `GET /api/sessions/:sessionId`) plus `/health`, each with a copy-pasteable `curl` example; Swagger UI noted at `/api-docs`
- **Assumptions**: in-memory storage, 90s activity window, UTC timestamps, single-process, no auth
- **Tools & AI used**: Claude Code via plan-driven workflow; references `plan/PLAN.md`
- **Trade-offs**: formatted as a table covering persistence, deduplication, auth, scaling, rate limiting, and observability

---

## Verification

```bash
# Start
pnpm dev

# Run all tests
pnpm test

# Smoke test ingestion
curl -X POST http://localhost:3000/api/events \
  -H "Content-Type: application/json" \
  -d '{"sessionId":"<uuid>","userId":"u1","eventId":"stream-123","eventType":"start","eventTimestamp":"2026-03-16T00:00:00Z","receivedAt":"2026-03-16T00:00:00Z","payload":{"position":0,"quality":"1080p"}}'

# Query viewers
curl http://localhost:3000/api/streams/stream-123/viewers

# Query session
curl http://localhost:3000/api/sessions/<uuid>
```
