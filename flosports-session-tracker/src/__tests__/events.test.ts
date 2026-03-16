import { beforeEach, describe, expect, it } from 'vitest'
import request from 'supertest'
import app from '../app'
import { sessionRepository } from '../repositories/session.repository'

const SESSION_ID = '123e4567-e89b-12d3-a456-426614174000'

const validPayload = {
  sessionId: SESSION_ID,
  userId: 'user-1',
  eventId: 'stream-100',
  eventType: 'start',
  eventTimestamp: '2026-03-16T00:00:00.000Z',
  receivedAt: '2026-03-16T00:00:00.000Z',
  payload: { position: 0, quality: '1080p' },
}

describe('POST /api/events', () => {
  beforeEach(() => {
    sessionRepository._clear()
  })

  it('returns 202 with sessionId and state for a valid start event', async () => {
    const res = await request(app).post('/api/events').send(validPayload)
    expect(res.status).toBe(202)
    expect(res.body.sessionId).toBe(SESSION_ID)
    expect(res.body.state).toBe('active')
  })

  it('returns 400 when sessionId is missing', async () => {
    const { sessionId: _omit, ...body } = validPayload
    const res = await request(app).post('/api/events').send(body)
    expect(res.status).toBe(400)
  })

  it('returns 400 when sessionId is not a UUID', async () => {
    const res = await request(app).post('/api/events').send({ ...validPayload, sessionId: 'not-a-uuid' })
    expect(res.status).toBe(400)
  })

  it('returns 400 when eventType is invalid', async () => {
    const res = await request(app).post('/api/events').send({ ...validPayload, eventType: 'unknown_event' })
    expect(res.status).toBe(400)
  })

  it('returns 400 when payload is missing position', async () => {
    const res = await request(app)
      .post('/api/events')
      .send({ ...validPayload, payload: { quality: '1080p' } })
    expect(res.status).toBe(400)
  })

  it('returns 400 when eventTimestamp is not a valid datetime', async () => {
    const res = await request(app)
      .post('/api/events')
      .send({ ...validPayload, eventTimestamp: 'not-a-date' })
    expect(res.status).toBe(400)
  })

  it('returns 202 and updates state on heartbeat for existing session', async () => {
    await request(app).post('/api/events').send(validPayload)
    const res = await request(app)
      .post('/api/events')
      .send({ ...validPayload, eventType: 'heartbeat', eventTimestamp: '2026-03-16T00:00:30.000Z' })
    expect(res.status).toBe(202)
    expect(res.body.state).toBe('active')
  })

  it('returns 202 and state is paused after pause event', async () => {
    await request(app).post('/api/events').send(validPayload)
    const res = await request(app)
      .post('/api/events')
      .send({ ...validPayload, eventType: 'pause', eventTimestamp: '2026-03-16T00:01:00.000Z' })
    expect(res.status).toBe(202)
    expect(res.body.state).toBe('paused')
  })
})
