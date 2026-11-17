/**
 * `TChatApp` keeps the place of each page and of the conversation:
 *
 * - a page of the `view` slot comes back scrolled where it was left, and a page
 *   not seen before starts at the top, though the pages share one scroller;
 * - the slot stays mounted across a visit to the conversation, so a
 *   `<KeepAlive>` in it keeps its pages (they are not mounted again);
 * - the conversation comes back where it was left when it was not at its end.
 */
import { afterEach, describe, expect, it } from 'vitest';
import { KeepAlive, createApp, defineComponent, h, nextTick, onActivated, onMounted, ref, type App } from 'vue';

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

const counts = { mounted: 0, activated: 0 };
const Agents = defineComponent({
  name: 'AgentsPage',
  setup() {
    onMounted(() => (counts.mounted += 1));
    onActivated(() => (counts.activated += 1));
    return () => h('div', { class: 'agents-page' }, 'Agents');
  },
});
const Skills = defineComponent({ name: 'SkillsPage', setup: () => () => h('div', { class: 'skills-page' }, 'Skills') });

function mountShell(initialView: string, extra: Record<string, unknown> = {}) {
  const view = ref(initialView);
  const host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp(
    defineComponent({
      setup: () => () =>
        h(
          TChatApp,
          {
            sidebarStorageKey: null,
            autoApplyTheme: false,
            view: view.value,
            'onUpdate:view': (next: string) => (view.value = next),
            ...extra,
          },
          {
            view: ({ view: current }: { view: string }) =>
              h(KeepAlive, null, [current === 'agents' ? h(Agents) : current === 'skills' ? h(Skills) : null]),
          },
        ),
    }),
  );
  app.mount(host);
  return view;
}

const pageScroller = () => document.querySelector<HTMLElement>('section.t-chat-app__view')!;

describe('page scroll positions', () => {
  it('brings each page back where it was, and a new one at the top', async () => {
    counts.mounted = 0;
    const view = mountShell('agents');
    await settle();
    pageScroller().scrollTop = 400;

    view.value = 'chat';
    await settle();
    expect(document.querySelector('.t-chat-app__workspace')).not.toBeNull();
    view.value = 'agents';
    await settle();
    expect(pageScroller().scrollTop).toBe(400);

    view.value = 'skills';
    await settle();
    expect(pageScroller().scrollTop).toBe(0);
    pageScroller().scrollTop = 250;
    view.value = 'agents';
    await settle();
    expect(pageScroller().scrollTop).toBe(400);
    view.value = 'skills';
    await settle();
    expect(pageScroller().scrollTop).toBe(250);
  });

  it('keeps the view slot mounted, so a KeepAlive in it keeps its pages', async () => {
    counts.mounted = 0;
    counts.activated = 0;
    const view = mountShell('agents');
    await settle();
    expect(document.querySelector('.agents-page')).not.toBeNull();
    view.value = 'chat';
    await settle();
    expect(pageScroller().style.display).toBe('none');
    view.value = 'agents';
    await settle();
    expect(pageScroller().style.display).toBe('');
    expect(document.querySelector('.agents-page')).not.toBeNull();
    expect(counts.mounted).toBe(1);
    expect(counts.activated).toBe(2);
  });
});

describe('the conversation', () => {
  const messages = Array.from({ length: 6 }, (_, i) => ({
    id: `m${i}`,
    role: i % 2 === 0 ? 'user' : 'assistant',
    content: `Message ${i}`,
  }));

  function geometry(el: HTMLElement, top: number) {
    const state = { top };
    Object.defineProperty(el, 'scrollHeight', { configurable: true, get: () => 3000 });
    Object.defineProperty(el, 'clientHeight', { configurable: true, get: () => 500 });
    Object.defineProperty(el, 'scrollTop', {
      configurable: true,
      get: () => state.top,
      set: (v: number) => (state.top = Math.max(0, Math.min(v, 2500))),
    });
    el.dispatchEvent(new Event('scroll'));
  }

  it('comes back where it was left when that was not its end', async () => {
    const view = mountShell('chat', { messages, activeThreadId: 't1' });
    await settle();
    geometry(document.querySelector<HTMLElement>('.t-chat-app__thread')!, 700);
    view.value = 'agents';
    await settle();
    view.value = 'chat';
    await settle();
    expect(document.querySelector<HTMLElement>('.t-chat-app__thread')!.scrollTop).toBe(700);
  });

  it('comes back at its end, following, when it was left there', async () => {
    const view = mountShell('chat', { messages, activeThreadId: 't1' });
    await settle();
    geometry(document.querySelector<HTMLElement>('.t-chat-app__thread')!, 2500);
    view.value = 'agents';
    await settle();
    view.value = 'chat';
    await settle();
    // Opened at the latest turn as usual: the composable writes the new
    // scroller's scrollHeight, which is 0 in this DOM.
    expect(document.querySelector<HTMLElement>('.t-chat-app__thread')!.scrollTop).toBe(0);
  });
});
