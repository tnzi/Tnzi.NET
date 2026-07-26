import { describe, it, expect } from 'vitest';
import {
  normalizeApiResult,
  isSuccess,
  isFailed,
  unwrapData,
  extractData,
  ensureOk,
} from '../../http/response';
import { createFailedApiResult } from '../../errors/api-error';

describe('normalizeApiResult', () => {
  it('normalizes a camelCase envelope', () => {
    const result = normalizeApiResult<{ id: number }>({
      succeeded: true,
      success: true,
      code: 200,
      data: { id: 1 },
    });
    expect(result).toMatchObject({ succeeded: true, success: true, code: 200, data: { id: 1 } });
  });

  it('normalizes a PascalCase envelope', () => {
    const result = normalizeApiResult<string>({ Succeeded: true, Code: 200, Data: 'ok' });
    expect(result.succeeded).toBe(true);
    expect(result.data).toBe('ok');
  });

  it('treats a null body as an empty payload, not a parse failure', () => {
    // `JSON.parse('null')` is perfectly valid JSON. Dereferencing `raw.code`
    // threw, and the caller reported "Invalid JSON response" for a well-formed
    // body.
    const result = normalizeApiResult<null>(null);
    expect(result.succeeded).toBe(true);
    expect(result.code).toBe(200);
    expect(result.data).toBeUndefined();
    expect(result.message).toBeUndefined();
  });

  it('carries a bare scalar body through as the payload', () => {
    const result = normalizeApiResult<number>(42 as unknown as Record<string, unknown>);
    expect(result.succeeded).toBe(true);
    expect(result.data).toBe(42);
  });

  it('defaults succeeded from the code when only success is present', () => {
    const result = normalizeApiResult({ success: false, code: 500, message: 'boom' });
    expect(result.succeeded).toBe(false);
    expect(result.message).toBe('boom');
  });
});

describe('isSuccess / isFailed', () => {
  it('reports success on a succeeded envelope', () => {
    const result = normalizeApiResult<string>({ succeeded: true, code: 200, data: 'x' });
    expect(isSuccess(result)).toBe(true);
    expect(isFailed(result)).toBe(false);
  });

  it('reports failure on a failed envelope and on null/undefined', () => {
    expect(isSuccess(createFailedApiResult<string>({ message: 'nope' }))).toBe(false);
    expect(isSuccess(null)).toBe(false);
    expect(isSuccess(undefined)).toBe(false);
    expect(isFailed(null)).toBe(true);
  });

  it('narrows data inside the guard', () => {
    // Type-level contract (checked by tsc): `data` is optional on ApiResult and
    // only proven present on the true branch of isSuccess. The runtime shape
    // asserted here is what that narrowing describes.
    const result = normalizeApiResult<{ id: number }>({ succeeded: true, code: 200, data: { id: 7 } });
    if (isSuccess(result)) {
      expect(result.data.id).toBe(7);
    } else {
      throw new Error('expected success');
    }
  });

  it('leaves data undefined on a failed envelope', () => {
    const failed = createFailedApiResult<{ id: number }>({ message: 'nope', code: 403 });
    expect(failed.data).toBeUndefined();
    expect(failed.code).toBe(403);
  });
});

describe('unwrapData / extractData / ensureOk', () => {
  it('unwrapData returns the payload on success', () => {
    const result = normalizeApiResult<string>({ succeeded: true, code: 200, data: 'x' });
    expect(unwrapData(result)).toBe('x');
  });

  it('unwrapData throws on failure and on null data', () => {
    expect(() => unwrapData(createFailedApiResult<string>({ message: 'nope' }))).toThrow();
    expect(() =>
      unwrapData(normalizeApiResult<string>({ succeeded: true, code: 200, data: null }))
    ).toThrow();
  });

  it('extractData returns null on failure', () => {
    expect(extractData(createFailedApiResult<string>({ message: 'nope' }))).toBeNull();
  });

  it('ensureOk throws on a failed envelope and passes success through', () => {
    expect(() => ensureOk(createFailedApiResult({ message: 'refused' }))).toThrow('refused');
    expect(() => ensureOk({ succeeded: true })).not.toThrow();
    expect(() => ensureOk(undefined)).not.toThrow();
  });
});
