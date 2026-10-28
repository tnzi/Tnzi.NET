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

/**
 * A money value as the ledger holds it: rounded half away from zero to whole
 * cents (the backend rounds the same way, `MidpointRounding.AwayFromZero`).
 *
 * Any money the page DERIVES (an outstanding balance, a remaining amount, a
 * sum of allocations) must pass through this before it is shown as an input
 * value, compared against another amount or sent. In IEEE doubles
 * `50.3 - 20.1` is `30.199999999999996`; sent as an allocation, that leaves the
 * document a hair short of paid forever. The `1 + EPSILON` nudge makes the
 * representation of a half cent (`1.005` is `1.00499999999999989...`) round up
 * the way the decimal it stands for does.
 */
export function roundMoney(value: number | null | undefined, decimals = 2): number {
  if (value == null || !Number.isFinite(value)) return 0
  const factor = 10 ** decimals
  const rounded = Math.round(Math.abs(value) * factor * (1 + Number.EPSILON)) / factor
  // `|| 0` folds a negative zero into 0 so a cleared balance never renders as "-0.00".
  return (value < 0 ? -rounded : rounded) || 0
}

/** One currency's share of a set of amounts. */
export interface CurrencyTotal {
  currency: string
  total: number
}

/**
 * Per-currency totals of a set of rows, rounded to cents, in currency-code
 * order. A queue or a pay run can hold several currencies; adding them up and
 * labelling the sum with one of them gives a figure that looks exact and means
 * nothing, so each currency is totalled on its own. A row with no code counts
 * under `''`.
 */
export function totalsByCurrency<T>(
  rows: readonly T[],
  amount: (row: T) => number | null | undefined,
  currency: (row: T) => string | null | undefined,
): CurrencyTotal[] {
  const sums = new Map<string, number>()
  for (const row of rows) {
    const code = normalizeCurrencyCode(currency(row))
    sums.set(code, (sums.get(code) ?? 0) + roundMoney(amount(row)))
  }
  return [...sums.entries()]
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([code, total]) => ({ currency: code, total: roundMoney(total) }))
}

/**
 * Per-currency totals as one figure: a single currency formats as money,
 * several are listed by code (`500.00 CAD · 1,000.00 USD`; the narrow symbols of
 * CAD and USD are both `$`), none is a plain zero.
 */
export function fmtCurrencyTotals(totals: readonly CurrencyTotal[]): string {
  if (totals.length === 0) return fmtAmount(0)
  if (totals.length === 1) return fmtMoney(totals[0]!.total, totals[0]!.currency || null)
  return totals.map((t) => `${fmtAmount(t.total)} ${t.currency}`.trim()).join(' · ')
}

/** A currency code the way a payload sends it: trimmed, upper case, '' for none. */
export function normalizeCurrencyCode(value: string | null | undefined): string {
  return (value ?? '').trim().toUpperCase()
}

/**
 * The manual exchange rate a loaded draft carries, if it still applies to the
 * currency about to be saved; otherwise `null` (resolve from the rate table at
 * posting).
 *
 * Draft updates replace the whole document (`ExchangeRate = input ?? 0` on the
 * server), so a rate the form does not show must be sent back or it is lost -
 * but only while the currency is the one it was set for. A stored `0` is the
 * server's "not set", not a rate.
 */
export function carriedExchangeRate(
  loaded: { currency?: string | null; exchangeRate?: number | null } | null | undefined,
  currency: string | null | undefined,
): number | null {
  const rate = loaded?.exchangeRate
  if (!rate || rate <= 0) return null
  return normalizeCurrencyCode(loaded?.currency) === normalizeCurrencyCode(currency) ? rate : null
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
