import { sessionRepository } from '../repositories/session.repository'
import type { EventId, SessionId, SessionState, UserId, WatchEvent, WatchSession } from '../types/session'

function nextState(current: SessionState, eventType: WatchEvent['eventType']): SessionState {
  switch (eventType) {
    case 'start':
    case 'heartbeat':
    case 'resume':
    case 'seek':
    case 'quality_change':
    case 'buffer_end':
      return 'active'
    case 'pause':
      return 'paused'
    case 'buffer_start':
      return 'buffering'
    case 'end':
      return 'ended'
    default:
      return current
  }
}

const ACTIVE_WINDOW_MS = 90_000

export const sessionService = {
  getActiveViewerCount(eventId: EventId): number {
    const cutoff = new Date(Date.now() - ACTIVE_WINDOW_MS)
    return sessionRepository
      .getSessionsByEventId(eventId)
      .filter((s) => s.state !== 'ended' && new Date(s.lastEventAt) > cutoff).length
  },

  getSessionDetail(
    sessionId: SessionId,
  ): (WatchSession & { durationMs: number; eventCount: number }) | undefined {
    const session = sessionRepository.getSession(sessionId)
    if (!session) return undefined

    const end = session.endedAt ?? session.lastEventAt
    const durationMs = new Date(end).getTime() - new Date(session.startedAt).getTime()

    return { ...session, durationMs, eventCount: session.events.length }
  },

  processEvent(event: WatchEvent): WatchSession {
    const existing = sessionRepository.getSession(event.sessionId)

    if (!existing || event.eventType === 'start') {
      const session: WatchSession = {
        sessionId: event.sessionId,
        userId: event.userId as unknown as UserId,
        eventId: event.eventId as unknown as EventId,
        state: 'active',
        startedAt: event.eventTimestamp,
        lastEventAt: event.eventTimestamp,
        events: [event],
      }
      return sessionRepository.upsertSession(session)
    }

    const newState = nextState(existing.state, event.eventType)
    const updated: WatchSession = {
      ...existing,
      state: newState,
      lastEventAt: event.eventTimestamp,
      endedAt: newState === 'ended' ? event.eventTimestamp : existing.endedAt,
      events: [...existing.events, event],
    }
    return sessionRepository.upsertSession(updated)
  },
}
