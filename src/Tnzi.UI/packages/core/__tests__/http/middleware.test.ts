import { describe, it, expect, vi, afterEach } from 'vitest';
import { z } from 'zod';
import {
  createSchemaResolver,
  createSchemaValidationMiddleware,
  createErrorMappingMiddleware,
  type HttpResponseContext,
} from '../../src/http/middleware';
import { createFailedApiResult, HttpError } from '../../src/errors/api-error';
import { setMessageAdapter, resetMessageAdapter, type MessageAdapter } from '../../src/adapters/message';
import type { ApiResult } from '../../src/types/api';

const context = (overrides: Partial<HttpResponseContext> = {}): HttpResponseContext => ({
  method: 'GET',
  url: '/api/users/1',
  fullUrl: 'https://host.example/api/users/1',
  status: 200,
  ...overrides,
});

const ok = <T>(data: T): ApiResult<T> => ({ succeeded: true, success: true, code: 200, data });

describe('createSchemaResolver', () => {
  const users = z.object({ id: z.number() });
  const orders = z.object({ total: z.number() });

  it('matches a string path after stripping the host and any trailing slash', () => {
    const resolve = createSchemaResolver([{ path: '/api/users/1/', schema: users }]);

    expect(resolve(context({ url: 'https://host.example/api/users/1' }))).toBe(users);
    expect(resolve(context({ url: '/api/users/1' }))).toBe(users);
    expect(resolve(context({ url: '/api/users/2' }))).toBeUndefined();
  });

  it('matches a RegExp path against the normalised url', () => {
    const resolve = createSchemaResolver([{ path: /\/api\/users\/\d+$/, schema: users }]);

    expect(resolve(context({ url: '/api/users/42' }))).toBe(users);
    expect(resolve(context({ url: '/api/users/list' }))).toBeUndefined();
  });

  it('only applies a binding with a method to that method', () => {
    const resolve = createSchemaResolver([{ method: 'POST', path: '/api/users/1', schema: users }]);

    expect(resolve(context({ method: 'GET' }))).toBeUndefined();
    expect(resolve(context({ method: 'POST' }))).toBe(users);
  });

  it('returns the first binding that matches', () => {
    const resolve = createSchemaResolver([
      { path: /\/api\//, schema: orders },
      { path: '/api/users/1', schema: users },
    ]);

    expect(resolve(context())).toBe(orders);
  });
});

describe('createSchemaValidationMiddleware', () => {
  const schema = z.object({ id: z.coerce.number() });

  it('passes a failed result through untouched', () => {
    const middleware = createSchemaValidationMiddleware({ resolveSchema: () => schema });
    const failed = createFailedApiResult<{ id: number }>({ message: 'nope', code: 404 });

    expect(middleware(failed, context())).toBe(failed);
  });

  it('passes the result through when no schema is bound to the request', () => {
    const middleware = createSchemaValidationMiddleware({ resolveSchema: () => undefined });
    const result = ok({ id: 'not-validated' });

    expect(middleware(result, context())).toBe(result);
  });

  it('replaces the payload with the coerced value by default', () => {
    const middleware = createSchemaValidationMiddleware({ resolveSchema: () => schema });

    const validated = middleware(ok({ id: '7' } as unknown as { id: number }), context());

    expect(validated.succeeded).toBe(true);
    expect(validated.data).toEqual({ id: 7 });
  });

  it('keeps the original payload when coercion is turned off', () => {
    const middleware = createSchemaValidationMiddleware({ resolveSchema: () => schema, coerceData: false });
    const result = ok({ id: '7' } as unknown as { id: number });

    expect(middleware(result, context())).toBe(result);
  });

  it('turns a payload that fails validation into a 422 failure and reports it', () => {
    const onValidationError = vi.fn();
    const middleware = createSchemaValidationMiddleware({ resolveSchema: () => schema, onValidationError });
    const ctx = context({ method: 'GET', url: '/api/users/1' });

    const outcome = middleware(ok({ id: 'not a number' } as unknown as { id: number }), ctx);

    expect(outcome.succeeded).toBe(false);
    expect(outcome.code).toBe(422);
    expect(outcome.errorCode).toBe('ResponseValidationFailed');
    expect(outcome.message).toBe('Response validation failed for GET /api/users/1');
    expect(outcome.data).toBeUndefined();
    expect(outcome.errorDetails).toMatchObject({ path: '/api/users/1', method: 'GET' });
    expect((outcome.errorDetails?.issues as unknown[]).length).toBeGreaterThan(0);

    expect(onValidationError).toHaveBeenCalledTimes(1);
    const [error, reportedContext] = onValidationError.mock.calls[0];
    expect(error).toBeInstanceOf(HttpError);
    expect((error as HttpError).statusCode).toBe(422);
    expect(reportedContext).toBe(ctx);
  });

  it('honours a custom error code and message', () => {
    const middleware = createSchemaValidationMiddleware({
      resolveSchema: () => schema,
      errorCode: 'ContractDrift',
      message: 'The server answered in a shape this client does not know',
    });

    const outcome = middleware(ok({} as { id: number }), context());

    expect(outcome.errorCode).toBe('ContractDrift');
    expect(outcome.message).toBe('The server answered in a shape this client does not know');
  });
});

describe('createErrorMappingMiddleware', () => {
  afterEach(() => resetMessageAdapter());

  const failure = (errorCode?: string, message?: string) =>
    createFailedApiResult<unknown>({ message: message ?? 'server said no', code: 400, errorCode });

  it('leaves a successful result alone and never notifies', () => {
    const notifier = vi.fn();
    const middleware = createErrorMappingMiddleware({ mappings: {}, notifier });
    const result = ok('fine');

    expect(middleware(result, context())).toBe(result);
    expect(notifier).not.toHaveBeenCalled();
  });

  it('returns the failure unchanged after notifying', () => {
    const notifier = vi.fn();
    const middleware = createErrorMappingMiddleware({ mappings: {}, notifier });
    const result = failure('X');

    expect(middleware(result, context())).toBe(result);
    expect(notifier).toHaveBeenCalledTimes(1);
  });

  it('stays silent for codes listed in silentErrorCodes', () => {
    const notifier = vi.fn();
    const middleware = createErrorMappingMiddleware({ mappings: {}, silentErrorCodes: ['Quiet'], notifier });

    middleware(failure('Quiet'), context());
    expect(notifier).not.toHaveBeenCalled();

    middleware(failure('Loud'), context());
    expect(notifier).toHaveBeenCalledTimes(1);
  });

  it('lets shouldNotify veto the notification', () => {
    const notifier = vi.fn();
    const middleware = createErrorMappingMiddleware({
      mappings: {},
      notifier,
      shouldNotify: (_result, ctx) => ctx.status !== 401,
    });

    middleware(failure('X'), context({ status: 401 }));
    expect(notifier).not.toHaveBeenCalled();

    middleware(failure('X'), context({ status: 500 }));
    expect(notifier).toHaveBeenCalledTimes(1);
  });

  it('uses the mapped message and level for a known error code', () => {
    const notifier = vi.fn();
    const middleware = createErrorMappingMiddleware({
      mappings: { OutOfStock: { message: 'Sold out', level: 'warning' } },
      notifier,
    });
    const result = failure('OutOfStock');
    const ctx = context();

    middleware(result, ctx);

    expect(notifier).toHaveBeenCalledWith('Sold out', 'warning', result, ctx);
  });

  it('resolves an i18n key through the active runtime', () => {
    // The default catalogue has no such key, and a missing key resolves to the
    // key itself, which is exactly what proves the lookup went through `t`.
    const notifier = vi.fn();
    const middleware = createErrorMappingMiddleware({
      mappings: { Mapped: { i18nKey: 'errors.mapped.key' } },
      notifier,
    });

    middleware(failure('Mapped'), context());

    expect(notifier.mock.calls[0][0]).toBe('errors.mapped.key');
    expect(notifier.mock.calls[0][1]).toBe('error');
  });

  it('falls back in order: fallbackI18nKey, fallbackMessage, then the result message', () => {
    const notifier = vi.fn();

    createErrorMappingMiddleware({
      mappings: {},
      fallbackI18nKey: 'errors.generic',
      fallbackMessage: 'Something went wrong',
      notifier,
    })(failure('Unmapped'), context());
    expect(notifier.mock.calls[0][0]).toBe('errors.generic');

    createErrorMappingMiddleware({ mappings: {}, fallbackMessage: 'Something went wrong', notifier })(
      failure('Unmapped'),
      context()
    );
    expect(notifier.mock.calls[1][0]).toBe('Something went wrong');

    createErrorMappingMiddleware({ mappings: {}, notifier })(failure('Unmapped', 'server said no'), context());
    expect(notifier.mock.calls[2][0]).toBe('server said no');
  });

  it('applies defaultLevel when the mapping does not set one', () => {
    const notifier = vi.fn();
    const middleware = createErrorMappingMiddleware({
      mappings: { Soft: { message: 'Heads up' } },
      defaultLevel: 'info',
      notifier,
    });

    middleware(failure('Soft'), context());

    expect(notifier.mock.calls[0][1]).toBe('info');
  });

  it('routes to the message adapter at the mapped level when no notifier is given', () => {
    const adapter: MessageAdapter = {
      info: vi.fn(),
      success: vi.fn(),
      warning: vi.fn(),
      error: vi.fn(),
      loading: vi.fn(() => () => {}),
    };
    setMessageAdapter(adapter);

    const middleware = createErrorMappingMiddleware({
      mappings: { Warn: { message: 'Careful', level: 'warning' } },
    });

    middleware(failure('Warn'), context());
    middleware(failure('Other', 'Boom'), context());

    expect(adapter.warning).toHaveBeenCalledWith('Careful');
    expect(adapter.error).toHaveBeenCalledWith('Boom');
    expect(adapter.info).not.toHaveBeenCalled();
  });
});
