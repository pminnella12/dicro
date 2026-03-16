import { Request, Response, NextFunction } from 'express'
import { sessionService } from '../services/session.service'
import type { EventId, SessionId } from '../types/session'

export async function getViewers(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const eventId = req.params.eventId as unknown as EventId
    const activeViewers = sessionService.getActiveViewerCount(eventId)
    res.json({ eventId, activeViewers, timestamp: new Date().toISOString() })
  } catch (err) {
    next(err)
  }
}

export async function getSession(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const sessionId = req.params.sessionId as unknown as SessionId
    const session = sessionService.getSessionDetail(sessionId)
    if (!session) {
      res.status(404).json({ error: 'Session not found' })
      return
    }
    res.json(session)
  } catch (err) {
    next(err)
  }
}
