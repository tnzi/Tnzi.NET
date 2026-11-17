/**
 * `TChatApp` folds its sidebar into the rail by itself when the conversation
 * has too little room (`sidebarAutoCollapse`, `conversationMinWidth`):
 *
 * - narrow, then wide again: folds, then unfolds;
 * - a rail the user chose stays when the window widens, and across a reload;
 * - nothing auto-fit does is written to `sidebarStorageKey`;
 * - opening the side panel at a width that was fine folds it, closing it unfolds;
 * - a user expanding it while it is short keeps it expanded;
 * - turned off, it never folds; the phone drawer is as before.
 *
 * happy-dom does no layout, so the shell's width falls back to the window's;
 * the rules themselves are pinned in `headless/useSidebarAutoFit.test.ts`.
 */
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { createApp, h, nextTick, type App } from 'vue';

const { default: TChatApp } = await import('../../src/components/chat/TChatApp.vue');

const KEY = 'test-auto-collapse';
let app: App | null = null;
let host: HTMLElement | null = null;

function setWidth(px: number): void {
  Object.defineProperty(window, 'innerWidth', { configurable: true, value: px });
}

async function resizeTo(px: number): Promise<void> {
  setWidth(px);
  window.dispatchEvent(new Event('resize'));
  await settle();
}

async function settle(): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

const messages = [{ id: 'm1', role: 'user', content: 'hi' }];

function mountShell(props: Record<string, unknown> = {}, slots: Record<string, unknown> = {}): HTMLElement {
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp({
    render: () =>
      h(
        TChatApp,
        {
          sidebarStorageKey: KEY,
          asideStorageKey: `${KEY}:aside`,
          autoApplyTheme: false,
          threads: [{ id: 't1', title: 'Alpha' }],
          ...props,
        },
        slots,
      ),
  });
  app.mount(host);
  return host;
}

function unmount(): void {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
}

const sidebarClass = (el: HTMLElement) => (el.querySelector('.t-collapsible-sidebar') as HTMLElement).className;
const isRail = (el: HTMLElement) => sidebarClass(el).includes('t-collapsible-sidebar--icon');
const isExpanded = (el: HTMLElement) => sidebarClass(el).includes('t-collapsible-sidebar--expanded');

beforeEach(() => {
  localStorage.clear();
  setWidth(1280);
});
afterEach(() => {
  unmount();
  document.body.innerHTML = '';
  setWidth(1280);
});

describe('sidebar auto-collapse', () => {
  it('narrow folds it into the rail, wide again restores it, and the preference stays "expanded"', async () => {
    localStorage.setItem(KEY, 'expanded');
    const el = mountShell();
    await settle();
    expect(isExpanded(el)).toBe(true);

    // 800 - 300 = 500px for the conversation: under the 560 default.
    await resizeTo(800);
    expect(isRail(el)).toBe(true);
    expect(localStorage.getItem(KEY)).toBe('expanded');

    await resizeTo(1280);
    expect(isExpanded(el)).toBe(true);
    expect(localStorage.getItem(KEY)).toBe('expanded');
  });

  it('a narrow window does not become the remembered mode: a wide reload opens expanded', async () => {
    setWidth(800);
    const el = mountShell();
    await settle();
    expect(isRail(el)).toBe(true);
    unmount();

    setWidth(1280);
    const again = mountShell();
    await settle();
    expect(isExpanded(again)).toBe(true);
    expect(localStorage.getItem(KEY)).not.toBe('icon');
  });

  it('a rail the user chose stays a rail when the window widens, and after a reload', async () => {
    const el = mountShell();
    await settle();
    (el.querySelector('.t-chat-app__brand-toggle') as HTMLButtonElement).click();
    await settle();
    expect(isRail(el)).toBe(true);
    expect(localStorage.getItem(KEY)).toBe('icon');

    await resizeTo(800);
    await resizeTo(2400);
    expect(isRail(el)).toBe(true);

    unmount();
    const again = mountShell();
    await settle();
    expect(isRail(again)).toBe(true);
  });

  it('opening the side panel at a width that was fine folds it; closing the panel unfolds it', async () => {
    // 1100 - 300 = 800 without the panel; with a 420px panel, 380.
    setWidth(1100);
    const el = mountShell({ activeThreadId: 't1', messages }, { aside: () => h('div', { class: 'probe' }) });
    await settle();
    expect(isExpanded(el)).toBe(true);

    (el.querySelector('.t-chat-app__aside-toggle') as HTMLButtonElement).click();
    await settle();
    expect(el.querySelector('.t-aside-panel')).not.toBeNull();
    expect(isRail(el)).toBe(true);
    expect(localStorage.getItem(KEY)).not.toBe('icon');

    (el.querySelector('.t-chat-app__aside-toggle') as HTMLButtonElement).click();
    await settle();
    expect(isExpanded(el)).toBe(true);
  });

  it('a user who expands it while the room is short is not folded again under them', async () => {
    setWidth(800);
    const el = mountShell();
    await settle();
    expect(isRail(el)).toBe(true);
    (el.querySelector('.t-chat-rail__brand') as HTMLButtonElement).click();
    await settle();
    expect(isExpanded(el)).toBe(true);
    await resizeTo(790);
    expect(isExpanded(el)).toBe(true);
  });

  it('takes its own minimum: conversationMinWidth', async () => {
    const el = mountShell({ conversationMinWidth: 1000 });
    await settle();
    // 1280 - 300 = 980 < 1000.
    expect(isRail(el)).toBe(true);
  });

  it('turned off, it never folds', async () => {
    const el = mountShell({ sidebarAutoCollapse: false });
    await settle();
    await resizeTo(800);
    expect(isExpanded(el)).toBe(true);
  });

  it('the phone drawer behaves as before', async () => {
    localStorage.setItem(KEY, 'expanded');
    setWidth(390);
    const el = mountShell();
    await settle();
    expect(sidebarClass(el)).toContain('t-collapsible-sidebar--hidden');
    (el.querySelector('.t-chat-app__topbar-toggle') as HTMLButtonElement).click();
    await settle();
    expect(sidebarClass(el)).toContain('t-collapsible-sidebar--drawer');
    expect(isExpanded(el)).toBe(true);
    expect(el.querySelector('.t-chat-rail')).toBeNull();
    expect(localStorage.getItem(KEY)).toBe('expanded');
  });
});
