/**
 * Performance Module Types — mirrors `Tnzi.Performance.Dtos.*` on the .NET side.
 *
 * Models the runtime request-timing surface exposed by
 * `Tnzi.Performance/Controllers/DefaultPerformanceAdminController`
 * (`/admin/performance/*`). `Tnzi.Performance` is an optional infrastructure
 * module — when the host app doesn't load it, every endpoint returns 404.
 */

/** Percentile breakdown of request durations over a window. */
export interface PercentileResultDto {
  p50: number;
  p95: number;
  p99: number;
  average: number;
  min: number;
  max: number;
  sampleCount: number;
}

/** Per-endpoint aggregate statistics. */
export interface EndpointStatsDto {
  path: string;
  method: string;
  requestCount: number;
  averageDurationMs: number;
  p95DurationMs: number;
  minDurationMs: number;
  maxDurationMs: number;
  errorCount: number;
  lastRequestTime: string;
}

/** A single slow-request record above the configured threshold. */
export interface SlowRequestRecordDto {
  path: string;
  method: string;
  statusCode: number;
  durationMs: number;
  userId?: string | null;
  requestId?: string | null;
  timestamp: string;
}
