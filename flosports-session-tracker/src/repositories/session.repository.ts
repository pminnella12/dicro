import type { SessionId, EventId, WatchSession } from '../types/session'

const sessions = new Map<SessionId, WatchSession>()

export const sessionRepository = {
  upsertSession(session: WatchSession): WatchSession {
    sessions.set(session.sessionId, session)
    return session
  },

  getSession(sessionId: SessionId): WatchSession | undefined {
    return sessions.get(sessionId)
  },

  getAllSessions(): WatchSession[] {
    return Array.from(sessions.values())
  },

  getSessionsByEventId(eventId: EventId): WatchSession[] {
    return Array.from(sessions.values()).filter((s) => s.eventId === eventId)
  },

  // Exposed for testing — clears all sessions
  _clear(): void {
    sessions.clear()
  },
}
