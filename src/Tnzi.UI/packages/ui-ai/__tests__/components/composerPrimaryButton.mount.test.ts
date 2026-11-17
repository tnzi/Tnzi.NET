/**
 * TThreadComposer's one primary button, mounted: which action it offers, its
 * accessible name in each state, and that pressing it (or Enter) does that
 * action and nothing else. The decision itself is unit-tested in
 * composer-primary-action.test.ts.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, defineComponent, h, nextTick, ref, type App } from 'vue';

const { default: TThreadComposer } = await import('../../src/components/chat/TThreadComposer.vue');
const { default: TChatApp } = await import('../../src/components/chat/TChatApp.vue');
const { en } = await import('../../src/locales/en');

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

let recogniser: FakeRecognition | null = null;
class FakeRecognition {
  continuous = false;
  interimResults = false;
  lang = '';
  onstart: (() => void) | null = null;
  onresult: ((e: { results: unknown }) => void) | null = null;
  onerror = null;
  onend: (() => void) | null = null;
  stopped = false;
  constructor() {
    recogniser = this;
  }
  start(): void {}
  stop(): void {
    this.stopped = true;
  }
  hear(text: string): void {
    this.onresult?.({ results: [Object.assign([{ transcript: text }], { isFinal: true })] });
  }
}

const primary = (el: HTMLElement) => el.querySelector('.t-thread-composer__send') as HTMLButtonElement;
const enter = () => new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true });

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  recogniser = null;
  document.body.innerHTML = '';
  vi.unstubAllGlobals();
});

describe('TThreadComposer primary button', () => {
  it('is the microphone when empty with speech input, then send once text is typed', async () => {
    vi.stubGlobal('SpeechRecognition', FakeRecognition);
    const text = ref('');
    const el = mount(() => ({ modelValue: text.value }));
    await settle();
    expect(primary(el).dataset.action).toBe('voice');
    expect(primary(el).getAttribute('aria-label')).toBe(en.composer.voiceInput);
    expect(primary(el).disabled).toBe(false);

    text.value = 'hello';
    await settle();
    expect(primary(el).dataset.action).toBe('send');
    expect(primary(el).getAttribute('aria-label')).toBe(en.composer.send);
  });

  it('shows no microphone anywhere where speech input is unsupported: a disabled send instead', async () => {
    const el = mount(() => ({ modelValue: '' }));
    await settle();
    expect(primary(el).dataset.action).toBe('send');
    expect(primary(el).disabled).toBe(true);
    expect(el.querySelector(`[aria-label="${en.composer.voiceInput}"]`)).toBeNull();
    expect(el.querySelectorAll('button.t-thread-composer__send')).toHaveLength(1);
  });

  it('shows no microphone when enableVoice is off, even where it would work', async () => {
    vi.stubGlobal('SpeechRecognition', FakeRecognition);
    const el = mount(() => ({ modelValue: '', enableVoice: false }));
    await settle();
    expect(primary(el).dataset.action).toBe('send');
    expect(primary(el).disabled).toBe(true);
  });

  it('sends an attachment with no text', async () => {
    vi.stubGlobal('SpeechRecognition', FakeRecognition);
    const send = vi.fn();
    const composer = ref<{ addAttachments: (f: File[]) => unknown } | null>(null);
    host = document.createElement('div');
    document.body.appendChild(host);
    app = createApp(defineComponent({
      setup: () => () => h(TThreadComposer, { ref: composer, modelValue: '', enableAttachments: true, onSend: send }),
    }));
    app.mount(host);
    await settle();
    const file = new File(['x'], 'notes.txt', { type: 'text/plain' });
    composer.value!.addAttachments([file]);
    await settle();
    expect(primary(host).dataset.action).toBe('send');
    primary(host).click();
    expect(send).toHaveBeenCalledWith('', [file]);
  });

  it('is stop while running, with text in the box: the button stops and Enter does not send', async () => {
    vi.stubGlobal('SpeechRecognition', FakeRecognition);
    const send = vi.fn();
    const stop = vi.fn();
    const el = mount(() => ({ modelValue: 'next question', running: true, onSend: send, onStop: stop }));
    await settle();
    expect(primary(el).dataset.action).toBe('stop');
    expect(primary(el).getAttribute('aria-label')).toBe(en.chat.stop);
    expect(primary(el).disabled).toBe(false);

    el.querySelector('textarea')!.dispatchEvent(enter());
    expect(send).not.toHaveBeenCalled();
    primary(el).click();
    expect(stop).toHaveBeenCalledTimes(1);
    expect(send).not.toHaveBeenCalled();
  });

  it('starts and ends a dictation from the same button, keeping what was heard', async () => {
    vi.stubGlobal('SpeechRecognition', FakeRecognition);
    const text = ref('');
    const el = mount(() => ({ modelValue: text.value, 'onUpdate:modelValue': (v: string) => (text.value = v) }));
    await settle();

    primary(el).click();
    await settle();
    expect(primary(el).dataset.action).toBe('end-dictation');
    expect(primary(el).classList.contains('is-recording')).toBe(true);
    expect(primary(el).getAttribute('aria-label')).toBe(en.composer.stopRecording);
    expect(el.querySelector('[role="status"]')?.textContent).toBe(en.composer.listening);
    expect(el.querySelector('textarea')!.placeholder).toBe(en.composer.listening);

    recogniser!.hear('book a table');
    await settle();
    expect(text.value).toBe('book a table');
    // Text has arrived, but the button still ends the dictation.
    expect(primary(el).dataset.action).toBe('end-dictation');

    primary(el).click();
    await settle();
    expect(recogniser!.stopped).toBe(true);
    recogniser!.hear('book a table for two'); // the last words arrive after stop
    await settle();
    expect(text.value).toBe('book a table for two');
    expect(primary(el).dataset.action).toBe('send');
  });

  it('Enter during a dictation sends, ends it, and late words do not refill the box', async () => {
    vi.stubGlobal('SpeechRecognition', FakeRecognition);
    const send = vi.fn();
    const text = ref('');
    const el = mount(() => ({
      modelValue: text.value,
      'onUpdate:modelValue': (v: string) => (text.value = v),
      onSend: (t: string) => {
        send(t);
        text.value = '';
      },
    }));
    await settle();
    primary(el).click();
    recogniser!.hear('hello');
    await settle();

    el.querySelector('textarea')!.dispatchEvent(enter());
    await settle();
    expect(send).toHaveBeenCalledWith('hello');
    expect(recogniser!.stopped).toBe(true);
    recogniser!.hear('hello there');
    await settle();
    expect(text.value).toBe('');
  });

  it('ends a dictation when a turn starts running', async () => {
    vi.stubGlobal('SpeechRecognition', FakeRecognition);
    const running = ref(false);
    const el = mount(() => ({ modelValue: '', running: running.value }));
    await settle();
    primary(el).click();
    await settle();
    running.value = true;
    await settle();
    expect(recogniser!.stopped).toBe(true);
    expect(primary(el).dataset.action).toBe('stop');
  });

  it('keeps the toolbar to one primary button in every state', async () => {
    vi.stubGlobal('SpeechRecognition', FakeRecognition);
    const state = ref<Record<string, unknown>>({ modelValue: '' });
    const el = mount(() => state.value);
    for (const next of [{ modelValue: 'x' }, { modelValue: 'x', running: true }, { modelValue: '' }]) {
      state.value = next;
      await settle();
      expect(el.querySelectorAll('.t-thread-composer__bar button')).toHaveLength(1);
    }
  });

  it('hands the decided action to a #send replacement', async () => {
    const seen: string[] = [];
    host = document.createElement('div');
    document.body.appendChild(host);
    app = createApp(defineComponent({
      setup: () => () =>
        h(TThreadComposer, { modelValue: 'hi', running: true }, {
          send: (p: { action: { kind: string }; stop: () => void }) => {
            seen.push(p.action.kind);
            return h('button', { class: 'custom', onClick: p.stop });
          },
        }),
    }));
    app.mount(host);
    await settle();
    expect(seen.at(-1)).toBe('stop');
    expect(host.querySelector('.t-thread-composer__send')).toBeNull();
  });
});

describe('TChatApp stop', () => {
  it('stops a running turn from the composer primary button, with no second stop button', async () => {
    const stop = vi.fn();
    const el = mount(
      () => ({
        sidebarStorageKey: null,
        autoApplyTheme: false,
        messages: [{ id: 'u1', role: 'user', content: 'Plan my trip', createdAt: '' }],
        isStreaming: true,
        inputText: 'draft',
        onStop: stop,
      }),
      TChatApp,
    );
    await settle();
    const buttons = [...el.querySelectorAll(`button[aria-label="${en.chat.stop}"]`)] as HTMLButtonElement[];
    expect(buttons).toHaveLength(1);
    expect(buttons[0]!.classList.contains('t-thread-composer__send')).toBe(true);
    buttons[0]!.click();
    expect(stop).toHaveBeenCalledTimes(1);
  });
});
