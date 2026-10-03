// @vitest-environment happy-dom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import {
  installAppUpdate,
  isChunkLoadError,
  readShellFingerprint,
  RECOVERY_WINDOW_MS,
  type AppUpdateHandle,
  type AppUpdateRoute,
} from '../../src/app-update';

type Guard = (to: AppUpdateRoute, from: AppUpdateRoute) => boolean | void;
type ErrorHandler = (error: unknown, to: AppUpdateRoute, from: AppUpdateRoute) => void;

function fakeRouter() {
  const guards: Guard[] = [];
  const errorHandlers: ErrorHandler[] = [];
  return {
    guards,
    errorHandlers,
    beforeEach(guard: Guard) {
      guards.push(guard);
      return () => guards.splice(guards.indexOf(guard), 1);
    },
    onError(handler: ErrorHandler) {
      errorHandlers.push(handler);
      return () => errorHandlers.splice(errorHandlers.indexOf(handler), 1);
    },
    resolve(to: string) {
      return { href: `/app${to}` };
    },
    navigate(to: string, from = '/home'): boolean | void {
      let result: boolean | void;
      for (const guard of guards) {
        result = guard({ fullPath: to }, { fullPath: from });
        if (result === false) return false;
      }
      return result;
    },
    fail(error: unknown, to: string) {
      for (const handler of errorHandlers) handler(error, { fullPath: to }, { fullPath: '/home' });
    },
  };
}

function shell(...entries: string[]): string {
  return `<!doctype html><html><head>${entries
    .map((src) => `<script type="module" crossorigin src="${src}"></script>`)
    .join('')}</head><body><div id="app"></div></body></html>`;
}

function setDocumentEntries(...entries: string[]) {
  document.head.innerHTML = entries
    .map((src) => `<script type="module" src="${src}"></script>`)
    .join('');
}

function htmlResponse(body: string, init: ResponseInit = {}) {
  return new Response(body, {
    status: 200,
    headers: { 'content-type': 'text/html; charset=utf-8' },
    ...init,
  });
}

describe('isChunkLoadError', () => {
  it.each([
    'Failed to fetch dynamically imported module: https://x/assets/a-1.js',
    'error loading dynamically imported module: https://x/assets/a-1.js',
    'Importing a module script failed.',
    'Unable to preload CSS for /assets/a-1.css',
    "'text/html' is not a valid JavaScript MIME type.",
  ])('recognises %s', (message) => {
    expect(isChunkLoadError(new TypeError(message))).toBe(true);
  });

  it('recognises a webpack ChunkLoadError by name', () => {
    const error = new Error('Loading chunk 3 failed');
    error.name = 'ChunkLoadError';
    expect(isChunkLoadError(error)).toBe(true);
  });

  it('ignores ordinary errors', () => {
    expect(isChunkLoadError(new Error('Request failed with status 500'))).toBe(false);
    expect(isChunkLoadError(null)).toBe(false);
  });
});

describe('readShellFingerprint', () => {
  it('collects module entry scripts as sorted pathnames', () => {
    const doc = new DOMParser().parseFromString(
      shell('/assets/vendor-b.js', 'https://site.test/assets/index-a.js'),
      'text/html',
    );
    expect(readShellFingerprint(doc, 'https://site.test/admin/users')).toBe(
      '/assets/index-a.js|/assets/vendor-b.js',
    );
  });

  it('returns empty for a page without entry scripts', () => {
    const doc = new DOMParser().parseFromString('<html><body>Maintenance</body></html>', 'text/html');
    expect(readShellFingerprint(doc, 'https://site.test/')).toBe('');
  });
});

describe('installAppUpdate', () => {
  let handle: AppUpdateHandle | undefined;
  let assign: ReturnType<typeof vi.fn>;
  let reload: ReturnType<typeof vi.fn>;
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: false });
    window.sessionStorage.clear();
    setDocumentEntries('/assets/index-old.js');
    assign = vi.fn();
    reload = vi.fn();
    vi.spyOn(window.location, 'assign').mockImplementation(assign);
    vi.spyOn(window.location, 'reload').mockImplementation(reload);
    fetchMock = vi.fn(async () => htmlResponse(shell('/assets/index-old.js')));
    vi.stubGlobal('fetch', fetchMock);
  });

  afterEach(() => {
    handle?.dispose();
    handle = undefined;
    vi.useRealTimers();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
    document.head.innerHTML = '';
  });

  it('is inert on the Vite dev server', async () => {
    document.head.innerHTML =
      '<script type="module" src="/@vite/client"></script><script type="module" src="/src/main.ts"></script>';
    const router = fakeRouter();
    handle = installAppUpdate({ router });
    expect(router.guards).toHaveLength(0);
    expect(router.errorHandlers).toHaveLength(0);
    expect(await handle.checkNow()).toBe(false);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('installs once per page and hands back the same handle', () => {
    const router = fakeRouter();
    handle = installAppUpdate({ router });
    expect(installAppUpdate({ router })).toBe(handle);
    expect(router.guards).toHaveLength(1);
    expect(router.errorHandlers).toHaveLength(1);
  });

  it('reports no update while the shell is unchanged', async () => {
    handle = installAppUpdate({ router: fakeRouter() });
    expect(await handle.checkNow()).toBe(false);
    expect(fetchMock).toHaveBeenCalledWith(expect.any(String), expect.objectContaining({ cache: 'no-store' }));
  });

  it('detects a new build and reloads into the next navigation target', async () => {
    fetchMock.mockImplementation(async () => htmlResponse(shell('/assets/index-new.js')));
    const onUpdateAvailable = vi.fn();
    const router = fakeRouter();
    handle = installAppUpdate({ router, onUpdateAvailable });

    expect(await handle.checkNow()).toBe(true);
    expect(onUpdateAvailable).toHaveBeenCalledTimes(1);
    expect(handle.updateAvailable).toBe(true);
    // Detection alone never interrupts the page.
    expect(assign).not.toHaveBeenCalled();

    expect(router.navigate('/orders')).toBe(false);
    expect(assign).toHaveBeenCalledWith(new URL('/app/orders', window.location.href).href);
  });

  it('does not reload twice for the same remote build (stale cached shell)', async () => {
    fetchMock.mockImplementation(async () => htmlResponse(shell('/assets/index-new.js')));
    window.sessionStorage.setItem('tnzi:app-update:reloaded-for', '/assets/index-new.js');
    const router = fakeRouter();
    handle = installAppUpdate({ router });
    await handle.checkNow();

    expect(router.navigate('/orders')).toBeUndefined();
    expect(assign).not.toHaveBeenCalled();
  });

  it.each([
    ['a maintenance page', () => htmlResponse('<html><body>Down for maintenance</body></html>')],
    ['a gateway error', () => htmlResponse(shell('/assets/index-new.js'), { status: 502 })],
    ['a non-HTML answer', () => new Response('{}', { headers: { 'content-type': 'application/json' } })],
  ])('ignores %s', async (_label, respond) => {
    fetchMock.mockImplementation(async () => respond());
    handle = installAppUpdate({ router: fakeRouter() });
    expect(await handle.checkNow()).toBe(false);
  });

  it('ignores a network failure', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));
    handle = installAppUpdate({ router: fakeRouter() });
    expect(await handle.checkNow()).toBe(false);
  });

  it('only notifies in notify mode', async () => {
    fetchMock.mockImplementation(async () => htmlResponse(shell('/assets/index-new.js')));
    const router = fakeRouter();
    handle = installAppUpdate({ router, mode: 'notify' });
    expect(await handle.checkNow()).toBe(true);
    expect(router.navigate('/orders')).toBeUndefined();
    expect(assign).not.toHaveBeenCalled();
  });

  it('asks once in prompt mode and reloads on acceptance', async () => {
    fetchMock.mockImplementation(async () => htmlResponse(shell('/assets/index-new.js')));
    const prompt = vi.fn(async () => true);
    handle = installAppUpdate({ router: fakeRouter(), mode: 'prompt', prompt });

    await handle.checkNow();
    await handle.checkNow();
    await vi.waitFor(() => expect(reload).toHaveBeenCalledTimes(1));
    expect(prompt).toHaveBeenCalledTimes(1);
  });

  it('leaves the page alone when the prompt is declined', async () => {
    fetchMock.mockImplementation(async () => htmlResponse(shell('/assets/index-new.js')));
    const router = fakeRouter();
    handle = installAppUpdate({ router, mode: 'prompt', prompt: async () => false });
    await handle.checkNow();
    await Promise.resolve();

    expect(router.navigate('/orders')).toBeUndefined();
    expect(reload).not.toHaveBeenCalled();
    expect(assign).not.toHaveBeenCalled();
  });

  it('checks when the tab comes back, at most once per gap', async () => {
    handle = installAppUpdate({ router: fakeRouter(), checkInterval: 0 });
    document.dispatchEvent(new Event('visibilitychange'));
    document.dispatchEvent(new Event('visibilitychange'));
    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
  });

  it('checks on the interval while visible', async () => {
    handle = installAppUpdate({ router: fakeRouter(), checkInterval: 60_000 });
    vi.advanceTimersByTime(60_000);
    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
  });

  describe('chunk load failure', () => {
    const chunkError = new TypeError('Failed to fetch dynamically imported module: /assets/Orders-1.js');

    it('loads the navigation target in full', () => {
      const router = fakeRouter();
      handle = installAppUpdate({ router });
      router.fail(chunkError, '/orders');
      expect(assign).toHaveBeenCalledWith(new URL('/app/orders', window.location.href).href);
    });

    it('ignores errors that are not chunk failures', () => {
      const router = fakeRouter();
      handle = installAppUpdate({ router });
      router.fail(new Error('boom'), '/orders');
      expect(assign).not.toHaveBeenCalled();
    });

    it('retries the same target only once within the window', () => {
      window.sessionStorage.setItem(
        'tnzi:app-update:recovery',
        JSON.stringify({ href: new URL('/app/orders', window.location.href).href, at: Date.now() }),
      );
      const router = fakeRouter();
      handle = installAppUpdate({ router });
      router.fail(chunkError, '/orders');
      expect(assign).not.toHaveBeenCalled();
    });

    it('retries again once the window has passed', () => {
      window.sessionStorage.setItem(
        'tnzi:app-update:recovery',
        JSON.stringify({
          href: new URL('/app/orders', window.location.href).href,
          at: Date.now() - RECOVERY_WINDOW_MS - 1,
        }),
      );
      const router = fakeRouter();
      handle = installAppUpdate({ router });
      router.fail(chunkError, '/orders');
      expect(assign).toHaveBeenCalledTimes(1);
    });

    it('does not reload when session storage is unavailable (no loop guard)', () => {
      vi.spyOn(window, 'sessionStorage', 'get').mockImplementation(() => {
        throw new Error('SecurityError');
      });
      const router = fakeRouter();
      handle = installAppUpdate({ router });
      router.fail(chunkError, '/orders');
      expect(assign).not.toHaveBeenCalled();
    });

    it('reloads the current page for a non-route lazy chunk', () => {
      handle = installAppUpdate({ router: fakeRouter() });
      window.dispatchEvent(new Event('vite:preloadError'));
      vi.advanceTimersByTime(0);
      expect(reload).toHaveBeenCalledTimes(1);
    });

    it('lets the route handler win when both report the same failure', () => {
      const router = fakeRouter();
      handle = installAppUpdate({ router });
      window.dispatchEvent(new Event('vite:preloadError'));
      router.fail(chunkError, '/orders');
      vi.advanceTimersByTime(0);
      expect(assign).toHaveBeenCalledTimes(1);
      expect(reload).not.toHaveBeenCalled();
    });

    it('does not fall back to reloading the current page when the route recovery is refused', () => {
      window.sessionStorage.setItem(
        'tnzi:app-update:recovery',
        JSON.stringify({ href: new URL('/app/orders', window.location.href).href, at: Date.now() }),
      );
      const router = fakeRouter();
      handle = installAppUpdate({ router });
      window.dispatchEvent(new Event('vite:preloadError'));
      router.fail(chunkError, '/orders');
      vi.advanceTimersByTime(0);
      expect(assign).not.toHaveBeenCalled();
      expect(reload).not.toHaveBeenCalled();
    });

    it('can be switched off', () => {
      const router = fakeRouter();
      handle = installAppUpdate({ router, recoverChunkErrors: false });
      expect(router.errorHandlers).toHaveLength(0);
    });
  });

  it('dispose removes every hook and frees the slot', () => {
    const router = fakeRouter();
    handle = installAppUpdate({ router });
    handle.dispose();
    expect(router.guards).toHaveLength(0);
    expect(router.errorHandlers).toHaveLength(0);
    const next = installAppUpdate({ router });
    expect(next).not.toBe(handle);
    handle = next;
  });
});
