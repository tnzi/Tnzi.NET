import { defineConfig, presetWind4, presetIcons, type Preset } from 'unocss';

/**
 * Inline copy of @tnzi/ui's presetTnzi - exposes Tnzi CSS variables as atomic
 * utility classes (bg-primary, text-tnzi-base, border-tnzi-border, ...).
 * Mirrored here rather than imported from `@tnzi/ui/theme` because that entry
 * is not published: `@tnzi/ui`'s package.json exports only `./theme/presets/*`
 * (the preset JSON files), not the preset factory itself.
 *
 * ONE DELIBERATE DIVERGENCE from packages/ui/src/theme/uno-preset.ts: the
 * functional surface/text/border tokens are declared as THEME COLOURS rather
 * than static `rules`. A static rule cannot take an opacity modifier, and this
 * package needs `bg-tnzi-layout/50` and `border-tnzi-border/50`. At full
 * opacity the two forms produce equivalent declarations, and the class names
 * are unchanged except `border-tnzi` -> `border-tnzi-border` (which also buys
 * `bg-tnzi-border` / `text-tnzi-border` for hairline dividers and SVG tracks).
 * Keep the role colours and shadow rules in sync with that file.
 */
function presetTnzi(): Preset {
  const roles = ['primary', 'info', 'success', 'warning', 'error'] as const;
  const levels = [50, 100, 200, 300, 400, 500, 600, 700, 800, 900, 950] as const;
  const colors: Record<string, string> = {};
  for (const role of roles) {
    colors[role] = `var(--tnzi-${role})`;
    for (const level of levels) {
      colors[`${role}-${level}`] = `var(--tnzi-${role}-${level})`;
    }
  }
  return {
    name: '@tnzi/ui-ai/preset-tnzi',
    theme: {
      colors: {
        ...colors,
        // Functional tokens - see the divergence note above.
        'tnzi-container': 'var(--tnzi-container-bg)',
        'tnzi-layout': 'var(--tnzi-layout-bg)',
        'tnzi-base': 'var(--tnzi-base-text)',
        'tnzi-muted': 'var(--tnzi-base-text-muted)',
        'tnzi-border': 'var(--tnzi-border)',
      },
    },
    rules: [
      ['shadow-tnzi-header', { 'box-shadow': 'var(--tnzi-shadow-header)' }],
      ['shadow-tnzi-sider', { 'box-shadow': 'var(--tnzi-shadow-sider)' }],
      ['shadow-tnzi-tab', { 'box-shadow': 'var(--tnzi-shadow-tab)' }],
      ['shadow-tnzi-card', { 'box-shadow': 'var(--tnzi-shadow-card)' }],
    ],
  };
}

/**
 * UnoCSS configuration for @tnzi/ui-ai.
 *
 * presetWind4 supplies the generic utilities (flex, spacing, type scale),
 * presetTnzi the Tnzi brand + surface tokens, and `theme.colors.ai` this
 * package's own semantic colours, which bind to the `--tnzi-ai-*` variables
 * declared in src/styles/index.css.
 *
 * There is deliberately NO shadcn compatibility layer here. The package was
 * built on Tailwind + shadcn, and when it moved to UnoCSS (2026-04) the shadcn
 * class vocabulary (`text-muted-foreground`, `bg-accent`, `border-border`, ...)
 * was kept alive by mapping those names onto `--tnzi-*` tokens so that the
 * templates would not have to be rewritten. That map is gone as of 2026-08-03
 * and the templates now use the same Tnzi vocabulary as the rest of the
 * ecosystem. Do NOT reintroduce it, and do NOT reintroduce `tailwindcss` or a
 * `postcss.config.js`: a second vocabulary for the same tokens means markup
 * copied out of this package renders unstyled in @tnzi/ui / @tnzi/ui-admin,
 * which have no such mapping and never did.
 */
export default defineConfig({
  presets: [
    presetWind4(),
    presetTnzi(),
    presetIcons({
      scale: 1.2,
      extraProperties: {
        'display': 'inline-block',
        'vertical-align': 'middle',
      },
    }),
  ],

  theme: {
    colors: {
      // AI semantic colours - bind to --tnzi-ai-* tokens declared in
      // src/styles/index.css. Those tokens in turn derive from the Tnzi
      // surface/status palette, so recolouring the brand reaches them too.
      ai: {
        'user-bubble': 'var(--tnzi-ai-chat-user-bg)',
        'assistant-bubble': 'var(--tnzi-ai-chat-assistant-bg)',
        'reasoning-bg': 'var(--tnzi-ai-reasoning-bg)',
        'tool-call-bg': 'var(--tnzi-ai-tool-call-bg)',
        'streaming-cursor': 'var(--tnzi-ai-streaming-cursor)',
        'code-bg': 'var(--tnzi-ai-code-bg)',
        'node-active': 'var(--tnzi-ai-node-active)',
        'node-completed': 'var(--tnzi-ai-node-completed)',
        'node-failed': 'var(--tnzi-ai-node-failed)',
        'handoff-accent': 'var(--tnzi-ai-handoff-accent)',
      },
    },
  },

  shortcuts: {
    // Layout helpers (mirrors @tnzi/ui shortcuts for consistency).
    'flex-center': 'flex items-center justify-center',
    'flex-between': 'flex items-center justify-between',
    'flex-col-center': 'flex flex-col items-center justify-center',
  },

  safelist: [],

  content: {
    filesystem: ['./src/**/*.{vue,ts,tsx}'],
  },
});
