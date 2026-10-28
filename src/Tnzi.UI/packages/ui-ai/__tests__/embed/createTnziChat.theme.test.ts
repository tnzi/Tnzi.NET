/**
 * The embed widgets are mounted in an app of their own, so nothing above them
 * provides a naive theme. Without one their naive controls (the launcher, the
 * send button) fell back to naive's stock green, light on any page. The widget
 * now provides the Tnzi palette and follows the `dark` class on `<html>`, the
 * same switch the package's own `--tnzi-ai-*` colours follow.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { nextTick } from 'vue';
import { buildNaiveThemeOverrides, defaultThemeSettings } from '@tnzi/ui';

const seen = vi.hoisted(() => ({ primaryColor: '', bodyColor: '' }));

vi.mock('../../src/embed/TInlineChat.vue', async () => {
  const { defineComponent: define } = await import('vue');
  const { useThemeVars: themeVars } = await import('naive-ui');
  return {
    default: define({
      setup() {
        const vars = themeVars();
        return () => {
          seen.primaryColor = vars.value.primaryColor;
          seen.bodyColor = vars.value.bodyColor;
          return null;
        };
      },
    }),
  };
});
vi.mock('../../src/embed/TFloatingChat.vue', () => ({ default: { render: () => null } }));
vi.mock('../../src/embed/TSidebarChat.vue', () => ({ default: { render: () => null } }));
vi.mock('@tnzi/core/services/ai', () => ({ streamChat: vi.fn() }));

const { createTnziChat } = await import('../../src/embed/createTnziChat');

const NAIVE_STOCK_PRIMARY = '#18a058';
const tnziPrimary = () => buildNaiveThemeOverrides(defaultThemeSettings).common?.primaryColor;

async function settle(): Promise<void> {
  for (let i = 0; i < 3; i += 1) {
    await Promise.resolve();
    await nextTick();
  }
}

afterEach(() => {
  document.documentElement.classList.remove('dark');
  document.documentElement.removeAttribute('data-theme');
  document.body.innerHTML = '';
});

describe('createTnziChat theme', () => {
  it('gives the widget the Tnzi palette instead of naive stock colours', async () => {
    document.body.innerHTML = '<div id="host"></div>';
    const chat = createTnziChat({ mode: 'inline', el: '#host', onSend: () => {} });
    await settle();

    expect(tnziPrimary()).toBeTruthy();
    expect(seen.primaryColor).toBe(tnziPrimary());
    expect(seen.primaryColor).not.toBe(NAIVE_STOCK_PRIMARY);
    chat.destroy();
  });

  it('follows the dark class on the document, both ways', async () => {
    document.body.innerHTML = '<div id="host"></div>';
    const chat = createTnziChat({ mode: 'inline', el: '#host', onSend: () => {} });
    await settle();
    const lightBody = seen.bodyColor;

    document.documentElement.classList.add('dark');
    await settle();
    const darkBody = seen.bodyColor;
    expect(darkBody).not.toBe(lightBody);

    document.documentElement.classList.remove('dark');
    await settle();
    expect(seen.bodyColor).toBe(lightBody);
    chat.destroy();
  });

  it('also reads data-theme="dark", which the stylesheet honours too', async () => {
    document.body.innerHTML = '<div id="host"></div>';
    const chat = createTnziChat({ mode: 'inline', el: '#host', onSend: () => {} });
    await settle();
    const lightBody = seen.bodyColor;

    document.documentElement.setAttribute('data-theme', 'dark');
    await settle();
    expect(seen.bodyColor).not.toBe(lightBody);
    chat.destroy();
  });

  it('starts dark when the page already is', async () => {
    document.documentElement.classList.add('dark');
    document.body.innerHTML = '<div id="a"></div><div id="b"></div>';
    const dark = createTnziChat({ mode: 'inline', el: '#a', onSend: () => {} });
    await settle();
    const darkBody = seen.bodyColor;
    dark.destroy();

    document.documentElement.classList.remove('dark');
    const light = createTnziChat({ mode: 'inline', el: '#b', onSend: () => {} });
    await settle();
    expect(seen.bodyColor).not.toBe(darkBody);
    light.destroy();
  });
});
