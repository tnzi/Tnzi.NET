/**
 * `@tnzi/ui-ai/theme`
 *
 * The default light and dark palettes ship as CSS in `@tnzi/ui-ai/style.css`;
 * dark mode activates from a `.dark` (or `[data-theme="dark"]`) class on the
 * document element. This module only covers runtime *overrides* on top of it.
 */
export {
  type AiThemeTokens,
  applyAiTheme,
  applyThemeVars,
  resetAiTheme,
} from './tokens';

export {
  AI_THEME_SNAPSHOT_VERSION,
  applyAiThemeSnapshot,
  buildAiThemeSnapshot,
  isValidAiThemeSnapshot,
  type AiThemeSnapshot,
  type AiThemeSnapshotV1,
  type BuildAiThemeSnapshotInput,
} from './snapshot';

import { applyThemeVars, resetAiTheme } from './tokens';

/**
 * @deprecated Renamed to `applyThemeVars`. Kept as an alias so existing
 * imports keep working; it now writes the `--tnzi-ai-*` variables the package
 * actually reads instead of the never-consumed `--ai-*` ones.
 */
export const applyTheme = applyThemeVars;

/**
 * @deprecated Renamed to `resetAiTheme`. Kept as an alias so existing imports
 * keep working; it now clears `--tnzi-ai-*` instead of `--ai-*`.
 */
export const resetTheme = resetAiTheme;
