/**
 * @tnzi/ui/theme/document
 *
 * The single place in this package that writes the `dark` class onto
 * `<html>`.
 *
 * There used to be three, each with its own copy of the "is this dark?"
 * decision: the theme context watcher in `headless/theme/useTheme.ts`,
 * `applyThemeToDOM` in `utils/naive-helpers.ts` (reached through the core
 * `ThemeAdapter`, so `AppStateManager` / `useAppStore` / `useUserStore` all
 * went down it), and `TThemeSchemaSwitch`'s own `resolveDark`. Two of them
 * spelled "follow the OS" as `'auto'` and one as `'system'`, so an app that
 * drove the theme through both a store and the theme context could end up
 * with the two halves disagreeing about what `<html>` should look like -
 * with nothing to fail, only a wrong colour.
 *
 * Several callers may still *trigger* a theme application; they now all route
 * the decision through here.
 */

import type { ThemeMode } from '@tnzi/core/types'
import { resolveThemeMode } from './naive-bridge'

/** The class toggled on `<html>` to put the document into dark mode. */
export const DARK_CLASS = 'dark'

/**
 * Resolve `mode` (turning `'auto'` into the OS colour scheme) and write the
 * result to `<html>`.
 *
 * No-ops outside a browser (SSR, node test runs) and still returns the
 * resolved value, so callers can use the return without branching on
 * environment.
 *
 * @param mode - The requested theme mode.
 * @returns The mode actually in effect: `'light'` or `'dark'`.
 */
export function applyThemeModeToDocument(mode: ThemeMode): 'light' | 'dark' {
  const { resolved } = resolveThemeMode(mode)
  if (typeof document !== 'undefined') {
    document.documentElement.classList.toggle(DARK_CLASS, resolved === 'dark')
  }
  return resolved
}
