import { Router } from 'express'
import { ingestEvent } from '../controllers/events.controller'

const router = Router()

/**
 * @openapi
 * components:
 *   schemas:
 *     WatchEventRequest:
 *       type: object
 *       required:
 *         - sessionId
 *         - userId
 *         - eventId
 *         - eventType
 *         - eventTimestamp
 *         - receivedAt
 *         - payload
 *       properties:
 *         sessionId:
 *           type: string
 *           format: uuid
 *           example: "123e4567-e89b-12d3-a456-426614174000"
 *         userId:
 *           type: string
 *           example: "user-42"
 *         eventId:
 *           type: string
 *           example: "stream-123"
 *         eventType:
 *           type: string
 *           enum: [start, heartbeat, pause, resume, seek, quality_change, buffer_start, buffer_end, end]
 *           example: "start"
 *         eventTimestamp:
 *           type: string
 *           format: date-time
 *           example: "2026-03-16T00:00:00Z"
 *         receivedAt:
 *           type: string
 *           format: date-time
 *           example: "2026-03-16T00:00:00Z"
 *         payload:
 *           type: object
 *           required:
 *             - position
 *             - quality
 *           properties:
 *             position:
 *               type: number
 *               example: 0
 *             quality:
 *               type: string
 *               example: "1080p"
 *     IngestEventResponse:
 *       type: object
 *       properties:
 *         sessionId:
 *           type: string
 *           format: uuid
 *         state:
 *           type: string
 *           enum: [active, paused, buffering, ended]
 *
 * /api/events:
 *   post:
 *     summary: Ingest a watch event
 *     description: Accepts a player SDK event and updates the associated watch session state.
 *     requestBody:
 *       required: true
 *       content:
 *         application/json:
 *           schema:
 *             $ref: '#/components/schemas/WatchEventRequest'
 *     responses:
 *       202:
 *         description: Event accepted and session updated
 *         content:
 *           application/json:
 *             schema:
 *               $ref: '#/components/schemas/IngestEventResponse'
 *       400:
 *         description: Invalid request body
 *         content:
 *           application/json:
 *             schema:
 *               type: object
 *               properties:
 *                 issues:
 *                   type: array
 *                   items:
 *                     type: object
 *       500:
 *         description: Internal server error
 */
router.post('/api/events', ingestEvent)

export default router
