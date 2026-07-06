import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { TRelativeTime } from '@tnzi/ui'
import { formatDateOnly } from '@tnzi/core'
import { FinanceDocumentStatus } from '../../services/bridges/finance-bridge'
import { amountCell, fmtMoney } from './money'

/** All-optional row shape shared by the five document list pages. */
export interface FinanceDocRow {
  id?: string
  number?: string | null
  status?: FinanceDocumentStatus
  docDate?: string
  dueDate?: string | null
  customerName?: string | null
  vendorName?: string | null
  partyName?: string | null
  currency?: string
  total?: number
  amount?: number
  appliedTotal?: number
  memo?: string | null
  creationTime?: string
}

/** FinanceDocumentStatus → badge meta（键为后端 PascalCase 成员名；label 为页面作用域 i18n 键）。 */
export const DOC_STATUS_META: Record<string, { type: 'info' | 'success' | 'warning' | 'error' | 'default'; label: string }> = {
  [FinanceDocumentStatus.Draft]: { type: 'warning', label: 'status.draft' },
  [FinanceDocumentStatus.Posted]: { type: 'success', label: 'status.posted' },
  [FinanceDocumentStatus.PartiallyPaid]: { type: 'info', label: 'status.partiallyPaid' },
  [FinanceDocumentStatus.Paid]: { type: 'default', label: 'status.paid' },
  [FinanceDocumentStatus.Voided]: { type: 'error', label: 'status.voided' },
}

export function docStatusBadge(t: (key: string) => string, status?: FinanceDocumentStatus) {
  const meta = DOC_STATUS_META[status ?? '']
  return h(TStatusBadge, {
    value: status ?? '',
    type: meta?.type ?? 'default',
    label: meta ? t(meta.label) : String(status ?? ''),
  })
}

/** 单据列表列（partyKey: 'customerName' | 'vendorName' | 'partyName'；amountKey: 'total' | 'amount'）。 */
export function buildDocumentColumns(
  t: (key: string) => string,
  partyKey: 'customerName' | 'vendorName' | 'partyName',
  options?: { amountKey?: 'total' | 'amount'; showApplied?: boolean; showDueDate?: boolean },
): ColumnDef<FinanceDocRow>[] {
  const amountKey = options?.amountKey ?? 'total'
  const columns: ColumnDef<FinanceDocRow>[] = [
    { key: 'number', title: 'columns.number', width: 130, primary: true, render: (row) => row.number ?? '—' },
    { key: 'status', title: 'columns.status', width: 120, render: (row) => docStatusBadge(t, row.status) },
    { key: 'docDate', title: 'columns.docDate', width: 110, render: (row) => formatDateOnly(row.docDate, { utc: true }) },
    { key: partyKey, title: 'columns.party', minWidth: 160, render: (row) => row[partyKey] ?? '—' },
    { key: 'currency', title: 'columns.currency', width: 90, mobileHidden: true },
    { key: amountKey, title: 'columns.total', width: 130, render: (row) => amountCell(fmtMoney(row[amountKey], row.currency), true) },
  ]

  if (options?.showApplied) {
    columns.push({ key: 'appliedTotal', title: 'columns.applied', width: 120, mobileHidden: true, render: (row) => amountCell(fmtMoney(row.appliedTotal, row.currency)) })
  }
  if (options?.showDueDate) {
    columns.push({ key: 'dueDate', title: 'columns.dueDate', width: 110, mobileHidden: true, render: (row) => formatDateOnly(row.dueDate, { utc: true }) })
  }

  columns.push({
    key: 'creationTime',
    title: 'columns.creationTime',
    width: 140,
    mobileHidden: true,
    render: (row) => h(TRelativeTime, { value: row.creationTime }),
  })
  return columns
}
