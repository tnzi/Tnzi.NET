/**
 * Avatar helpers - deterministic colour + initial derivation shared by
 * `TAvatar` and any consumer that needs to render an identity glyph without
 * the full component (e.g. inside a naive-ui `render` function).
 *
 * Pure (no Vue, no IO) so they are equally usable from headless code.
 */

/**
 * Muted, desaturated palette for initials-avatars. White text reads cleanly on
 * every entry. Neutral on purpose so it never clashes with a host app's accent.
 */
export const AVATAR_COLORS: readonly string[] = [
  '#5b8c9e', // steel blue
  '#6b8e7f', // sage
  '#a8829e', // mauve
  '#c2906b', // clay
  '#7e8bb0', // periwinkle
  '#8f9a5b', // olive
  '#b07e8a', // rosewood
  '#5fa3a3', // teal
  '#9a8bb5', // lavender
  '#c79a5b', // amber
]

/** Deterministic avatar background for a seed (user id or name). Stable across renders. */
export function avatarColor(seed?: string | null): string {
  const fallback = AVATAR_COLORS[0] ?? '#5b8c9e'
  const s = (seed ?? '').trim()
  if (!s) return fallback
  let hash = 0
  for (let i = 0; i < s.length; i++) hash = (hash * 31 + s.charCodeAt(i)) >>> 0
  return AVATAR_COLORS[hash % AVATAR_COLORS.length] ?? fallback
}

/** First visible character of a string (surrogate-pair safe for emoji / CJK ext). */
function firstChar(s: string): string {
  return [...s][0] ?? ''
}

/**
 * Initial(s) for a display name, upper-cased; falls back to '?' for an empty name.
 *
 * - `max = 1` (default) → a single leading letter.
 * - `max = 2` → two-letter initials: the first letters of the first two
 *   whitespace-separated words, or the first two characters of a single word.
 */
export function avatarInitial(name?: string | null, max = 1): string {
  const s = (name ?? '').trim()
  if (!s) return '?'
  if (max <= 1) return firstChar(s).toUpperCase()
  const parts = s.split(/\s+/).filter(Boolean)
  if (parts.length >= 2) return (firstChar(parts[0] ?? '') + firstChar(parts[1] ?? '')).toUpperCase()
  return [...s].slice(0, 2).join('').toUpperCase()
}
