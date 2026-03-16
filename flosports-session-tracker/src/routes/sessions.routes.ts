import { Router } from 'express'
import { getViewers, getSession } from '../controllers/sessions.controller'

const router = Router()

/**
 * @openapi
 * components:
 *   schemas:
 *     ViewersResponse:
 *       type: object
 *       properties:
 *         eventId:
 *           type: string
 *           example: "stream-123"
 *         activeViewers:
 *           type: integer
 *           example: 42
 *         timestamp:
 *           type: string
 *           format: date-time
 *           example: "2026-03-16T00:00:00Z"
 *     WatchEvent:
 *       type: object
 *       properties:
 *         eventId:
 *           type: string
 *         sessionId:
 *           type: string
 *           format: uuid
 *         userId:
 *           type: string
 *         eventType:
 *           type: string
 *           enum: [start, heartbeat, pause, resume, seek, quality_change, buffer_start, buffer_end, end]
 *         eventTimestamp:
 *           type: string
 *           format: date-time
 *         receivedAt:
 *           type: string
 *           format: date-time
 *         payload:
 *           type: object
 *           properties:
 *             position:
 *               type: number
 *             quality:
 *               type: string
 *     SessionDetailResponse:
 *       type: object
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
 *         state:
 *           type: string
 *           enum: [active, paused, buffering, ended]
 *         durationMs:
 *           type: integer
 *           example: 120000
 *         startedAt:
 *           type: string
 *           format: date-time
 *         lastEventAt:
 *           type: string
 *           format: date-time
 *         endedAt:
 *           type: string
 *           format: date-time
 *           nullable: true
 *         eventCount:
 *           type: integer
 *           example: 5
 *         events:
 *           type: array
 *           items:
 *             $ref: '#/components/schemas/WatchEvent'
 *
 * /api/streams/{eventId}/viewers:
 *   get:
 *     summary: Get concurrent viewer count
 *     description: Returns the number of active viewers for a given stream. A session is considered active if its state is not "ended" and its last event was received within the past 90 seconds.
 *     parameters:
 *       - in: path
 *         name: eventId
 *         required: true
 *         schema:
 *           type: string
 *         example: "stream-123"
 *     responses:
 *       200:
 *         description: Viewer count returned
 *         content:
 *           application/json:
 *             schema:
 *               $ref: '#/components/schemas/ViewersResponse'
 *       500:
 *         description: Internal server error
 */
router.get('/api/streams/:eventId/viewers', getViewers)

/**
 * @openapi
 * /api/sessions/{sessionId}:
 *   get:
 *     summary: Get session detail
 *     description: Returns full session detail including all events and computed duration.
 *     parameters:
 *       - in: path
 *         name: sessionId
 *         required: true
 *         schema:
 *           type: string
 *           format: uuid
 *         example: "123e4567-e89b-12d3-a456-426614174000"
 *     responses:
 *       200:
 *         description: Session detail returned
 *         content:
 *           application/json:
 *             schema:
 *               $ref: '#/components/schemas/SessionDetailResponse'
 *       404:
 *         description: Session not found
 *         content:
 *           application/json:
 *             schema:
 *               type: object
 *               properties:
 *                 error:
 *                   type: string
 *                   example: "Session not found"
 *       500:
 *         description: Internal server error
 */
router.get('/api/sessions/:sessionId', getSession)

export default router
