import { beforeEach, describe, expect, it } from 'vitest'
import { sessionRepository } from '../repositories/session.repository'
import { sessionService } from '../services/session.service'
import type { EventId, SessionId, UserId, WatchEvent } from '../types/session'

const SESSION_ID = '00000000-0000-0000-0000-000000000001' as unknown as SessionId
const USER_ID = 'user-1' as unknown as UserId
const EVENT_ID = 'stream-100' as unknown as EventId

function makeEvent(eventType: WatchEvent['eventType'], overrides: Partial<WatchEvent> = {}): WatchEvent {
  return {
    eventId: EVENT_ID as unknown as string,
    sessionId: SESSION_ID,
    userId: USER_ID,
    eventType,
    eventTimestamp: new Date().toISOString(),
    receivedAt: new Date().toISOString(),
    payload: { position: 0, quality: '1080p' },
    ...overrides,
  }
}

describe('sessionService', () => {
  beforeEach(() => {
    sessionRepository._clear()
  })

  describe('processEvent', () => {
    it('start event creates a new session in active state', () => {
      const session = sessionService.processEvent(makeEvent('start'))
      expect(session.state).toBe('active')
      expect(session.events).toHaveLength(1)
    })

    it('pause transitions active → paused', () => {
      sessionService.processEvent(makeEvent('start'))
      const session = sessionService.processEvent(makeEvent('pause'))
      expect(session.state).toBe('paused')
    })

    it('buffer_start transitions → buffering', () => {
      sessionService.processEvent(makeEvent('start'))
      const session = sessionService.processEvent(makeEvent('buffer_start'))
      expect(session.state).toBe('buffering')
    })

    it('end transitions any state → ended and sets endedAt', () => {
      sessionService.processEvent(makeEvent('start'))
      sessionService.processEvent(makeEvent('pause'))
      const endTs = '2026-03-16T01:00:00.000Z'
      const session = sessionService.processEvent(makeEvent('end', { eventTimestamp: endTs }))
      expect(session.state).toBe('ended')
      expect(session.endedAt).toBe(endTs)
    })

    it('heartbeat updates lastEventAt without creating a new session', () => {
      const t1 = '2026-03-16T00:00:00.000Z'
      const t2 = '2026-03-16T00:00:30.000Z'
      sessionService.processEvent(makeEvent('start', { eventTimestamp: t1 }))
      const session = sessionService.processEvent(makeEvent('heartbeat', { eventTimestamp: t2 }))
      expect(session.lastEventAt).toBe(t2)
      expect(session.events).toHaveLength(2)
    })

    it('duplicate start creates a fresh session, discarding prior events', () => {
      sessionService.processEvent(makeEvent('start'))
      sessionService.processEvent(makeEvent('heartbeat'))
      const session = sessionService.processEvent(makeEvent('start'))
      expect(session.events).toHaveLength(1)
      expect(session.state).toBe('active')
    })

    it('buffer_end transitions buffering → active', () => {
      sessionService.processEvent(makeEvent('start'))
      sessionService.processEvent(makeEvent('buffer_start'))
      const session = sessionService.processEvent(makeEvent('buffer_end'))
      expect(session.state).toBe('active')
    })
  })

  describe('getActiveViewerCount', () => {
    it('counts active sessions for a given eventId', () => {
      sessionService.processEvent(makeEvent('start'))
      expect(sessionService.getActiveViewerCount(EVENT_ID)).toBe(1)
    })

    it('excludes ended sessions', () => {
      sessionService.processEvent(makeEvent('start'))
      sessionService.processEvent(makeEvent('end'))
      expect(sessionService.getActiveViewerCount(EVENT_ID)).toBe(0)
    })

    it('excludes sessions with lastEventAt older than 90s', () => {
      const staleTs = new Date(Date.now() - 91_000).toISOString()
      sessionService.processEvent(makeEvent('start', { eventTimestamp: staleTs }))
      expect(sessionService.getActiveViewerCount(EVENT_ID)).toBe(0)
    })

    it('returns 0 for an eventId with no sessions', () => {
      expect(sessionService.getActiveViewerCount('unknown' as unknown as EventId)).toBe(0)
    })
  })

  describe('getSessionDetail', () => {
    it('returns undefined for unknown sessionId', () => {
      expect(sessionService.getSessionDetail('unknown' as unknown as SessionId)).toBeUndefined()
    })

    it('computes durationMs using lastEventAt for an in-progress session', () => {
      const start = '2026-03-16T00:00:00.000Z'
      const last = '2026-03-16T00:02:00.000Z' // 120 000 ms
      sessionService.processEvent(makeEvent('start', { eventTimestamp: start }))
      sessionService.processEvent(makeEvent('heartbeat', { eventTimestamp: last }))
      const detail = sessionService.getSessionDetail(SESSION_ID)
      expect(detail?.durationMs).toBe(120_000)
      expect(detail?.eventCount).toBe(2)
    })

    it('computes durationMs using endedAt for an ended session', () => {
      const start = '2026-03-16T00:00:00.000Z'
      const end = '2026-03-16T00:05:00.000Z' // 300 000 ms
      sessionService.processEvent(makeEvent('start', { eventTimestamp: start }))
      sessionService.processEvent(makeEvent('end', { eventTimestamp: end }))
      const detail = sessionService.getSessionDetail(SESSION_ID)
      expect(detail?.durationMs).toBe(300_000)
      expect(detail?.eventCount).toBe(2)
    })
  })
})
