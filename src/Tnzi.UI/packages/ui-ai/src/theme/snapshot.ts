/**
 * Global theme snapshots for the AI surface.
 *
 * ## What a snapshot is
 *
 * A serialisable record of every token an operator is allowed to change, plus
 * the light/dark preference. The backend stores it as an opaque JSON document
 * under a `scope` (`'chat'` here) and never interprets it - so the schema below
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
  /** Light / dark / follow-system default for the product. */
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
 * Apply a snapshot to the document, or clear every override when given null.
 *
 * Resets first on purpose: a snapshot is the complete set of deviations, so a
 * token the operator removed must stop being overridden. Without the reset,
 * removing a key would appear to do nothing until a full reload.
 */
export function applyAiThemeSnapshot(
  snapshot: AiThemeSnapshot | null,
  target: HTMLElement | null = typeof document === 'undefined' ? null : document.documentElement,
): void {
  if (!target) return;

  resetAiTheme(target);
  clearOwnedSharedVars(target);

  if (!snapshot) return;

  if (snapshot.ui?.primary) writeSharedVar(target, '--tnzi-primary', snapshot.ui.primary);
  if (snapshot.ai) applyAiTheme(snapshot.ai, target);
}
