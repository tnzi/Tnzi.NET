import { EMPTY_DASH } from '../../utils/placeholders'
import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { DataTableColumns } from 'naive-ui'
import type { DepositDto, DepositLineDto, UndepositedReceiptDto } from '../../services/bridges/finance-bridge'
import TMoney from '../../components/finance/TMoney.vue'
import type { SearchFieldItem } from '../../components/crud/TCrudSearchAdvanced.vue'
import { docStatusBadge } from './document-config'
import { fmtDate } from './money'

/** All-optional row shape (house pattern) so ColumnDef stays assignable. */
export type DepositRow = Partial<DepositDto>

/** Deposit list columns. */
export function buildDepositColumns(t: (key: string) => string): ColumnDef<DepositRow>[] {
  return [
    { key: 'number', title: t('columns.number'), width: 130, primary: true, render: (r) => r.number ?? t('draftLabel') },
    // Deposit shares FinanceDocumentStatus with the five documents: the badge
    // comes from document-config so the palette cannot drift per page.
    { key: 'status', title: t('columns.status'), width: 110, render: (r) => docStatusBadge(t, r.status) },
    { key: 'depositDate', title: t('columns.date'), width: 120, render: (r) => fmtDate(r.depositDate) },
    { key: 'fromAccountName', title: t('columns.from'), minWidth: 160, render: (r) => r.fromAccountName ?? r.fromAccountId ?? EMPTY_DASH },
    { key: 'toAccountName', title: t('columns.to'), minWidth: 160, render: (r) => r.toAccountName ?? r.toAccountId ?? EMPTY_DASH },
    { key: 'amount', title: t('columns.amount'), minWidth: 140, render: (r) => h(TMoney, { value: r.amount, currency: r.currency }) },
    { key: 'reference', title: t('columns.reference'), minWidth: 120, mobileHidden: true, render: (r) => r.reference ?? EMPTY_DASH },
  ]
}

/**
 * Candidate list columns (posted inbound receipts no live deposit has claimed).
 *
 * This is the list a deposit is built from, so it carries what the person at
 * the counter reads off the cheques: who it came from, the cheque number and
 * the amount.
 */
export function buildUndepositedColumns(t: (key: string) => string): DataTableColumns<UndepositedReceiptDto> {
  return [
    { type: 'selection' },
    { key: 'paymentNumber', title: t('queue.columns.receipt'), width: 130, render: (r) => r.paymentNumber ?? EMPTY_DASH },
    { key: 'partyName', title: t('queue.columns.party'), minWidth: 160, render: (r) => r.partyName ?? EMPTY_DASH },
    { key: 'docDate', title: t('queue.columns.date'), width: 110, render: (r) => fmtDate(r.docDate) },
    { key: 'paymentMethod', title: t('queue.columns.method'), width: 120, render: (r) => r.paymentMethod ?? EMPTY_DASH },
    { key: 'reference', title: t('queue.columns.reference'), width: 120, render: (r) => r.reference ?? EMPTY_DASH },
    { key: 'amount', title: t('queue.columns.amount'), width: 130, render: (r) => h(TMoney, { value: r.amount, currency: r.currency }) },
  ]
}

/**
 * Deposit detail line columns.
 *
 * A receipt line shows the receipt number and payer; an other-funds line shows
 * the credit account instead. Both render in one table because on the deposit
 * slip they are both just "money that went in".
 */
export function buildDepositLineColumns(t: (key: string) => string): DataTableColumns<DepositLineDto> {
  return [
    { key: 'lineNumber', title: t('lines.no'), width: 60, render: (r) => String(r.lineNumber) },
    {
      key: 'source',
      title: t('lines.source'),
      minWidth: 200,
      render: (r) => (r.paymentEntryId ? (r.paymentNumber ?? t('lines.receipt')) : (r.accountName ?? t('lines.otherFunds'))),
    },
    { key: 'partyName', title: t('lines.party'), minWidth: 140, render: (r) => r.partyName ?? r.description ?? EMPTY_DASH },
    { key: 'reference', title: t('lines.reference'), width: 120, render: (r) => r.reference ?? EMPTY_DASH },
    { key: 'amount', title: t('lines.amount'), width: 140, render: (r) => h(TMoney, { value: r.amount }) },
  ]
}

/** 存款单筛选：后端 `DepositQueryDto` 支持状态与日期区间（`from` / `to`）。 */
export function buildDepositSearchFields(t: (key: string) => string): SearchFieldItem[] {
  return [
    {
      key: 'status',
      label: t('columns.status'),
      type: 'select',
      options: [
        { label: t('status.draft'), value: 'Draft' },
        { label: t('status.posted'), value: 'Posted' },
        { label: t('status.voided'), value: 'Voided' },
      ],
    },
    { key: 'from', label: t('search.dateFrom'), type: 'date' },
    { key: 'to', label: t('search.dateTo'), type: 'date' },
  ]
}
