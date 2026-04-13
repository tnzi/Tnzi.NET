import { colord, extend } from 'colord'
import mixPlugin from 'colord/plugins/mix'
import type { ColorPalette, PaletteLevel } from './types'

extend([mixPlugin])

/**
 * Compute a single palette color at the requested level (50-950).
 *
 * Level 500 returns the input color unchanged. Lower levels (50-400) are
 * created by mixing the base color with white in proportion to the distance
 * from 500. Higher levels (600-950) are mixed with black.
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
  if (level < 500) {
    // Levels 50-400: mix with white.
    const weights: Record<number, number> = {
      50: 0.95,
      100: 0.9,
      200: 0.8,
      300: 0.6,
      400: 0.3,
    }
    return base.mix('#ffffff', weights[level] ?? 0).toHex()
  }
  // Levels 600-950: mix with black.
  const weights: Record<number, number> = {
    600: 0.15,
    700: 0.3,
    800: 0.45,
    900: 0.6,
    950: 0.75,
  }
  return base.mix('#000000', weights[level] ?? 0).toHex()
}

/**
 * Compute the full 11-level palette for a base color.
 */
export function getColorPalette(color: string): ColorPalette {
  const levels: PaletteLevel[] = [50, 100, 200, 300, 400, 500, 600, 700, 800, 900, 950]
  const palette = {} as ColorPalette
  for (const level of levels) {
    palette[level] = getPaletteColorByNumber(color, level)
  }
  return palette
}
