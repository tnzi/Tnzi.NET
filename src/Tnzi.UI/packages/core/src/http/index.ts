/**
 * @tnzi/core/http
 *
 * HTTP client for Tnzi.NET backend.
 */

// HTTP Client
export { HttpClient, createHttpClient } from './http';
export type { HttpClientConfig, RetryConfig } from './http';

// Middleware helpers
export type {
  HttpResponseContext,
  HttpResponseMiddleware,
  HttpSchemaBinding,
  SchemaValidationMiddlewareOptions,
  ErrorMessageLevel,
  ErrorMessageMapping,
  ErrorMappingMiddlewareOptions,
} from './middleware';
export {
  createSchemaResolver,
  createSchemaValidationMiddleware,
  createErrorMappingMiddleware,
} from './middleware';

// Response utilities
export {
  normalizeApiResult,
  isSuccess,
  isFailed,
  getErrorMessage,
  getErrorCode,
  unwrapData,
  extractData,
  extractDataOrThrow,
  emptyPaged,
} from './response';

export { HttpError, isHttpError } from '../errors/api-error';
