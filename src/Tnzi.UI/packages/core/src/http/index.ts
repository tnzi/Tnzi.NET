/**
 * @tnzi/core/http
 *
 * HTTP client for Tnzi.NET backend.
 */

// HTTP Client
export { HttpClient, createHttpClient, DEFAULT_REQUEST_TIMEOUT, REQUEST_TIMEOUT_ERROR_CODE } from './http';
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
  ensureOk,
  unwrapResult,
  extractData,
  extractDataOrThrow,
  emptyPaged,
} from './response';

export { HttpError, isHttpError } from '../errors/api-error';
export { TimeoutError, isTimeoutError } from '../errors/network-error';
