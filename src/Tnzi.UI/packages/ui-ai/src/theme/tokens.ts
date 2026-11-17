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
 * Values are full CSS values (`#7c3aed`, `rgb(0 0 0 / 8%)`, a `color-mix(...)`
 * expression, a font stack, a length) - never a bare HSL triplet. That caveat
 * is aimed at one specific habit: shadcn stores `262.1 83.3% 57.8%` and wraps
 * it in `hsl(...)` at each use site, so a value copied from a shadcn theme
 * would be handed to `setProperty` here and silently dropped as invalid.
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
  /** `--tnzi-ai-sidebar-heading` sidebar group headings (Recents, titled nav groups). */
  readonly sidebarHeading?: string;
  /** `--tnzi-ai-sidebar-hover` hover fill of every sidebar row and button (and the open conversation's fill). */
  readonly sidebarHover?: string;
  /** `--tnzi-ai-sidebar-selected` fill of the selected nav entry. */
  readonly sidebarSelected?: string;

  // -- Lines ---------------------------------------------------------------
  /** `--tnzi-ai-border` hairline borders. */
  readonly border?: string;
  /** `--tnzi-ai-border-strong` emphasised borders (composer outline). */
  readonly borderStrong?: string;
  /** `--tnzi-ai-divider` section dividers. */
  readonly divider?: string;

  // -- Accent --------------------------------------------------------------
  /**
   * `--tnzi-ai-accent` brand accent fill (send button, focus ring).
   *
   * This overrides the fill only. `onAccent` and `accentText` keep following
   * the `@tnzi/ui` primary, so set them alongside it - or, better, recolour the
   * brand through the theme context (`setColor('primary', ...)`, or a snapshot's
   * `ui.primary`), which recomputes both for whatever colour it is.
   */
  readonly accent?: string;
  /** `--tnzi-ai-accent-soft` tinted accent background. */
  readonly accentSoft?: string;
  /** `--tnzi-ai-accent-glow` accent shadow colour. */
  readonly accentGlow?: string;
  /** `--tnzi-ai-on-accent` foreground on the accent fill. */
  readonly onAccent?: string;
  /** `--tnzi-ai-accent-text` the accent as a text / icon colour on the canvas.
   *  An override here is one value for both light and dark mode. */
  readonly accentText?: string;
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
  /** `--tnzi-ai-on-status` foreground on a status fill (failed stage, subtask badges). */
  readonly onStatus?: string;

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
  /** `--tnzi-ai-code-frame-bg` the frame of a fenced code block in a message. */
  readonly codeFrameBg?: string;
  /** `--tnzi-ai-code-inline-text` inline code text in a message. */
  readonly codeInlineText?: string;

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
  /** `--tnzi-ai-brand-mark-glyph-size` size of the glyph in the brand tile, sidebar header and rail (CSS length). */
  readonly brandMarkGlyphSize?: string;
  /** `--tnzi-ai-focus-border` border colour of a focused form control. */
  readonly focusBorder?: string;
  /** `--tnzi-ai-focus-ring-width` width of a focused form control's ring (CSS length). */
  readonly focusRingWidth?: string;
  /** `--tnzi-ai-focus-ring-color` colour of a focused form control's ring. */
  readonly focusRingColor?: string;
  /** `--tnzi-ai-focus-ring` the whole focus ring (full box-shadow value). */
  readonly focusRing?: string;
  /** `--tnzi-ai-focus-border-error` border colour of a focused field in error. */
  readonly focusBorderError?: string;
  /** `--tnzi-ai-focus-ring-error` focus ring of a field in error (full box-shadow value). */
  readonly focusRingError?: string;
  /** `--tnzi-ai-focus-border-warning` border colour of a focused field with a warning. */
  readonly focusBorderWarning?: string;
  /** `--tnzi-ai-focus-ring-warning` focus ring of a field with a warning (full box-shadow value). */
  readonly focusRingWarning?: string;
  /** `--tnzi-ai-control-border-hover` border colour of a form control under the pointer. */
  readonly controlBorderHover?: string;
  /** `--tnzi-ai-modal-radius` corner radius of modal surfaces (CSS length). */
  readonly modalRadius?: string;
  /** `--tnzi-ai-composer-radius` corner radius of the composer (CSS length). */
  readonly composerRadius?: string;
  /** `--tnzi-ai-radius-xs` smallest corner step: check boxes, chips inside a field (CSS length). */
  readonly radiusXs?: string;
  /** `--tnzi-ai-radius-sm` small corner step: tooltips, multi-select chips (CSS length). */
  readonly radiusSm?: string;
  /** `--tnzi-ai-radius-md` the controls' corner: buttons, fields, menu entries (CSS length). */
  readonly radiusMd?: string;
  /** `--tnzi-ai-radius-lg` panel corner: menus, popovers, notices, cards (CSS length). */
  readonly radiusLg?: string;
  /** `--tnzi-ai-radius-pill` fully rounded ends: switches, tags (CSS length). */
  readonly radiusPill?: string;
  /** `--tnzi-ai-composer-shadow` composer elevation (full box-shadow value). */
  readonly composerShadow?: string;
  /** `--tnzi-ai-dialog-padding` inset of a dialog's title, body and footer from its edges (CSS length). */
  readonly dialogPadding?: string;
  /** `--tnzi-ai-dialog-padding-narrow` the same inset on a phone (CSS length). */
  readonly dialogPaddingNarrow?: string;
  /** `--tnzi-ai-dialog-close-size` size of the dialog close button (CSS length). */
  readonly dialogCloseSize?: string;
  /** `--tnzi-ai-dialog-close-size-narrow` size of the dialog close button on a phone (CSS length). */
  readonly dialogCloseSizeNarrow?: string;
  /** `--tnzi-ai-dialog-close-inset` distance of the close button from a dialog's top and right edges (CSS length). */
  readonly dialogCloseInset?: string;
  /** `--tnzi-ai-dialog-title-size` dialog title font size (CSS length). */
  readonly dialogTitleSize?: string;
  /** `--tnzi-ai-dialog-shadow` dialog elevation (full box-shadow value). */
  readonly dialogShadow?: string;
  /** `--tnzi-ai-popover-shadow` elevation of menus anchored to a control (full box-shadow value). */
  readonly popoverShadow?: string;
  /** `--tnzi-ai-dialog-max-height` tallest a dialog grows on a wide screen (CSS length). */
  readonly dialogMaxHeight?: string;
  /** `--tnzi-ai-dialog-width-sm` width of a `size="sm"` dialog (CSS length). */
  readonly dialogWidthSm?: string;
  /** `--tnzi-ai-dialog-width-md` width of a `size="md"` dialog, the default (CSS length). */
  readonly dialogWidthMd?: string;
  /** `--tnzi-ai-dialog-width-lg` width of a `size="lg"` dialog (CSS length). */
  readonly dialogWidthLg?: string;
  /** `--tnzi-ai-dialog-width-xl` width of a `size="xl"` dialog (CSS length). */
  readonly dialogWidthXl?: string;
  /** `--tnzi-ai-pending-approvals-max-height` tallest the band of waiting cards above the composer, and any card in it, grows (CSS length). */
  readonly pendingApprovalsMaxHeight?: string;
  /** `--tnzi-ai-z-sticky` layer of bars and buttons raised over their own pane (integer). */
  readonly zSticky?: string;
  /** `--tnzi-ai-z-drawer` layer of the sidebar drawer and embedded chat panels (integer). */
  readonly zDrawer?: string;
  /** `--tnzi-ai-z-dialog` base layer of modal dialogs; each open dialog adds its depth (integer). */
  readonly zDialog?: string;
  /** `--tnzi-ai-z-popover` layer of menus and popovers, above any dialog (integer). */
  readonly zPopover?: string;
  /** `--tnzi-ai-z-toast` layer of transient notices (integer). */
  readonly zToast?: string;
  /** `--tnzi-ai-z-tooltip` layer of tooltips (integer). */
  readonly zTooltip?: string;
  /** `--tnzi-ai-tooltip-bg` fill of a tooltip (`vTooltip`); defaults to the text colour, so it inverts with the mode. */
  readonly tooltipBg?: string;
  /** `--tnzi-ai-tooltip-fg` text of a tooltip; defaults to the surface colour. */
  readonly tooltipFg?: string;
  /** `--tnzi-ai-page-max-width` readable column of a content page and its toolbar (CSS length). */
  readonly pageMaxWidth?: string;
  /** `--tnzi-ai-page-padding-x` gutter each side of a content page (CSS length). */
  readonly pagePaddingX?: string;
  /** `--tnzi-ai-page-padding-x-narrow` the same gutter on a phone (CSS length). */
  readonly pagePaddingXNarrow?: string;
  /** `--tnzi-ai-page-padding-bottom` space under a content page's body (CSS length). */
  readonly pagePaddingBottom?: string;
  /** `--tnzi-ai-toolbar-control-size` height of a page toolbar's controls (CSS length). */
  readonly toolbarControlSize?: string;
  /** `--tnzi-ai-backdrop` colour of the veil behind a modal. */
  readonly backdrop?: string;
  /** `--tnzi-ai-backdrop-blur` modal backdrop blur radius (CSS length). */
  readonly backdropBlur?: string;
  /** `--tnzi-ai-focus-outline` keyboard focus line of a button, row or option (full `outline` value). */
  readonly focusOutline?: string;
  /** `--tnzi-ai-switch-knob` knob of a switch while it is off. */
  readonly switchKnob?: string;
  /** `--tnzi-ai-image-veil` veil over a picture that is a control (`TImagePicker`). */
  readonly imageVeil?: string;
  /** `--tnzi-ai-on-image` icons and spinner drawn on that veil. */
  readonly onImage?: string;
  /** `--tnzi-ai-image-viewer-backdrop` behind a picture opened full size (`TImageViewer`). */
  readonly imageViewerBackdrop?: string;
  /** `--tnzi-ai-wave` mask tile of the wavy hairline (a `url()`). */
  readonly wave?: string;
  /** `--tnzi-ai-scrollbar-size` scrollbar thickness (CSS length). */
  readonly scrollbarSize?: string;
  /** `--tnzi-ai-code-line-height` line height of multi-line code (unitless). */
  readonly codeLineHeight?: string;
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
  sidebarHeading: '--tnzi-ai-sidebar-heading',
  sidebarHover: '--tnzi-ai-sidebar-hover',
  sidebarSelected: '--tnzi-ai-sidebar-selected',

  border: '--tnzi-ai-border',
  borderStrong: '--tnzi-ai-border-strong',
  divider: '--tnzi-ai-divider',

  accent: '--tnzi-ai-accent',
  accentSoft: '--tnzi-ai-accent-soft',
  accentGlow: '--tnzi-ai-accent-glow',
  onAccent: '--tnzi-ai-on-accent',
  accentText: '--tnzi-ai-accent-text',

  success: '--tnzi-ai-success',
  warning: '--tnzi-ai-warning',
  danger: '--tnzi-ai-danger',
  dangerSoft: '--tnzi-ai-danger-soft',
  onStatus: '--tnzi-ai-on-status',
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
  codeFrameBg: '--tnzi-ai-code-frame-bg',
  codeInlineText: '--tnzi-ai-code-inline-text',

  nodeActive: '--tnzi-ai-node-active',
  nodeCompleted: '--tnzi-ai-node-completed',
  nodeFailed: '--tnzi-ai-node-failed',
  handoffAccent: '--tnzi-ai-handoff-accent',

  fontDisplay: '--tnzi-ai-font-display',
  fontBody: '--tnzi-ai-font-body',
  fontMono: '--tnzi-ai-font-mono',
  contentWidth: '--tnzi-ai-content-width',

  brandMarkGlyphSize: '--tnzi-ai-brand-mark-glyph-size',
  focusBorder: '--tnzi-ai-focus-border',
  focusRingWidth: '--tnzi-ai-focus-ring-width',
  focusRingColor: '--tnzi-ai-focus-ring-color',
  focusRing: '--tnzi-ai-focus-ring',
  focusBorderError: '--tnzi-ai-focus-border-error',
  focusRingError: '--tnzi-ai-focus-ring-error',
  focusBorderWarning: '--tnzi-ai-focus-border-warning',
  focusRingWarning: '--tnzi-ai-focus-ring-warning',
  controlBorderHover: '--tnzi-ai-control-border-hover',
  modalRadius: '--tnzi-ai-modal-radius',
  composerRadius: '--tnzi-ai-composer-radius',
  radiusXs: '--tnzi-ai-radius-xs',
  radiusSm: '--tnzi-ai-radius-sm',
  radiusMd: '--tnzi-ai-radius-md',
  radiusLg: '--tnzi-ai-radius-lg',
  radiusPill: '--tnzi-ai-radius-pill',
  composerShadow: '--tnzi-ai-composer-shadow',
  dialogPadding: '--tnzi-ai-dialog-padding',
  dialogPaddingNarrow: '--tnzi-ai-dialog-padding-narrow',
  dialogCloseSize: '--tnzi-ai-dialog-close-size',
  dialogCloseSizeNarrow: '--tnzi-ai-dialog-close-size-narrow',
  dialogCloseInset: '--tnzi-ai-dialog-close-inset',
  dialogTitleSize: '--tnzi-ai-dialog-title-size',
  dialogShadow: '--tnzi-ai-dialog-shadow',
  popoverShadow: '--tnzi-ai-popover-shadow',
  dialogMaxHeight: '--tnzi-ai-dialog-max-height',
  dialogWidthSm: '--tnzi-ai-dialog-width-sm',
  dialogWidthMd: '--tnzi-ai-dialog-width-md',
  dialogWidthLg: '--tnzi-ai-dialog-width-lg',
  dialogWidthXl: '--tnzi-ai-dialog-width-xl',
  pendingApprovalsMaxHeight: '--tnzi-ai-pending-approvals-max-height',
  zSticky: '--tnzi-ai-z-sticky',
  zDrawer: '--tnzi-ai-z-drawer',
  zDialog: '--tnzi-ai-z-dialog',
  zPopover: '--tnzi-ai-z-popover',
  zToast: '--tnzi-ai-z-toast',
  zTooltip: '--tnzi-ai-z-tooltip',
  tooltipBg: '--tnzi-ai-tooltip-bg',
  tooltipFg: '--tnzi-ai-tooltip-fg',
  pageMaxWidth: '--tnzi-ai-page-max-width',
  pagePaddingX: '--tnzi-ai-page-padding-x',
  pagePaddingXNarrow: '--tnzi-ai-page-padding-x-narrow',
  pagePaddingBottom: '--tnzi-ai-page-padding-bottom',
  toolbarControlSize: '--tnzi-ai-toolbar-control-size',
  backdrop: '--tnzi-ai-backdrop',
  backdropBlur: '--tnzi-ai-backdrop-blur',
  focusOutline: '--tnzi-ai-focus-outline',
  switchKnob: '--tnzi-ai-switch-knob',
  imageVeil: '--tnzi-ai-image-veil',
  imageViewerBackdrop: '--tnzi-ai-image-viewer-backdrop',
  onImage: '--tnzi-ai-on-image',
  wave: '--tnzi-ai-wave',
  scrollbarSize: '--tnzi-ai-scrollbar-size',
  codeLineHeight: '--tnzi-ai-code-line-height',
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
