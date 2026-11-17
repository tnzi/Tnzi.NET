/**
 * @tnzi/ui/theme/document
 *
 * The single place in this package that writes the `dark` class onto
 * `<html>`, and the one that decides what `'auto'` resolves to.
 *
 * There used to be three writers, each with its own copy of the "is this
 * dark?" decision: the theme context watcher in `headless/theme/useTheme.ts`,
 * `applyThemeToDOM` (reached through the core `ThemeAdapter`, so
 * `AppStateManager` / `useAppStore` / `useUserStore` all went down it), and
 * `TThemeSchemaSwitch`'s own `resolveDark`. Two of them spelled "follow the
 * OS" as `'auto'` and one as `'system'`, so an app that drove the theme
 * through both a store and the theme context could end up with the two halves
 * disagreeing about what `<html>` should look like - with nothing to fail,
 * only a wrong colour.
 *
 * Several callers may still *trigger* a theme application; they now all route
 * the decision through here.
 *
 * Nothing in this file knows a component library: the theme context, the
 * stores and the theme adapter all reach it, and none of them may pull a
 * component library in behind them.
 */

import type { ThemeMode } from '@tnzi/core/types'

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

/**
 * Resolve 'auto' mode to either 'light' or 'dark' based on system preference.
 *
 * Contract for `onChange`: it fires ONLY on subsequent system changes, never
 * on the initial resolution. Callers should seed their state from the returned
 * `resolved` value and use `onChange` purely for reactivity. This split keeps
 * the initial return synchronous and side-effect free.
 *
 * Returns the resolved mode and (when `onChange` is provided) a cleanup
 * function that removes the media query listener.
 */
export function resolveThemeMode(
  mode: ThemeMode,
  onChange?: (resolved: 'light' | 'dark') => void,
): { resolved: 'light' | 'dark'; cleanup?: () => void } {
  if (mode !== 'auto') {
    return { resolved: mode }
  }
  if (typeof window === 'undefined') {
    return { resolved: 'light' } // SSR fallback
  }
  const mq = window.matchMedia('(prefers-color-scheme: dark)')
  const resolved = mq.matches ? 'dark' : 'light'
  // `addEventListener` on MediaQueryList is missing from Safari before 14 and
  // from some test doubles; there the mode is still resolved, just not followed.
  if (onChange && typeof mq.addEventListener === 'function') {
    const handler = (e: MediaQueryListEvent) => onChange(e.matches ? 'dark' : 'light')
    mq.addEventListener('change', handler)
    return { resolved, cleanup: () => mq.removeEventListener('change', handler) }
  }
  return { resolved }
}

// ============================================
// DOM helpers (theme & language)
// ============================================

/**
 * Apply theme mode to the document root element by toggling the 'dark' class.
 * Resolves `'auto'` using the `prefers-color-scheme` media query.
 * Safe to call in SSR (no-ops when `document` is unavailable).
 *
 * Thin delegate to {@link applyThemeModeToDocument}, which is the package's one
 * writer of that class. Kept as a named export because it is the shape the core
 * `ThemeAdapter` contract is wired to.
 */
export function applyThemeToDOM(theme: ThemeMode): void {
  applyThemeModeToDocument(theme)
}

/**
 * Apply language to the document root element's `lang` attribute.
 * Safe to call in SSR (no-ops when `document` is unavailable).
 */
export function applyLanguageToDOM(language: string): void {
  if (typeof document === 'undefined') return
  document.documentElement.lang = language
}
