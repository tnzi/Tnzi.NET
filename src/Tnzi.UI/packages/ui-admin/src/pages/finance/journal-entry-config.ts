import { EMPTY_DASH } from '../../utils/placeholders'
import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { TRelativeTime } from '@tnzi/ui'

import { JournalEntryStatus } from '../../services/bridges/finance-bridge'
import { fmtDate } from './money'
import { financeSourceTypeLabel } from './source-type'
import TMoney from '../../components/finance/TMoney.vue'

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
  txnTotalDebit?: number
  txnTotalCredit?: number
  creationTime?: string
}

/**
 * 合计取值：草稿读交易币,其余读本位币。
 *
 * 草稿的本位币合计按设计为 0（尚无汇率,本位币金额还不存在）,直接渲染 totalDebit 会让列表上
 * 每张草稿都显示 $0.00 —— 偏偏"平不平"正是看草稿时唯一要问的事。两者币种口径不同,所以按状态
 * 取,不做 `||` 回退:已过账凭证若合计真为 0,回退会把它替换成交易币数字,反而说了谎。
 * 币种列就在旁边,外币草稿的读数因此仍然自洽。
 */
export function entryTotal(row: JournalRow, side: 'debit' | 'credit'): number | undefined {
  const draft = row.status === JournalEntryStatus.Draft
  if (side === 'debit') return draft ? row.txnTotalDebit : row.totalDebit
  return draft ? row.txnTotalCredit : row.totalCredit
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
      render: (row) => row.number ?? EMPTY_DASH,
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
    { key: 'postingDate', title: 'columns.postingDate', width: 120, render: (row) => fmtDate(row.postingDate) },
    { key: 'memo', title: 'columns.memo', minWidth: 200, render: (row) => row.memo ?? EMPTY_DASH },
    { key: 'currency', title: 'columns.currency', width: 90, mobileHidden: true },
    {
      key: 'totalDebit',
      title: 'columns.totalDebit',
      width: 130,
      render: (row) => h(TMoney, { value: entryTotal(row, 'debit'), class: 'font-600' }),
    },
    {
      key: 'totalCredit',
      title: 'columns.totalCredit',
      width: 130,
      render: (row) => h(TMoney, { value: entryTotal(row, 'credit'), class: 'font-600' }),
    },
    {
      key: 'sourceType',
      title: 'columns.sourceType',
      width: 130,
      mobileHidden: true,
      render: (row) => financeSourceTypeLabel(row.sourceType),
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
