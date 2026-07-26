/**
 * `moneyPairColumns` - the two sides of an amount, rendered with the right words.
 *
 * Finance screens keep hitting the same fork: a table has one column for money
 * arriving and one for money leaving, and the two headings must be either
 * `Debit / Credit` or `Money in / Money out`. Getting it wrong is not a
 * cosmetic slip - "Money out" over a voucher's credit column tells the reader
 * the entry sent money out of the business, when the credit may be revenue
 * earned or a liability taken on.
 *
 * **The choice is a property of the view, not of the viewer.** We deliberately
 * do not ship a business/accountant terminology switch: an accountant who opens
 * the client's screen has to see the client's words to be able to talk about
 * them, which is why QuickBooks is dismantling its own two-view split. Xero's
 * model is the one to copy - fixed vocabulary per screen, reachability by role.
 *
 * How to pick:
 *
 * | The view shows                                  | `presentation` |
 * | ----------------------------------------------- | -------------- |
 * | one funds account, its own side only            | `'flow'`       |
 * | a whole double-entry voucher, or several accounts | `'ledger'`   |
 *
 * Quick test: **is there a second account in this view?** Yes → `'ledger'`.
 * Only the account you are already looking at → `'flow'`. Anything carrying a
 * debit/credit totals row is a ledger view by definition.
 *
 * A third vocabulary exists and is not modelled here: the columns of an
 * external bank file (`Withdrawal / Deposit`) belong to the import screen,
 * where they name the bank's own words rather than our books.
 *
 * ```ts
 * const columns = [
 *   dateColumn,
 *   memoColumn,
 *   // Reconciliation worksheet: every row is a posted line of the one bank
 *   // account being reconciled, so it reads as money in / money out.
 *   ...moneyPairColumns<CandidateLine>({
 *     presentation: 'flow',
 *     translate: t,
 *     debit: (r) => r.debit,
 *     credit: (r) => r.credit,
 *   }),
 * ]
 * ```
 */
import { h } from 'vue'
import type { DataTableColumns } from 'naive-ui'

import TMoney from './TMoney.vue'

/** Which vocabulary a view uses for the two sides of an amount. */
export type MoneyPresentation = 'ledger' | 'flow'

export interface MoneyPairColumnsOptions<T> {
  /** See the table in this module's doc comment. Never derive this from the user. */
  presentation: MoneyPresentation
  /** Debit-side amount, i.e. money arriving in the account being looked at. */
  debit: (row: T) => number | null | undefined
  /** Credit-side amount, i.e. money leaving it. */
  credit: (row: T) => number | null | undefined
  /**
   * Page translator. The labels live in the shared `admin.shared.*` dictionary,
   * so passing any page's `makePageTranslator` result resolves them - the
   * wording is not the page's to own.
   */
  translate?: (key: string) => string
  /** ISO currency code; omit for bare report bodies that carry it in a header. */
  currency?: string | null
  /** Column width in px. */
  width?: number
  /** Render a zero as the empty placeholder rather than `0.00`. Default true. */
  zeroDash?: boolean
  /** Row-data keys, when the caller's DTO does not use `debit` / `credit`. */
  debitKey?: string
  creditKey?: string
}

const LABEL_KEYS: Record<MoneyPresentation, { debit: string; credit: string }> = {
  ledger: { debit: 'admin.shared.ledger.debit', credit: 'admin.shared.ledger.credit' },
  flow: { debit: 'admin.shared.moneyFlow.in', credit: 'admin.shared.moneyFlow.out' },
}

// Used only when a caller omits `translate` (a bare mount, a consumer app that
// has not wired the dictionary). Never reached in the built-in pages.
const FALLBACK: Record<MoneyPresentation, { debit: string; credit: string }> = {
  ledger: { debit: 'Debit', credit: 'Credit' },
  flow: { debit: 'Money in', credit: 'Money out' },
}

/**
 * Builds the debit/credit column pair for a naive-ui table.
 *
 * Both columns render through `TMoney`, so the accounting conventions - and in
 * particular the accessible name carrying a real `-` for negatives, which
 * parentheses alone do not convey - come for free rather than per call site.
 */
export function moneyPairColumns<T>(options: MoneyPairColumnsOptions<T>): DataTableColumns<T> {
  const {
    presentation,
    debit,
    credit,
    translate,
    currency,
    width = 120,
    zeroDash = true,
    debitKey = 'debit',
    creditKey = 'credit',
  } = options

  const label = (side: 'debit' | 'credit') =>
    translate?.(LABEL_KEYS[presentation][side]) || FALLBACK[presentation][side]

  const cell = (value: number | null | undefined) =>
    h(TMoney, { value: value ?? 0, currency, zeroDash })

  return [
    { key: debitKey, title: label('debit'), width, align: 'right', render: (row: T) => cell(debit(row)) },
    { key: creditKey, title: label('credit'), width, align: 'right', render: (row: T) => cell(credit(row)) },
  ]
}
