/**
 * @tnzi/shadcn/adapters/theme
 *
 * Theme adapter for shadcn-vue (Tailwind CSS dark mode via class strategy).
 * Manages document.documentElement 'dark' class and listens for system theme changes.
 */

import type { ThemeAdapter } from '@tnzi/core/adapters/theme';
import type { ThemeMode } from '@tnzi/core/types';

/**
 * Create a shadcn theme adapter.
 *
 * shadcn-vue uses Tailwind CSS `class` dark mode strategy — adding/removing
 * the 'dark' class on `<html>` toggles between light and dark palettes.
 */
export function createShadcnThemeAdapter(): ThemeAdapter {
  return {
    applyTheme(mode: ThemeMode) {
      if (typeof document === 'undefined') return;

      const isDark =
        mode === 'dark' ||
        (mode === 'system' &&
          window.matchMedia('(prefers-color-scheme: dark)').matches);

      document.documentElement.classList.toggle('dark', isDark);
    },

    getResolvedTheme(): 'light' | 'dark' {
      if (typeof window === 'undefined') return 'light';
      return document.documentElement.classList.contains('dark') ? 'dark' : 'light';
    },

    onSystemThemeChange(callback: (theme: 'light' | 'dark') => void) {
      if (typeof window === 'undefined') return () => {};
      const mql = window.matchMedia('(prefers-color-scheme: dark)');
      const handler = (e: MediaQueryListEvent) => callback(e.matches ? 'dark' : 'light');
      mql.addEventListener('change', handler);
      return () => mql.removeEventListener('change', handler);
    },

    setPrimaryColor(color: string) {
      if (typeof document === 'undefined') return;
      document.documentElement.style.setProperty('--primary', color);
    },
  };
}
