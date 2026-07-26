/**
 * @tnzi/mobile/adapters/theme
 *
 * Theme adapter implementation for Vant.
 *
 * Vant 4 has no theme provider component: dark mode is a class on an ancestor
 * element (`van-theme-dark`) that re-declares every `--van-*` token. This
 * adapter puts that class on `<html>` so the whole document, including
 * teleported dialogs and toasts, follows the store's theme.
 */

import type { ThemeAdapter } from '@tnzi/core/adapters/theme';
import type { ThemeMode } from '@tnzi/core/types';

/** Vant 4 dark-mode class. */
const DARK_CLASS = 'van-theme-dark';
/** Vant 4 light-mode class, applied so nested dark scopes can be overridden. */
const LIGHT_CLASS = 'van-theme-light';
/** Kept in sync with the Vant class so UnoCSS `dark:` utilities resolve too. */
const UNO_DARK_CLASS = 'dark';

function prefersDark(): boolean {
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return false;
  return window.matchMedia('(prefers-color-scheme: dark)').matches;
}

function resolveMode(mode: ThemeMode): 'light' | 'dark' {
  if (mode === 'dark') return 'dark';
  if (mode === 'light') return 'light';
  return prefersDark() ? 'dark' : 'light';
}

export function createVantThemeAdapter(): ThemeAdapter {
  return {
    applyTheme(mode: ThemeMode) {
      if (typeof document === 'undefined') return;
      const resolved = resolveMode(mode);
      const root = document.documentElement;
      root.classList.toggle(DARK_CLASS, resolved === 'dark');
      root.classList.toggle(LIGHT_CLASS, resolved === 'light');
      root.classList.toggle(UNO_DARK_CLASS, resolved === 'dark');
      root.style.colorScheme = resolved;
    },

    getResolvedTheme(): 'light' | 'dark' {
      if (typeof document === 'undefined') return 'light';
      return document.documentElement.classList.contains(DARK_CLASS) ? 'dark' : 'light';
    },

    onSystemThemeChange(callback: (theme: 'light' | 'dark') => void) {
      if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return () => {};
      const query = window.matchMedia('(prefers-color-scheme: dark)');
      const handler = (event: MediaQueryListEvent) => callback(event.matches ? 'dark' : 'light');
      query.addEventListener('change', handler);
      return () => query.removeEventListener('change', handler);
    },

    setPrimaryColor(color: string) {
      if (typeof document === 'undefined') return;
      document.documentElement.style.setProperty('--van-primary-color', color);
    },
  };
}
