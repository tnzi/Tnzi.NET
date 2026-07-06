import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { TRelativeTime } from '@tnzi/ui'
import { formatDateOnly } from '@tnzi/core'
import { JournalEntryStatus } from '../../services/bridges/finance-bridge'
import { amountCell, fmtMoney } from './money'

/** All-optional row shape (house pattern). */
export interface JournalRow {
  id?: string
  number?: string | null
  status?: JournalEntryStatus
  postingDate?: string
  memo?: string | null
  currency?: string
  exchangeRate?: number
  sourceType?: string | null
  sourceId?: string | null
  totalDebit?: number
  totalCredit?: number
  creationTime?: string
}

/** JournalEntryStatus → badge meta（键为后端 PascalCase 成员名；labels 为页面作用域 i18n 键）。 */
export const ENTRY_STATUS_META: Record<string, { type: 'info' | 'success' | 'warning' | 'error' | 'default'; label: string }> = {
  [JournalEntryStatus.Draft]: { type: 'warning', label: 'status.draft' },
  [JournalEntryStatus.Posted]: { type: 'success', label: 'status.posted' },
  [JournalEntryStatus.Reversed]: { type: 'default', label: 'status.reversed' },
}

export function buildJournalEntryColumns(t: (key: string) => string): ColumnDef<JournalRow>[] {
  return [
    {
      key: 'number',
      title: 'columns.number',
      width: 130,
      primary: true,
      render: (row) => row.number ?? '—',
    },
    {
      key: 'status',
      title: 'columns.status',
      width: 110,
      render: (row) => {
        const meta = ENTRY_STATUS_META[row.status ?? '']
        return h(TStatusBadge, {
          value: row.status ?? '',
          type: meta?.type ?? 'default',
          label: meta ? t(meta.label) : String(row.status ?? ''),
        })
      },
    },
    { key: 'postingDate', title: 'columns.postingDate', width: 120, render: (row) => formatDateOnly(row.postingDate, { utc: true }) },
    { key: 'memo', title: 'columns.memo', minWidth: 200, render: (row) => row.memo ?? '—' },
    { key: 'currency', title: 'columns.currency', width: 90, mobileHidden: true },
    {
      key: 'totalDebit',
      title: 'columns.totalDebit',
      width: 130,
      render: (row) => amountCell(fmtMoney(row.totalDebit, undefined), true),
    },
    {
      key: 'totalCredit',
      title: 'columns.totalCredit',
      width: 130,
      render: (row) => amountCell(fmtMoney(row.totalCredit, undefined), true),
    },
    {
      key: 'sourceType',
      title: 'columns.sourceType',
      width: 130,
      mobileHidden: true,
      render: (row) => row.sourceType ?? '—',
    },
    {
      key: 'creationTime',
      title: 'columns.creationTime',
      width: 140,
      mobileHidden: true,
      render: (row) => h(TRelativeTime, { value: row.creationTime }),
    },
  ]
}
