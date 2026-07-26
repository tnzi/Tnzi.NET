import { defineConfig, presetWind4, type Preset } from 'unocss';

/**
 * Vant design-token preset.
 *
 * Vant 4 drives its entire look through `--van-*` CSS variables and flips them
 * under `.van-theme-dark`. Exposing those variables as UnoCSS theme colors means
 * every utility this package uses stays theme-aware: no hardcoded `#fff` /
 * `text-slate-500` that would render black-on-black in dark mode.
 *
 * Usage inside this package: `text-van-muted`, `bg-van-surface`,
 * `border-van-line`, `text-van-primary`, ...
 */
function presetVant(): Preset {
  return {
    name: '@tnzi/mobile/preset-vant',
    theme: {
      colors: {
        van: {
          text: 'var(--van-text-color)',
          muted: 'var(--van-text-color-2)',
          subtle: 'var(--van-text-color-3)',
          primary: 'var(--van-primary-color)',
          success: 'var(--van-success-color)',
          warning: 'var(--van-warning-color)',
          danger: 'var(--van-danger-color)',
          surface: 'var(--van-background-2)',
          page: 'var(--van-background)',
          line: 'var(--van-border-color)',
          active: 'var(--van-active-color)',
        },
      },
    },
  };
}

/**
 * UnoCSS configuration for @tnzi/mobile.
 *
 * Why this exists: the components in this package were authored with atomic
 * classes (`px-4`, `grid-cols-2`, `flex items-center`, ...) but the package had
 * no atomic CSS engine, so none of those classes ever produced a rule and the
 * layouts silently collapsed. UnoCSS now generates them into `dist/style.css`,
 * which is the same stylesheet consumers already import.
 *
 * `preflights.reset` is deliberately off: this is a component library layered on
 * top of Vant, and shipping a Tailwind-style global reset inside `style.css`
 * would rewrite element defaults in every consuming app.
 */
export default defineConfig({
  presets: [
    presetWind4({
      preflights: { reset: false },
    }),
    presetVant(),
  ],

  shortcuts: {
    'flex-center': 'flex items-center justify-center',
    'flex-between': 'flex items-center justify-between',
  },

  safelist: [],

  content: {
    filesystem: ['./src/**/*.{vue,ts,tsx}'],
  },
});
