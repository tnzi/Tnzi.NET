/**
 * @tnzi/ui/adapters/theme
 *
 * Theme adapter using Naive UI's theme system.
 */

import type { ThemeAdapter } from '@tnzi/core/adapters/theme';
import type { ThemeMode } from '@tnzi/core/types';
import { applyThemeToDOM } from '../utils/naive-helpers';

/**
 * Create a Naive UI theme adapter.
 *
 * Naive UI 使用 `n-config-provider` 的 `theme` prop 控制主题：
 * - darkTheme 用于暗色模式
 * - null 用于亮色模式
 *
 * 此适配器同时操作 document.documentElement 的 class 以便 CSS 层面配合。
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
