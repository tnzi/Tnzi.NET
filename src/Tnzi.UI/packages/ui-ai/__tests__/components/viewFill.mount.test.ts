/**
 * `useChatViewFill`: a page of the `view` slot that fills the view area and scrolls itself takes
 * away the area's scrolling and scrollbar room (`.t-chat-app__view--fill`) while it is on screen,
 * through `<KeepAlive>` deactivation and a plain unmount alike; every other page leaves the area
 * as it was. Layout itself is checked in a browser (`__tests__/browser/viewFill.browser.test.ts`).
 */
import { afterEach, describe, expect, it } from 'vitest';
import { KeepAlive, createApp, defineComponent, h, nextTick, ref, type App, type Ref } from 'vue';
import { useChatViewFill } from '../../src/headless/useChatViewFill';

const { default: TChatApp } = await import('../../src/components/chat/TChatApp.vue');

let app: App | null = null;

async function settle(): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

afterEach(() => {
  app?.unmount();
  app = null;
  document.body.innerHTML = '';
});

const filling = ref(true);
const FramePage = defineComponent({
  name: 'FramePage',
  setup() {
    useChatViewFill(filling);
    return () => h('div', { class: 'frame-page' }, 'Frame');
  },
});
const ListPage = defineComponent({ name: 'ListPage', setup: () => () => h('div', { class: 'list-page' }, 'List') });

function mountShell(initialView: string, keepAlive: boolean): Ref<string> {
  const view = ref(initialView);
  const host = document.createElement('div');
  document.body.appendChild(host);
  const page = (current: string) => (current === 'frame' ? h(FramePage) : current === 'list' ? h(ListPage) : null);
  app = createApp(
    defineComponent({
      setup: () => () =>
        h(
          TChatApp,
          { sidebarStorageKey: null, autoApplyTheme: false, view: view.value, 'onUpdate:view': (next: string) => (view.value = next) },
          { view: ({ view: current }: { view: string }) => (keepAlive ? h(KeepAlive, null, [page(current)]) : page(current)) },
        ),
    }),
  );
  app.mount(host);
  return view;
}

const filled = () => document.querySelector('section.t-chat-app__view')!.classList.contains('t-chat-app__view--fill');

describe('a page that fills the view area', () => {
  for (const keepAlive of [true, false]) {
    it(`fills it only while on screen${keepAlive ? ' (KeepAlive)' : ''}`, async () => {
      filling.value = true;
      const view = mountShell('list', keepAlive);
      await settle();
      expect(filled()).toBe(false);

      view.value = 'frame';
      await settle();
      expect(document.querySelector('.frame-page')).not.toBeNull();
      expect(filled()).toBe(true);

      view.value = 'list';
      await settle();
      expect(filled()).toBe(false);

      // Away to the conversation and back: the page section is kept, the claim follows the page.
      view.value = 'frame';
      await settle();
      view.value = 'chat';
      await settle();
      view.value = 'frame';
      await settle();
      expect(filled()).toBe(true);
    });
  }

  it('follows a reactive flag', async () => {
    filling.value = false;
    mountShell('frame', true);
    await settle();
    expect(filled()).toBe(false);
    filling.value = true;
    await settle();
    expect(filled()).toBe(true);
    filling.value = false;
    await settle();
    expect(filled()).toBe(false);
    filling.value = true;
  });

  it('does nothing outside a TChatApp', async () => {
    const host = document.createElement('div');
    document.body.appendChild(host);
    app = createApp(FramePage);
    app.mount(host);
    await settle();
    expect(document.querySelector('.frame-page')).not.toBeNull();
  });
});
