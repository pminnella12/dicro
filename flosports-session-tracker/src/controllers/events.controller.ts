import { Request, Response, NextFunction } from 'express'
import { z } from 'zod'
import { sessionService } from '../services/session.service'
import type { SessionId, UserId, WatchEvent } from '../types/session'

const WatchEventSchema = z.object({
  sessionId: z.string().uuid(),
  userId: z.string(),
  eventId: z.string(),
  eventType: z.enum([
    'start',
    'heartbeat',
    'pause',
    'resume',
    'seek',
    'quality_change',
    'buffer_start',
    'buffer_end',
    'end',
  ]),
  eventTimestamp: z.iso.datetime(),
  receivedAt: z.iso.datetime(),
  payload: z.object({
    position: z.number(),
    quality: z.string(),
  }),
})

export async function ingestEvent(req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const parsed = WatchEventSchema.parse(req.body)

    const event: WatchEvent = {
      eventId: parsed.eventId,
      sessionId: parsed.sessionId as unknown as SessionId,
      userId: parsed.userId as unknown as UserId,
      eventType: parsed.eventType,
      eventTimestamp: parsed.eventTimestamp,
      receivedAt: parsed.receivedAt,
      payload: parsed.payload,
    }

    const session = sessionService.processEvent(event)

    res.status(202).json({ sessionId: session.sessionId, state: session.state })
  } catch (err) {
    next(err)
  }
}
