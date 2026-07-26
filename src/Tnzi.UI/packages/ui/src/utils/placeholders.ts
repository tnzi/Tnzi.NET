/**
 * The single "no value" placeholder for every Tnzi surface.
 *
 * Every table cell, KPI tile, description row and widget that has nothing to
 * show renders THIS string. It used to be an em-dash hard-coded in ~100 files,
 * which had two problems: the house style bans em/en dashes in anything that
 * reaches the screen, and a glyph repeated across a hundred call sites cannot
 * be changed without a hundred edits.
 *
 * Import it rather than typing a dash:
 *
 * ```ts
 * import { EMPTY_DASH } from '@tnzi/ui'
 * render: (row) => row.code ?? EMPTY_DASH
 * ```
 *
 * A dash is deliberately NOT the same thing as `0`: "we have no figure" and
 * "the figure is zero" are different statements about a ledger, and collapsing
 * them is how a report starts lying quietly.
 *
 * Sunk from `@tnzi/ui-admin/utils/placeholders` so display primitives that live
 * here (TDescriptions, TSchemaForm's read-only layout) share the one glyph with
 * the admin pages. `@tnzi/ui-admin` re-exports this constant, so existing
 * admin imports keep working unchanged.
 */
export const EMPTY_DASH = '-'

/**
 * True when a value should render as {@link EMPTY_DASH}.
 *
 * `false` and `0` are values, not absences - only `null` / `undefined` / the
 * empty string (and an empty array) count as "nothing to show".
 */
export function isEmptyValue(value: unknown): boolean {
  if (value === null || value === undefined) return true
  if (typeof value === 'string') return value.trim() === ''
  if (Array.isArray(value)) return value.length === 0
  return false
}
