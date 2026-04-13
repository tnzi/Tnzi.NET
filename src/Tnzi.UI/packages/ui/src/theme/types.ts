/**
 * Theme type definitions shared across the theme subsystem.
 */
import type { GlobalThemeOverrides } from 'naive-ui'

/** The 11 palette levels used for color ramps. 500 is the base color. */
export type PaletteLevel = 50 | 100 | 200 | 300 | 400 | 500 | 600 | 700 | 800 | 900 | 950

/** The 5 semantic color roles. */
export type ColorRole = 'primary' | 'info' | 'success' | 'warning' | 'error'

/** A full 11-level palette for one color role. */
export type ColorPalette = Record<PaletteLevel, string>

/** The raw 5 base colors chosen by the user/consumer. */
export interface ThemeColors {
  primary: string
  info: string
  success: string
  warning: string
  error: string
}

/** A complete theme settings object, including all palettes. */
export interface ThemeSettings {
  /** Base 5 colors (the "seed" the palette is generated from). */
  colors: ThemeColors
  /** Generated 11-level palettes for each role. */
  palettes: Record<ColorRole, ColorPalette>
  /** Theme mode: 'light' | 'dark' | 'auto'. */
  mode: 'light' | 'dark' | 'auto'
  /** Whether info color should follow primary. */
  isInfoFollowPrimary: boolean
  /** Whether the user's chosen color should be recommended to the nearest soybean-style palette. */
  recommendColor: boolean
  /** Naive UI overrides (raw, consumer-provided). Merged over the generated base by `buildNaiveThemeOverrides`. */
  naiveOverrides?: GlobalThemeOverrides
  /** The name of the preset this theme is derived from, if any. */
  presetName?: 'default' | 'dark' | 'compact' | 'azir' | string
}

/** CSS variable key → value map. */
export type CssVarMap = Record<string, string>
