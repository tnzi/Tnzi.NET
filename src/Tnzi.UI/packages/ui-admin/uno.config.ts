import { defineConfig, presetWind4, presetIcons } from 'unocss'

/**
 * UnoCSS configuration for @tnzi/ui-admin.
 *
 * Shares the atomic vocabulary of `@tnzi/ui/uno.config.ts`. Internal components
 * prefer numeric scales (`text-4`, `gap-12px`) and the design-token shortcuts
 * below; consumer apps that include this package's `dist/style.css`
 * automatically receive the compiled atoms.
 *
 * Soybean-style flex shortcuts (`flex-y-center`, `flex-center`,
 * `flex-col-center`, `flex-between`) match the soybean-admin source we
 * pixel-replicate in Phase I.7+.
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
    // Layout - matches soybean's source 1:1
    'flex-center': 'flex items-center justify-center',
    'flex-y-center': 'flex items-center',
    'flex-x-center': 'flex justify-center',
    'flex-between': 'flex items-center justify-between',
    'flex-col-center': 'flex flex-col items-center justify-center',
    'i-flex-col': 'inline-flex flex-col',

    // Tnzi admin design tokens (use `var(--tnzi-…)` CSS variables seeded
    // by `@tnzi/ui` theme system + ui-admin's own layout tokens).
    'text-primary': 'text-[var(--tnzi-primary,#646cff)]',
    'text-muted': 'text-[var(--tnzi-base-text-muted,#888)]',
    // Semantic status colors mapped to base theme tokens (theme-aware,
    // mirrors @tnzi/ui). Fallbacks match Naive UI's default status palette
    // so atoms still paint if a token is missing.
    'text-success': 'text-[var(--tnzi-success,#18a058)]',
    'text-error': 'text-[var(--tnzi-error,#d03050)]',
    'text-warning': 'text-[var(--tnzi-warning,#f0a020)]',
    'text-info': 'text-[var(--tnzi-info,#2080f0)]',
    'border-tnzi': 'border border-[var(--tnzi-border,#e5e7eb)]',
    'bg-layout': 'bg-[var(--tnzi-layout-bg,#f5f7fa)]',
    'bg-card': 'bg-[var(--tnzi-container-bg,#fff)]',
    'text-disabled': 'text-[var(--n-text-color-disabled,#999)]',

    // Naive UI design tokens: the variables naive sets on its own components,
    // for markup drawn inside one of them.
    'border-naive': 'border border-[var(--n-border-color,#e0e0e6)]',
    'bg-naive-hover': 'bg-[var(--n-color-hover,rgba(0,0,0,0.03))]',
    'bg-naive-card': 'bg-[var(--n-card-color,#fff)]',
    'text-naive-secondary': 'text-[var(--n-text-color-3,#999)]',
    'text-naive-primary': 'text-[var(--n-text-color-1,#333)]',
  },

  safelist: [],

  // presetWind4 mis-compiles `font-[family-name:var(--tnzi-font-mono)]` into an
  // invalid `font-family:family-name:var(...)` - it does NOT strip Tailwind's
  // `family-name:` data-type hint - which corrupts the whole compiled
  // dist/style.css under a strict postcss parser (consumer dev server
  // white-screens on the very first transform). Pages use the plain `tnzi-mono`
  // class instead; provide it as raw CSS here so the token semantics survive.
  preflights: [
    {
      getCSS: () => '.tnzi-mono{font-family:var(--tnzi-font-mono)}',
    },
  ],

  content: {
    filesystem: ['./src/**/*.{vue,ts,tsx}'],
  },
})
