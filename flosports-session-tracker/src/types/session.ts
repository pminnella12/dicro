declare const __brand: unique symbol

type Brand<T, B> = T & { readonly [__brand]: B }

export type SessionId = Brand<string, 'SessionId'>
export type UserId = Brand<string, 'UserId'>
export type EventId = Brand<string, 'EventId'>

export type EventType =
  | 'start'
  | 'heartbeat'
  | 'pause'
  | 'resume'
  | 'seek'
  | 'quality_change'
  | 'buffer_start'
  | 'buffer_end'
  | 'end'

export type SessionState = 'active' | 'paused' | 'buffering' | 'ended'

export interface WatchEvent {
  eventId: string
  sessionId: SessionId
  userId: UserId
  eventType: EventType
  eventTimestamp: string
  receivedAt: string
  payload: {
    position: number
    quality: string
  }
}

export interface WatchSession {
  sessionId: SessionId
  userId: UserId
  eventId: EventId
  state: SessionState
  startedAt: string
  lastEventAt: string
  endedAt?: string
  events: WatchEvent[]
}
