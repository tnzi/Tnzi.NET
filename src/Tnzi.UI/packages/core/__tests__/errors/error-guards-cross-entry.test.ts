// @vitest-environment node
/**
 * Cross-entry probe for the error type guards.
 *
 * tsup builds every sub-path with `splitting: false`, so `dist/state`,
 * `dist/http`, `dist/errors` and the root each carry their own inlined
 * `HttpError` class. `docs/coding-standards/ui-frontend.md` tells an app to
 * build its client with `createTnziClient` from `@tnzi/core/state` and to
 * continue a sign-in challenge with `isHttpError(e) && e.errorCode ===
 * '2FA_REQUIRED'` using the guard from `@tnzi/core/http`. With `instanceof`
 * guards that check was false for every challenge thrown by the state bundle.
 *
 * The unit tests in `api-error-guards.test.ts` import one module instance from
 * `src/` and cannot see this. This probe loads the BUILT entries, so it needs
 * `dist/` - run `pnpm --filter @tnzi/core build` first, as
 * `capabilities-cross-entry.test.ts` already requires.
 */
import { describe, it, expect, afterEach, vi } from 'vitest';
import { existsSync } from 'node:fs';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { dirname, join, resolve } from 'node:path';

const packageRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');

type ErrorsEntry = typeof import('../../src/errors/index');
type HttpEntry = typeof import('../../src/http/index');
type StateEntry = typeof import('../../src/state/index');
type StorageEntry = typeof import('../../src/adapters/storage');
type RootEntry = typeof import('../../src/index');

async function loadDist<T>(subpath: string): Promise<T> {
  const file = join(packageRoot, 'dist', subpath, 'index.js');
  if (!existsSync(file)) {
    throw new Error(`${file} is missing. This probe reads the built output; run pnpm --filter @tnzi/core build first.`);
  }
  return (await import(pathToFileURL(file).href)) as T;
}

async function loadRoot(): Promise<RootEntry> {
  const file = join(packageRoot, 'dist', 'index.js');
  if (!existsSync(file)) {
    throw new Error(`${file} is missing. This probe reads the built output; run pnpm --filter @tnzi/core build first.`);
  }
  return (await import(pathToFileURL(file).href)) as RootEntry;
}

describe('error guards across tsup entries', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('the copies really are different classes (the reason the guards cannot use instanceof alone)', async () => {
    const errors = await loadDist<ErrorsEntry>('errors');
    const http = await loadDist<HttpEntry>('http');
    expect(http.HttpError).not.toBe(errors.HttpError);
  });

  it('a 2FA challenge thrown by dist/state login is recognised by the guards of every other entry', async () => {
    const state = await loadDist<StateEntry>('state');
    const http = await loadDist<HttpEntry>('http');
    const errors = await loadDist<ErrorsEntry>('errors');
    const root = await loadRoot();
    const storage = await loadDist<StorageEntry>('adapters/storage');

    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        new Response(
          JSON.stringify({
            succeeded: false,
            code: 403,
            errorCode: '2FA_REQUIRED',
            message: 'Two-factor authentication required',
            errorDetails: { tempToken: 't-1' },
          }),
          { status: 403, headers: { 'content-type': 'application/json' } },
        ),
      ),
    );

    const { auth } = state.createTnziClient({ baseUrl: '/api', storage: storage.createMemoryStorageAdapter() });
    const rejection = await auth.login({ userName: 'u', password: 'p' } as never).then(
      () => null,
      (error: unknown) => error,
    );

    expect((rejection as { errorCode?: string } | null)?.errorCode).toBe('2FA_REQUIRED');
    expect(http.isHttpError(rejection)).toBe(true);
    expect(errors.isHttpError(rejection)).toBe(true);
    expect(errors.isApiError(rejection)).toBe(true);
    expect(root.isHttpError(rejection)).toBe(true);
  });

  it('an HttpError from dist/http is recognised by dist/errors, and the reverse', async () => {
    const http = await loadDist<HttpEntry>('http');
    const errors = await loadDist<ErrorsEntry>('errors');

    const fromHttp = new http.HttpError({ succeeded: false, success: false, code: 404, message: 'missing' });
    const fromErrors = new errors.HttpError({ succeeded: false, success: false, code: 409, message: 'conflict' });

    expect(errors.isHttpError(fromHttp)).toBe(true);
    expect(errors.isApiError(fromHttp)).toBe(true);
    expect(http.isHttpError(fromErrors)).toBe(true);
  });

  it('timeout errors are recognised across entries, and the guards still tell the kinds apart', async () => {
    const http = await loadDist<HttpEntry>('http');
    const errors = await loadDist<ErrorsEntry>('errors');

    const timeout = new http.TimeoutError(30000);
    expect(errors.isTimeoutError(timeout)).toBe(true);
    expect(errors.isNetworkError(timeout)).toBe(true);
    expect(errors.isAbortError(timeout)).toBe(false);
    expect(errors.isHttpError(timeout)).toBe(false);

    const apiOnly = new errors.ApiError({ message: 'not an http error' });
    expect(http.isHttpError(apiOnly)).toBe(false);
    expect(http.isTimeoutError(new errors.AbortError())).toBe(false);
  });
});
