import { describe, it, expect, vi } from 'vitest';
import type { App } from 'vue';
import type { Router } from 'vue-router';
import { defineChatApp } from '../../src/plugin/defineChatApp';
import { TOOL_RENDERERS_KEY } from '../../src/components/chat/chat-render-context';

vi.mock('../../src/auth/TAuthRoute.vue', () => ({ default: { name: 'TAuthRoute' } }));
vi.mock('../../src/auth/TGuestWelcome.vue', () => ({ default: { name: 'TGuestWelcome' } }));
vi.mock('@tnzi/core/app-update', () => ({ installAppUpdate: () => undefined }));

/**
 * The opt-in welcome screen for signed-out visitors (type first, sign in on send), and
 * tool renderers registered for the whole app.
 */
type Guard = (to: Record<string, unknown>, from: Record<string, unknown>) => Promise<unknown>;

function setup(loggedIn: boolean, options: Record<string, unknown> = {}) {
  const runtime = {
    auth: { isLoggedIn: loggedIn, restoreAuth: vi.fn(async () => undefined) },
    http: { addUnauthorizedListener: vi.fn(() => () => undefined) },
    authApi: {},
  };
  const guards: Guard[] = [];
  const router = { beforeEach: vi.fn((g: Guard) => guards.push(g)), replace: vi.fn(), currentRoute: { value: { name: 'home', fullPath: '/' } } };
  const provided = new Map<unknown, unknown>();
  const app = { _context: { provides: {} }, provide: vi.fn((k: unknown, v: unknown) => provided.set(k, v)) } as unknown as App;
  const result = defineChatApp({ runtime: runtime as never, home: {} as never, theme: false, ...options });
  result.install(app, router as unknown as Router);
  return { result, guard: guards[0]!, provided };
}

const route = (name: string, path: string, meta: Record<string, unknown> = {}) =>
  ({ name, path, fullPath: path, query: {}, meta });

describe('guestWelcome', () => {
  it('is off by default: a signed-out visit to the conversation goes to sign-in', async () => {
    const { result, guard } = setup(false);
    expect(result.routes.some((r) => r.name === 'welcome')).toBe(false);
    expect(await guard(route('home', '/', { requiresAuth: true }), route('', '/'))).toEqual({ name: 'login', query: { redirect: '/' } });
  });

  it('adds a public welcome route and sends a signed-out visit to the conversation there', async () => {
    const { result, guard } = setup(false, { guestWelcome: { greeting: 'Hello there' }, login: { brandName: 'Acme' } });

    const welcome = result.routes.find((r) => r.name === 'welcome')!;
    expect(welcome.path).toBe('/welcome');
    expect(welcome.meta).toEqual({ requiresAuth: false });
    expect(welcome.props).toMatchObject({ greeting: 'Hello there', brandName: 'Acme', loginName: 'login', homePath: '/' });

    expect(await guard(route('home', '/', { requiresAuth: true }), route('', '/'))).toEqual({ name: 'welcome' });
    expect(await guard(route('welcome', '/welcome', { requiresAuth: false }), route('', '/'))).toBe(true);
  });

  it('login branding, the wordmark included, reaches the sign-in route and the welcome screen', () => {
    const login = { brandName: 'Acme', brandIcon: 'acme:mark', brandWordmark: 'acme:wordmark' };
    const { result } = setup(false, { guestWelcome: true, login });

    expect(result.routes.find((r) => r.name === 'login')!.props).toMatchObject(login);
    expect(result.routes.find((r) => r.name === 'welcome')!.props).toMatchObject(login);
  });

  // A page the consumer swapped in (`component`) gets the route props; a key it does not
  // declare would land on its root as an attribute, so an unset wordmark adds no key at all.
  it('passes no wordmark key when none is configured', () => {
    const { result } = setup(false, { guestWelcome: true, login: { brandName: 'Acme' } });

    expect(result.routes.find((r) => r.name === 'login')!.props).not.toHaveProperty('brandWordmark');
    expect(result.routes.find((r) => r.name === 'welcome')!.props).not.toHaveProperty('brandWordmark');
  });

  it('other protected pages still go to sign-in', async () => {
    const { guard } = setup(false, { guestWelcome: true, routes: [{ path: '/settings', name: 'settings', component: {}, meta: { requiresAuth: true } }] });
    expect(await guard(route('settings', '/settings', { requiresAuth: true }), route('', '/'))).toEqual({
      name: 'login', query: { redirect: '/settings' },
    });
  });

  it('a signed-in visitor goes straight to the conversation', async () => {
    const { guard } = setup(true, { guestWelcome: true });
    expect(await guard(route('home', '/', { requiresAuth: true }), route('', '/'))).toBe(true);
  });
});

describe('toolRenderers', () => {
  it('are provided to the whole app', () => {
    const Shot = { name: 'Shot' };
    const { provided } = setup(true, { toolRenderers: { take_screenshot: Shot } });
    expect(provided.get(TOOL_RENDERERS_KEY)).toEqual({ take_screenshot: Shot });
  });
});
