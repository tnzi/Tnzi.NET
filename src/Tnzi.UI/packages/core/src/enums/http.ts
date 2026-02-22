/**
 * HTTP Enums - HTTP-related enumerations
 */

/**
 * HTTP Status code categories
 */
export enum HttpStatusCategory {
  Informational = 1,
  Success = 2,
  Redirection = 3,
  ClientError = 4,
  ServerError = 5,
}

/**
 * Common HTTP status codes
 */
export enum HttpStatus {
  // 2xx Success
  Ok = 200,
  Created = 201,
  Accepted = 202,
  NoContent = 204,
  // 3xx Redirection
  MovedPermanently = 301,
  Found = 302,
  NotModified = 304,
  TemporaryRedirect = 307,
  PermanentRedirect = 308,
  // 4xx Client Errors
  BadRequest = 400,
  Unauthorized = 401,
  Forbidden = 403,
  NotFound = 404,
  MethodNotAllowed = 405,
  NotAcceptable = 406,
  RequestTimeout = 408,
  Conflict = 409,
  Gone = 410,
  PayloadTooLarge = 413,
  UriTooLong = 414,
  UnsupportedMediaType = 415,
  UnprocessableEntity = 422,
  TooManyRequests = 429,
  // 5xx Server Errors
  InternalServerError = 500,
  NotImplemented = 501,
  BadGateway = 502,
  ServiceUnavailable = 503,
  GatewayTimeout = 504,
}

/**
 * Check if status code indicates success
 */
export function isSuccessStatus(status: number): boolean {
  return status >= 200 && status < 300;
}

/**
 * Check if status code indicates client error
 */
export function isClientError(status: number): boolean {
  return status >= 400 && status < 500;
}

/**
 * Check if status code indicates server error
 */
export function isServerError(status: number): boolean {
  return status >= 500 && status < 600;
}

/**
 * Get HTTP status category
 */
export function getStatusCategory(status: number): HttpStatusCategory {
  return Math.floor(status / 100) as HttpStatusCategory;
}

/**
 * Get HTTP status message
 */
export function getStatusMessage(status: number): string {
  const messages: Record<number, string> = {
    [HttpStatus.Ok]: 'OK',
    [HttpStatus.Created]: 'Created',
    [HttpStatus.Accepted]: 'Accepted',
    [HttpStatus.NoContent]: 'No Content',
    [HttpStatus.BadRequest]: 'Bad Request',
    [HttpStatus.Unauthorized]: 'Unauthorized',
    [HttpStatus.Forbidden]: 'Forbidden',
    [HttpStatus.NotFound]: 'Not Found',
    [HttpStatus.MethodNotAllowed]: 'Method Not Allowed',
    [HttpStatus.Conflict]: 'Conflict',
    [HttpStatus.UnprocessableEntity]: 'Unprocessable Entity',
    [HttpStatus.TooManyRequests]: 'Too Many Requests',
    [HttpStatus.InternalServerError]: 'Internal Server Error',
    [HttpStatus.ServiceUnavailable]: 'Service Unavailable',
    [HttpStatus.GatewayTimeout]: 'Gateway Timeout',
  };
  return messages[status] ?? 'Unknown Status';
}

/**
 * HTTP Methods
 */
export enum HttpMethod {
  Get = 'GET',
  Post = 'POST',
  Put = 'PUT',
  Patch = 'PATCH',
  Delete = 'DELETE',
  Head = 'HEAD',
  Options = 'OPTIONS',
}

/**
 * Content types
 */
export enum ContentType {
  Json = 'application/json',
  FormData = 'multipart/form-data',
  FormUrlEncoded = 'application/x-www-form-urlencoded',
  Text = 'text/plain',
  Html = 'text/html',
  Xml = 'application/xml',
  Binary = 'application/octet-stream',
  Pdf = 'application/pdf',
  ImageJpeg = 'image/jpeg',
  ImagePng = 'image/png',
  ImageGif = 'image/gif',
  ImageWebP = 'image/webp',
  ImageSvg = 'image/svg+xml',
}
