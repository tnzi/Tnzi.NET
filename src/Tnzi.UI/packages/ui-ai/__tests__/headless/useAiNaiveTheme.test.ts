import { describe, it, expect } from 'vitest';
import { ref, computed, defineComponent, h, createApp } from 'vue';
import { darkTheme } from 'naive-ui';
import { THEME_CONTEXT_KEY, buildNaiveThemeOverrides, defaultThemeSettings } from '@tnzi/ui';
import { useAiNaiveTheme } from '../../src/headless/useAiNaiveTheme';

/** Run the hook inside a component so `inject` works, optionally with a host theme. */
function runInSetup<T>(fn: () => T, provides?: Record<symbol, unknown>): T {
  let result!: T;
  const Child = defineComponent({
    setup() {
      result = fn();
      return () => null;
    },
  });
  const Root = defineComponent({ setup: () => () => h(Child) });
  const app = createApp(Root);
  for (const key of Object.getOwnPropertySymbols(provides ?? {})) {
    app.provide(key as never, (provides as Record<symbol, unknown>)[key]);
  }
  app.mount(document.createElement('div'));
  app.unmount();
  return result;
}

describe('useAiNaiveTheme', () => {
  it('follows the mode it is given', () => {
    const isDark = ref(false);
    const t = runInSetup(() => useAiNaiveTheme(isDark));

    expect(t.theme.value).toBeNull(); // naive's light base
    isDark.value = true;
    expect(t.theme.value).toBe(darkTheme);
  });

  it('falls back to the Tnzi palette when no host theme was provided', () => {
    /* The point of the fallback: an app that never called `createTnziUi()` must
       still get Tnzi colours, not naive's stock blue. Returning `{}` here would
       silently hand every consumer naive's defaults. */
    const t = runInSetup(() => useAiNaiveTheme(ref(false)));

    expect(t.hasHostTheme.value).toBe(false);
    expect(t.themeOverrides.value).toEqual(buildNaiveThemeOverrides(defaultThemeSettings));
    expect(t.themeOverrides.value.common?.primaryColor).toBeTruthy();
  });

  it('prefers the host theme context when one exists', () => {
    const hostOverrides = { common: { primaryColor: '#ff0000' } };
    const host = {
      isDark: computed(() => false),
      naiveOverrides: computed(() => hostOverrides),
    };

    const t = runInSetup(() => useAiNaiveTheme(ref(false)), {
      [THEME_CONTEXT_KEY as symbol]: host,
    });

    expect(t.hasHostTheme.value).toBe(true);
    expect(t.themeOverrides.value).toBe(hostOverrides);
  });

  it('tracks host colour changes', () => {
    /* The whole reason for reading the context rather than snapshotting it:
       changing the product colour has to reach these surfaces live. */
    const primary = ref('#111111');
    const host = {
      isDark: computed(() => false),
      naiveOverrides: computed(() => ({ common: { primaryColor: primary.value } })),
    };

    const t = runInSetup(() => useAiNaiveTheme(ref(false)), {
      [THEME_CONTEXT_KEY as symbol]: host,
    });

    expect(t.themeOverrides.value.common?.primaryColor).toBe('#111111');
    primary.value = '#222222';
    expect(t.themeOverrides.value.common?.primaryColor).toBe('#222222');
  });
});
