/**
 * `TChatApp` with a host `@tnzi/ui` theme context.
 *
 * The context writes the mode-dependent `--tnzi-*` tokens (base text,
 * containers, borders) inline on `<html>`, which outranks the stylesheet's
 * `.dark` block. `defineChatApp()` provides one (in light mode) for every chat
 * product, and TChatApp only toggled the `dark` class: a user who picked Dark
 * got a dark canvas with headings, tool-call cards and borders still painted
 * with the light tokens. With a host context, the shell's resolved theme now
 * drives the context's mode, so the class and the tokens agree.
 */
import { afterEach, describe, expect, it } from 'vitest';
import { createApp, h, nextTick, ref, type App } from 'vue';
import { THEME_CONTEXT_KEY, createThemeContext, mergeThemeSettings, type ThemeContext } from '@tnzi/ui';

const { default: TChatApp } = await import('../../src/components/chat/TChatApp.vue');

const DARK_TEXT = 'rgb(224, 224, 224)';
const LIGHT_TEXT = 'rgb(51, 54, 57)';

let app: App | null = null;
let host: HTMLElement | null = null;

function mountShell(theme: ReturnType<typeof ref<'light' | 'dark' | 'auto'>>, context: ThemeContext | null) {
  host = document.createElement('div');
  document.body.appendChild(host);
  app = createApp({ render: () => h(TChatApp, { theme: theme.value }) });
  if (context) app.provide(THEME_CONTEXT_KEY, context);
  app.mount(host);
}

async function settle(): Promise<void> {
  for (let i = 0; i < 4; i += 1) {
    await nextTick();
    await Promise.resolve();
  }
}

const root = () => document.documentElement;

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
  root().classList.remove('dark');
  root().removeAttribute('style');
});

describe('TChatApp theme with a host theme context', () => {
  it('paints the base tokens for the mode the user picked', async () => {
    const context = createThemeContext(mergeThemeSettings({}));
    await settle();
    expect(root().style.getPropertyValue('--tnzi-base-text')).toBe(LIGHT_TEXT);

    const theme = ref<'light' | 'dark' | 'auto'>('dark');
    mountShell(theme, context);
    await settle();

    expect(root().classList.contains('dark')).toBe(true);
    expect(context.resolvedMode.value).toBe('dark');
    expect(root().style.getPropertyValue('--tnzi-base-text')).toBe(DARK_TEXT);
  });

  it('follows a switch back to light', async () => {
    const context = createThemeContext(mergeThemeSettings({}));
    const theme = ref<'light' | 'dark' | 'auto'>('dark');
    mountShell(theme, context);
    await settle();

    theme.value = 'light';
    await settle();

    expect(root().classList.contains('dark')).toBe(false);
    expect(root().style.getPropertyValue('--tnzi-base-text')).toBe(LIGHT_TEXT);
  });

  it('keeps the brand colour a mode switch re-emits', async () => {
    const context = createThemeContext(mergeThemeSettings({ colors: { primary: '#ff0000' } }));
    const theme = ref<'light' | 'dark' | 'auto'>('dark');
    mountShell(theme, context);
    await settle();

    expect(root().style.getPropertyValue('--tnzi-primary')).toBe('#ff0000');
  });
});

describe('TChatApp theme without a host theme context', () => {
  it('only toggles the dark class and writes no tokens of its own', async () => {
    const theme = ref<'light' | 'dark' | 'auto'>('dark');
    mountShell(theme, null);
    await settle();

    expect(root().classList.contains('dark')).toBe(true);
    expect(root().style.getPropertyValue('--tnzi-base-text')).toBe('');
  });
});
