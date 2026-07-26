/**
 * North-American accounting presentation conventions, as pure functions.
 *
 * These are the "iron laws" every finance surface must obey. They live here
 * (rather than inside a component) so report renderers, CSV builders, chart
 * labels and `aria-label`s all produce the same string, and so they can be
 * unit-tested without mounting anything.
 *
 * 1. **Negatives use parentheses** - `(1,234.56)`, never `-1,234.56`. This is
 *    the accounting convention: parentheses do not disturb column alignment
 *    and a negative is visible when scanning a dense column. A minus sign
 *    immediately reads as "not written by someone in this industry".
 * 2. **Parentheses are a visual signal only** - screen readers must still hear
 *    a real negative, so every helper has a `srAmount` twin that emits
 *    `-1,234.56` for `aria-label` (AODA / WCAG 2.0 AA: colour and glyph
 *    conventions can never be the sole carrier of meaning).
 * 3. **Dates are unambiguous** - `Jan 15, 2026`. `MM/DD/YYYY` (US) and
 *    `YYYY-MM-DD` (Canadian official) are both in daily use north of the
 *    border, and `toLocaleDateString(undefined)` silently picks whichever the
 *    viewer's browser prefers - so the same ledger reads differently for two
 *    colleagues. Pinning the month name removes the ambiguity entirely.
 * 4. **Never assume `$`** - a document without a currency code renders a bare
 *    number rather than mislabelling every non-USD amount as dollars.
 */

import { EMPTY_DASH } from './placeholders'

/** Fixed month abbreviations, deliberately NOT locale-derived (see law 3). */
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'] as const

/**
 * The placeholder rendered for "no value" in report cells.
 *
 * An alias of the app-wide {@link EMPTY_DASH} so a money cell and an audit cell
 * can never drift apart; the finance-flavoured name is kept because that is
 * what every report call site already reads as.
 */
export const MONEY_DASH = EMPTY_DASH

export interface FormatMoneyOptions {
  /** ISO currency code. Omitted → bare number (never assumes `$`). */
  currency?: string | null
  /**
   * Accounting negatives `(1,234.56)`. Default `true`.
   * Set `false` for inputs / CSV cells that must round-trip as numbers.
   */
  accounting?: boolean
  /** Decimal places. Default 2. */
  decimals?: number
  /** Render `0` as an em-dash. Default `false` (report totals opt in). */
  zeroDash?: boolean
  /** Render `null` / `undefined` as this. Default `MONEY_DASH`. */
  nullText?: string
  /** Always show `+` on positives (variance columns). Default `false`. */
  signed?: boolean
}

function groupDigits(abs: number, decimals: number): string {
  return abs.toLocaleString('en-US', {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  })
}

/**
 * Currency prefix for a code, e.g. `USD`/`CAD` → `$`, `EUR` → `€`.
 *
 * Derived from `Intl` rather than a hand-kept table so unusual codes still
 * render something meaningful; falls back to `CODE ` when the runtime has no
 * symbol for it. Note that `CAD` and `USD` both narrow to `$` under `en-US`
 * - that is intentional and matches QuickBooks/Xero, which disambiguate with
 * a ledger-level currency badge rather than by decorating every number.
 */
export function currencySymbol(currency?: string | null): string {
  if (!currency) return ''
  try {
    const parts = new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency,
      currencyDisplay: 'narrowSymbol',
    }).formatToParts(0)
    return parts.find((p) => p.type === 'currency')?.value ?? `${currency} `
  } catch {
    return `${currency} `
  }
}

/**
 * The canonical money string.
 *
 * ```ts
 * formatMoney(1234.5)                      // '1,234.50'
 * formatMoney(-1234.5)                     // '(1,234.50)'
 * formatMoney(-1234.5, { currency: 'CAD' })// '($1,234.50)'   ← sign wraps the symbol
 * formatMoney(0, { zeroDash: true })       // '-'
 * formatMoney(null)                        // '-'
 * formatMoney(42, { signed: true })        // '+42.00'
 * ```
 */
export function formatMoney(amount?: number | null, options: FormatMoneyOptions = {}): string {
  const { currency, accounting = true, decimals = 2, zeroDash = false, nullText = MONEY_DASH, signed = false } = options

  if (amount === null || amount === undefined || Number.isNaN(amount)) return nullText

  // Decide the sign AFTER rounding, not before. `-0.004` at 2 dp is zero, but
  // testing the raw value sends it down the negative branch and prints
  // `(0.00)` - a parenthesised negative in front of a reader for what is only a
  // rounding residual. The same test folds in `-0`, which compares equal to `0`
  // yet formats with a stray minus.
  const epsilon = 0.5 / 10 ** decimals
  const value = Math.abs(amount) < epsilon ? 0 : amount
  if (value === 0 && zeroDash) return nullText

  const symbol = currencySymbol(currency)
  const body = `${symbol}${groupDigits(Math.abs(value), decimals)}`

  if (value < 0) return accounting ? `(${body})` : `-${body}`
  return signed && value > 0 ? `+${body}` : body
}

/**
 * Screen-reader text for the same amount - an explicit `-` instead of the
 * parentheses. Pair it with `formatMoney` on every money cell:
 * `h('span', { 'aria-label': srMoney(v, o) }, formatMoney(v, o))`.
 */
export function srMoney(amount?: number | null, options: FormatMoneyOptions = {}): string {
  return formatMoney(amount, { ...options, accounting: false, signed: false })
}

/**
 * Plain grouped number without a currency symbol - report bodies where the
 * currency is stated once in the header rather than on every row.
 */
export function formatAmount(amount?: number | null, options: Omit<FormatMoneyOptions, 'currency'> = {}): string {
  return formatMoney(amount, { ...options, currency: null })
}

/** Percent with a fixed 1 decimal, e.g. `12.5%` / `(3.0%)`. */
export function formatPercent(value?: number | null, decimals = 1): string {
  if (value === null || value === undefined || Number.isNaN(value)) return MONEY_DASH
  const body = `${Math.abs(value).toFixed(decimals)}%`
  return value < 0 ? `(${body})` : body
}

/**
 * Parse a backend date-only value without letting the timezone shift the day.
 *
 * The API hands back UTC-midnight ISO strings for date-only fields; feeding
 * those to `new Date(...)` and reading local getters moves the calendar day
 * back for every viewer west of UTC. Reading the Y/M/D characters directly
 * keeps the day the server meant.
 */
function readDateParts(value: string | number | Date): { y: number; m: number; d: number } | null {
  if (typeof value === 'string') {
    const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(value)
    if (m) return { y: Number(m[1]), m: Number(m[2]), d: Number(m[3]) }
  }
  const date = value instanceof Date ? value : new Date(value)
  if (Number.isNaN(date.getTime())) return null
  return { y: date.getFullYear(), m: date.getMonth() + 1, d: date.getDate() }
}

/**
 * The canonical date string: `Jan 15, 2026`.
 *
 * Unambiguous on both sides of the border, and stable regardless of the
 * viewer's browser locale (see law 3).
 */
export function formatAccountingDate(
  value?: string | number | Date | null,
  options: { fallback?: string } = {},
): string {
  const fallback = options.fallback ?? MONEY_DASH
  if (value === null || value === undefined || value === '') return fallback
  const parts = readDateParts(value)
  if (!parts) return fallback
  return `${MONTHS[parts.m - 1]} ${parts.d}, ${parts.y}`
}

/**
 * `Jan 1 - Mar 31, 2026`, collapsing the repeated year and month.
 *
 * Same month collapses to `Mar 1 - 31, 2026`: repeating the month name inside a
 * single month is noise, and period headers are read at a glance.
 */
export function formatAccountingDateRange(
  from?: string | number | Date | null,
  to?: string | number | Date | null,
): string {
  const a = from ? readDateParts(from) : null
  const b = to ? readDateParts(to) : null
  if (!a && !b) return MONEY_DASH
  if (!a) return `… - ${formatAccountingDate(to)}`
  if (!b) return `${formatAccountingDate(from)} - …`
  if (a.y === b.y) {
    const right = a.m === b.m ? `${b.d}` : `${MONTHS[b.m - 1]} ${b.d}`
    return `${MONTHS[a.m - 1]} ${a.d} - ${right}, ${b.y}`
  }
  return `${formatAccountingDate(from)} - ${formatAccountingDate(to)}`
}

/** Local date (00:00) → the `YYYY-MM-DD` string the backend date binder accepts. */
export function tsToIsoDate(ts: number): string {
  const d = new Date(ts)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

/** Backend date-only value → local-midnight timestamp for naive date pickers. */
export function isoDateToLocalTs(iso: string): number {
  const p = readDateParts(iso)
  if (!p) return Number.NaN
  return new Date(p.y, p.m - 1, p.d).getTime()
}

/**
 * Variance between two periods.
 *
 * `percent` is `null` when the base is 0 - an "infinite % change" is a lie
 * that comparison tables love to print; the caller renders `-` instead.
 */
export function variance(current?: number | null, previous?: number | null): { delta: number; percent: number | null } {
  const c = current ?? 0
  const p = previous ?? 0
  const delta = c - p
  return { delta, percent: p === 0 ? null : (delta / Math.abs(p)) * 100 }
}
