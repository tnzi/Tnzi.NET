/**
 * `TPromptInput` mounted for real: the composer every embed widget renders
 * (`TChatBox` inside `TFloatingChat` / `TSidebarChat` / `TInlineChat`).
 *
 * Three things only a mount can show: the Enter that commits an IME candidate
 * must not send the half-composed text, Enter while an answer streams must not
 * stop it (Stop stays on the button), and a file refused at selection for its
 * size must be reported instead of vanishing.
 */
import { afterEach, describe, expect, it } from 'vitest';
import { createApp, h, nextTick, reactive, type App } from 'vue';

const { default: TPromptInput } = await import('../../src/components/chat/TPromptInput.vue');

let app: App | null = null;
let host: HTMLElement | null = null;

function mountInput(props: { modelValue: string; loading?: boolean; maxFileSize?: number }) {
  const state = reactive({ ...props });
  const submits: Array<[string, File[]]> = [];
  let stops = 0;
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp({
    render: () =>
      h(TPromptInput, {
        ...state,
        onSubmit: (content: string, files: File[]) => submits.push([content, files]),
        onStop: () => {
          stops += 1;
        },
      }),
  });
  app.mount(host);
  const textarea = host.querySelector('textarea')!;
  return { host, state, textarea, submits, stops: () => stops };
}

function enter(target: HTMLElement, init: KeyboardEventInit = {}): KeyboardEvent {
  const event = new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true, ...init });
  target.dispatchEvent(event);
  return event;
}

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
});

describe('TPromptInput keyboard', () => {
  it('sends on a plain Enter', () => {
    const { textarea, submits } = mountInput({ modelValue: 'hello' });
    enter(textarea);
    expect(submits).toEqual([['hello', []]]);
  });

  it('does not send on the Enter that commits an IME candidate', () => {
    const { textarea, submits } = mountInput({ modelValue: 'ni hao' });
    const event = enter(textarea, { isComposing: true });
    expect(submits).toEqual([]);
    // The IME owns that keystroke; cancelling it would swallow the commit.
    expect(event.defaultPrevented).toBe(false);
  });

  it('does not send on the keyCode 229 Enter Safari sends after compositionend', () => {
    const { textarea, submits } = mountInput({ modelValue: 'ni hao' });
    enter(textarea, { keyCode: 229 });
    expect(submits).toEqual([]);
  });

  it('Enter while an answer streams neither stops it nor sends', () => {
    const { textarea, submits, stops } = mountInput({ modelValue: 'next question', loading: true });
    const event = enter(textarea);
    expect(stops()).toBe(0);
    expect(submits).toEqual([]);
    // No newline either: the draft is waiting for the answer to finish.
    expect(event.defaultPrevented).toBe(true);
  });

  it('the Stop button still stops a streaming answer', () => {
    const { host, stops } = mountInput({ modelValue: '', loading: true });
    const buttons = host.querySelectorAll('button');
    (buttons[buttons.length - 1] as HTMLButtonElement).click();
    expect(stops()).toBe(1);
  });
});

describe('TPromptInput attachments', () => {
  it('reports a file refused for its size instead of dropping it silently', async () => {
    const { host } = mountInput({ modelValue: '', maxFileSize: 4 });
    const input = host.querySelector('input[type="file"]') as HTMLInputElement;
    const big = new File(['too large'], 'scan.png', { type: 'image/png' });
    Object.defineProperty(input, 'files', { value: [big], configurable: true });
    input.dispatchEvent(new Event('change'));
    await nextTick();

    const notice = host.querySelector('[role="status"]');
    expect(notice?.textContent).toContain('scan.png');
  });
});
