/**
 * Finance presentation primitives.
 *
 * Public on purpose: consumer applications building their own finance screens
 * get the North-American accounting conventions (parenthesised negatives,
 * tabular figures, unambiguous dates, accessible amounts) and the reconcile
 * flow for free, instead of re-deriving them per page. See
 * `docs/superpowers/specs/2026-07-25-finance-na-ux-research-and-plan.md`.
 */
export { default as TMoney } from './TMoney.vue'
// The debit/credit column pair, named by what the view shows (a single funds
// account → money in / out; a voucher or several accounts → debit / credit).
// Exported so consumer finance screens inherit the rule instead of re-deciding
// it per table.
export { moneyPairColumns } from './money-columns'
export type { MoneyPresentation, MoneyPairColumnsOptions } from './money-columns'
export { default as TPeriodPicker } from './TPeriodPicker.vue'
export { default as TDocStatusBadge } from './TDocStatusBadge.vue'
// One finance document as a row card (party / status / dates / money +
// outstanding). Shared by every document list so invoices, bills, expenses and
// credit memos read identically.
export { default as TDocumentCard } from './TDocumentCard.vue'
export { default as TFinanceViewToggle } from './TFinanceViewToggle.vue'
export { default as TAccountSelect } from './TAccountSelect.vue'
export { default as TPartySelect } from './TPartySelect.vue'
export { default as TReconcileWorkspace } from './TReconcileWorkspace.vue'
export { default as TReconcileRow } from './TReconcileRow.vue'
export { default as TAgingBar } from './TAgingBar.vue'
export type { AgingBuckets } from './TAgingBar.vue'
export { default as TTransactionList } from './TTransactionList.vue'
export { default as TRuleBuilder } from './TRuleBuilder.vue'
export type { RuleConditionRow, RuleField, RuleOperator } from './TRuleBuilder.vue'
