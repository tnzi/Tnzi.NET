/**
 * @tnzi/core/adapters/theme
 *
 * Theme adapter interface for UI framework abstraction.
 */

import type { ThemeMode } from '../../types/theme';

// ============================================
// Theme Adapter Interface
// ============================================

/**
 * Theme adapter for applying theme changes to the DOM.
 * Each UI package provides its own implementation.
 */
export interface ThemeAdapter {
  /**
   * Apply theme mode (light/dark/system) to the document.
   * @param mode - Theme mode to apply
   */
  applyTheme(mode: ThemeMode): void;

  /**
   * Get the resolved theme (resolves 'system' to actual value).
   */
  getResolvedTheme(): 'light' | 'dark';

  /**
   * Listen for system theme changes.
   * @param callback - Called when system theme changes
   * @returns Unsubscribe function
   */
  onSystemThemeChange?(callback: (theme: 'light' | 'dark') => void): () => void;

  /**
   * Set custom primary color.
   * @param color - CSS color value
   */
  setPrimaryColor?(color: string): void;
}

// ============================================
// Default Implementation (No-op)
// ============================================

/**
 * Create a no-op theme adapter (for SSR or when no UI framework is loaded).
 */
export function createNoopThemeAdapter(): ThemeAdapter {
  return {
    applyTheme() {},
    getResolvedTheme() {
      return 'light';
    },
  };
}
