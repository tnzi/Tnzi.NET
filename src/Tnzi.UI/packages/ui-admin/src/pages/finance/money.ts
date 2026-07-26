import { h, type VNode } from 'vue'
import {
  formatAccountingDate,
  formatAmount,
  formatMoney,
  isoDateToLocalTs,
  srMoney,
  tsToIsoDate,
} from '../../utils/finance-format'

/**
 * Finance-page shims over the shared accounting formatters.
 *
 * The real implementations live in `utils/finance-format.ts` (public on the
 * package root) so consumer apps get the same conventions. These wrappers keep
 * the call shape the 24 built-in finance pages already use.
 */

/**
 * Money with a currency symbol when the document carries a currency code, and
 * a bare number when it does not - a fixed `USD` fallback would mislabel every
 * non-USD document as dollars.
 *
 * Negatives render as `(1,234.56)` per the accounting convention.
 */
export function fmtMoney(amount?: number | null, currency?: string | null): string {
  return formatMoney(amount, { currency })
}

/** Plain 2-decimal amount without a currency symbol (report tables). */
export function fmtAmount(amount?: number | null): string {
  return formatAmount(amount)
}

/**
 * Tabular-nums cell for amount columns.
 *
 * `srText` carries the screen-reader form (explicit `-`, not parentheses);
 * pass it whenever the cell can hold a negative, so the accessible name is not
 * the purely visual accounting glyph. AODA / WCAG 2.0 AA.
 */
export function amountCell(text: string, emphasis = false, srText?: string): VNode {
  return h(
    'span',
    {
      style: `font-variant-numeric: tabular-nums;${emphasis ? ' font-weight: 600;' : ''}`,
      ...(srText ? { 'aria-label': srText } : {}),
    },
    text,
  )
}

/** `amountCell` for a raw number - formats and labels it in one step. */
export function moneyCell(amount?: number | null, currency?: string | null, emphasis = false): VNode {
  return amountCell(fmtMoney(amount, currency), emphasis, srMoney(amount, { currency }))
}

/**
 * The canonical finance date: `Jan 15, 2026`.
 *
 * Deliberately not `toLocaleDateString` - US `MM/DD/YYYY` and Canadian
 * `YYYY-MM-DD` are both common, and letting the browser choose means two
 * colleagues read the same ledger differently.
 */
export const fmtDate = formatAccountingDate

export { tsToIsoDate, isoDateToLocalTs }
