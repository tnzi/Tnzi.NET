/**
 * `sendIcon`: an application's own icon on the composer's send button. It
 * reaches every composer TChatApp renders (home screen and conversation) and
 * the guest welcome screen, and changes only the send state: stop, voice and
 * end-dictation keep their icons.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, defineComponent, h, nextTick, ref, type App } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';

vi.mock('@iconify/vue', async (importOriginal) => {
  const original = await importOriginal<typeof import('@iconify/vue')>();
  const { defineComponent: define, h: render } = await import('vue');
  return {
    ...original,
    Icon: define({
      props: { icon: { type: String, default: '' } },
      setup: (props) => () => render('i', { 'data-icon': props.icon }),
    }),
  };
});

const { default: TThreadComposer } = await import('../../src/components/chat/TThreadComposer.vue');
const { default: TChatApp } = await import('../../src/components/chat/TChatApp.vue');
const { default: TGuestWelcome } = await import('../../src/auth/TGuestWelcome.vue');
const { defineChatApp } = await import('../../src/plugin/defineChatApp');
const { DEFAULT_SEND_ICON } = await import('../../src/components/chat/composer-types');

const PAW = 'mdi:paw';

let app: App | null = null;
let host: HTMLElement | null = null;

function mount(props: () => Record<string, unknown>, component: unknown = TThreadComposer): HTMLElement {
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp(defineComponent({ setup: () => () => h(component as never, props()) }));
  app.mount(host);
  return host;
}

async function settle(): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

class FakeRecognition {
  continuous = false;
  interimResults = false;
  lang = '';
  onstart: (() => void) | null = null;
  onresult = null;
  onerror = null;
  onend: (() => void) | null = null;
  start(): void {
    this.onstart?.();
  }
  stop(): void {}
}

const primary = (el: ParentNode) => el.querySelector('.t-thread-composer__send') as HTMLButtonElement;
const iconOf = (button: Element) => button.querySelector('[data-icon]')?.getAttribute('data-icon');

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
  vi.unstubAllGlobals();
});

describe('TThreadComposer sendIcon', () => {
  it('defaults to the arrow', async () => {
    const el = mount(() => ({ modelValue: 'hello', enableVoice: false }));
    await settle();
    expect(primary(el).dataset.action).toBe('send');
    expect(iconOf(primary(el))).toBe(DEFAULT_SEND_ICON);
    expect(DEFAULT_SEND_ICON).toBe('lucide:arrow-up');
  });

  it('shows the given icon in the send state only', async () => {
    vi.stubGlobal('SpeechRecognition', FakeRecognition);
    const text = ref('');
    const running = ref(false);
    const el = mount(() => ({ modelValue: text.value, running: running.value, sendIcon: PAW }));
    await settle();

    expect(primary(el).dataset.action).toBe('voice');
    expect(iconOf(primary(el))).toBe('lucide:mic');

    primary(el).click();
    await settle();
    expect(primary(el).dataset.action).toBe('end-dictation');
    expect(iconOf(primary(el))).toBe('lucide:square');
    primary(el).click();
    await settle();

    text.value = 'hello';
    await settle();
    expect(primary(el).dataset.action).toBe('send');
    expect(iconOf(primary(el))).toBe(PAW);

    running.value = true;
    await settle();
    expect(primary(el).dataset.action).toBe('stop');
    expect(iconOf(primary(el))).toBe('lucide:square');
  });

  it('falls back to the arrow for an empty name', async () => {
    const el = mount(() => ({ modelValue: 'hello', enableVoice: false, sendIcon: '' }));
    await settle();
    expect(iconOf(primary(el))).toBe(DEFAULT_SEND_ICON);
  });
});

describe('TChatApp sendIcon', () => {
  const base = { sidebarStorageKey: null, autoApplyTheme: false, enableVoice: false, inputText: 'hello' };

  it('reaches the home screen composer', async () => {
    const el = mount(() => ({ ...base, sendIcon: PAW }), TChatApp);
    await settle();
    const landing = el.querySelector('.t-chat-app__landing');
    expect(landing).not.toBeNull();
    expect(iconOf(primary(landing!))).toBe(PAW);
  });

  it("reaches a conversation's composer", async () => {
    const el = mount(
      () => ({
        ...base,
        sendIcon: PAW,
        messages: [{ id: 'u1', role: 'user', content: 'Plan my trip', createdAt: '' }],
      }),
      TChatApp,
    );
    await settle();
    expect(el.querySelector('.t-chat-app__landing')).toBeNull();
    expect(iconOf(primary(el))).toBe(PAW);
  });

  it('keeps the arrow when not given', async () => {
    const el = mount(() => base, TChatApp);
    await settle();
    expect(iconOf(primary(el))).toBe(DEFAULT_SEND_ICON);
  });
});

describe('guest welcome sendIcon', () => {
  async function mountWelcome(props: Record<string, unknown>): Promise<HTMLElement> {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/:any(.*)*', component: { render: () => h('div') } }],
    });
    await router.push('/welcome');
    await router.isReady();
    host = document.createElement('div');
    document.body.appendChild(host);
    app = createApp({ render: () => h(TGuestWelcome, { runtime: { auth: { isLoggedIn: false } } as never, ...props }) });
    app.use(router);
    app.mount(host);
    await nextTick();
    return host;
  }

  const send = (el: HTMLElement) => el.querySelector('.t-guest-welcome__send') as HTMLButtonElement;

  it('TGuestWelcome shows the given icon, the arrow by default', async () => {
    expect(iconOf(send(await mountWelcome({ sendIcon: PAW })))).toBe(PAW);
    app?.unmount();
    host?.remove();
    expect(iconOf(send(await mountWelcome({})))).toBe(DEFAULT_SEND_ICON);
  });

  it('defineChatApp forwards guestWelcome.sendIcon to the welcome route, and nothing when unset', () => {
    const runtime = { auth: { isLoggedIn: false } } as never;
    const welcomeProps = (sendIcon?: string) => {
      const { routes } = defineChatApp({
        runtime,
        home: { render: () => h('div') },
        guestWelcome: sendIcon ? { sendIcon } : true,
        theme: false,
      });
      return routes.find((r) => r.name === 'welcome')!.props as Record<string, unknown>;
    };
    expect(welcomeProps(PAW).sendIcon).toBe(PAW);
    expect('sendIcon' in welcomeProps()).toBe(false);
  });
});
