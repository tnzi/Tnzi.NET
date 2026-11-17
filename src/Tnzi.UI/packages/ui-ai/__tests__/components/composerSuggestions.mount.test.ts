/**
 * TThreadComposer's suggestion-menu hook: the `#suggestions` slot gets the text, the caret and the
 * token before it, plus `replaceRange`; a key the consumer takes (`preventDefault` in `input-keydown`)
 * is not acted on, so Enter can pick an item instead of sending; `inputAttrs` reach the textarea.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, defineComponent, h, nextTick, ref, type App } from 'vue';
import type { ComposerSuggestionContext } from '../../src/components/chat/composer-suggestions';

const { default: TThreadComposer } = await import('../../src/components/chat/TThreadComposer.vue');

let app: App | null = null;
let host: HTMLElement | null = null;

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  document.body.innerHTML = '';
});

function mountComposer(options: { initial?: string; onKeydown?: (e: KeyboardEvent) => void; onSend?: (text: string) => void } = {}) {
  const text = ref(options.initial ?? '');
  let context: ComposerSuggestionContext | null = null;
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp(defineComponent({
    setup: () => () => h(
      TThreadComposer,
      {
        modelValue: text.value,
        enableVoice: false,
        inputAttrs: { role: 'combobox', 'aria-expanded': 'true', 'aria-controls': 'menu-1' },
        'onUpdate:modelValue': (v: string) => { text.value = v; },
        onInputKeydown: options.onKeydown,
        onSend: options.onSend,
      },
      {
        suggestions: (ctx: ComposerSuggestionContext) => {
          context = ctx;
          return ctx.token.text.startsWith('/') ? h('ul', { id: 'menu-1', class: 'menu' }, [h('li', 'review')]) : null;
        },
      },
    ),
  }));
  app.mount(host);
  const textarea = host.querySelector('textarea')!;
  const type = async (value: string) => {
    textarea.value = value;
    textarea.setSelectionRange(value.length, value.length);
    textarea.dispatchEvent(new Event('input'));
    await nextTick();
  };
  return { text, textarea, type, context: () => context! };
}

const enter = () => new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true });

describe('TThreadComposer suggestion hook', () => {
  it('hands the slot the text, the caret and the token before it, anchored above the box', async () => {
    const c = mountComposer();
    await c.type('/rev');

    expect(c.context().text).toBe('/rev');
    expect(c.context().selectionStart).toBe(4);
    expect(c.context().token).toEqual({ text: '/rev', start: 0, end: 4 });
    const anchor = host!.querySelector('.t-thread-composer__suggestions')!;
    expect(anchor.querySelector('#menu-1')).not.toBeNull();
    expect(anchor.parentElement!.classList.contains('t-thread-composer')).toBe(true);
  });

  it('replaceRange writes the pick back through v-model and puts the caret after it', async () => {
    const c = mountComposer();
    await c.type('please /rev');

    c.context().replaceRange(c.context().token.start, c.context().token.end, '/review ');
    await nextTick();
    await nextTick();

    expect(c.text.value).toBe('please /review ');
    expect(c.textarea.selectionStart).toBe('please /review '.length);
    expect(document.activeElement).toBe(c.textarea);
  });

  it('does not send on an Enter the menu took, and sends on one it left alone', async () => {
    const send = vi.fn();
    let menuOpen = true;
    const c = mountComposer({ onSend: send, onKeydown: (e) => { if (menuOpen && e.key === 'Enter') e.preventDefault(); } });
    await c.type('/rev');

    c.textarea.dispatchEvent(enter());
    await nextTick();
    expect(send).not.toHaveBeenCalled();

    menuOpen = false;
    c.textarea.dispatchEvent(enter());
    await nextTick();
    expect(send).toHaveBeenCalledWith('/rev', []);
  });

  it('puts inputAttrs on the textarea for the combobox wiring', async () => {
    const c = mountComposer();
    expect(c.textarea.getAttribute('role')).toBe('combobox');
    expect(c.textarea.getAttribute('aria-controls')).toBe('menu-1');
    expect(c.textarea.classList.contains('t-thread-composer__input')).toBe(true);
  });
});
