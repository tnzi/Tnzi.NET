/**
 * `TChatApp` below its narrow-screen breakpoint (a phone):
 *
 * - the sidebar drawer is always the full sidebar, never the icon rail, even
 *   when the wide-screen mode stored for this browser is `icon`, and opening or
 *   closing the drawer never rewrites that stored mode;
 * - the landing page and both composers use their compact layout (smaller
 *   headline, tighter padding); the landing composer keeps two lines, the one
 *   in a conversation is one line, and the `disclaimer` line is left out;
 * - the shell carries the touch-size tokens and is pinned to the viewport
 *   (unless `pinNarrowViewport` is off);
 * - wide screens are unchanged.
 */
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { createApp, h, nextTick, type App } from 'vue';
import { useSidebarState } from '../../src/headless/useSidebarState';

const { default: TChatApp } = await import('../../src/components/chat/TChatApp.vue');
const { default: TCollapsibleSidebar } = await import('../../src/components/layout/TCollapsibleSidebar.vue');

const KEY = 'test-sidebar-mode';
let app: App | null = null;
let host: HTMLElement | null = null;

function setWidth(px: number): void {
  Object.defineProperty(window, 'innerWidth', { configurable: true, value: px });
}

async function settle(): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

function mountShell(props: Record<string, unknown> = {}): HTMLElement {
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp({
    render: () =>
      h(TChatApp, {
        sidebarStorageKey: KEY,
        autoApplyTheme: false,
        threads: [{ id: 'a', title: 'Alpha' }],
        disclaimer: 'Can make mistakes.',
        ...props,
      }),
  });
  app.mount(host);
  return host;
}

const sidebar = (el: HTMLElement) => el.querySelector('.t-collapsible-sidebar') as HTMLElement;

beforeEach(() => localStorage.clear());
afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
  setWidth(1280);
});

describe('narrow-screen drawer', () => {
  it('stored wide-screen mode `icon`: the drawer opens as the full sidebar, and closing it keeps `icon` stored', async () => {
    localStorage.setItem(KEY, 'icon');
    setWidth(390);
    const el = mountShell();
    await settle();
    expect(sidebar(el).className).toContain('t-collapsible-sidebar--hidden');

    (el.querySelector('.t-chat-app__topbar-toggle') as HTMLButtonElement).click();
    await settle();
    expect(sidebar(el).className).toContain('t-collapsible-sidebar--drawer');
    expect(sidebar(el).className).toContain('t-collapsible-sidebar--expanded');
    expect(el.querySelector('.t-chat-app__brand')).not.toBeNull();
    expect(el.querySelector('.t-thread-list__row')).not.toBeNull();
    expect(el.querySelector('.t-chat-rail')).toBeNull();

    /* The header's collapse button closes the drawer instead of turning it into the rail. */
    const collapse = el.querySelector('.t-chat-app__brand-collapse') as HTMLButtonElement;
    expect(collapse.getAttribute('aria-label')).toBe('Close sidebar');
    collapse.click();
    await settle();
    expect(sidebar(el).className).toContain('t-collapsible-sidebar--hidden');
    expect(localStorage.getItem(KEY)).toBe('icon');
  });

  it('the stored mode comes back on a wide screen', async () => {
    localStorage.setItem(KEY, 'icon');
    setWidth(1280);
    const el = mountShell();
    await settle();
    expect(sidebar(el).className).toContain('t-collapsible-sidebar--icon');
    expect(sidebar(el).className).not.toContain('--drawer');
  });

  it('asking for the rail while narrow opens the drawer expanded and is not stored', () => {
    localStorage.setItem(KEY, 'icon');
    setWidth(390);
    const state = useSidebarState({ storageKey: KEY });
    state.setMode('icon');
    expect(state.mode.value).toBe('expanded');
    state.setMode('hidden');
    expect(state.mode.value).toBe('hidden');
    state.cycle();
    expect(state.mode.value).toBe('expanded');
    expect(localStorage.getItem(KEY)).toBe('icon');
    state.dispose();
  });

  it('TCollapsibleSidebar never draws the rail as a drawer, whatever mode it is handed', async () => {
    host = document.createElement('div');
    document.body.appendChild(host);
    app = createApp({
      render: () =>
        h(TCollapsibleSidebar, { modelValue: 'icon', isMobile: true }, {
          header: () => h('p', { class: 'hdr' }, 'header'),
          rail: () => h('p', { class: 'rail' }, 'rail'),
          content: () => h('p', { class: 'content' }, 'content'),
        }),
    });
    app.mount(host);
    await settle();
    expect(host.querySelector('.rail')).toBeNull();
    expect(host.querySelector('.hdr')).not.toBeNull();
    expect(host.querySelector('.content')).not.toBeNull();
    expect(sidebar(host).className).toContain('--expanded');
  });
});

describe('narrow-screen layout', () => {
  it('pinNarrowViewport false leaves the shell in its container', async () => {
    setWidth(390);
    const el = mountShell({ pinNarrowViewport: false });
    await settle();
    expect(el.querySelector('.t-chat-app')!.className).toContain('t-chat-app--narrow');
    expect(el.querySelector('.t-chat-app')!.className).not.toContain('t-chat-app--pinned');
  });

  it('landing: compact headline and a two-line composer; one-line composer and no disclaimer in a thread', async () => {
    setWidth(390);
    const landing = mountShell();
    await settle();
    expect(landing.querySelector('.t-landing')!.className).toContain('t-landing--compact');
    expect(landing.querySelector('.t-chat-app')!.className).toContain('t-chat-app--narrow');
    expect(landing.querySelector('.t-chat-app')!.className).toContain('t-chat-app--pinned');
    expect((landing.querySelector('.t-landing textarea') as HTMLTextAreaElement).getAttribute('rows')).toBe('2');
    app!.unmount();
    host!.remove();

    const thread = mountShell({ messages: [{ id: 'm', role: 'user', content: 'hi' }] });
    await settle();
    expect(thread.querySelector('.t-thread-composer-wrap')!.className).toContain('is-compact');
    expect((thread.querySelector('.t-thread-composer textarea') as HTMLTextAreaElement).getAttribute('rows')).toBe('1');
    expect(thread.textContent).not.toContain('Can make mistakes.');
  });

  it('wide screens are unchanged: two-line landing composer, full headline, disclaimer shown', async () => {
    setWidth(1280);
    const landing = mountShell();
    await settle();
    expect(landing.querySelector('.t-landing')!.className).not.toContain('--compact');
    expect((landing.querySelector('.t-landing textarea') as HTMLTextAreaElement).getAttribute('rows')).toBe('2');
    app!.unmount();
    host!.remove();

    const thread = mountShell({ messages: [{ id: 'm', role: 'user', content: 'hi' }] });
    await settle();
    expect(thread.querySelector('.t-thread-composer-wrap')!.className).not.toContain('is-compact');
    expect(thread.querySelector('.t-chat-app')!.className).not.toContain('t-chat-app--pinned');
    expect(thread.textContent).toContain('Can make mistakes.');
  });
});
