/**
 * Surface tone detection - the core of the "adaptive surfaces" appearance
 * model.
 *
 * A user can paint any admin surface (sider / header / tab / footer / content)
 * an arbitrary background color. To keep the surface readable regardless of the
 * chosen color, we derive a *tone* from the color's perceived brightness and
 * let the surface flip its foreground token set accordingly:
 *
 *   - `dark`  surface → light text / borders (the inverted token set)
 *   - `light` surface → dark text / borders (the default light token set)
 *
 * This removes the two classic failure modes of a bare background picker:
 *   1. dark background + default (near-black) text → illegible
 *   2. a custom sider color silently lost under the inverted-sider overlay
 *
 * Brightness uses WCAG relative luminance (sRGB-linearized, perceptually
 * accurate) rather than the simpler YIQ luma: YIQ mis-ranks bright saturated
 * colors (teal, orange, lime) as "dark enough for white text" when dark text
 * is clearly more readable. The threshold is tuned so genuinely dark/saturated
 * jewel tones (navy, blue, indigo, violet, deep red) keep the "colored chrome
 * = light text" convention, while bright colors flip to dark text for
 * readability. Users who disagree with a given call use the per-surface manual
 * override in the theme drawer.
 */

export type SurfaceTone = 'light' | 'dark'

/**
 * Parse a CSS color string into an `{ r, g, b }` triplet (0-255) or `null`
 * when the format is not a plain hex / rgb() color. Only the formats the
 * theme drawer's color picker can emit are supported (hex 3/6/8, rgb/rgba).
 */
export function parseColor(color: string): { r: number; g: number; b: number } | null {
  if (typeof color !== 'string') return null
  const value = color.trim()
  if (value === '') return null

  // #rgb / #rgba / #rrggbb / #rrggbbaa - strict digit check up front:
  // parseInt('1g', 16) parses the leading '1' instead of failing, so a
  // malformed value like '#1g0000' would otherwise slip through as near-black.
  if (value.startsWith('#')) {
    const hex = value.slice(1)
    if (!/^[0-9a-f]+$/i.test(hex)) return null
    if (hex.length === 3 || hex.length === 4) {
      const r = parseInt(hex.slice(0, 1).repeat(2), 16)
      const g = parseInt(hex.slice(1, 2).repeat(2), 16)
      const b = parseInt(hex.slice(2, 3).repeat(2), 16)
      return { r, g, b }
    }
    if (hex.length === 6 || hex.length === 8) {
      const r = parseInt(hex.slice(0, 2), 16)
      const g = parseInt(hex.slice(2, 4), 16)
      const b = parseInt(hex.slice(4, 6), 16)
      return { r, g, b }
    }
    return null
  }

  // rgb(…) / rgba(…)
  const match = value.match(/^rgba?\(([^)]+)\)$/i)
  if (match) {
    const parts = (match[1] ?? '').split(/[,\s/]+/).filter(Boolean)
    if (parts.length < 3) return null
    const r = Number(parts[0])
    const g = Number(parts[1])
    const b = Number(parts[2])
    if ([r, g, b].some((n) => Number.isNaN(n))) return null
    return { r, g, b }
  }

  return null
}

/** Linearize a single 0-255 sRGB channel to its 0-1 light-linear value. */
function linearizeChannel(c: number): number {
  const s = c / 255
  return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4)
}

/**
 * WCAG relative luminance (0 = black, 1 = white). Perceptually weighted
 * (green counts most). Returns `null` when the color can't be parsed.
 */
export function relativeLuminance(color: string): number | null {
  const rgb = parseColor(color)
  if (!rgb) return null
  return (
    0.2126 * linearizeChannel(rgb.r) +
    0.7152 * linearizeChannel(rgb.g) +
    0.0722 * linearizeChannel(rgb.b)
  )
}

/**
 * Perceived brightness (0-255, YIQ luma). Kept as a utility; the tone decision
 * uses {@link relativeLuminance} instead. Returns `null` when unparseable.
 */
export function perceivedBrightness(color: string): number | null {
  const rgb = parseColor(color)
  if (!rgb) return null
  return (rgb.r * 299 + rgb.g * 587 + rgb.b * 114) / 1000
}

/**
 * Relative luminance below this reads as a "dark" surface that wants light
 * foreground. The strict max-contrast crossover is ~0.18; we lift it to 0.30
 * so slightly-lighter-but-saturated jewel tones (medium blue / indigo / red)
 * keep the conventional light text, while genuinely bright colors (teal,
 * orange, lime, sky, yellow) fall on the dark-text side where they read best.
 */
export const DARK_SURFACE_THRESHOLD = 0.3

/** Whether a color is dark enough that light foreground reads better. */
export function isDarkSurface(color: string | null | undefined): boolean {
  if (!color) return false
  const luminance = relativeLuminance(color)
  if (luminance == null) return false
  return luminance < DARK_SURFACE_THRESHOLD
}

/**
 * Tone of a surface given its (optional) background override.
 *   - `null`  → no override set, the surface follows the global light/dark mode
 *   - `'dark'`  → the override is a dark color (use light foreground)
 *   - `'light'` → the override is a light color (use dark foreground)
 */
export function surfaceTone(color: string | null | undefined): SurfaceTone | null {
  if (!color) return null
  const luminance = relativeLuminance(color)
  if (luminance == null) return null
  return luminance < DARK_SURFACE_THRESHOLD ? 'dark' : 'light'
}
