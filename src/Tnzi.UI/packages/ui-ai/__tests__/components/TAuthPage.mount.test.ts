/**
 * `TAuthPage` mounted for real.
 *
 * The page wires `useCaptchaWidget` to state that `useAuthPage` returns. The
 * widget evaluates its `config` getter synchronously (an immediate watcher), so
 * the order of those two calls in `<script setup>` decides whether the page
 * renders at all: the other way round, the getter reads `captcha` inside its
 * temporal dead zone and setup throws. Only a mount sees that - the headless
 * tests never run the SFC's setup.
 *
 * `useCaptchaWidget` is replaced by a stand-in that keeps the one property that
 * matters here (the config getter is read immediately and tracked afterwards)
 * without loading a provider script into the test DOM.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, h, nextTick, ref, watch, type App } from 'vue';
import { DEFAULT_LOGIN_FEATURES, type LoginCallbacks, type LoginFeatures } from '@tnzi/ui';

const widgetConfigs: unknown[] = [];

vi.mock('@tnzi/core/services/captcha', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@tnzi/core/services/captcha')>();
  return {
    ...actual,
    useCaptchaWidget: (options: { config: () => unknown }) => {
      // Same contract as the real composable: evaluated now, re-evaluated on change.
      watch(() => options.config(), (value) => widgetConfigs.push(value ?? null), { immediate: true });
      return {
        container: ref(null),
        token: ref(''),
        ready: ref(false),
        error: ref(''),
        isScriptProvider: ref(false),
        isInvisible: ref(false),
        execute: vi.fn(async () => 'widget-token'),
        reset: vi.fn(),
      };
    },
  };
});

const { default: TAuthPage } = await import('../../src/auth/TAuthPage.vue');

const TURNSTILE = { enabled: true, provider: 'turnstile', siteKey: 'site-key' };

let app: App | null = null;
let host: HTMLElement | null = null;

function mountPage(props: { callbacks?: LoginCallbacks; features?: LoginFeatures } = {}) {
  host = document.createElement('div');
  document.body.appendChild(host);
  const errors: unknown[] = [];
  app = createApp({ render: () => h(TAuthPage, props) });
  app.config.errorHandler = (err) => {
    errors.push(err);
  };
  app.mount(host);
  return { host, errors };
}

function button(root: HTMLElement, text: string): HTMLButtonElement {
  const found = [...root.querySelectorAll('button')].find((b) => b.textContent?.trim() === text);
  if (!found) throw new Error(`No "${text}" button`);
  return found;
}

async function settle(): Promise<void> {
  for (let i = 0; i < 5; i += 1) {
    await nextTick();
    await Promise.resolve();
  }
}

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  widgetConfigs.length = 0;
});

describe('TAuthPage (mounted)', () => {
  it('renders the identify step without a setup error', async () => {
    const { host, errors } = mountPage();
    await settle();

    expect(errors).toEqual([]);
    expect(host.querySelector('.t-auth__heading')?.textContent).toBe('Sign in or sign up');
    expect(host.querySelector('.t-auth__pane input')).not.toBeNull();
    // No challenge revealed yet: the widget has nothing to render.
    expect(widgetConfigs).toEqual([null]);
  });

  it('hands the provider config to the widget once the backend reveals a script captcha', async () => {
    const pwdLogin = vi.fn<NonNullable<LoginCallbacks['pwdLogin']>>(async (_payload, helpers) => {
      helpers.setCaptchaRequired({ provider: 'turnstile' });
    });
    const features = { ...DEFAULT_LOGIN_FEATURES, captcha: TURNSTILE } as unknown as LoginFeatures;
    const { host, errors } = mountPage({ callbacks: { pwdLogin }, features });
    await settle();

    const identifier = host.querySelector<HTMLInputElement>('.t-auth__pane input')!;
    identifier.value = 'me@example.com';
    identifier.dispatchEvent(new Event('input'));
    await settle();
    button(host, 'Continue').click();
    await settle();

    const password = host.querySelector<HTMLInputElement>('input[type="password"]')!;
    password.value = 'pw';
    password.dispatchEvent(new Event('input'));
    await settle();
    button(host, 'Sign in').click();
    await settle();

    expect(errors).toEqual([]);
    expect(pwdLogin).toHaveBeenCalledTimes(1);
    expect(host.querySelector('.t-auth__captcha--widget')).not.toBeNull();
    // The widget saw the challenge appear - its config getter is tracked, not stuck.
    expect(widgetConfigs.at(-1)).toEqual(TURNSTILE);
  });
});
