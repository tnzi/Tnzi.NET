import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import type { TransferDto } from '../../services/bridges/finance-bridge'
import { amountCell, fmtMoney } from './money'
import { formatDateOnly } from '@tnzi/core'
import { docStatusBadge } from './document-config'

/** All-optional row shape (house pattern) so ColumnDef stays assignable. */
export type TransferRow = Partial<TransferDto>

export function buildTransferColumns(t: (key: string) => string): ColumnDef<TransferRow>[] {
  return [
    { key: 'number', title: t('columns.number'), width: 130, render: (r) => r.number ?? '—' },
    // Transfer 与五单据共用 FinanceDocumentStatus：徽章走 document-config 单一真值源
    { key: 'status', title: t('columns.status'), width: 110, render: (r) => docStatusBadge(t, r.status) },
    { key: 'transferDate', title: t('columns.date'), width: 120, render: (r) => formatDateOnly(r.transferDate, { utc: true }) },
    { key: 'fromAccountName', title: t('columns.from'), minWidth: 160, render: (r) => r.fromAccountName ?? r.fromAccountId ?? '—' },
    { key: 'toAccountName', title: t('columns.to'), minWidth: 160, render: (r) => r.toAccountName ?? r.toAccountId ?? '—' },
    { key: 'amount', title: t('columns.amount'), width: 130, render: (r) => amountCell(fmtMoney(r.amount, r.currency)) },
    { key: 'reference', title: t('columns.reference'), minWidth: 120, mobileHidden: true, render: (r) => r.reference ?? '—' },
  ]
}

/** Draft create/edit form (accounts through the finance-account renderer). */
export const transferFormSchema: FormSchemaItem[] = [
  { key: 'fromAccountId', labelKey: 'form.fromAccount', label: 'From Account', type: 'finance-account', required: true },
  { key: 'toAccountId', labelKey: 'form.toAccount', label: 'To Account', type: 'finance-account', required: true },
  { key: 'transferDate', labelKey: 'form.date', label: 'Date', type: 'date', required: true },
  { key: 'amount', labelKey: 'form.amount', label: 'Amount', type: 'number', required: true },
  { key: 'currency', labelKey: 'form.currency', label: 'Currency', type: 'text' },
  { key: 'reference', labelKey: 'form.reference', label: 'Reference', type: 'text' },
  { key: 'memo', labelKey: 'form.memo', label: 'Memo', type: 'textarea' },
]
