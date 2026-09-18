/**
 * Captcha widget driver - framework-agnostic.
 *
 * Loads the provider's script once, mounts its widget into a container element and
 * hands the token back through callbacks. reCAPTCHA v2, hCaptcha and Turnstile share
 * one shape (`render(container, { sitekey, callback })` → widget id); reCAPTCHA v3 has no
 * widget and produces its token on demand; Altcha is a web component that verifies
 * against a challenge URL. The built-in `image` / `sliding` providers are not scripts
 * and are rendered by the UI packages themselves.
 *
 * Vue callers use {@link useCaptchaWidget}; anything else (a plain form, a non-Vue app)
 * can call {@link mountCaptchaWidget} directly.
 */

import { createAdapterSingleton } from '../../adapters/singleton';
import type { CaptchaClientConfigDto } from './types';
import { isInvisibleCaptchaProvider, isScriptCaptchaProvider } from './types';

export interface CaptchaWidgetOptions {
  /** The purpose the token will be submitted for (bound into the token where the provider supports it). */
  purpose: string;
  /**
   * Challenge URL the widget can fetch, for providers that fetch their own challenge (Altcha):
   * absolute, or rooted at the page origin **including the API base** (`/api/captcha/...`).
   * Build it with `toFetchableChallengeUrl(config, purpose, client)`, which is
   * `client.resolveUrl(resolveChallengeUrl(config, purpose))` plus the refusal to guess without a client.
   */
  challengeUrl?: string;
  /** Widget colour scheme where the provider supports it. */
  theme?: 'light' | 'dark' | 'auto';
  /** Extra attributes for the `<altcha-widget>` element (e.g. `strings`, `hidefooter`). */
  altchaAttributes?: Record<string, string>;
  /** A token was obtained. */
  onToken: (token: string) => void;
  /** The token the widget held expired; callers should clear what they stored. */
  onExpired?: () => void;
  /** The widget failed (script blocked, network, provider error). */
  onError?: (error: unknown) => void;
}

export interface CaptchaWidgetHandle {
  /** Clear the widget so the user can solve it again (after a rejected submit or a consumed token). */
  reset: () => void;
  /**
   * Obtain a token now. Invisible providers (reCAPTCHA v3) run their check here; visible ones
   * return the token the user already produced and reject when there is none yet.
   */
  execute: () => Promise<string>;
  /** Remove the widget from the page. */
  destroy: () => void;
}

/** Thrown by {@link mountCaptchaWidget} when the config is not something this driver can mount. */
export class CaptchaWidgetError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'CaptchaWidgetError';
  }
}

// ---------------------------------------------------------------------------
// Script loading (one promise per URL, `onload=` callback for the hosted providers)
// ---------------------------------------------------------------------------

/**
 * One in-flight load per script URL + the onload callback counter. Parked on the
 * globalThis registry (see adapters/singleton.ts): with `splitting: false` a module-level
 * Map is copied into every bundle that inlines this file, and two copies would insert the
 * same provider script twice.
 */
interface ScriptLoadState {
  loads: Map<string, Promise<void>>;
  callbackSeq: number;
}
const scriptLoadState = createAdapterSingleton<ScriptLoadState>('captcha-script-loads', () => ({ loads: new Map(), callbackSeq: 0 }));

type ProviderGlobal = {
  render: (container: HTMLElement, params: Record<string, unknown>) => string | number;
  reset: (id?: string | number) => void;
  getResponse: (id?: string | number) => string;
  remove?: (id: string | number) => void;
  execute?: (siteKey: string, options: { action: string }) => Promise<string>;
  ready?: (cb: () => void) => void;
};

function globalName(provider: string): 'grecaptcha' | 'hcaptcha' | 'turnstile' {
  switch (provider) {
    case 'recaptcha':
    case 'recaptcha-v3':
      return 'grecaptcha';
    case 'hcaptcha':
      return 'hcaptcha';
    case 'turnstile':
      return 'turnstile';
    default:
      throw new CaptchaWidgetError(`Provider "${provider}" has no script global.`);
  }
}

function readGlobal(name: string): ProviderGlobal | undefined {
  return (globalThis as unknown as Record<string, ProviderGlobal | undefined>)[name];
}

/**
 * Load a hosted provider's script and resolve once its global is ready to render.
 * Uses the provider's `onload=` callback (all three support it) rather than polling.
 */
function loadHostedScript(provider: string, scriptUrl: string): Promise<void> {
  const name = globalName(provider);
  const existing = readGlobal(name);
  if (existing && typeof existing.render === 'function') return Promise.resolve();

  const state = scriptLoadState.use();
  let pending = state.loads.get(scriptUrl);
  if (pending) return pending;

  pending = new Promise<void>((resolve, reject) => {
    if (typeof document === 'undefined') {
      reject(new CaptchaWidgetError('Captcha widgets need a DOM.'));
      return;
    }
    const cbName = `__tnziCaptchaOnload${++state.callbackSeq}`;
    const w = globalThis as unknown as Record<string, unknown>;
    w[cbName] = () => {
      delete w[cbName];
      resolve();
    };
    const url = `${scriptUrl}${scriptUrl.includes('?') ? '&' : '?'}onload=${cbName}`;
    const script = document.createElement('script');
    script.src = url;
    script.async = true;
    script.defer = true;
    script.onerror = () => {
      delete w[cbName];
      state.loads.delete(scriptUrl);
      reject(new CaptchaWidgetError(`Failed to load the captcha script ${scriptUrl}`));
    };
    document.head.appendChild(script);
  });
  state.loads.set(scriptUrl, pending);
  return pending;
}

/** Load the Altcha web component script (defines `<altcha-widget>`). */
function loadAltchaScript(scriptUrl: string): Promise<void> {
  if (typeof customElements !== 'undefined' && customElements.get('altcha-widget')) return Promise.resolve();

  const state = scriptLoadState.use();
  let pending = state.loads.get(scriptUrl);
  if (pending) return pending;

  pending = new Promise<void>((resolve, reject) => {
    if (typeof document === 'undefined') {
      reject(new CaptchaWidgetError('Captcha widgets need a DOM.'));
      return;
    }
    const script = document.createElement('script');
    script.src = scriptUrl;
    script.async = true;
    script.type = 'module';
    script.onload = () => {
      customElements.whenDefined('altcha-widget').then(() => resolve(), reject);
    };
    script.onerror = () => {
      state.loads.delete(scriptUrl);
      reject(new CaptchaWidgetError(`Failed to load the Altcha script ${scriptUrl}`));
    };
    document.head.appendChild(script);
  });
  state.loads.set(scriptUrl, pending);
  return pending;
}

// ---------------------------------------------------------------------------
// Mounting
// ---------------------------------------------------------------------------

/**
 * Mount the active provider's widget into `container`.
 *
 * Rejects with {@link CaptchaWidgetError} when the config is disabled, names a provider this
 * driver does not know (`image` / `sliding` are rendered by the UI, not here), lacks a site key,
 * or when the script cannot be loaded.
 */
export async function mountCaptchaWidget(
  container: HTMLElement,
  config: CaptchaClientConfigDto,
  options: CaptchaWidgetOptions,
): Promise<CaptchaWidgetHandle> {
  const provider = config.provider ?? '';
  if (!config.enabled || !isScriptCaptchaProvider(provider)) {
    throw new CaptchaWidgetError(`Provider "${provider || '(none)'}" is not a script widget.`);
  }
  if (!config.scriptUrl) {
    throw new CaptchaWidgetError(`Provider "${provider}" did not publish a script URL.`);
  }

  if (provider === 'altcha') {
    return mountAltcha(container, config.scriptUrl, options);
  }

  if (!config.siteKey) {
    throw new CaptchaWidgetError(`Provider "${provider}" did not publish a site key.`);
  }

  await loadHostedScript(provider, config.scriptUrl);
  const api = readGlobal(globalName(provider));
  if (!api) throw new CaptchaWidgetError(`Provider "${provider}" script loaded but exposed no API.`);

  if (isInvisibleCaptchaProvider(provider)) {
    return mountInvisible(api, config.siteKey, options);
  }
  return mountRendered(provider, api, container, config.siteKey, options);
}

/** reCAPTCHA v2 / hCaptcha / Turnstile: explicit render into the container. */
function mountRendered(
  provider: string,
  api: ProviderGlobal,
  container: HTMLElement,
  siteKey: string,
  options: CaptchaWidgetOptions,
): CaptchaWidgetHandle {
  let current = '';
  const params: Record<string, unknown> = {
    sitekey: siteKey,
    callback: (token: string) => {
      current = token;
      options.onToken(token);
    },
    'expired-callback': () => {
      current = '';
      options.onExpired?.();
    },
    'error-callback': (err: unknown) => {
      current = '';
      options.onError?.(err ?? new CaptchaWidgetError(`${provider} reported an error.`));
    },
  };
  if (options.theme) params.theme = options.theme;
  // Turnstile binds the action into the token; reCAPTCHA v2 / hCaptcha have no such field.
  if (provider === 'turnstile') params.action = options.purpose;

  const id = api.render(container, params);
  return {
    reset: () => {
      current = '';
      api.reset(id);
    },
    execute: () => {
      const token = current || api.getResponse(id);
      return token ? Promise.resolve(token) : Promise.reject(new CaptchaWidgetError('Captcha not solved yet.'));
    },
    destroy: () => {
      current = '';
      if (api.remove) api.remove(id);
      else container.replaceChildren();
    },
  };
}

/**
 * reCAPTCHA v3 only accepts `[A-Za-z0-9/_]` in an action name; a purpose like `password-recovery`
 * would be refused. The server compares actions with the same hyphen/underscore folding.
 */
export function toRecaptchaAction(purpose: string): string {
  return purpose.replace(/[^A-Za-z0-9/_]/g, '_');
}

/** reCAPTCHA v3: nothing to render, the token is produced by `execute` with the purpose as action. */
function mountInvisible(api: ProviderGlobal, siteKey: string, options: CaptchaWidgetOptions): CaptchaWidgetHandle {
  const ready = () =>
    new Promise<void>((resolve) => {
      if (api.ready) api.ready(resolve);
      else resolve();
    });
  return {
    reset: () => undefined,
    execute: async () => {
      if (!api.execute) throw new CaptchaWidgetError('reCAPTCHA v3 script exposed no execute().');
      await ready();
      const token = await api.execute(siteKey, { action: toRecaptchaAction(options.purpose) });
      options.onToken(token);
      return token;
    },
    destroy: () => undefined,
  };
}

/**
 * Altcha: the `<altcha-widget>` web component, token = the verified payload.
 *
 * Targets the widget's **3.x** major (what the backend's default `ScriptUrl` pins and what the
 * real-widget test in this package loads). 3.x reads its challenge source from the `challenge`
 * attribute (a URL, or inline JSON when it starts with `{`); `challengeurl` was the 1.x / 2.x name
 * and 3.x ignores it. Both are set: the 2.x name costs nothing and keeps a self-hosted 2.x copy
 * working. Setting only the old name left 3.x with `challenge: ""`, so it fetched the page itself
 * and reported "invalid content-type … received text/html" (2026-09-17).
 *
 * The widget fetches the URL itself and validates the body as an Altcha document (a top-level
 * `challenge` key); the backend's challenge endpoint therefore answers the bare document, not
 * the `ApiResult` envelope.
 */
async function mountAltcha(container: HTMLElement, scriptUrl: string, options: CaptchaWidgetOptions): Promise<CaptchaWidgetHandle> {
  if (!options.challengeUrl) {
    throw new CaptchaWidgetError('Altcha needs a challenge URL (resolve the config\'s challengeUrl with the purpose).');
  }
  await loadAltchaScript(scriptUrl);

  const el = document.createElement('altcha-widget') as HTMLElement & { reset?: () => void; verify?: () => void };
  el.setAttribute('challenge', options.challengeUrl);
  el.setAttribute('challengeurl', options.challengeUrl);
  for (const [key, value] of Object.entries(options.altchaAttributes ?? {})) {
    el.setAttribute(key, value);
  }

  let current = '';
  const onStateChange = (event: Event) => {
    const detail = (event as CustomEvent<{ state?: string; payload?: string }>).detail ?? {};
    if (detail.state === 'verified' && detail.payload) {
      current = detail.payload;
      options.onToken(detail.payload);
    } else if (detail.state === 'error') {
      current = '';
      options.onError?.(new CaptchaWidgetError('Altcha could not verify the challenge.'));
    } else if (detail.state === 'unverified' || detail.state === 'expired') {
      if (current) {
        current = '';
        options.onExpired?.();
      }
    }
  };
  el.addEventListener('statechange', onStateChange);
  container.replaceChildren(el);

  return {
    reset: () => {
      current = '';
      el.reset?.();
    },
    execute: () => (current ? Promise.resolve(current) : Promise.reject(new CaptchaWidgetError('Captcha not solved yet.'))),
    destroy: () => {
      current = '';
      el.removeEventListener('statechange', onStateChange);
      el.remove();
    },
  };
}
