import express, { Application } from 'express';
import swaggerUi from 'swagger-ui-express';
import healthRoutes from './routes/health.routes';
import { swaggerSpec } from './config/swagger';

const app: Application = express();

app.use(express.json());

app.use('/docs', swaggerUi.serve, swaggerUi.setup(swaggerSpec));

app.use(healthRoutes);

export default app;
