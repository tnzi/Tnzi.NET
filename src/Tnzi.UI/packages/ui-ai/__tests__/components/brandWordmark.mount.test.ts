/**
 * The brand name drawn as a logotype (`brandWordmark`), mounted for real.
 *
 * A product with a drawn wordmark shows it where the brand name text was: the
 * sidebar header of `TChatApp`, the guest welcome bar and the sign-in page.
 * The real `@iconify/vue` renders here (no stand-in), because what is being
 * pinned is what Iconify does with the name: `1em` of the name's font size
 * high, as wide as the icon's own aspect ratio, and drawn as an inline SVG so
 * `currentColor` follows the theme while a fixed fill keeps its colour. The
 * wrapper around it keeps the text's line height (`1lh`), which only a browser
 * can show: happy-dom computes no layout. The name stays in the
 * DOM as visually hidden text: it is still what a screen reader announces.
 * Without a wordmark each header renders exactly the markup it always did.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, defineComponent, h, nextTick, ref, watch, type App, type Component } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';
import { addIcon } from '@iconify/vue';

vi.mock('@tnzi/core/services/captcha', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@tnzi/core/services/captcha')>();
  return {
    ...actual,
    useCaptchaWidget: (options: { config: () => unknown }) => {
      watch(() => options.config(), () => undefined, { immediate: true });
      return {
        container: ref(null),
        token: ref(''),
        ready: ref(false),
        error: ref(''),
        isScriptProvider: ref(false),
        isInvisible: ref(false),
        execute: vi.fn(async () => ''),
        reset: vi.fn(),
      };
    },
  };
});

const { default: TChatApp } = await import('../../src/components/chat/TChatApp.vue');
const { default: TGuestWelcome } = await import('../../src/auth/TGuestWelcome.vue');
const { default: TAuthPage } = await import('../../src/auth/TAuthPage.vue');
const { default: TAuthRoute } = await import('../../src/auth/TAuthRoute.vue');

// A 4.6:1 logotype: letters in currentColor, a dot in a fixed yellow.
const WORDMARK = 'brand:wordmark';
const WORDMARK_BOX = '0 0 46 10';
addIcon(WORDMARK, {
  width: 46,
  height: 10,
  body: '<path fill="currentColor" d="M0 2h38v8H0z"/><circle cx="43" cy="2" r="2" fill="#FFC800"/>',
});

let app: App | null = null;
let host: HTMLElement | null = null;

async function settle(): Promise<void> {
  for (let i = 0; i < 5; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

async function mount(component: Component, props: Record<string, unknown>, slots?: Record<string, () => unknown>): Promise<HTMLElement> {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'home', component: { render: () => h('div') } },
      { path: '/login', name: 'login', component: { render: () => h('div') } },
    ],
  });
  await router.push('/login');
  await router.isReady();
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp(defineComponent({ setup: () => () => h(component, props, slots) }));
  app.use(router);
  app.mount(host);
  await settle();
  return host;
}

/** Text a sighted reader sees: everything but the visually hidden spans. */
function visibleText(el: Element): string {
  const clone = el.cloneNode(true) as Element;
  clone.querySelectorAll('.sr-only').forEach((node) => node.remove());
  return clone.textContent?.trim() ?? '';
}

function expectDrawnWordmark(container: Element | null): SVGElement {
  const svg = container?.querySelector(`svg[viewBox="${WORDMARK_BOX}"]`) as SVGElement | null;
  expect(svg).not.toBeNull();
  // One em of the name's font size high, as wide as its aspect ratio makes it.
  // (The wrapper keeps the text's line height around it; that is layout, which
  // happy-dom does not compute.)
  expect(svg!.getAttribute('height')).toBe('1em');
  expect(svg!.getAttribute('width')).toBe('4.6em');
  // An inline SVG, not a CSS mask: currentColor follows the text colour and
  // the fixed dot keeps its own.
  expect(svg!.innerHTML).toContain('fill="currentColor"');
  expect(svg!.innerHTML).toContain('fill="#FFC800"');
  // The drawing is not what a screen reader reads; the hidden name is.
  expect(svg!.getAttribute('aria-hidden')).toBe('true');
  return svg!;
}

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
});

describe('TChatApp brand wordmark', () => {
  const shell = (extra: Record<string, unknown> = {}) =>
    mount(TChatApp, { sidebarStorageKey: null, autoApplyTheme: false, brandName: 'TuanZi', ...extra });

  it('draws the wordmark in place of the name, which stays the accessible name', async () => {
    const el = await shell({ brandWordmark: WORDMARK });
    const name = el.querySelector('.t-chat-app__brand-name');

    expectDrawnWordmark(name);
    expect(name?.querySelector('.sr-only')?.textContent).toBe('TuanZi');
    expect(name?.textContent?.trim()).toBe('TuanZi');
    expect(visibleText(name!)).toBe('');
  });

  it('keeps the brand mark beside it, the collapse button working and the name elsewhere', async () => {
    const el = await shell({ brandWordmark: WORDMARK });
    const brand = el.querySelector('.t-chat-app__brand')!;

    expect(brand.querySelector('.t-chat-app__brand-mark svg')).not.toBeNull();
    expect(el.querySelector('.t-chat-app__workspace-switcher')?.textContent?.trim()).toBe('TuanZi');

    (brand.querySelector('.t-chat-app__brand-collapse') as HTMLButtonElement).click();
    await settle();
    expect(el.querySelector('.t-collapsible-sidebar--icon')).not.toBeNull();
    expect(el.querySelector('.t-chat-app__brand')).toBeNull();
  });

  it('renders the name as text, exactly as before, without a wordmark', async () => {
    const el = await shell();
    const name = el.querySelector('.t-chat-app__brand-name');

    expect(name?.innerHTML).toBe('TuanZi');
    expect(el.querySelector(`svg[viewBox="${WORDMARK_BOX}"]`)).toBeNull();
  });

  it('a `brand` slot still replaces the whole header', async () => {
    const el = await mount(
      TChatApp,
      { sidebarStorageKey: null, autoApplyTheme: false, brandName: 'TuanZi', brandWordmark: WORDMARK },
      { brand: () => h('div', { class: 'own-brand' }, 'Own') },
    );

    expect(el.querySelector('.own-brand')).not.toBeNull();
    expect(el.querySelector('.t-chat-app__brand')).toBeNull();
    expect(el.querySelector(`svg[viewBox="${WORDMARK_BOX}"]`)).toBeNull();
  });
});

describe('TGuestWelcome brand wordmark', () => {
  const welcome = (extra: Record<string, unknown> = {}) =>
    mount(TGuestWelcome, { runtime: { auth: { isLoggedIn: false } }, brandName: 'Acme', ...extra });

  it('draws the wordmark next to the brand icon, the name hidden for screen readers', async () => {
    const el = await welcome({ brandIcon: 'brand:mark', brandWordmark: WORDMARK });
    const brand = el.querySelector('.t-guest-welcome__brand')!;

    expect(brand.querySelector('.t-guest-welcome__brand-icon')).not.toBeNull();
    expectDrawnWordmark(brand);
    expect(brand.querySelector('.sr-only')?.textContent).toBe('Acme');
    expect(visibleText(brand)).toBe('');
  });

  it('renders the name as text, exactly as before, without a wordmark', async () => {
    const el = await welcome();
    const brand = el.querySelector('.t-guest-welcome__brand')!;

    const name = brand.querySelector('span');
    expect(name?.className).toBe('');
    expect(name?.innerHTML).toBe('Acme');
    expect(brand.querySelector('svg')).toBeNull();
  });
});

describe('TAuthPage brand wordmark', () => {
  it('draws the wordmark in place of the name, which stays the accessible name', async () => {
    const el = await mount(TAuthPage, { brandName: 'Acme', brandIcon: 'brand:mark', brandWordmark: WORDMARK });
    const brand = el.querySelector('.t-auth__brand')!;

    expect(brand.querySelector('.t-auth__brand-mark')).not.toBeNull();
    const name = brand.querySelector('.t-auth__brand-name');
    expectDrawnWordmark(name);
    expect(name?.querySelector('.sr-only')?.textContent).toBe('Acme');
    expect(visibleText(name!)).toBe('');
  });

  it('shows the brand line for a wordmark alone', async () => {
    const el = await mount(TAuthPage, { brandWordmark: WORDMARK });
    expectDrawnWordmark(el.querySelector('.t-auth__brand'));
  });

  it('renders the name as text, exactly as before, without a wordmark', async () => {
    const el = await mount(TAuthPage, { brandName: 'Acme' });

    expect(el.querySelector('.t-auth__brand-name')?.innerHTML).toBe('Acme');
    expect(el.querySelector(`svg[viewBox="${WORDMARK_BOX}"]`)).toBeNull();
  });

  it('a `brand` slot still replaces the default line', async () => {
    const el = await mount(
      TAuthPage,
      { brandName: 'Acme', brandWordmark: WORDMARK },
      { brand: () => h('span', { class: 'own-brand' }, 'Own') },
    );

    expect(el.querySelector('.t-auth__brand .own-brand')).not.toBeNull();
    expect(el.querySelector(`svg[viewBox="${WORDMARK_BOX}"]`)).toBeNull();
  });
});

describe('TAuthRoute brand wordmark', () => {
  it('forwards it to the sign-in page', async () => {
    const runtime = {
      auth: { sessionEndReason: null },
      authApi: { getConfig: vi.fn(async () => null) },
      http: { resolveUrl: (url: string) => url },
    };
    const el = await mount(TAuthRoute, { runtime, brandName: 'Acme', brandWordmark: WORDMARK });

    const name = el.querySelector('.t-auth__brand-name');
    expectDrawnWordmark(name);
    expect(name?.querySelector('.sr-only')?.textContent).toBe('Acme');
  });
});
