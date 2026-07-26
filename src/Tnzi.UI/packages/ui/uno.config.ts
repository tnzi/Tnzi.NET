import { defineConfig, presetWind4, presetIcons } from 'unocss';

/**
 * UnoCSS configuration for @tnzi/ui.
 *
 * Architectural note:
 * - `presetWind4` is integrated to provide **Tailwind v4-compatible utilities for
 *   end-user application code** that consumes @tnzi/ui. End users can use Tailwind
 *   conventions like `text-blue-500`, `font-medium`, `shadow-lg`, etc. in their own
 *   code without extra setup.
 * - **@tnzi/ui's own components** should NOT use Tailwind-specific conventions.
 *   Internal code uses pure UnoCSS conventions: numeric scales (`text-4`, `font-500`),
 *   Naive UI design tokens via shortcuts (`text-success`, `bg-naive-card`), and
 *   arbitrary values (`text-[13px]`, `rounded-[3px]`). This keeps internal styling
 *   theme-aware and decoupled from any specific color palette.
 */
export default defineConfig({
  presets: [
    presetWind4(),
    presetIcons({
      scale: 1.2,
      extraProperties: {
        'display': 'inline-block',
        'vertical-align': 'middle',
      },
    }),
  ],

  shortcuts: {
    // Naive UI design tokens - use these instead of hardcoded Tailwind colors
    'border-naive': 'border border-[var(--n-border-color,#e0e0e6)]',
    'bg-naive-hover': 'bg-[var(--n-color-hover,rgba(0,0,0,0.03))]',
    'bg-naive-card': 'bg-[var(--n-card-color,#fff)]',
    'text-naive-secondary': 'text-[var(--n-text-color-3,#999)]',
    'text-naive-primary': 'text-[var(--n-text-color-1,#333)]',
    // Semantic colors mapped to Naive UI status tokens (theme-aware)
    'text-success': 'text-[var(--n-success-color,#18a058)]',
    'text-error': 'text-[var(--n-error-color,#d03050)]',
    'text-warning': 'text-[var(--n-warning-color,#f0a020)]',
    'text-info': 'text-[var(--n-info-color,#2080f0)]',
    'text-disabled': 'text-[var(--n-text-color-disabled,#999)]',
    // Layout shortcuts
    'flex-center': 'flex items-center justify-center',
    'flex-between': 'flex items-center justify-between',
    'flex-col-center': 'flex flex-col items-center justify-center',
  },

  safelist: [],

  content: {
    filesystem: ['./src/**/*.{vue,ts,tsx}'],
  },
});
