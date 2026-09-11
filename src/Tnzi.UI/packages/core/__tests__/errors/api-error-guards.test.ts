import { describe, it, expect } from 'vitest';
import {
  ApiError,
  HttpError,
  createFailedApiResult,
  createFailedApiResultFromError,
  isApiError,
  isHttpError,
} from '../../src/errors/api-error';

describe('createFailedApiResultFromError', () => {
  it('uses the Error message and defaults to a 500 code', () => {
    const result = createFailedApiResultFromError<string>(new Error('db down'));

    expect(result).toMatchObject({ succeeded: false, success: false, code: 500, message: 'db down' });
    expect(result.data).toBeUndefined();
  });

  it('stringifies a non-Error throwable and keeps the caller supplied code and details', () => {
    const result = createFailedApiResultFromError<string>('plain string', {
      code: 502,
      errorCode: 'Upstream',
      details: { attempt: 2 },
    });

    expect(result.message).toBe('plain string');
    expect(result.code).toBe(502);
    expect(result.errorCode).toBe('Upstream');
    expect(result.errorDetails).toEqual({ attempt: 2 });
  });
});

describe('error type guards', () => {
  const api = new ApiError({ message: 'api', code: 400 });
  const http = new HttpError(createFailedApiResult({ message: 'http', code: 404 }));

  it('isApiError accepts both ApiError and its HttpError subclass', () => {
    expect(isApiError(api)).toBe(true);
    expect(isApiError(http)).toBe(true);
    expect(isApiError(new Error('x'))).toBe(false);
    expect(isApiError(null)).toBe(false);
  });

  it('isHttpError only accepts HttpError', () => {
    expect(isHttpError(http)).toBe(true);
    expect(isHttpError(api)).toBe(false);
    expect(isHttpError(undefined)).toBe(false);
  });

  it('HttpError falls back to 500 when the envelope carries no code', () => {
    const error = new HttpError({ succeeded: false, success: false, code: undefined as unknown as number });

    expect(error.statusCode).toBe(500);
    expect(error.message).toBe('Error undefined');
  });
});
