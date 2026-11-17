/**
 * Queueing a message while a turn runs (`queueWhileRunning`), on TThreadComposer
 * and through TChatApp: the queue button and Enter emit `queue` with the text and
 * the files exactly as `send` would carry them, the composer empties, and Stop
 * stays reachable beside it. Off, the composer behaves as before.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, defineComponent, h, nextTick, ref, type App } from 'vue';

const { default: TThreadComposer } = await import('../../src/components/chat/TThreadComposer.vue');
const { default: TChatApp } = await import('../../src/components/chat/TChatApp.vue');
const { en } = await import('../../src/locales/en');

let app: App | null = null;
let host: HTMLElement | null = null;

function mount(render: () => ReturnType<typeof h>): HTMLElement {
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp(defineComponent({ setup: () => render }));
  app.mount(host);
  return host;
}

async function settle(): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

const primary = (el: HTMLElement) =>
  el.querySelector('.t-thread-composer__send:not(.t-thread-composer__side-stop)') as HTMLButtonElement;
const sideStop = (el: HTMLElement) => el.querySelector('.t-thread-composer__side-stop') as HTMLButtonElement | null;
const enter = () => new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true });

type ComposerHandle = { addAttachments: (f: File[]) => unknown };

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
});

describe('TThreadComposer queueWhileRunning', () => {
  it('offers queue with Stop beside it while running with text, and the button queues', async () => {
    const queue = vi.fn();
    const send = vi.fn();
    const stop = vi.fn();
    const el = mount(() =>
      h(TThreadComposer, { modelValue: ' next question ', running: true, queueWhileRunning: true, onQueue: queue, onSend: send, onStop: stop }),
    );
    await settle();
    expect(primary(el).dataset.action).toBe('queue');
    expect(primary(el).getAttribute('aria-label')).toBe(en.composer.queue);
    expect(primary(el).classList.contains('is-ready')).toBe(true);
    expect(sideStop(el)?.getAttribute('aria-label')).toBe(en.chat.stop);

    primary(el).click();
    expect(queue).toHaveBeenCalledWith('next question', []);
    sideStop(el)!.click();
    expect(stop).toHaveBeenCalledTimes(1);
    expect(send).not.toHaveBeenCalled();
  });

  it('gives the text box focus back after the queue button, so the next keystrokes land there', async () => {
    const el = mount(() => h(TThreadComposer, { modelValue: 'also do X', running: true, queueWhileRunning: true }));
    await settle();
    primary(el).focus();
    expect(document.activeElement).toBe(primary(el));
    primary(el).click();
    expect(document.activeElement).toBe(el.querySelector('textarea'));
  });

  it('queues on Enter, IME commits excepted', async () => {
    const queue = vi.fn();
    const el = mount(() => h(TThreadComposer, { modelValue: 'later', running: true, queueWhileRunning: true, onQueue: queue }));
    await settle();
    const textarea = el.querySelector('textarea')!;
    textarea.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', isComposing: true, bubbles: true, cancelable: true }));
    expect(queue).not.toHaveBeenCalled();
    textarea.dispatchEvent(enter());
    expect(queue).toHaveBeenCalledWith('later', []);
  });

  it('with `queueAttachments: false` attachments keep the button on Stop, and stay for after the turn', async () => {
    const queue = vi.fn();
    const composer = ref<ComposerHandle | null>(null);
    const el = mount(() =>
      h(TThreadComposer, {
        ref: composer, modelValue: 'look', running: true, queueWhileRunning: true, queueAttachments: false, enableAttachments: true, onQueue: queue,
      }),
    );
    await settle();
    expect(primary(el).dataset.action).toBe('queue');
    composer.value!.addAttachments([new File(['x'], 'notes.txt', { type: 'text/plain' })]);
    await settle();
    expect(primary(el).dataset.action).toBe('stop');
    el.querySelector('textarea')!.dispatchEvent(enter());
    await settle();
    expect(queue).not.toHaveBeenCalled();
    expect(el.querySelectorAll('.t-thread-composer__chip')).toHaveLength(1);
  });

  it('hands the attachments over and clears them, files only included', async () => {
    const queue = vi.fn();
    const composer = ref<ComposerHandle | null>(null);
    const el = mount(() =>
      h(TThreadComposer, { ref: composer, modelValue: '', running: true, queueWhileRunning: true, enableAttachments: true, onQueue: queue }),
    );
    await settle();
    expect(primary(el).dataset.action).toBe('stop');
    const file = new File(['x'], 'notes.txt', { type: 'text/plain' });
    composer.value!.addAttachments([file]);
    await settle();
    expect(primary(el).dataset.action).toBe('queue');

    primary(el).click();
    await settle();
    expect(queue).toHaveBeenCalledWith('', [file]);
    expect(el.querySelectorAll('.t-thread-composer__chip')).toHaveLength(0);
    expect(primary(el).dataset.action).toBe('stop');
    expect(sideStop(el)).toBeNull();
  });

  it('is plain Stop with an empty box, and Enter does nothing', async () => {
    const queue = vi.fn();
    const el = mount(() => h(TThreadComposer, { modelValue: '  ', running: true, queueWhileRunning: true, onQueue: queue }));
    await settle();
    expect(primary(el).dataset.action).toBe('stop');
    expect(sideStop(el)).toBeNull();
    el.querySelector('textarea')!.dispatchEvent(enter());
    expect(queue).not.toHaveBeenCalled();
  });

  it('does not queue when disabled', async () => {
    const queue = vi.fn();
    const el = mount(() =>
      h(TThreadComposer, { modelValue: 'x', running: true, queueWhileRunning: true, disabled: true, onQueue: queue }),
    );
    await settle();
    expect(primary(el).dataset.action).toBe('queue');
    expect(primary(el).disabled).toBe(true);
    el.querySelector('textarea')!.dispatchEvent(enter());
    expect(queue).not.toHaveBeenCalled();
    expect(sideStop(el)?.disabled).toBe(false);
  });

  it('sends normally once the turn is over', async () => {
    const queue = vi.fn();
    const send = vi.fn();
    const el = mount(() => h(TThreadComposer, { modelValue: 'x', queueWhileRunning: true, onQueue: queue, onSend: send }));
    await settle();
    expect(primary(el).dataset.action).toBe('send');
    el.querySelector('textarea')!.dispatchEvent(enter());
    expect(send).toHaveBeenCalledWith('x', []);
    expect(queue).not.toHaveBeenCalled();
  });

  it('without the opt-in keeps one Stop button and never queues', async () => {
    const queue = vi.fn();
    const el = mount(() => h(TThreadComposer, { modelValue: 'x', running: true, onQueue: queue }));
    await settle();
    expect(primary(el).dataset.action).toBe('stop');
    expect(el.querySelectorAll('.t-thread-composer__bar button')).toHaveLength(1);
    el.querySelector('textarea')!.dispatchEvent(enter());
    expect(queue).not.toHaveBeenCalled();
  });

  it('hands queue and canQueue to a #send replacement', async () => {
    const seen: Array<{ kind: string; canQueue: boolean }> = [];
    const queue = vi.fn();
    const el = mount(() =>
      h(TThreadComposer, { modelValue: 'hi', running: true, queueWhileRunning: true, onQueue: queue }, {
        send: (p: { action: { kind: string }; canQueue: boolean; queue: () => void }) => {
          seen.push({ kind: p.action.kind, canQueue: p.canQueue });
          return h('button', { class: 'custom', onClick: p.queue });
        },
      }),
    );
    await settle();
    expect(seen.at(-1)).toEqual({ kind: 'queue', canQueue: true });
    (el.querySelector('.custom') as HTMLButtonElement).click();
    expect(queue).toHaveBeenCalledWith('hi', []);
  });
});

describe('TChatApp queueWhileRunning', () => {
  const baseProps = {
    sidebarStorageKey: null,
    autoApplyTheme: false,
    messages: [{ id: 'u1', role: 'user', content: 'Plan my trip', createdAt: '' }],
    isStreaming: true,
  };

  it('emits queue with the text and files and empties the composer', async () => {
    const text = ref('and book the hotel');
    const queue = vi.fn();
    const send = vi.fn();
    const chat = ref<{ addAttachments: (f: File[]) => unknown } | null>(null);
    const el = mount(() =>
      h(TChatApp, {
        ...baseProps,
        ref: chat,
        queueWhileRunning: true,
        enableAttachments: true,
        inputText: text.value,
        'onUpdate:inputText': (v: string) => (text.value = v),
        onQueue: queue,
        onSend: send,
      }),
    );
    await settle();
    const file = new File(['x'], 'plan.pdf', { type: 'application/pdf' });
    chat.value!.addAttachments([file]);
    await settle();
    expect(primary(el).dataset.action).toBe('queue');

    primary(el).click();
    await settle();
    expect(queue).toHaveBeenCalledWith('and book the hotel', [file]);
    expect(send).not.toHaveBeenCalled();
    expect(text.value).toBe('');
    expect(el.querySelectorAll('.t-thread-composer__chip')).toHaveLength(0);
    expect(primary(el).dataset.action).toBe('stop');
  });

  it('without the opt-in offers only Stop while streaming', async () => {
    const queue = vi.fn();
    const el = mount(() => h(TChatApp, { ...baseProps, inputText: 'draft', onQueue: queue }));
    await settle();
    expect(primary(el).dataset.action).toBe('stop');
    expect(sideStop(el)).toBeNull();
    el.querySelector('.t-thread-composer textarea')!.dispatchEvent(enter());
    expect(queue).not.toHaveBeenCalled();
  });
});
