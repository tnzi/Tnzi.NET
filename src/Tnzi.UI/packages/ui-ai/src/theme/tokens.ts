/**
 * AI theme token overrides.
 *
 * `packages/ui-ai/src/styles/index.css` is the single source of truth for the
 * default palette: it declares every `--tnzi-ai-*` variable for light mode on
 * `:root` and the dark counterparts under `.dark` / `[data-theme='dark']`.
 * Switching between light and dark is therefore a class toggle, not a token
 * copy, and this module never duplicates that palette.
 *
 * What this module provides is the *override* path: a typed map from friendly
 * key names to the real CSS variables, so an application can recolour the AI
 * surface at runtime without hand-writing variable names.
 *
 * ```ts
 * import { applyAiTheme, resetAiTheme } from '@tnzi/ui-ai/theme'
 *
 * applyAiTheme({ accent: '#7c3aed', codeBg: '#101014' })
 * resetAiTheme() // drop the overrides, fall back to the stylesheet
 * ```
 *
 * Values are full CSS values (`#7c3aed`, `hsl(262 83% 58%)`, `rgb(0 0 0 / 8%)`,
 * a font stack, a length), not bare HSL triplets.
 */

/**
 * Every `--tnzi-ai-*` variable that is safe to override, keyed by a friendly
 * name. All keys are optional: pass only what you want to change.
 */
export interface AiThemeTokens {
  // -- Surfaces ------------------------------------------------------------
  /** `--tnzi-ai-bg` main content canvas. */
  readonly bg?: string;
  /** `--tnzi-ai-sidebar-bg` sidebar canvas. */
  readonly sidebarBg?: string;
  /** `--tnzi-ai-surface` elevated cards / composer / modals. */
  readonly surface?: string;
  /** `--tnzi-ai-rail` collapsed sidebar rail. */
  readonly rail?: string;

  // -- Text ----------------------------------------------------------------
  /** `--tnzi-ai-text` primary text. */
  readonly text?: string;
  /** `--tnzi-ai-text-secondary` secondary text. */
  readonly textSecondary?: string;
  /** `--tnzi-ai-text-tertiary` tertiary / placeholder text. */
  readonly textTertiary?: string;

  // -- Lines ---------------------------------------------------------------
  /** `--tnzi-ai-border` hairline borders. */
  readonly border?: string;
  /** `--tnzi-ai-border-strong` emphasised borders (composer outline). */
  readonly borderStrong?: string;
  /** `--tnzi-ai-divider` section dividers. */
  readonly divider?: string;

  // -- Accent --------------------------------------------------------------
  /** `--tnzi-ai-accent` brand accent (send button, focus ring). */
  readonly accent?: string;
  /** `--tnzi-ai-accent-soft` tinted accent background. */
  readonly accentSoft?: string;
  /** `--tnzi-ai-accent-glow` accent shadow colour. */
  readonly accentGlow?: string;
  /** `--tnzi-ai-accent-contrast` high-contrast neutral fill for a primary
   *  action that must out-rank the brand accent around it. */
  readonly accentContrast?: string;
  /** `--tnzi-ai-on-accent-contrast` foreground on `accentContrast`. */
  readonly onAccentContrast?: string;

  // -- Status --------------------------------------------------------------
  /** `--tnzi-ai-success` */
  readonly success?: string;
  /** `--tnzi-ai-warning` */
  readonly warning?: string;
  /** `--tnzi-ai-danger` */
  readonly danger?: string;
  /** `--tnzi-ai-danger-soft` hover wash behind a destructive row. */
  readonly dangerSoft?: string;

  // -- Interaction overlays ------------------------------------------------
  /** `--tnzi-ai-hover` */
  readonly hover?: string;
  /** `--tnzi-ai-press` */
  readonly press?: string;
  /** `--tnzi-ai-selected` */
  readonly selected?: string;

  // -- Conversation --------------------------------------------------------
  /** `--tnzi-ai-chat-user-bg` user message bubble background. */
  readonly userBubble?: string;
  /** `--tnzi-ai-chat-assistant-bg` assistant message bubble background. */
  readonly assistantBubble?: string;
  /** `--tnzi-ai-reasoning-bg` reasoning / thinking block background. */
  readonly reasoningBg?: string;
  /** `--tnzi-ai-tool-call-bg` tool call block background. */
  readonly toolCallBg?: string;
  /** `--tnzi-ai-streaming-cursor` streaming caret colour. */
  readonly streamingCursor?: string;
  /** `--tnzi-ai-code-bg` code block background. */
  readonly codeBg?: string;

  // -- Workflow ------------------------------------------------------------
  /** `--tnzi-ai-node-active` running workflow node. */
  readonly nodeActive?: string;
  /** `--tnzi-ai-node-completed` completed workflow node. */
  readonly nodeCompleted?: string;
  /** `--tnzi-ai-node-failed` failed workflow node. */
  readonly nodeFailed?: string;
  /** `--tnzi-ai-handoff-accent` agent handoff accent. */
  readonly handoffAccent?: string;

  // -- Typography / metrics ------------------------------------------------
  /** `--tnzi-ai-font-display` serif display face. */
  readonly fontDisplay?: string;
  /** `--tnzi-ai-font-body` body face. */
  readonly fontBody?: string;
  /** `--tnzi-ai-font-mono` monospace face. */
  readonly fontMono?: string;
  /** `--tnzi-ai-content-width` conversation column max width (CSS length). */
  readonly contentWidth?: string;

  // -- Shape / motion ------------------------------------------------------
  // Structural rather than chromatic: these are the "interface detail" knobs an
  // operator reaches for after the colours are settled. They were declared in
  // the stylesheet from the start but were missing from this map, so the only
  // way to reach them was `applyThemeVars` with a hand-written variable name -
  // which is exactly the raw-name API this typed map exists to avoid.
  /** `--tnzi-ai-modal-radius` corner radius of modal surfaces (CSS length). */
  readonly modalRadius?: string;
  /** `--tnzi-ai-composer-radius` corner radius of the composer (CSS length). */
  readonly composerRadius?: string;
  /** `--tnzi-ai-composer-shadow` composer elevation (full box-shadow value). */
  readonly composerShadow?: string;
  /** `--tnzi-ai-backdrop-blur` modal backdrop blur radius (CSS length). */
  readonly backdropBlur?: string;
  /** `--tnzi-ai-scrollbar-size` scrollbar thickness (CSS length). */
  readonly scrollbarSize?: string;
  /** `--tnzi-ai-duration-fast` micro-interaction duration (CSS time). */
  readonly durationFast?: string;
  /** `--tnzi-ai-duration-base` default transition duration (CSS time). */
  readonly durationBase?: string;
  /** `--tnzi-ai-duration-slow` large-surface transition duration (CSS time). */
  readonly durationSlow?: string;
  /** `--tnzi-ai-easing` shared easing curve. */
  readonly easing?: string;
}

/**
 * Friendly key to real CSS variable. These names must stay in lockstep with
 * `src/styles/index.css`; anything not listed here is not overridable through
 * the typed API (use `applyThemeVars` for one-off raw names).
 */
/**
 * Typed keys to the CSS variables they write.
 *
 * Exported for the conformance test that checks every entry names a variable
 * `styles/index.css` actually declares. Deliberately NOT re-exported from
 * `theme/index.ts`: it is an internal table, not public API.
 */
export const TOKEN_TO_VAR: Record<keyof AiThemeTokens, string> = {
  bg: '--tnzi-ai-bg',
  sidebarBg: '--tnzi-ai-sidebar-bg',
  surface: '--tnzi-ai-surface',
  rail: '--tnzi-ai-rail',

  text: '--tnzi-ai-text',
  textSecondary: '--tnzi-ai-text-secondary',
  textTertiary: '--tnzi-ai-text-tertiary',

  border: '--tnzi-ai-border',
  borderStrong: '--tnzi-ai-border-strong',
  divider: '--tnzi-ai-divider',

  accent: '--tnzi-ai-accent',
  accentSoft: '--tnzi-ai-accent-soft',
  accentGlow: '--tnzi-ai-accent-glow',

  success: '--tnzi-ai-success',
  warning: '--tnzi-ai-warning',
  danger: '--tnzi-ai-danger',
  dangerSoft: '--tnzi-ai-danger-soft',
  accentContrast: '--tnzi-ai-accent-contrast',
  onAccentContrast: '--tnzi-ai-on-accent-contrast',

  hover: '--tnzi-ai-hover',
  press: '--tnzi-ai-press',
  selected: '--tnzi-ai-selected',

  userBubble: '--tnzi-ai-chat-user-bg',
  assistantBubble: '--tnzi-ai-chat-assistant-bg',
  reasoningBg: '--tnzi-ai-reasoning-bg',
  toolCallBg: '--tnzi-ai-tool-call-bg',
  streamingCursor: '--tnzi-ai-streaming-cursor',
  codeBg: '--tnzi-ai-code-bg',

  nodeActive: '--tnzi-ai-node-active',
  nodeCompleted: '--tnzi-ai-node-completed',
  nodeFailed: '--tnzi-ai-node-failed',
  handoffAccent: '--tnzi-ai-handoff-accent',

  fontDisplay: '--tnzi-ai-font-display',
  fontBody: '--tnzi-ai-font-body',
  fontMono: '--tnzi-ai-font-mono',
  contentWidth: '--tnzi-ai-content-width',

  modalRadius: '--tnzi-ai-modal-radius',
  composerRadius: '--tnzi-ai-composer-radius',
  composerShadow: '--tnzi-ai-composer-shadow',
  backdropBlur: '--tnzi-ai-backdrop-blur',
  scrollbarSize: '--tnzi-ai-scrollbar-size',
  durationFast: '--tnzi-ai-duration-fast',
  durationBase: '--tnzi-ai-duration-base',
  durationSlow: '--tnzi-ai-duration-slow',
  easing: '--tnzi-ai-easing',
};

/** Prefix owned by this package. `resetAiTheme` only removes these. */
const VAR_PREFIX = '--tnzi-ai-';

function resolveTarget(target?: HTMLElement): HTMLElement | null {
  if (target) return target;
  if (typeof document === 'undefined') return null;
  return document.documentElement;
}

/**
 * Write AI theme overrides as inline CSS variables on `target`
 * (`document.documentElement` by default).
 *
 * Only the keys present in `tokens` are written, so successive calls compose.
 * Inline styles beat the stylesheet, including the `.dark` block: an override
 * applied here stays in force across light/dark switches until
 * {@link resetAiTheme} removes it.
 */
export function applyAiTheme(tokens: AiThemeTokens, target?: HTMLElement): void {
  const el = resolveTarget(target);
  if (!el) return;
  for (const [key, value] of Object.entries(tokens)) {
    if (value == null) continue;
    const varName = TOKEN_TO_VAR[key as keyof AiThemeTokens];
    if (varName) el.style.setProperty(varName, value);
  }
}

/**
 * Write raw `--tnzi-ai-*` variables, for tokens the typed map does not cover.
 * Names may be given with or without the leading `--`.
 *
 * @example applyThemeVars({ 'tnzi-ai-modal-radius': '12px' })
 */
export function applyThemeVars(
  overrides: Readonly<Record<string, string | null | undefined>>,
  target?: HTMLElement,
): void {
  const el = resolveTarget(target);
  if (!el) return;
  for (const [key, value] of Object.entries(overrides)) {
    if (value == null) continue;
    el.style.setProperty(key.startsWith('--') ? key : `--${key}`, value);
  }
}

/**
 * Remove every inline `--tnzi-ai-*` override previously written by
 * {@link applyAiTheme} / {@link applyThemeVars}, restoring the values that the
 * package stylesheet provides.
 */
export function resetAiTheme(target?: HTMLElement): void {
  const el = resolveTarget(target);
  if (!el) return;
  const style = el.style;
  for (let i = style.length - 1; i >= 0; i--) {
    const prop = style[i];
    if (prop?.startsWith(VAR_PREFIX)) {
      style.removeProperty(prop);
    }
  }
}
