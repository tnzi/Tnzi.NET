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

// ---------------------------------------------------------------------------
// Contrast: what goes on a colour, and the colour as text.
//
// A brand colour can be anything a person picks at runtime, light or dark, so
// neither "white on the primary" nor "the primary as text" can be a constant.
// These two functions answer both questions for any colour; the theme layer
// (`buildCssVars`, `buildNaiveThemeOverrides`) applies them to the primary.
//
// Ratios follow WCAG 2.x (relative luminance, (L1 + 0.05) / (L2 + 0.05)).
// Alpha is ignored: a ratio is only defined between two opaque colours.
// ---------------------------------------------------------------------------

/** The foreground {@link getContrastTextColor} puts on a dark fill. */
export const CONTRAST_TEXT_LIGHT = '#ffffff'

/**
 * The foreground {@link getContrastTextColor} puts on a light fill.
 *
 * Near-black rather than black: on a saturated fill pure black reads harsher
 * than it needs to. Any ink with a relative luminance at or below 0.0278 reads
 * at 4.5:1 on every fill where white falls under 3:1; this one (0.007) reads at
 * 6.1:1 or more there.
 */
export const CONTRAST_TEXT_DARK = '#141414'

/**
 * White stays on a fill down to this ratio: the WCAG floor for large text and
 * for interface components (1.4.3 / 1.4.11).
 *
 * Why not "whichever reads better": white text on a mid-tone brand colour
 * (the default teal at 3.7:1, most blues and purples) is how those colours are
 * designed to be used, and a pure maximum-contrast rule would turn all of them
 * dark. Below 3:1 white is no longer a design choice but illegible (yellow at
 * 1.4:1, orange at 2.8:1), and the dark ink then reads at 6:1 or more.
 */
export const MIN_LIGHT_TEXT_CONTRAST = 3

/** WCAG AA for normal-size text. */
export const READABLE_TEXT_CONTRAST = 4.5

function linearChannel(channel: number): number {
  const c = channel / 255
  return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4
}

function parseOrThrow(color: string, caller: string) {
  const parsed = colord(color)
  if (!parsed.isValid()) {
    throw new Error(`${caller}: invalid color "${color}"`)
  }
  return parsed
}

/**
 * WCAG 2.x relative luminance, 0 (black) to 1 (white). Not exported: the
 * public question is always a ratio between two colours, and the admin
 * package already exports a `relativeLuminance` with a different contract
 * (`null` for an unparseable colour).
 */
function luminanceOf(color: string): number {
  const { r, g, b } = parseOrThrow(color, 'contrastRatio').toRgb()
  return 0.2126 * linearChannel(r) + 0.7152 * linearChannel(g) + 0.0722 * linearChannel(b)
}

/**
 * WCAG 2.x contrast ratio between two colours, 1 to 21. Order does not matter.
 *
 * @throws If either color cannot be parsed.
 */
export function contrastRatio(a: string, b: string): number {
  const la = luminanceOf(a)
  const lb = luminanceOf(b)
  return (Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05)
}

export interface ContrastTextOptions {
  /** Foreground for a dark fill. Default {@link CONTRAST_TEXT_LIGHT}. */
  light?: string
  /** Foreground for a light fill. Default {@link CONTRAST_TEXT_DARK}. */
  dark?: string
  /** `light` is kept while it reads at least this well. Default {@link MIN_LIGHT_TEXT_CONTRAST}. */
  minLightContrast?: number
}

/**
 * The text / icon colour for content sitting ON a fill of `fill`: white while
 * white reads at {@link MIN_LIGHT_TEXT_CONTRAST} or better, otherwise whichever
 * of the two candidates reads better (the dark ink, for every real colour).
 *
 * @example getContrastTextColor('#0d9488') // '#ffffff' (3.7:1)
 * @example getContrastTextColor('#ffd217') // '#141414' (12.7:1; white is 1.4:1)
 * @throws If any color cannot be parsed.
 */
export function getContrastTextColor(fill: string, options: ContrastTextOptions = {}): string {
  const {
    light = CONTRAST_TEXT_LIGHT,
    dark = CONTRAST_TEXT_DARK,
    minLightContrast = MIN_LIGHT_TEXT_CONTRAST,
  } = options
  const onLight = contrastRatio(light, fill)
  if (onLight >= minLightContrast) return light
  return contrastRatio(dark, fill) > onLight ? dark : light
}

/**
 * `color` as a text / icon colour on `background`: the colour itself when it
 * already reads at `minContrast`, otherwise the same hue moved darker (on a
 * light background) or lighter (on a dark one) just far enough to read.
 *
 * The move is along HSL lightness with hue and saturation held, so the result
 * is recognisably the same colour (a light yellow becomes a dark gold, not a
 * grey); the smallest move that reaches the ratio is taken, so a colour that
 * nearly reads barely changes. HSL rather than a perceptual space is enough
 * here because the ratio is measured on every candidate, and it keeps the
 * lab / xyz conversions of a colour-space plugin out of every bundle that
 * builds a theme.
 *
 * @example getReadableColor('#ffd217', '#ebebeb') // '#816800'
 * @example getReadableColor('#0d9488', '#1c1c1c') // '#0d9488' (already 4.5:1)
 * @throws If either color cannot be parsed.
 */
export function getReadableColor(
  color: string,
  background: string,
  minContrast: number = READABLE_TEXT_CONTRAST,
): string {
  const base = parseOrThrow(color, 'getReadableColor')
  parseOrThrow(background, 'getReadableColor')
  const hex = base.alpha(1).toHex()
  if (contrastRatio(hex, background) >= minContrast) return hex

  const darken = contrastRatio('#000000', background) >= contrastRatio('#ffffff', background)
  const { h, s, l } = base.toHsl()
  const reads = (lightness: number): boolean =>
    contrastRatio(colord({ h, s, l: lightness }).toHex(), background) >= minContrast

  // Bisect between the colour's own lightness (does not read) and the far end
  // (black / white, which reads unless the target is impossible).
  let fails = l
  let passes = darken ? 0 : 100
  if (!reads(passes)) return darken ? '#000000' : '#ffffff'
  for (let step = 0; step < 24; step += 1) {
    const mid = (fails + passes) / 2
    if (reads(mid)) passes = mid
    else fails = mid
  }
  return colord({ h, s, l: passes }).toHex()
}
