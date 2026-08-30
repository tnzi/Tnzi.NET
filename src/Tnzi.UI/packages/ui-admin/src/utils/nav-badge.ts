/**
 * The one display rule for navigation count badges - "there is work waiting
 * behind this door" on a sidebar menu entry or a detail-page section.
 *
 * Pure on purpose. What counts as "nothing to show" and where a count is
 * capped has to be identical across the sidebar, the collapsed rail, the top
 * menu and the detail nav, and it is the part worth testing without mounting
 * anything. The vnode side lives in `components/layout/nav-badge.ts`.
 *
 * The framework only RENDERS the value it is handed - it never fetches, polls,
 * aggregates or interprets it.
 */

/** What a consumer may hand a nav surface as a badge. */
export type NavBadgeValue = string | number | null | undefined

/**
 * Numeric cap, shared with `THeaderBell`'s unread badge so every count in the
 * shell tops out the same way.
 */
export const NAV_BADGE_MAX = 99

/**
 * Resolve a badge value to the text to paint, or `null` for "paint nothing".
 *
 * `null` rather than `''` so callers branch on presence and emit no badge
 * element at all: an empty chip beside every menu entry is worse than no
 * badge feature.
 *
 * - nullish / `0` / negative / `NaN` -> nothing (zero IS "no work waiting")
 * - greater than {@link NAV_BADGE_MAX} -> `"99+"`
 * - blank or whitespace-only string -> nothing
 * - any other string -> verbatim, never capped (short labels like `NEW`)
 */
export function normalizeNavBadge(value: NavBadgeValue): string | null {
  if (value == null) return null
  if (typeof value === 'number') {
    if (!Number.isFinite(value) || value <= 0) return null
    return value > NAV_BADGE_MAX ? `${NAV_BADGE_MAX}+` : String(value)
  }
  const text = value.trim()
  return text.length > 0 ? text : null
}
