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
 *   `--tnzi-*` theme variables and arbitrary values (`text-[13px]`, `rounded-[3px]`).
 *   No shortcut here reads a component library's variables (`--n-*`): this package
 *   renders with none.
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
