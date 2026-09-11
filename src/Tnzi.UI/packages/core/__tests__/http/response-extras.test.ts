import { describe, it, expect } from 'vitest';
import { extractData, extractDataOrThrow, emptyPaged, isSuccess, unwrapOk } from '../../src/http/response';
import { createFailedApiResult } from '../../src/errors/api-error';

describe('extractData', () => {
  it('returns the payload of a successful envelope and null for a failed one', () => {
    expect(extractData({ succeeded: true, success: true, code: 200, data: { id: 1 } })).toEqual({ id: 1 });
    expect(extractData(createFailedApiResult<{ id: number }>({ message: 'no' }))).toBeNull();
  });
});

describe('extractDataOrThrow', () => {
  it('unwraps success and throws on failure like unwrapData', () => {
    expect(extractDataOrThrow({ succeeded: true, success: true, code: 200, data: 'ok' })).toBe('ok');
    expect(() => extractDataOrThrow(createFailedApiResult<string>({ message: 'denied', code: 403 }))).toThrow(
      'denied'
    );
  });
});

describe('emptyPaged', () => {
  it('is a successful envelope positioned on the first page with nothing in it', () => {
    const result = emptyPaged<number>();

    expect(isSuccess(result)).toBe(true);
    expect(result.code).toBe(200);
    expect(result.data).toMatchObject({ pageIndex: 1 });
    expect(Object.values(result.data ?? {}).some(value => Array.isArray(value) && value.length === 0)).toBe(true);
  });
});

describe('unwrapOk', () => {
  it('returns the payload of a successful envelope', () => {
    const res = { succeeded: true, success: true, code: 200, data: { id: 'a1' } };
    expect(unwrapOk(res)).toEqual({ id: 'a1' });
  });

  it('passes a bare (non-envelope) value through unchanged', () => {
    expect(unwrapOk({ id: 'bare' })).toEqual({ id: 'bare' });
    expect(unwrapOk(undefined as unknown)).toBeUndefined();
  });

  it('throws with the server message when the envelope reports failure', () => {
    // HttpClient RESOLVES a 400 business refusal as a failed envelope; a bare
    // unwrap would hand the caller `null` and the UI would announce "saved".
    const res = { succeeded: false, success: false, code: 400, data: null, message: 'Nothing reads this key' };
    expect(() => unwrapOk(res)).toThrow('Nothing reads this key');
  });

  it('falls back to the caller-supplied message when the envelope has none', () => {
    const res = { succeeded: false, success: false, code: 400, data: null };
    expect(() => unwrapOk(res, 'Save failed')).toThrow('Save failed');
  });
});
