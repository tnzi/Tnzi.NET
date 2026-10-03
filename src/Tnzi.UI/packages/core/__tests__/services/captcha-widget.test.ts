// @vitest-environment happy-dom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { effectScope, nextTick, ref } from 'vue';
import { CaptchaWidgetError, mountCaptchaWidget, toRecaptchaAction } from '../../src/services/captcha/widget';
import { toFetchableChallengeUrl, useCaptchaWidget } from '../../src/services/captcha/useCaptchaWidget';
import type { CaptchaClientConfigDto } from '../../src/services/captcha/types';
import { HttpClient } from '../../src/http/http';

// ---------------------------------------------------------------------------
// The driver talks to third-party scripts through their globals. The tests stub
// those globals and intercept the <script> insertion, firing the provider's
// `onload=` callback the way the real script would. What is pinned: the render
// parameters each provider needs, token plumbing, reset / expiry, and the
// failure modes that must surface instead of hanging.
// ---------------------------------------------------------------------------

type Rendered = { container: HTMLElement; params: Record<string, unknown> };

function fakeHostedGlobal(name: 'grecaptcha' | 'hcaptcha' | 'turnstile') {
  const rendered: Rendered[] = [];
  const api = {
    render: vi.fn((container: HTMLElement, params: Record<string, unknown>) => {
      rendered.push({ container, params });
      return `w${rendered.length}`;
    }),
    reset: vi.fn(),
    remove: vi.fn(),
    getResponse: vi.fn(() => ''),
    execute: vi.fn(() => Promise.resolve('v3-token')),
    ready: vi.fn((cb: () => void) => cb()),
  };
  (globalThis as unknown as Record<string, unknown>)[name] = api;
  return { api, rendered };
}

/** Intercept script insertion: run the onload callback (or fail) instead of fetching anything. */
function interceptScripts(mode: 'onload' | 'error' | 'altcha' = 'onload', beforeOnload?: () => void) {
  const appended: string[] = [];
  const original = document.head.appendChild.bind(document.head);
  const spy = vi.spyOn(document.head, 'appendChild').mockImplementation((node: Node) => {
    const script = node as HTMLScriptElement;
    appended.push(script.src);
    queueMicrotask(() => {
      if (mode === 'error') {
        script.onerror?.(new Event('error'));
        return;
      }
      if (mode === 'altcha') {
        script.onload?.(new Event('load'));
        return;
      }
      beforeOnload?.();
      const cb = new URL(script.src).searchParams.get('onload');
      const fn = cb ? (globalThis as unknown as Record<string, () => void>)[cb] : undefined;
      fn?.();
    });
    return node;
  });
  return { appended, restore: () => spy.mockRestore(), original };
}

function config(overrides: Partial<CaptchaClientConfigDto>): CaptchaClientConfigDto {
  return { enabled: true, provider: 'turnstile', siteKey: 'site', scriptUrl: 'https://cdn.example/turnstile.js?render=explicit', ...overrides };
}

const globalsToClear = ['grecaptcha', 'hcaptcha', 'turnstile'];

beforeEach(() => {
  for (const g of globalsToClear) delete (globalThis as unknown as Record<string, unknown>)[g];
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe('mountCaptchaWidget', () => {
  it('refuses configs it cannot mount, loudly', async () => {
    const el = document.createElement('div');
    await expect(mountCaptchaWidget(el, { enabled: false }, { purpose: 'x', onToken: () => undefined })).rejects.toBeInstanceOf(CaptchaWidgetError);
    await expect(mountCaptchaWidget(el, config({ provider: 'image' }), { purpose: 'x', onToken: () => undefined })).rejects.toBeInstanceOf(CaptchaWidgetError);
    await expect(mountCaptchaWidget(el, config({ scriptUrl: null }), { purpose: 'x', onToken: () => undefined })).rejects.toBeInstanceOf(CaptchaWidgetError);
    await expect(mountCaptchaWidget(el, config({ siteKey: null }), { purpose: 'x', onToken: () => undefined })).rejects.toBeInstanceOf(CaptchaWidgetError);
  });

  it('loads the script with an onload callback and renders Turnstile with the purpose as action', async () => {
    // The global appears "after the script loaded": install it right before onload fires.
    let fake!: ReturnType<typeof fakeHostedGlobal>;
    const scripts = interceptScripts('onload', () => {
      fake = fakeHostedGlobal('turnstile');
    });
    const el = document.createElement('div');
    const onToken = vi.fn();

    const handle = await mountCaptchaWidget(el, config({}), { purpose: 'contact', theme: 'dark', onToken });

    const { api, rendered } = fake;
    expect(scripts.appended[0]).toMatch(/turnstile\.js\?render=explicit&onload=__tnziCaptchaOnload\d+$/);
    expect(rendered).toHaveLength(1);
    expect(rendered[0].container).toBe(el);
    expect(rendered[0].params.sitekey).toBe('site');
    expect(rendered[0].params.action).toBe('contact');
    expect(rendered[0].params.theme).toBe('dark');

    // Token plumbing through the provider's callback, then expiry clears it.
    (rendered[0].params.callback as (t: string) => void)('tok-1');
    expect(onToken).toHaveBeenCalledWith('tok-1');
    await expect(handle.execute()).resolves.toBe('tok-1');
    (rendered[0].params['expired-callback'] as () => void)();
    await expect(handle.execute()).rejects.toBeInstanceOf(CaptchaWidgetError);

    handle.reset();
    expect(api.reset).toHaveBeenCalledWith('w1');
    handle.destroy();
    expect(api.remove).toHaveBeenCalledWith('w1');
    scripts.restore();
  });

  it('does not pass an action to reCAPTCHA v2 / hCaptcha (they have no such field)', async () => {
    const scripts = interceptScripts();
    const { rendered } = fakeHostedGlobal('hcaptcha');
    const el = document.createElement('div');

    await mountCaptchaWidget(el, config({ provider: 'hcaptcha', scriptUrl: 'https://js.hcaptcha.com/1/api.js?render=explicit' }), { purpose: 'login', onToken: () => undefined });

    expect(rendered[0].params).not.toHaveProperty('action');
    scripts.restore();
  });

  it('reCAPTCHA v3 renders nothing and produces the token on execute with the purpose as action', async () => {
    const scripts = interceptScripts();
    const { api, rendered } = fakeHostedGlobal('grecaptcha');
    const el = document.createElement('div');
    const onToken = vi.fn();

    const handle = await mountCaptchaWidget(
      el,
      config({ provider: 'recaptcha-v3', scriptUrl: 'https://www.google.com/recaptcha/api.js?render=site' }),
      { purpose: 'login', onToken },
    );

    expect(rendered).toHaveLength(0);
    await expect(handle.execute()).resolves.toBe('v3-token');
    expect(api.execute).toHaveBeenCalledWith('site', { action: 'login' });
    expect(onToken).toHaveBeenCalledWith('v3-token');
    scripts.restore();
  });

  it('reCAPTCHA v3 folds a hyphenated purpose into an action name Google accepts', async () => {
    const scripts = interceptScripts();
    const { api } = fakeHostedGlobal('grecaptcha');

    const handle = await mountCaptchaWidget(
      document.createElement('div'),
      config({ provider: 'recaptcha-v3', scriptUrl: 'https://www.google.com/recaptcha/api.js?render=site' }),
      { purpose: 'password-recovery', onToken: () => undefined },
    );
    await handle.execute();

    expect(api.execute).toHaveBeenCalledWith('site', { action: 'password_recovery' });
    expect(toRecaptchaAction('a/b_c-d e')).toBe('a/b_c_d_e');
    scripts.restore();
  });

  it('reuses an already-loaded global without inserting a second script', async () => {
    const scripts = interceptScripts();
    fakeHostedGlobal('turnstile');

    await mountCaptchaWidget(document.createElement('div'), config({}), { purpose: 'a', onToken: () => undefined });

    expect(scripts.appended).toHaveLength(0);
    scripts.restore();
  });

  it('surfaces a blocked script as an error instead of hanging', async () => {
    const scripts = interceptScripts('error');

    await expect(
      mountCaptchaWidget(document.createElement('div'), config({ scriptUrl: 'https://blocked.example/x.js' }), { purpose: 'a', onToken: () => undefined }),
    ).rejects.toThrow(/Failed to load/);
    scripts.restore();
  });

  it('Altcha mounts the web component with the challenge URL and forwards the verified payload', async () => {
    const scripts = interceptScripts('altcha');
    if (!customElements.get('altcha-widget')) {
      customElements.define('altcha-widget', class extends HTMLElement {
        reset = vi.fn();
      });
    }
    const el = document.createElement('div');
    const onToken = vi.fn();
    const onExpired = vi.fn();

    const handle = await mountCaptchaWidget(
      el,
      config({ provider: 'altcha', siteKey: null, scriptUrl: 'https://cdn.example/altcha.js', challengeUrl: 'captcha/altcha/challenge?purpose={purpose}' }),
      { purpose: 'login', challengeUrl: 'https://api.example/api/captcha/altcha/challenge?purpose=login', onToken, onExpired, altchaAttributes: { hidefooter: '' } },
    );

    const widget = el.querySelector('altcha-widget')!;
    // 3.x reads `challenge`; `challengeurl` is the 1.x / 2.x name (kept for a self-hosted older copy).
    expect(widget.getAttribute('challenge')).toBe('https://api.example/api/captcha/altcha/challenge?purpose=login');
    expect(widget.getAttribute('challengeurl')).toBe('https://api.example/api/captcha/altcha/challenge?purpose=login');
    expect(widget.hasAttribute('hidefooter')).toBe(true);

    widget.dispatchEvent(new CustomEvent('statechange', { detail: { state: 'verified', payload: 'base64-payload' } }));
    expect(onToken).toHaveBeenCalledWith('base64-payload');
    await expect(handle.execute()).resolves.toBe('base64-payload');

    widget.dispatchEvent(new CustomEvent('statechange', { detail: { state: 'unverified' } }));
    expect(onExpired).toHaveBeenCalled();

    handle.destroy();
    expect(el.querySelector('altcha-widget')).toBeNull();
    scripts.restore();
  });

  it('Altcha needs a challenge URL', async () => {
    await expect(
      mountCaptchaWidget(document.createElement('div'), config({ provider: 'altcha', scriptUrl: 'https://cdn.example/altcha.js' }), { purpose: 'login', onToken: () => undefined }),
    ).rejects.toThrow(/challenge URL/);
  });
});

describe('useCaptchaWidget', () => {
  it('reports image / disabled configs as not a script provider and never mounts', async () => {
    const cfg = ref<CaptchaClientConfigDto | null>({ enabled: true, provider: 'image', challengeUrl: 'auth/captcha/{purpose}/json' });
    const w = useCaptchaWidget({ config: cfg, purpose: 'login' });
    w.container.value = document.createElement('div');
    await nextTick();

    expect(w.isScriptProvider.value).toBe(false);
    expect(w.ready.value).toBe(false);
    await expect(w.execute()).rejects.toThrow();
  });

  it('mounts once the container is bound, tracks the token and resets it', async () => {
    const scripts = interceptScripts();
    const { api, rendered } = fakeHostedGlobal('turnstile');
    const cfg = ref<CaptchaClientConfigDto | null>(config({}));
    const w = useCaptchaWidget({ config: cfg, purpose: 'register' });

    w.container.value = document.createElement('div');
    await nextTick();
    await vi.waitFor(() => expect(w.ready.value).toBe(true));

    (rendered[0].params.callback as (t: string) => void)('tok');
    expect(w.token.value).toBe('tok');

    w.reset();
    expect(w.token.value).toBe('');
    expect(api.reset).toHaveBeenCalled();
    scripts.restore();
  });

  it('turns a failed script load into an error message rather than a rejection', async () => {
    const scripts = interceptScripts('error');
    const cfg = ref<CaptchaClientConfigDto | null>(config({ scriptUrl: 'https://blocked.example/y.js' }));
    const w = useCaptchaWidget({ config: cfg, purpose: 'register', translate: (_k, f) => f ?? '' });

    w.container.value = document.createElement('div');
    await nextTick();
    await vi.waitFor(() => expect(w.error.value).not.toBe(''));

    expect(w.ready.value).toBe(false);
    scripts.restore();
  });

  it('★ resolves the Altcha challenge URL through a real HttpClient: <apiBase>/captcha/..., not page-relative', async () => {
    const scripts = interceptScripts('altcha');
    if (!customElements.get('altcha-widget')) {
      customElements.define('altcha-widget', class extends HTMLElement {});
    }
    const cfg = ref<CaptchaClientConfigDto | null>(
      config({ provider: 'altcha', siteKey: null, scriptUrl: 'https://cdn.example/altcha.js', challengeUrl: 'captcha/altcha/challenge?purpose={purpose}' }),
    );
    // A real client, not a hand-written resolver: the join is the thing under test. With the raw
    // backend template this produced `/apicaptcha/altcha/challenge` against a `/api` base.
    const client = new HttpClient({ baseUrl: '/api' });
    const resolveUrl = vi.spyOn(client, 'resolveUrl');
    const w = useCaptchaWidget({ config: cfg, purpose: 'login', client });

    const el = document.createElement('div');
    w.container.value = el;
    await nextTick();
    await vi.waitFor(() => expect(w.ready.value).toBe(true));

    expect(resolveUrl).toHaveBeenCalledWith('/captcha/altcha/challenge?purpose=login');
    expect(el.querySelector('altcha-widget')!.getAttribute('challengeurl')).toBe('/api/captcha/altcha/challenge?purpose=login');
    scripts.restore();
  });

  it('★ refuses to mount Altcha without a client instead of fetching the challenge relative to the page', async () => {
    const scripts = interceptScripts('altcha');
    if (!customElements.get('altcha-widget')) {
      customElements.define('altcha-widget', class extends HTMLElement {});
    }
    const cfg = ref<CaptchaClientConfigDto | null>(
      config({ provider: 'altcha', siteKey: null, scriptUrl: 'https://cdn.example/altcha.js', challengeUrl: 'captcha/altcha/challenge?purpose={purpose}' }),
    );
    const w = useCaptchaWidget({ config: cfg, purpose: 'login' });

    const el = document.createElement('div');
    w.container.value = el;
    await nextTick();
    await vi.waitFor(() => expect(w.error.value).not.toBe(''));

    expect(w.error.value).toMatch(/relative to the API root/);
    expect(w.ready.value).toBe(false);
    expect(el.querySelector('altcha-widget')).toBeNull();
    scripts.restore();
  });

  it('an absolute challenge template needs no client', async () => {
    const scripts = interceptScripts('altcha');
    if (!customElements.get('altcha-widget')) {
      customElements.define('altcha-widget', class extends HTMLElement {});
    }
    const cfg = ref<CaptchaClientConfigDto | null>(
      config({ provider: 'altcha', siteKey: null, scriptUrl: 'https://cdn.example/altcha.js', challengeUrl: 'https://api.example/api/captcha/altcha/challenge?purpose={purpose}' }),
    );
    const w = useCaptchaWidget({ config: cfg, purpose: 'contact' });

    const el = document.createElement('div');
    w.container.value = el;
    await nextTick();
    await vi.waitFor(() => expect(w.ready.value).toBe(true));

    expect(el.querySelector('altcha-widget')!.getAttribute('challengeurl')).toBe('https://api.example/api/captcha/altcha/challenge?purpose=contact');
    scripts.restore();
  });
});

/**
 * A mount can be torn down while its provider script is still loading: the
 * container leaves the page (v-if), or the owning component unmounts. The
 * late mount must not install a widget nobody will ever destroy.
 */
describe('useCaptchaWidget - teardown while the script is loading', () => {
  /**
   * A Turnstile global that only appears once its script "loads": inserted
   * scripts are held until `loadAll()`, which installs the global and fires the
   * `onload=` callback the way the real script would. (With the global already
   * present the loader skips the script and the mount completes at once.)
   */
  function holdScripts() {
    const fake = fakeHostedGlobal('turnstile');
    delete (globalThis as unknown as Record<string, unknown>).turnstile;
    const pending: HTMLScriptElement[] = [];
    vi.spyOn(document.head, 'appendChild').mockImplementation((node: Node) => {
      pending.push(node as HTMLScriptElement);
      return node;
    });
    return {
      ...fake,
      loadAll: () => {
        (globalThis as unknown as Record<string, unknown>).turnstile = fake.api;
        for (const script of pending.splice(0)) {
          const cb = new URL(script.src).searchParams.get('onload');
          (cb ? (globalThis as unknown as Record<string, () => void>)[cb] : undefined)?.();
        }
      },
    };
  }

  it('does not keep a widget whose container went away mid-load', async () => {
    const scripts = holdScripts();
    const { api, rendered } = scripts;
    const cfg = ref<CaptchaClientConfigDto | null>(config({ scriptUrl: 'https://cdn.example/turnstile-a.js?render=explicit' }));
    const w = useCaptchaWidget({ config: cfg, purpose: 'login' });

    w.container.value = document.createElement('div');
    await nextTick();
    expect(rendered.length).toBe(0); // still loading
    w.container.value = null;
    await nextTick();
    scripts.loadAll();
    await vi.waitFor(() => expect(rendered.length).toBe(1));
    await Promise.resolve();

    expect(w.ready.value).toBe(false);
    expect(api.remove).toHaveBeenCalled();
    await expect(w.execute()).rejects.toThrow();
  });

  it('does not keep a widget after its scope was disposed mid-load', async () => {
    const scripts = holdScripts();
    const { api, rendered } = scripts;
    const scope = effectScope();
    const w = scope.run(() =>
      useCaptchaWidget({ config: ref<CaptchaClientConfigDto | null>(config({ scriptUrl: 'https://cdn.example/turnstile-b.js?render=explicit' })), purpose: 'login' }),
    )!;

    w.container.value = document.createElement('div');
    await nextTick();
    expect(rendered.length).toBe(0); // still loading
    scope.stop();
    scripts.loadAll();
    await vi.waitFor(() => expect(rendered.length).toBe(1));
    await Promise.resolve();

    expect(w.ready.value).toBe(false);
    expect(api.remove).toHaveBeenCalled();
  });
});

describe('toFetchableChallengeUrl', () => {
  const altcha = { challengeUrl: 'captcha/altcha/challenge?purpose={purpose}' };

  it('joins the client path onto the API base', () => {
    expect(toFetchableChallengeUrl(altcha, 'login', new HttpClient({ baseUrl: '/api' }))).toBe('/api/captcha/altcha/challenge?purpose=login');
    expect(toFetchableChallengeUrl(altcha, 'login', new HttpClient({ baseUrl: 'https://api.example/api' }))).toBe(
      'https://api.example/api/captcha/altcha/challenge?purpose=login',
    );
  });

  it('yields nothing for providers without a template, passes absolute templates through, and refuses to guess otherwise', () => {
    expect(toFetchableChallengeUrl({ challengeUrl: null }, 'login', undefined)).toBeUndefined();
    expect(toFetchableChallengeUrl({ challengeUrl: 'https://api.example/c?purpose={purpose}' }, 'login', undefined)).toBe('https://api.example/c?purpose=login');
    expect(() => toFetchableChallengeUrl(altcha, 'login', undefined)).toThrow(CaptchaWidgetError);
  });
});
