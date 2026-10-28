/**
 * Text fields that act on Enter must leave alone the Enter that commits an IME
 * candidate (Chinese, Japanese, Korean input): at that moment the field's model
 * still holds the text from before the composition, or the half-composed text
 * itself, so acting sends, picks or submits the wrong thing.
 *
 * `TPromptInput` has its own mount test; these are the other fields in the
 * package that act on Enter. Safari fires the committing Enter with
 * `isComposing` false and keyCode 229, so a check on `isComposing` alone
 * still sends there.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, h, nextTick, type App } from 'vue';

const { default: TCommandPalette } = await import('../../src/components/overlay/TCommandPalette.vue');
const { default: TMessageFeedback } = await import('../../src/components/chat/TMessageFeedback.vue');
const { default: TAuthField } = await import('../../src/auth/TAuthField.vue');
const { default: TThreadComposer } = await import('../../src/components/chat/TThreadComposer.vue');

let app: App | null = null;
let host: HTMLElement | null = null;

function mount(render: () => unknown): HTMLElement {
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp({ render });
  app.mount(host);
  return host;
}

const composingEnter = () => new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true, isComposing: true });
const safariCommitEnter = () => new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true, keyCode: 229 });
const plainEnter = () => new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true });

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
});

describe('IME-safe Enter', () => {
  it('TCommandPalette does not run the highlighted command while the search box is composing', async () => {
    const run = vi.fn();
    mount(() => h(TCommandPalette, { modelValue: true, actions: [{ id: 'a', label: 'Archive', run }] }));
    await nextTick();

    window.dispatchEvent(composingEnter());
    window.dispatchEvent(safariCommitEnter());
    await nextTick();
    expect(run).not.toHaveBeenCalled();

    window.dispatchEvent(plainEnter());
    await nextTick();
    expect(run).toHaveBeenCalledTimes(1);
  });

  it('TMessageFeedback does not submit the reason on a composing Enter', async () => {
    const feedback = vi.fn();
    const el = mount(() => h(TMessageFeedback, { value: null, onFeedback: feedback }));
    const buttons = el.querySelectorAll('button');
    (buttons[1] as HTMLButtonElement).click(); // thumbs down: shows the reason box
    await nextTick();
    const input = el.querySelector('input')!;

    input.dispatchEvent(composingEnter());
    input.dispatchEvent(safariCommitEnter());
    expect(feedback).not.toHaveBeenCalled();

    input.dispatchEvent(plainEnter());
    expect(feedback).toHaveBeenCalledWith('negative', undefined);
  });

  it('TThreadComposer does not send on a composing Enter, Safari included', () => {
    const send = vi.fn();
    const el = mount(() => h(TThreadComposer, { modelValue: 'ni hao', enableVoice: false, onSend: send }));
    const textarea = el.querySelector('textarea')!;

    textarea.dispatchEvent(composingEnter());
    textarea.dispatchEvent(safariCommitEnter());
    expect(send).not.toHaveBeenCalled();

    textarea.dispatchEvent(plainEnter());
    expect(send).toHaveBeenCalledWith('ni hao', []);
  });

  it('TAuthField does not submit on a composing Enter', () => {
    const submit = vi.fn();
    const el = mount(() => h(TAuthField, { onSubmit: submit }));
    const input = el.querySelector('input')!;

    const composing = composingEnter();
    input.dispatchEvent(composing);
    input.dispatchEvent(safariCommitEnter());
    expect(submit).not.toHaveBeenCalled();
    expect(composing.defaultPrevented).toBe(false);

    const plain = plainEnter();
    input.dispatchEvent(plain);
    expect(submit).toHaveBeenCalledTimes(1);
    expect(plain.defaultPrevented).toBe(true);
  });
});
