# FloSports Session Tracker

A real-time watch session microservice that replaces FloSports' hourly batch pipeline. Tracks concurrent viewers and session patterns with ~10–15s latency.

---

## Quick Start

```bash
# Install dependencies
pnpm install

# Start the dev server (http://localhost:3000)
pnpm dev

# Run all tests
pnpm test
```

---

## API Overview

### POST /api/events
Ingest a player SDK event. Creates or updates a watch session.

```bash
curl -X POST http://localhost:3000/api/events \
  -H "Content-Type: application/json" \
  -d '{
    "sessionId": "123e4567-e89b-12d3-a456-426614174000",
    "userId": "user-42",
    "eventId": "stream-nfl-championship",
    "eventType": "start",
    "eventTimestamp": "2026-03-16T00:00:00Z",
    "receivedAt": "2026-03-16T00:00:00Z",
    "payload": { "position": 0, "quality": "1080p" }
  }'
# 202 Accepted → { "sessionId": "...", "state": "active" }
```

Supported `eventType` values: `start`, `heartbeat`, `pause`, `resume`, `seek`, `quality_change`, `buffer_start`, `buffer_end`, `end`

### GET /api/streams/:eventId/viewers
Returns the concurrent viewer count for a content stream.

```bash
curl http://localhost:3000/api/streams/stream-nfl-championship/viewers
# 200 OK → { "eventId": "stream-nfl-championship", "activeViewers": 3, "timestamp": "..." }
```

### GET /api/sessions/:sessionId
Returns full detail for a watch session.

```bash
curl http://localhost:3000/api/sessions/123e4567-e89b-12d3-a456-426614174000
# 200 OK →
# {
#   "sessionId": "...",
#   "userId": "...",
#   "eventId": "...",
#   "state": "active",
#   "durationMs": 120000,
#   "startedAt": "...",
#   "lastEventAt": "...",
#   "eventCount": 5,
#   "events": [...]
# }
```

### GET /health
Liveness check.

```bash
curl http://localhost:3000/health
# 200 OK → { "status": "ok" }
```

Interactive docs are also available at `http://localhost:3000/api-docs` (Swagger UI).

---

## Assumptions

- **In-memory storage**: Sessions live in a module-level `Map`. All data is lost on restart. This matches the v1 requirements; a real deployment would swap in Redis or a database.
- **90-second activity window**: A session is counted as active if its `lastEventAt` is within 90 seconds of the current time (3× the expected heartbeat interval of 30s).
- **UTC timestamps**: All `eventTimestamp` and `receivedAt` fields must be ISO 8601 UTC strings (e.g. `2026-03-16T00:00:00Z`).
- **Single process**: The service is stateless per-process with no shared cache, so horizontal scaling is not supported without an external store.
- **No authentication**: Endpoints are unauthenticated. Auth would be added as Express middleware before the routes in a production deployment.

---

## Tools & AI Used

- **Claude Code** (Anthropic) — used throughout for scaffolding, implementation, and test generation via an iterative plan-driven workflow. The full implementation plan lives in `plan/PLAN.md`.

---

## Trade-offs & Known Limitations

| Area | Current behavior | Production alternative |
|---|---|---|
| Persistence | In-memory `Map` | Redis / PostgreSQL |
| Deduplication | None — replayed events are appended | Idempotency key per `eventId` |
| Authentication | None | JWT / API key middleware |
| Horizontal scaling | Single process only | Shared external store |
| Rate limiting | None | `express-rate-limit` |
| Observability | Console logs only | Structured logging + metrics |
