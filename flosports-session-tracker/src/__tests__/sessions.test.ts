import { beforeEach, describe, expect, it } from 'vitest'
import request from 'supertest'
import app from '../app'
import { sessionRepository } from '../repositories/session.repository'

const SESSION_ID = '123e4567-e89b-12d3-a456-426614174000'
const EVENT_ID = 'stream-100'

// Static timestamps for duration/state tests (order of events matters, not recency)
const FIXED_START_TS = '2026-03-16T00:00:00.000Z'

const startEvent = {
  sessionId: SESSION_ID,
  userId: 'user-1',
  eventId: EVENT_ID,
  eventType: 'start',
  eventTimestamp: FIXED_START_TS,
  receivedAt: FIXED_START_TS,
  payload: { position: 0, quality: '1080p' },
}

// Builds a start event with a fresh timestamp so the session is within the 90s active window
function recentStartEvent() {
  const now = new Date().toISOString()
  return { ...startEvent, eventTimestamp: now, receivedAt: now }
}

describe('GET /api/streams/:eventId/viewers', () => {
  beforeEach(() => {
    sessionRepository._clear()
  })

  it('returns 200 with activeViewers and timestamp', async () => {
    await request(app).post('/api/events').send(recentStartEvent())
    const res = await request(app).get(`/api/streams/${EVENT_ID}/viewers`)
    expect(res.status).toBe(200)
    expect(res.body.eventId).toBe(EVENT_ID)
    expect(res.body.activeViewers).toBe(1)
    expect(res.body.timestamp).toBeDefined()
  })

  it('excludes ended sessions from viewer count', async () => {
    await request(app).post('/api/events').send(startEvent)
    await request(app)
      .post('/api/events')
      .send({ ...startEvent, eventType: 'end', eventTimestamp: '2026-03-16T00:01:00.000Z' })
    const res = await request(app).get(`/api/streams/${EVENT_ID}/viewers`)
    expect(res.status).toBe(200)
    expect(res.body.activeViewers).toBe(0)
  })

  it('returns 0 viewers for an unknown eventId', async () => {
    const res = await request(app).get('/api/streams/no-such-stream/viewers')
    expect(res.status).toBe(200)
    expect(res.body.activeViewers).toBe(0)
  })
})

describe('GET /api/sessions/:sessionId', () => {
  beforeEach(() => {
    sessionRepository._clear()
  })

  it('returns 200 with full session detail', async () => {
    await request(app).post('/api/events').send(startEvent)
    const res = await request(app).get(`/api/sessions/${SESSION_ID}`)
    expect(res.status).toBe(200)
    expect(res.body.sessionId).toBe(SESSION_ID)
    expect(res.body.userId).toBe('user-1')
    expect(res.body.eventId).toBe(EVENT_ID)
    expect(res.body.state).toBe('active')
    expect(res.body.eventCount).toBe(1)
    expect(typeof res.body.durationMs).toBe('number')
    expect(Array.isArray(res.body.events)).toBe(true)
  })

  it('returns 404 for an unknown sessionId', async () => {
    const res = await request(app).get('/api/sessions/123e4567-e89b-12d3-a456-426614174099')
    expect(res.status).toBe(404)
  })

  it('includes durationMs based on endedAt for an ended session', async () => {
    await request(app).post('/api/events').send(startEvent)
    await request(app)
      .post('/api/events')
      .send({ ...startEvent, eventType: 'end', eventTimestamp: '2026-03-16T00:03:00.000Z' })
    const res = await request(app).get(`/api/sessions/${SESSION_ID}`)
    expect(res.status).toBe(200)
    expect(res.body.state).toBe('ended')
    expect(res.body.durationMs).toBe(180_000) // 3 minutes
  })
})
