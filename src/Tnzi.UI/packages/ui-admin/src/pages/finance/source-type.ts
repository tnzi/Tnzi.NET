import { translatePageKey } from '../_shared/translate'

/**
 * Journal `sourceType` token → i18n key, keyed by the wire token the backend
 * writes (`Tnzi.Finance.Metadata.FinanceSourceTypes`, mirrored in core's
 * `FINANCE_SOURCE_TYPES`).
 *
 * Keys resolve under the shared `finance.docs` namespace so every surface that
 * shows a posting's origin — general ledger, journal list, journal detail —
 * reads the same words.
 *
 * Must cover every token in `FINANCE_SOURCE_TYPES`; `source-type.test.ts` fails
 * if the two drift apart. (The check is a test rather than a runtime guard on
 * purpose: this module is imported by every finance page, and importing the
 * token list for a dev-only warning would force every page's bridge mock to
 * re-export it.)
 */
export const FINANCE_SOURCE_TYPE_LABEL_KEYS: Record<string, string> = {
  Invoice: 'sourceType.invoice',
  Bill: 'sourceType.bill',
  CreditMemo: 'sourceType.creditMemo',
  Expense: 'sourceType.expense',
  PaymentEntry: 'sourceType.paymentEntry',
  PaymentApplication: 'sourceType.paymentApplication',
  Transfer: 'sourceType.transfer',
  Revaluation: 'sourceType.revaluation',
}

/**
 * Label for a journal `sourceType` token.
 *
 * The token set is open, not an enum: consuming apps that post programmatically
 * through `ILedgerPostingService` write their own tokens, so an unknown token
 * falls back to itself rather than being hidden or blanked — a source the
 * framework does not recognise is still a source the accountant must see.
 * `placeholder` covers manual journal entries, which carry no source at all.
 */
export function financeSourceTypeLabel(token?: string | null, placeholder = '—'): string {
  if (!token) return placeholder
  const key = FINANCE_SOURCE_TYPE_LABEL_KEYS[token]
  return key ? translatePageKey('finance.docs', key) : token
}
