/**
 * @tnzi/ui/adapters/theme
 *
 * The core `ThemeAdapter` for a browser document: the mode lands on `<html>`
 * through `theme/document`, the one writer of the `dark` class. It knows no
 * component library; a package that renders one (Naive UI's
 * `n-config-provider`, Vant's theme class) follows the same document state or
 * the theme context on its own.
 */

import type { ThemeAdapter } from '@tnzi/core/adapters/theme';
import type { ThemeMode } from '@tnzi/core/types';
import { applyThemeToDOM } from '../theme/document';

/**
 * Create the document theme adapter: applying a mode writes it to `<html>`,
 * the resolved mode is read back from there, and OS colour-scheme changes are
 * reported through `prefers-color-scheme`.
 */
export function createThemeAdapter(): ThemeAdapter {
  return {
    applyTheme(mode: ThemeMode) {
      applyThemeToDOM(mode);
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
  };
}
