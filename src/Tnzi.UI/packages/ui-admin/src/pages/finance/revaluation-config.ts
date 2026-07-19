import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import { JournalEntryStatus, type JournalEntryDto } from '../../services/bridges/finance-bridge'
import { amountCell, fmtAmount } from './money'
import { formatDateOnly, formatDateTime } from '@tnzi/core'
import TStatusBadge from '../../components/display/TStatusBadge.vue'

/**
 * Revaluation history rows are the summary vouchers a run posts
 * (journal entries with `sourceType == "Revaluation"`). All-optional shape
 * (house pattern) so ColumnDef stays assignable to the CRUD shell.
 */
export type RevaluationHistoryRow = Partial<JournalEntryDto>

const STATUS_BADGE: Record<string, { label: string; type: 'success' | 'default' | 'warning' }> = {
  [JournalEntryStatus.Draft]: { label: 'status.draft', type: 'warning' },
  [JournalEntryStatus.Posted]: { label: 'status.posted', type: 'success' },
  [JournalEntryStatus.Reversed]: { label: 'status.reversed', type: 'default' },
}

export function buildRevaluationHistoryColumns(t: (key: string) => string): ColumnDef<RevaluationHistoryRow>[] {
  return [
    { key: 'number', title: t('history.number'), width: 140, primary: true, render: (r) => r.number ?? '—' },
    // 凭证 postingDate == 重估截止日（asOf）；sourceId 亦为 asOf 的 yyyy-MM-dd。
    { key: 'postingDate', title: t('history.asOf'), width: 120, render: (r) => formatDateOnly(r.postingDate, { utc: true }) },
    { key: 'memo', title: t('history.memo'), minWidth: 200, render: (r) => r.memo ?? '—' },
    // 借贷相等，展示 totalDebit 作为本次重估的调整总额（本位币）。
    { key: 'totalDebit', title: t('history.adjustment'), width: 140, render: (r) => amountCell(fmtAmount(r.totalDebit ?? 0)) },
    {
      key: 'status',
      title: t('history.status'),
      width: 110,
      render: (r) => {
        const meta = STATUS_BADGE[String(r.status ?? '')]
        return meta ? h(TStatusBadge, { value: String(r.status ?? ''), type: meta.type, label: t(meta.label) }) : String(r.status ?? '')
      },
    },
    { key: 'creationTime', title: t('history.creationTime'), width: 160, mobileHidden: true, render: (r) => formatDateTime(r.creationTime) },
  ]
}
