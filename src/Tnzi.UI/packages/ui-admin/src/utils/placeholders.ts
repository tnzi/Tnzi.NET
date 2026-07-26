/**
 * The single "no value" placeholder for every admin surface.
 *
 * The constant itself now lives in `@tnzi/ui/utils/placeholders` (sunk there so
 * the display primitives that render empty cells - TDescriptions, TSchemaForm's
 * read-only layout - share the one glyph with the admin pages instead of each
 * package owning a copy). This module stays as the admin-facing import path so
 * the ~100 existing `import { EMPTY_DASH } from '<rel>/utils/placeholders'`
 * call sites keep working unchanged.
 *
 * Import it rather than typing a dash:
 *
 * ```ts
 * import { EMPTY_DASH } from '../../utils/placeholders'
 * render: (row) => row.code ?? EMPTY_DASH
 * ```
 *
 * A dash is deliberately NOT the same thing as `0`: "we have no figure" and
 * "the figure is zero" are different statements about a ledger, and collapsing
 * them is how a report starts lying quietly.
 */
export { EMPTY_DASH, isEmptyValue } from '@tnzi/ui'
