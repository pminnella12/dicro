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

export const sessionService = {
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
