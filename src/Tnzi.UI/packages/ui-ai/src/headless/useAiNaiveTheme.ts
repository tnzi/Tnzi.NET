/**
 * The naive-ui theme for everything this package renders.
 *
 * ## Why this exists
 *
 * `@tnzi/ui-ai` is an application package built on `@tnzi/ui`, and `@tnzi/ui`
 * owns the theme: one `ThemeSettings` (5 semantic roles x 11 palette levels)
 * fans out to naive-ui overrides, `--tnzi-*` CSS variables and UnoCSS atoms.
 * Until now this package took **none** of it - it rendered 25+ naive-ui
 * components while never providing or configuring a naive theme, so those
 * components fell back to naive's stock look unless the host app happened to
 * mount its own `NConfigProvider`. A product could change its primary colour
 * and the chat surfaces would not follow.
 *
 * That is what "styling is controllable" requires and what this closes: the
 * shell provides the theme, so a consumer sets a colour once and both the
 * naive controls and this package's own painted surfaces move together.
 *
 * ## Where each half comes from
 *
 * - **mode** (light/dark base) follows the shell's own resolved mode - the same
 *   value it writes to `<html>` - so the naive surfaces and the CSS variables
 *   can never disagree about which mode is showing. A host that wants to own
 *   the mode drives it through `v-model:theme`.
 * - **overrides** (primary/info/success/warning/error, radius, …) come from the
 *   host's `@tnzi/ui` theme context when one was provided by `createTnziUi()`,
 *   and otherwise from `defaultThemeSettings`. Falling back to the defaults
 *   rather than to `{}` is the point: an app that never called `createTnziUi()`
 *   still gets the Tnzi palette instead of naive's stock blue.
 */
import { computed, inject, type ComputedRef, type Ref } from 'vue';
import { darkTheme, type GlobalTheme, type GlobalThemeOverrides } from 'naive-ui';
import {
  THEME_CONTEXT_KEY,
  buildNaiveThemeOverrides,
  defaultThemeSettings,
  type ThemeContext,
} from '@tnzi/ui';

export interface UseAiNaiveThemeReturn {
  /** Bind to `<NConfigProvider :theme>`. `null` means naive's light base. */
  readonly theme: ComputedRef<GlobalTheme | null>;
  /** Bind to `<NConfigProvider :theme-overrides>`. */
  readonly themeOverrides: ComputedRef<GlobalThemeOverrides>;
  /** True when a host `createTnziUi()` context was found. Diagnostics only. */
  readonly hasHostTheme: ComputedRef<boolean>;
}

/**
 * @param isDark the shell's resolved mode. Must be the same ref that drives
 *        the `dark` class on the document, or the naive controls and the
 *        painted surfaces will disagree.
 */
export function useAiNaiveTheme(isDark: Ref<boolean> | ComputedRef<boolean>): UseAiNaiveThemeReturn {
  const hostTheme = inject<ThemeContext | undefined>(THEME_CONTEXT_KEY, undefined);

  /* Computed once and reused - `buildNaiveThemeOverrides` walks 5 palettes, and
     without a host context the input never changes. */
  const fallbackOverrides = buildNaiveThemeOverrides(defaultThemeSettings);

  return {
    theme: computed<GlobalTheme | null>(() => (isDark.value ? darkTheme : null)),
    themeOverrides: computed(() => hostTheme?.naiveOverrides.value ?? fallbackOverrides),
    hasHostTheme: computed(() => hostTheme !== undefined),
  };
}
