import { colord, extend } from 'colord'
import mixPlugin from 'colord/plugins/mix'
import type { ColorPalette, PaletteLevel } from './types'

extend([mixPlugin])

const HUE_STEP = 2
const SAT_STEP_LIGHT = 16
const SAT_STEP_DARK = 5
const VAL_STEP_LIGHT = 5
const VAL_STEP_DARK = 15
const LIGHT_COUNT = 5
const DARK_COUNT = 4

const LEVELS: readonly PaletteLevel[] = [50, 100, 200, 300, 400, 500, 600, 700, 800, 900, 950] as const

function levelToIndex(level: PaletteLevel): number {
  switch (level) {
    case 50: return 1
    case 100: return 2
    case 200: return 3
    case 300: return 4
    case 400: return 5
    case 500: return 6
    case 600: return 7
    case 700: return 8
    case 800: return 9
    case 900: return 10
    case 950: return 11
    default: return 6
  }
}

function adjustHue(h: number, distance: number, isLight: boolean): number {
  const hsvH = Math.round(h)
  let hue: number
  if (hsvH >= 60 && hsvH <= 240) {
    hue = isLight ? hsvH - HUE_STEP * distance : hsvH + HUE_STEP * distance
  } else {
    hue = isLight ? hsvH + HUE_STEP * distance : hsvH - HUE_STEP * distance
  }
  if (hue < 0) hue += 360
  if (hue >= 360) hue -= 360
  return hue
}

function adjustSaturation(s: number, distance: number, isLight: boolean): number {
  if (s === 0) return s
  let saturation: number
  if (isLight) {
    saturation = s - SAT_STEP_LIGHT * distance
  } else if (distance === DARK_COUNT) {
    saturation = s + SAT_STEP_LIGHT
  } else {
    saturation = s + SAT_STEP_DARK * distance
  }
  if (saturation > 100) saturation = 100
  if (isLight && distance === LIGHT_COUNT && saturation > 10) saturation = 10
  if (saturation < 6) saturation = 6
  return saturation
}

function adjustValue(v: number, distance: number, isLight: boolean): number {
  let value = isLight ? v + VAL_STEP_LIGHT * distance : v - VAL_STEP_DARK * distance
  if (value > 100) value = 100
  if (value < 0) value = 0
  return value
}

/**
 * Compute a single palette color at the requested level (50-950) via AntD HSV algorithm.
 *
 * Index 6 (level 500) returns the input color unchanged. Indices 1-5 (levels 50-400)
 * shift hue/saturation/value to lighten; indices 7-11 (levels 600-950) shift to darken.
 *
 * @param color - Base color as any colord-parseable string (hex, rgb, hsl, named).
 * @param level - Palette level from 50 (lightest) to 950 (darkest).
 * @returns Hex color string (lowercase, with leading #).
 * @throws If the input color cannot be parsed.
 */
export function getPaletteColorByNumber(color: string, level: PaletteLevel): string {
  const base = colord(color)
  if (!base.isValid()) {
    throw new Error(`getPaletteColorByNumber: invalid color "${color}"`)
  }
  if (level === 500) {
    return base.toHex()
  }
  const index = levelToIndex(level)
  const isLight = index < 6
  const distance = isLight ? LIGHT_COUNT + 1 - index : index - LIGHT_COUNT - 1
  const hsv = base.toHsv()
  return colord({
    h: adjustHue(hsv.h, distance, isLight),
    s: adjustSaturation(hsv.s, distance, isLight),
    v: adjustValue(hsv.v, distance, isLight),
  }).toHex()
}

/**
 * Compute the full 11-level palette for a base color.
 */
export function getColorPalette(color: string): ColorPalette {
  const palette = {} as ColorPalette
  for (const level of LEVELS) {
    palette[level] = getPaletteColorByNumber(color, level)
  }
  return palette
}

/**
 * Convert a color to its `R G B` triplet form for use in CSS `rgb()` / `rgb() with alpha`.
 *
 * Returns a space-separated string like `"100 108 255"`. Used to populate
 * `--tnzi-{role}-rgb` variables so consumers can write
 * `background: rgb(var(--tnzi-primary-rgb) / 0.5)` for dynamic transparency.
 */
export function colorToRgbTriplet(color: string): string {
  const { r, g, b } = colord(color).toRgb()
  return `${r} ${g} ${b}`
}

/**
 * Add alpha to a color, returning a hex (with alpha channel) string.
 */
export function addColorAlpha(color: string, alpha: number): string {
  return colord(color).alpha(alpha).toHex()
}

/**
 * Linearly blend `from` toward `to` by `ratio` (0 = pure `from`, 1 = pure `to`).
 *
 * Implementation uses colord's mix plugin. Soybean-admin uses the same primitive
 * to compute the login-page background:
 * `mixColor('#ffffff', themeColor, 0.2)` (dark mode uses 0.5).
 *
 * @param from - Base color (e.g. `'#ffffff'` for a white wash).
 * @param to - Tint color (e.g. the brand primary).
 * @param ratio - Blend ratio 0-1. Values outside the range are clamped.
 * @returns Hex color string.
 */
export function mixColor(from: string, to: string, ratio: number): string {
  const clamped = Math.max(0, Math.min(1, ratio))
  return colord(from).mix(to, clamped).toHex()
}
