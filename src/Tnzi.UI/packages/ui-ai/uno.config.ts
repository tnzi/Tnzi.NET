import { defineConfig, presetWind4, presetIcons, type Preset } from 'unocss';

/**
 * Inline copy of @tnzi/ui's presetTnzi - exposes Tnzi CSS variables as
 * atomic utility classes (bg-primary, text-tnzi-base, border-tnzi, ...).
 * Mirrored here rather than imported from `@tnzi/ui/theme` because @tnzi/ui's
 * dist build does not emit a `theme` entry; wiring that up would require
 * cross-package changes outside the scope of this refactor. Keep in sync
 * with packages/ui/src/theme/uno-preset.ts.
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
    theme: { colors },
    rules: [
      ['bg-tnzi-container', { 'background-color': 'var(--tnzi-container-bg)' }],
      ['bg-tnzi-layout', { 'background-color': 'var(--tnzi-layout-bg)' }],
      ['text-tnzi-base', { color: 'var(--tnzi-base-text)' }],
      ['text-tnzi-muted', { color: 'var(--tnzi-base-text-muted)' }],
      ['border-tnzi', { 'border-color': 'var(--tnzi-border)' }],
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
 * Replaces the previous Tailwind CSS + shadcn color architecture. The AI
 * package now consumes the same UnoCSS ecosystem that @tnzi/ui provides -
 * presetWind4 for generic utilities, presetTnzi for Tnzi brand tokens, plus
 * a local theme layer that maps the legacy shadcn class names (text-foreground,
 * bg-accent, etc.) and AI-specific semantic colors onto --tnzi-* CSS variables.
 *
 * Migration note: this avoids per-file template rewrites. Shadcn utility classes
 * continue to render the same as before, but they now resolve to Tnzi brand
 * tokens defined in @tnzi/ui/style.css + src/styles/index.css.
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
      // Shadcn semantic color compat - maps legacy classes to --tnzi-* tokens.
      // presetTnzi already provides `primary`, `info`, `success`, `warning`, `error`.
      border: 'var(--tnzi-border)',
      input: 'var(--tnzi-border)',
      ring: 'var(--tnzi-primary)',
      background: 'var(--tnzi-container-bg)',
      foreground: 'var(--tnzi-base-text)',
      muted: {
        DEFAULT: 'var(--tnzi-layout-bg)',
        foreground: 'var(--tnzi-base-text-muted)',
      },
      accent: {
        DEFAULT: 'var(--tnzi-layout-bg)',
        foreground: 'var(--tnzi-base-text)',
      },
      card: {
        DEFAULT: 'var(--tnzi-container-bg)',
        foreground: 'var(--tnzi-base-text)',
      },
      popover: {
        DEFAULT: 'var(--tnzi-container-bg)',
        foreground: 'var(--tnzi-base-text)',
      },
      destructive: {
        DEFAULT: 'var(--tnzi-error)',
        foreground: '#ffffff',
      },
      // Standalone `primary-foreground` (presetTnzi's primary is the token, foreground is white on-primary).
      'primary-foreground': '#ffffff',
      'secondary-foreground': 'var(--tnzi-base-text)',
      // AI semantic colors - bind to --tnzi-ai-* tokens declared in src/styles/index.css.
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
    borderRadius: {
      lg: 'var(--radius, 8px)',
      md: 'calc(var(--radius, 8px) - 2px)',
      sm: 'calc(var(--radius, 8px) - 4px)',
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
