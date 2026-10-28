import { EMPTY_DASH } from '../../utils/placeholders'
import { translatePageKey } from '../_shared/translate'

/**
 * Journal `sourceType` token → i18n key, keyed by the wire token the backend
 * writes (`Tnzi.Finance.Metadata.FinanceSourceTypes`, mirrored in core's
 * `FINANCE_SOURCE_TYPES`).
 *
 * Keys resolve under the shared `finance.docs` namespace so every surface that
 * shows a posting's origin - general ledger, journal list, journal detail -
 * reads the same words.
 *
 * Must cover every token in `FINANCE_SOURCE_TYPES`; `source-type.test.ts` fails
 * if the two drift apart. (The check is a test rather than a runtime guard on
 * purpose: this module is imported by every finance page, and importing the
 * token list for a dev-only warning would force every page's bridge mock to
 * re-export it.)
 */
/**
 * The wire tokens themselves, named.
 *
 * Same strings as core's `FINANCE_SOURCE_TYPES` (which is a readonly array, so
 * it cannot be dotted into). Pages address attachments / comments / drill-downs
 * by token, and a named constant keeps those call sites from turning into
 * scattered magic strings that a rename would silently miss.
 */
export const FinanceDocToken = {
  Invoice: 'Invoice',
  Bill: 'Bill',
  CreditMemo: 'CreditMemo',
  Expense: 'Expense',
  PaymentEntry: 'PaymentEntry',
  Transfer: 'Transfer',
} as const

export const FINANCE_SOURCE_TYPE_LABEL_KEYS: Record<string, string> = {
  Invoice: 'sourceType.invoice',
  Bill: 'sourceType.bill',
  CreditMemo: 'sourceType.creditMemo',
  Expense: 'sourceType.expense',
  PaymentEntry: 'sourceType.paymentEntry',
  PaymentApplication: 'sourceType.paymentApplication',
  Transfer: 'sourceType.transfer',
  Deposit: 'sourceType.deposit',
  Revaluation: 'sourceType.revaluation',
}

/**
 * Source tokens whose journal entries are undone by voiding the document, not by
 * reversing the entry from the ledger.
 *
 * The ledger's reverse endpoint refuses these with 409 (the backend's
 * document-projected set): reversing an invoice's entry there would offset the
 * ledger while the invoice stays Posted and keeps ageing. The core documents plus
 * the payroll sub-module's two pay-run tokens. `Revaluation` and
 * `PaymentApplication` are deliberately absent - they have no document-level void,
 * so reversing from the ledger is their only undo. A consuming app can add its
 * own tokens on the server; those still offer Reverse and are refused there.
 */
const VOID_ONLY_SOURCE_TYPES: ReadonlySet<string> = new Set([
  'Invoice',
  'Bill',
  'CreditMemo',
  'Expense',
  'PaymentEntry',
  'Transfer',
  'Deposit',
  'PayRun',
  'PayRun.Payment',
])

/** Whether the ledger itself may reverse an entry with this source (manual entries carry none). */
export function isReversibleFromLedger(token?: string | null): boolean {
  return !token || !VOID_ONLY_SOURCE_TYPES.has(token)
}

/**
 * Label for a journal `sourceType` token.
 *
 * The token set is open, not an enum: consuming apps that post programmatically
 * through `ILedgerPostingService` write their own tokens, so an unknown token
 * falls back to itself rather than being hidden or blanked - a source the
 * framework does not recognise is still a source the accountant must see.
 * `placeholder` covers manual journal entries, which carry no source at all.
 */
export function financeSourceTypeLabel(token?: string | null, placeholder = EMPTY_DASH): string {
  if (!token) return placeholder
  const key = FINANCE_SOURCE_TYPE_LABEL_KEYS[token]
  return key ? translatePageKey('finance.docs', key) : token
}
