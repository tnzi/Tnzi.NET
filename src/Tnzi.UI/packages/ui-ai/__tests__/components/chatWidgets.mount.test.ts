/**
 * The small chat pieces and embed widgets, mounted: each control does what it
 * says when pressed, and every icon-only button is named.
 *
 * Covers `TPromptInput` (send / stop button), `TMessageFeedback`,
 * `TSuggestions`, `TMessageBranch`, `TScrollButton`, `TCodeBlock`'s copy
 * button and the open / close / minimise buttons of `TFloatingChat` and
 * `TSidebarChat`. Keyboard behaviour of the prompt is in
 * `TPromptInput.mount.test.ts` and `imeEnter.mount.test.ts`.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, h, nextTick, reactive, type App, type Component } from 'vue';

const { en } = await import('../../src/locales/en');
const { default: TPromptInput } = await import('../../src/components/chat/TPromptInput.vue');
const { default: TMessageFeedback } = await import('../../src/components/chat/TMessageFeedback.vue');
const { default: TSuggestions } = await import('../../src/components/chat/TSuggestions.vue');
const { default: TMessageBranch } = await import('../../src/components/chat/TMessageBranch.vue');
const { default: TScrollButton } = await import('../../src/components/chat/TScrollButton.vue');
const { default: TCodeBlock } = await import('../../src/components/streaming/TCodeBlock.vue');
const { default: TFloatingChat } = await import('../../src/embed/TFloatingChat.vue');
const { default: TSidebarChat } = await import('../../src/embed/TSidebarChat.vue');

let app: App | null = null;
let host: HTMLElement | null = null;

type Props = Record<string, unknown>;

/** `props` may be a function, read on each render, so reactive state in it re-renders. */
function mount(component: Component, props: Props | (() => Props)): HTMLElement {
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp({ render: () => h(component, typeof props === 'function' ? props() : props) });
  app.mount(host);
  return host;
}

async function settle(): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

/** A leave `<Transition>` removes its element a frame later. */
async function afterTransition(): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 50));
  await settle();
}

const byLabel = (el: ParentNode, label: string) =>
  el.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`);

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
});

describe('TPromptInput button', () => {
  it('sends the text and clears it, and is disabled with nothing to send', async () => {
    const state = reactive({ modelValue: '' });
    const submit = vi.fn();
    const el = mount(TPromptInput, () => ({
      modelValue: state.modelValue,
      'onUpdate:modelValue': (v: string) => (state.modelValue = v),
      onSubmit: submit,
    }));
    const send = byLabel(el, en.composer.send)!;
    expect(send.disabled).toBe(true);

    state.modelValue = 'hello';
    await nextTick();
    expect(send.disabled).toBe(false);
    expect(send.classList.contains('is-ready')).toBe(true);
    send.click();
    expect(submit).toHaveBeenCalledWith('hello', []);
    await nextTick();
    expect(state.modelValue).toBe('');
  });

  it('is Stop while an answer streams', () => {
    const stop = vi.fn();
    const el = mount(TPromptInput, { modelValue: '', loading: true, onStop: stop });
    const button = byLabel(el, en.chat.stop)!;
    expect(button.disabled).toBe(false);
    button.click();
    expect(stop).toHaveBeenCalledTimes(1);
  });

  it('names the paperclip, and hides it without attachments', async () => {
    const el = mount(TPromptInput, { modelValue: '' });
    expect(byLabel(el, en.composer.attach)).not.toBeNull();
    app!.unmount();
    host!.remove();
    const off = mount(TPromptInput, { modelValue: '', enableAttachments: false });
    expect(byLabel(off, en.composer.attach)).toBeNull();
  });

  it('removes an attached file with its named cross', async () => {
    const el = mount(TPromptInput, { modelValue: '' });
    const input = el.querySelector('input[type="file"]') as HTMLInputElement;
    Object.defineProperty(input, 'files', { value: [new File(['x'], 'a.txt')], configurable: true });
    input.dispatchEvent(new Event('change'));
    await settle();
    expect(el.textContent).toContain('a.txt');
    byLabel(el, en.composer.removeAttachment)!.click();
    await settle();
    expect(el.textContent).not.toContain('a.txt');
  });
});

describe('TMessageFeedback', () => {
  it('thumbs up rates positive, and pressed again clears', async () => {
    const state = reactive({ value: null as 'positive' | 'negative' | null });
    const feedback = vi.fn((v: 'positive' | 'negative' | null) => (state.value = v));
    const el = mount(TMessageFeedback, () => ({ value: state.value, onFeedback: feedback }));
    const up = byLabel(el, en.feedback.helpful)!;
    expect(up.getAttribute('aria-pressed')).toBe('false');
    up.click();
    await nextTick();
    expect(feedback).toHaveBeenLastCalledWith('positive');
    expect(up.getAttribute('aria-pressed')).toBe('true');
    up.click();
    expect(feedback).toHaveBeenLastCalledWith(null);
  });

  it('thumbs down asks for a reason and sends it with the button', async () => {
    const feedback = vi.fn();
    const el = mount(TMessageFeedback, { value: null, onFeedback: feedback });
    byLabel(el, en.feedback.notHelpful)!.click();
    await settle();
    const input = el.querySelector<HTMLInputElement>('input.t-input')!;
    expect(input.getAttribute('aria-label')).toBe(en.feedback.reasonPlaceholder);
    expect(document.activeElement).toBe(input);
    expect(feedback).not.toHaveBeenCalled();

    input.value = '  wrong answer ';
    input.dispatchEvent(new Event('input'));
    byLabel(el, en.feedback.sendReason)!.click();
    expect(feedback).toHaveBeenCalledWith('negative', 'wrong answer');
    await nextTick();
    expect(el.querySelector('input')).toBeNull();
  });

  it('Escape or a second thumbs down puts the reason box away without rating', async () => {
    const feedback = vi.fn();
    const el = mount(TMessageFeedback, { value: null, onFeedback: feedback });
    const down = byLabel(el, en.feedback.notHelpful)!;
    down.click();
    await nextTick();
    el.querySelector('input')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await nextTick();
    expect(el.querySelector('input')).toBeNull();

    down.click();
    await nextTick();
    down.click();
    await nextTick();
    expect(el.querySelector('input')).toBeNull();
    expect(feedback).not.toHaveBeenCalled();
  });
});

describe('TSuggestions', () => {
  it('emits the text of the chip pressed, in a sideways scroll row', () => {
    const select = vi.fn();
    const el = mount(TSuggestions, {
      suggestions: [{ text: 'Summarise' }, { text: 'Translate', icon: 'lucide:languages' }],
      onSelect: select,
    });
    const row = el.querySelector('.t-suggestions')!;
    expect(row.classList.contains('t-ai-scroll')).toBe(true);
    const chips = row.querySelectorAll('button');
    expect([...chips].map((c) => c.textContent?.trim())).toEqual(['Summarise', 'Translate']);
    (chips[1] as HTMLButtonElement).click();
    expect(select).toHaveBeenCalledWith('Translate');
  });
});

describe('TMessageBranch', () => {
  it('shows the position and emits prev / next', () => {
    const prev = vi.fn();
    const next = vi.fn();
    const el = mount(TMessageBranch, { currentBranch: 2, totalBranches: 3, onPrev: prev, onNext: next });
    expect(el.textContent).toContain('2/3');
    byLabel(el, en.chat.previousVersion)!.click();
    byLabel(el, en.chat.nextVersion)!.click();
    expect(prev).toHaveBeenCalledTimes(1);
    expect(next).toHaveBeenCalledTimes(1);
  });

  it('renders nothing for a single branch', () => {
    const el = mount(TMessageBranch, { currentBranch: 1, totalBranches: 1 });
    expect(el.querySelector('button')).toBeNull();
  });
});

describe('TScrollButton', () => {
  it('is a named button while visible, and emits click', async () => {
    const state = reactive({ visible: false });
    const click = vi.fn();
    const el = mount(TScrollButton, () => ({ visible: state.visible, onClick: click }));
    expect(el.querySelector('button')).toBeNull();
    state.visible = true;
    await nextTick();
    byLabel(el, en.chat.scrollToLatest)!.click();
    expect(click).toHaveBeenCalledTimes(1);
  });
});

describe('TCodeBlock copy button', () => {
  it('copies the code and says so', async () => {
    const writeText = vi.fn(async () => {});
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    const el = mount(TCodeBlock, { code: 'const a = 1', language: 'ts' });
    await settle();
    byLabel(el, en.chat.copyCode)!.click();
    await nextTick();
    expect(writeText).toHaveBeenCalledWith('const a = 1');
    expect(byLabel(el, en.chat.copied)).not.toBeNull();
  });
});

describe('embed widgets', () => {
  it('TFloatingChat opens, minimises, expands and closes from its buttons', async () => {
    const open = vi.fn();
    const el = mount(TFloatingChat, { messages: [], 'onUpdate:open': open });
    await settle();
    const fab = el.querySelector<HTMLButtonElement>('.t-floating-chat__fab')!;
    expect(fab.getAttribute('aria-expanded')).toBe('false');

    fab.click();
    await settle();
    expect(open).toHaveBeenLastCalledWith(true);
    expect(el.querySelector('.t-floating-chat__window')).not.toBeNull();
    expect(fab.getAttribute('aria-expanded')).toBe('true');

    byLabel(el.querySelector('.t-floating-chat__header')!, en.embed.minimize)!.click();
    await afterTransition();
    expect(el.querySelector('.t-floating-chat__window')).toBeNull();
    el.querySelector<HTMLButtonElement>('.t-floating-chat__expand')!.click();
    await settle();
    expect(el.querySelector('.t-floating-chat__window')).not.toBeNull();

    byLabel(el.querySelector('.t-floating-chat__header')!, en.embed.close)!.click();
    await afterTransition();
    expect(open).toHaveBeenLastCalledWith(false);
    expect(el.querySelector('.t-floating-chat__window')).toBeNull();
  });

  it('TFloatingChat closes from the minimised bar without expanding it first', async () => {
    const el = mount(TFloatingChat, { messages: [] });
    await settle();
    el.querySelector<HTMLButtonElement>('.t-floating-chat__fab')!.click();
    await settle();
    byLabel(el, en.embed.minimize)!.click();
    await afterTransition();
    byLabel(el.querySelector('.t-floating-chat__minimized')!, en.embed.close)!.click();
    await afterTransition();
    expect(el.querySelector('.t-floating-chat__minimized')).toBeNull();
    expect(el.querySelector('.t-floating-chat__window')).toBeNull();
  });

  it('TSidebarChat closes from its header button', async () => {
    const props = reactive({ messages: [], open: false });
    const open = vi.fn();
    const el = mount(TSidebarChat, () => ({ messages: props.messages, open: props.open, 'onUpdate:open': open }));
    await settle();
    props.open = true;
    await settle();
    expect(el.querySelector('.t-sidebar-chat__panel')).not.toBeNull();
    byLabel(el.querySelector('.t-sidebar-chat__header')!, en.embed.close)!.click();
    await settle();
    expect(open).toHaveBeenLastCalledWith(false);
  });
});
