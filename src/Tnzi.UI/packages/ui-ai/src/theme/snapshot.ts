/**
 * Global theme snapshots for the AI surface.
 *
 * ## What a snapshot is
 *
 * A serialisable record of every token an operator is allowed to change (and a
 * reserved light/dark field, see `mode`). The backend stores it as an opaque
 * JSON document under a `scope` (`'chat'` here) and never interprets it - so the schema below
 * is owned entirely by this package. See `Tnzi.System`'s `IAppearanceService`.
 *
 * ## Why the envelope has two halves
 *
 * `ui` carries the tokens that belong to `@tnzi/ui` and are therefore shared
 * with any other product on the same deployment - today that is the brand
 * colour, which this package's accent already derives from. `ai` carries this
 * package's own tokens. The split is what lets a super admin set the brand once
 * and have it reach both the admin console and the chat app, while the warm
 * chat palette stays this product's own business.
 *
 * ## What it is NOT
 *
 * Not a second copy of the palette. Defaults live in `styles/index.css`; a
 * snapshot only records DEVIATIONS an operator made. An absent key means "use
 * the stylesheet", which is why `applySnapshot` resets before applying: dropping
 * a key from a saved snapshot has to actually drop the override, otherwise
 * un-setting something would be impossible without a hard reload.
 */
import { buildCssVars, defaultThemeSettings } from '@tnzi/ui/theme';
import type { ThemeContext } from '@tnzi/ui';
import { applyAiTheme, resetAiTheme, type AiThemeTokens } from './tokens';

/** Snapshot format version. Bump only on a breaking shape change. */
export const AI_THEME_SNAPSHOT_VERSION = 1;

export interface AiThemeSnapshotV1 {
  version: 1;
  /** ISO timestamp, set when the snapshot is built. */
  exportedAt: string;
  /** Tokens shared with the rest of the deployment. */
  ui: {
    /** `--tnzi-primary`. The AI accent derives from it. */
    primary?: string;
  };
  /** This package's own tokens. Absent keys fall back to the stylesheet. */
  ai: AiThemeTokens;
  /**
   * Reserved for a light / dark / follow-system default. Not applied by
   * `applyAiThemeSnapshot` and not editable in `TAppearanceAdmin`, which only
   * keeps a stored value across a save. The chat's mode comes from
   * `TChatApp`'s `theme` prop and the viewer's own choice.
   */
  mode?: 'light' | 'dark' | 'auto';
}

export type AiThemeSnapshot = AiThemeSnapshotV1;

/** Structural check - a snapshot arrives as opaque JSON from the server. */
export function isValidAiThemeSnapshot(value: unknown): value is AiThemeSnapshot {
  if (!value || typeof value !== 'object') return false;
  const snapshot = value as Partial<AiThemeSnapshotV1>;
  if (snapshot.version !== AI_THEME_SNAPSHOT_VERSION) return false;
  if (snapshot.ai !== undefined && typeof snapshot.ai !== 'object') return false;
  if (snapshot.ui !== undefined && typeof snapshot.ui !== 'object') return false;
  return true;
}

export interface BuildAiThemeSnapshotInput {
  ai?: AiThemeTokens;
  primary?: string;
  mode?: AiThemeSnapshotV1['mode'];
  /** Injectable for tests; defaults to now. */
  now?: () => Date;
}

export function buildAiThemeSnapshot(input: BuildAiThemeSnapshotInput = {}): AiThemeSnapshot {
  const now = input.now ? input.now() : new Date();
  return {
    version: AI_THEME_SNAPSHOT_VERSION,
    exportedAt: now.toISOString(),
    ui: input.primary ? { primary: input.primary } : {},
    ai: { ...(input.ai ?? {}) },
    ...(input.mode ? { mode: input.mode } : {}),
  };
}

/**
 * Which variables OUTSIDE this package's `--tnzi-ai-*` namespace this module has
 * written on a given element.
 *
 * ★ Reset must only clear its OWN writes. `--tnzi-primary` belongs to
 * `@tnzi/ui`, whose theme system writes it as inline style on `<html>` at mount
 * (`injectCssVars`, called by `createTnziUi()`). Clearing it unconditionally -
 * which an earlier version of this function did - wipes the host application's
 * brand colour on every reset, and the symptom is "the brand colour reverted on
 * its own" somewhere far from here.
 */
const OWNED_SHARED_VARS = new WeakMap<HTMLElement, Set<string>>();

function clearOwnedSharedVars(target: HTMLElement): void {
  const owned = OWNED_SHARED_VARS.get(target);
  if (!owned) return;
  for (const name of owned) target.style.removeProperty(name);
  owned.clear();
}

function writeSharedVar(target: HTMLElement, name: string, value: string): void {
  target.style.setProperty(name, value);
  let owned = OWNED_SHARED_VARS.get(target);
  if (!owned) {
    owned = new Set();
    OWNED_SHARED_VARS.set(target, owned);
  }
  owned.add(name);
}

/**
 * `--tnzi-primary` and every variable derived from it: `--tnzi-primary-rgb`
 * (which the accent tints are built from), the palette levels with their
 * `-rgb` twins, and the readable companions (`--tnzi-on-primary`, the
 * foreground on an accent fill, and the `--tnzi-primary-text-on-light` /
 * `-on-dark` pair, the accent as text). Computed by `@tnzi/ui`'s own builder so
 * the values match what its theme context would write for the same colour.
 *
 * Built for light mode, written once: every variable taken here is the same in
 * both modes (the stylesheet picks the text colour per mode), so an inline
 * write that cannot follow a mode switch is still right after one.
 */
function primaryVars(primary: string): Record<string, string> {
  const all = buildCssVars({ ...defaultThemeSettings.colors, primary }, 'light');
  return Object.fromEntries(
    Object.entries(all).filter(
      ([name]) => name === '--tnzi-primary' || name === '--tnzi-on-primary' || name.startsWith('--tnzi-primary-'),
    ),
  );
}

/** The primary each host theme context had before a snapshot overrode it. */
const CONTEXT_BASE_PRIMARY = new WeakMap<ThemeContext, string>();

/**
 * Set (or hand back) the brand colour through the host's theme context.
 *
 * ★ The context owns `--tnzi-primary`: it re-writes the whole variable map on
 * every change to its settings, a light/dark switch included, so an inline
 * write beside it was undone by the next mode switch. Going through
 * `setColor` also moves what an inline write never reached: the palette, the
 * `-rgb` triplets and the naive-ui overrides the controls are painted with.
 */
function applyBrandThroughContext(context: ThemeContext, primary: string | undefined): void {
  const current = context.settings.value.colors.primary;
  if (primary) {
    if (!CONTEXT_BASE_PRIMARY.has(context)) CONTEXT_BASE_PRIMARY.set(context, current);
    if (current !== primary) context.setColor('primary', primary);
    return;
  }
  const base = CONTEXT_BASE_PRIMARY.get(context);
  if (base === undefined) return;
  CONTEXT_BASE_PRIMARY.delete(context);
  if (current !== base) context.setColor('primary', base);
}

/**
 * Apply a snapshot to the document, or clear every override when given null.
 *
 * Resets first on purpose: a snapshot is the complete set of deviations, so a
 * token the operator removed must stop being overridden. Without the reset,
 * removing a key would appear to do nothing until a full reload.
 *
 * @param themeContext the host's `@tnzi/ui` theme context, when there is one
 *        (`defineChatApp()` and `createTnziUi()` provide it). The brand colour
 *        then goes through it; without one the primary variables are written
 *        inline on `target`.
 */
export function applyAiThemeSnapshot(
  snapshot: AiThemeSnapshot | null,
  target: HTMLElement | null = typeof document === 'undefined' ? null : document.documentElement,
  themeContext?: ThemeContext | null,
): void {
  if (!target) return;

  resetAiTheme(target);
  clearOwnedSharedVars(target);

  const primary = snapshot?.ui?.primary || undefined;
  if (themeContext) {
    applyBrandThroughContext(themeContext, primary);
  } else if (primary) {
    for (const [name, value] of Object.entries(primaryVars(primary))) writeSharedVar(target, name, value);
  }

  if (snapshot?.ai) applyAiTheme(snapshot.ai, target);
}
