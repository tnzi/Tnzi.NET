import { describe, it, expect, afterEach } from 'vitest';
import { createApp, defineComponent, h, ref, nextTick, type App } from 'vue';
import { useAutoGrowTextarea } from '../../src/headless/useAutoGrowTextarea';

/**
 * The composable needs a real component instance (it uses `onMounted`), so the
 * tests mount a throwaway app rather than pulling in @vue/test-utils, which
 * this package does not depend on.
 *
 * happy-dom always reports `scrollHeight: 0`, so each test stubs it to the
 * height it wants the composable to react to.
 */
let app: App | null = null;
let host: HTMLElement | null = null;

function mountHost(setup: () => () => unknown): void {
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp(defineComponent({ setup: setup as never }));
  app.mount(host);
}

afterEach(() => {
  app?.unmount();
  app = null;
  host?.remove();
  host = null;
});

function mountTextarea(scrollHeight: number, maxHeight = 200) {
  const text = ref('');
  const elRef = ref<HTMLTextAreaElement | null>(null);

  mountHost(() => {
    useAutoGrowTextarea(elRef, text, maxHeight);
    return () => h('textarea', { ref: elRef });
  });

  const node = host!.querySelector('textarea') as HTMLTextAreaElement;
  Object.defineProperty(node, 'scrollHeight', { value: scrollHeight, configurable: true });
  return { text, node };
}

describe('useAutoGrowTextarea', () => {
  it('grows the textarea to its content height', async () => {
    const { text, node } = mountTextarea(80);
    text.value = 'two\nlines';
    await nextTick();
    await nextTick();
    expect(node.style.height).toBe('80px');
    expect(node.style.overflowY).toBe('hidden');
  });

  it('stops growing at maxHeight and starts scrolling', async () => {
    const { text, node } = mountTextarea(500, 200);
    text.value = 'a very tall block of text';
    await nextTick();
    await nextTick();
    expect(node.style.height).toBe('200px');
    expect(node.style.overflowY).toBe('auto');
  });

  it('does not throw when the element ref is empty', async () => {
    const text = ref('hello');
    const elRef = ref<HTMLTextAreaElement | null>(null);

    mountHost(() => {
      useAutoGrowTextarea(elRef, text);
      return () => h('div');
    });

    text.value = 'changed';
    await nextTick();
    await nextTick();
    expect(host?.querySelector('div')).not.toBeNull();
  });
});
