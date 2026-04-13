import type { ThemeColors, ThemeSettings } from './types'
import { getColorPalette } from './palette'

const defaultColors: ThemeColors = {
  primary: '#18a058',
  info: '#2080f0',
  success: '#18a058',
  warning: '#f0a020',
  error: '#d03050',
}

/**
 * The single source-of-truth default theme settings.
 * Consumers can override by calling `createTnziUi({ theme: { ... } })`.
 */
export const defaultThemeSettings: ThemeSettings = {
  colors: defaultColors,
  palettes: {
    primary: getColorPalette(defaultColors.primary),
    info: getColorPalette(defaultColors.info),
    success: getColorPalette(defaultColors.success),
    warning: getColorPalette(defaultColors.warning),
    error: getColorPalette(defaultColors.error),
  },
  mode: 'light',
  isInfoFollowPrimary: false,
  recommendColor: false,
  presetName: 'default',
}

/**
 * Input type for {@link mergeThemeSettings}. Narrower than `Partial<ThemeSettings>`
 * because `colors` may itself be partial — missing roles inherit from defaults.
 */
export interface MergeThemeSettingsInput extends Omit<Partial<ThemeSettings>, 'colors' | 'palettes'> {
  /** Optional partial override of base colors — missing roles inherit from defaults. */
  colors?: Partial<ThemeColors>
}

/**
 * Merge a partial settings object with defaults.
 * Regenerates palettes if colors change.
 */
export function mergeThemeSettings(
  partial: MergeThemeSettingsInput,
): ThemeSettings {
  const colors = { ...defaultThemeSettings.colors, ...(partial.colors ?? {}) }
  return {
    colors,
    palettes: {
      primary: getColorPalette(colors.primary),
      info: getColorPalette(colors.info),
      success: getColorPalette(colors.success),
      warning: getColorPalette(colors.warning),
      error: getColorPalette(colors.error),
    },
    mode: partial.mode ?? defaultThemeSettings.mode,
    isInfoFollowPrimary: partial.isInfoFollowPrimary ?? defaultThemeSettings.isInfoFollowPrimary,
    recommendColor: partial.recommendColor ?? defaultThemeSettings.recommendColor,
    naiveOverrides: partial.naiveOverrides,
    presetName: partial.presetName ?? defaultThemeSettings.presetName,
  }
}
