// @vitest-environment node
/**
 * Cross-entry probe for the capability declaration.
 *
 * `docs/coding-standards/capabilities.md` tells an app to
 * `import { declareClientCapability } from '@tnzi/core/http'`, while the only
 * `HttpClient` most apps ever build comes from `createTnziClient` in
 * `@tnzi/core/state`. Those are two tsup entries; with `splitting: false` each
 * carries its own inlined copy of `capabilities.ts`, and in the `state` copy
 * `declareClientCapability` is tree-shaken out entirely. A module-level `Set`
 * therefore meant: declare through one entry, the client reads an always-empty
 * copy through the other, and `X-Tnzi-Capabilities` never leaves the process -
 * the server resolves `ClientCapabilities.None` and every `Supports()` answers
 * false with no error on either side.
 *
 * The unit tests in `capabilities.test.ts` import both symbols from `src/` (one
 * module instance) and cannot see this. This probe loads the BUILT entries, so
 * it needs `dist/` - run `pnpm --filter @tnzi/core build` first, as
 * `dist-treeshakeable.test.ts` already requires.
 */
import { describe, it, expect, afterEach, vi } from 'vitest';
import { existsSync } from 'node:fs';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { dirname, join, resolve } from 'node:path';

const packageRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');

type HttpEntry = typeof import('../../src/http/index');
type StateEntry = typeof import('../../src/state/index');
type StorageEntry = typeof import('../../src/adapters/storage');

async function loadDist<T>(subpath: string): Promise<T> {
  const file = join(packageRoot, 'dist', subpath, 'index.js');
  if (!existsSync(file)) {
    throw new Error(`${file} is missing. This probe reads the built output; run pnpm --filter @tnzi/core build first.`);
  }
  return (await import(pathToFileURL(file).href)) as T;
}

describe('capability declaration across tsup entries', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('a capability declared via dist/http is sent by a client built via dist/state', async () => {
    const http = await loadDist<HttpEntry>('http');
    const state = await loadDist<StateEntry>('state');
    const storage = await loadDist<StorageEntry>('adapters/storage');

    http.resetClientCapabilities();
    http.declareClientCapability('probe-v1');

    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ succeeded: true, code: 200, data: null }), { status: 200 })
    );
    vi.stubGlobal('fetch', fetchMock);

    const { http: client } = state.createTnziClient({
      baseUrl: '/api',
      storage: storage.createMemoryStorageAdapter(),
    });

    await client.get('/probe');

    const init = fetchMock.mock.calls[0]?.[1] as { headers?: Record<string, string> } | undefined;
    expect(init?.headers?.[http.CAPABILITY_HEADER]).toBe('probe-v1');

    http.resetClientCapabilities();
  });
});
