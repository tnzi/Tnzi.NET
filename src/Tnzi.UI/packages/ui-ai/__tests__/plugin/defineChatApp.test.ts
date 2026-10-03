import { describe, it, expect, vi } from 'vitest';
import type { App } from 'vue';
import type { Router } from 'vue-router';
import { defineChatApp } from '../../src/plugin/defineChatApp';

// `defineChatApp` imports the sign-in SFC for its default login route. This
// package's vitest config has no Vue SFC plugin (its unit tests target the
// headless layer), so stub the component; it plays no part in what is tested.
vi.mock('../../src/auth/TAuthRoute.vue', () => ({ default: {} }));

const installAppUpdate = vi.fn();
vi.mock('@tnzi/core/app-update', () => ({ installAppUpdate: (o: unknown) => installAppUpdate(o) }));

/**
 * Session expiry in a chat app.
 *
 * `createTnziClient` clears the auth state when a 401 survives the refresh
 * attempt, but until 2026-09-04 nothing in `defineChatApp` moved the user off
 * the page: the next click failed again with no explanation. The install step
 * now subscribes to the client's unauthorized listener and sends the user to
 * the login route with the current location in the redirect query, the same
 * behaviour `@tnzi/ui-admin` has had since 2026-07-04.
 */

type Listener = () => void;

function makeRuntime() {
  const listeners: Listener[] = [];
  const runtime = {
    auth: { isLoggedIn: false, restoreAuth: vi.fn(async () => undefined) },
    http: {
      addUnauthorizedListener: vi.fn((fn: Listener) => {
        listeners.push(fn);
        return () => undefined;
      }),
    },
    authApi: {},
  };
  return { runtime: runtime as never, listeners, http: runtime.http };
}

function makeRouter(current: { name: string; fullPath: string }) {
  const replace = vi.fn(async () => undefined);
  const router = { beforeEach: vi.fn(), replace, currentRoute: { value: current } };
  return { router: router as unknown as Router, replace };
}

// Theme provisioning is skipped (`theme: false`), so the app stub only needs
// the shape the guard branch touches.
const app = { _context: { provides: {} }, provide: vi.fn() } as unknown as App;
const home = {} as never;

describe('defineChatApp session expiry', () => {
  it('sends an expired session to the login route, carrying the current location', () => {
    const { runtime, listeners } = makeRuntime();
    const { router, replace } = makeRouter({ name: 'home', fullPath: '/threads/42?tab=files' });

    defineChatApp({ runtime, home, theme: false }).install(app, router);

    expect(listeners).toHaveLength(1);
    listeners[0]!();
    expect(replace).toHaveBeenCalledWith({ name: 'login', query: { redirect: '/threads/42?tab=files' } });
  });

  it('honours custom login route name and redirect query key', () => {
    const { runtime, listeners } = makeRuntime();
    const { router, replace } = makeRouter({ name: 'home', fullPath: '/x' });

    defineChatApp({
      runtime,
      home,
      theme: false,
      login: { name: 'sign-in' },
      redirectQuery: 'next',
    }).install(app, router);

    listeners[0]!();
    expect(replace).toHaveBeenCalledWith({ name: 'sign-in', query: { next: '/x' } });
  });

  it('does not bounce a user who is already on the login route', () => {
    const { runtime, listeners } = makeRuntime();
    const { router, replace } = makeRouter({ name: 'login', fullPath: '/login?redirect=%2Fx' });

    defineChatApp({ runtime, home, theme: false }).install(app, router);
    listeners[0]!();
    expect(replace).not.toHaveBeenCalled();
  });

  it('registers nothing when the host opted out of the guard', () => {
    const { runtime, http } = makeRuntime();
    const { router } = makeRouter({ name: 'home', fullPath: '/x' });

    defineChatApp({ runtime, home, theme: false, guard: false }).install(app, router);
    expect(http.addUnauthorizedListener).not.toHaveBeenCalled();
  });
});

describe('defineChatApp app update', () => {
  it('is on by default and receives the router, even with the guard off', () => {
    installAppUpdate.mockClear();
    const { runtime } = makeRuntime();
    const { router } = makeRouter({ name: 'home', fullPath: '/' });
    defineChatApp({ runtime, home, theme: false, guard: false, appUpdate: { checkInterval: 0 } }).install(app, router);
    expect(installAppUpdate).toHaveBeenCalledWith({ checkInterval: 0, router });
  });

  it('can be switched off', () => {
    installAppUpdate.mockClear();
    const { runtime } = makeRuntime();
    const { router } = makeRouter({ name: 'home', fullPath: '/' });
    defineChatApp({ runtime, home, theme: false, appUpdate: false }).install(app, router);
    expect(installAppUpdate).not.toHaveBeenCalled();
  });
});
