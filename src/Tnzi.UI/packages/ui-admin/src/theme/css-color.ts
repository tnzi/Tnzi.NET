/**
 * A CSS color value, or `null` when the value is something else.
 *
 * For theme values that arrive from outside the browser: the global theme
 * snapshot (one privileged user's settings, applied for every user) and a
 * snapshot file pasted into the theme drawer. Their surface colors are written
 * to root custom properties that `background:` shorthands read, so a value
 * such as `url(https://...)` there makes every browser that applies the theme
 * fetch an image from wherever it points.
 *
 * Accepted: hex (`#rgb`, `#rgba`, `#rrggbb`, `#rrggbbaa`); the color functions
 * (`rgb()`, `hsl()`, `hwb()`, `lab()`, `lch()`, `oklab()`, `oklch()`,
 * `color()`) with plain numeric arguments and nothing nested inside; a bare
 * keyword (named colors, `transparent`, `currentcolor`); and a plain
 * `var(--name)` reference to a property the application's own stylesheet
 * defines. Anything else, `url()` and gradients included, is refused.
 */
const HEX = /^#(?:[0-9a-f]{3,4}|[0-9a-f]{6}|[0-9a-f]{8})$/i
const COLOR_FUNCTION = /^(?:rgba?|hsla?|hwb|lab|lch|oklab|oklch|color)\([\w\s.,%/+-]*\)$/i
const KEYWORD = /^[a-z]+$/i
const VARIABLE = /^var\(--[\w-]+\)$/

export function cssColorOrNull(value: unknown): string | null {
  if (typeof value !== 'string') return null
  const color = value.trim()
  if (!color) return null
  return HEX.test(color) || COLOR_FUNCTION.test(color) || KEYWORD.test(color) || VARIABLE.test(color)
    ? color
    : null
}
