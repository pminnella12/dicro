import express, { Application } from 'express';
import swaggerUi from 'swagger-ui-express';
import healthRoutes from './routes/health.routes';
import eventsRoutes from './routes/events.routes';
import sessionsRoutes from './routes/sessions.routes';
import { swaggerSpec } from './config/swagger';
import { errorHandler } from './middleware/errorHandler';

const app: Application = express();

app.use(express.json());

app.use('/docs', swaggerUi.serve, swaggerUi.setup(swaggerSpec));

app.use(healthRoutes);
app.use(eventsRoutes);
app.use(sessionsRoutes);

app.use(errorHandler);

export default app;
