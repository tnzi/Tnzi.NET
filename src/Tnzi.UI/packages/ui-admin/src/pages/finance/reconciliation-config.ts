import { EMPTY_DASH } from '../../utils/placeholders'
import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import { ReconciliationStatus, type ReconciliationDto } from '../../services/bridges/finance-bridge'
import { fmtDate } from './money'

import TStatusBadge from '../../components/display/TStatusBadge.vue'
import TMoney from '../../components/finance/TMoney.vue'
import type { SearchFieldItem } from '../../components/crud/TCrudSearchAdvanced.vue'

/** All-optional row shape (house pattern) so ColumnDef stays assignable. */
export type ReconciliationRow = Partial<ReconciliationDto>

// Draft 色调与单据页 DOC_STATUS_META 对齐（warning）
const STATUS_BADGE: Record<string, { label: string; type: 'warning' | 'success' }> = {
  [ReconciliationStatus.Draft]: { label: 'status.draft', type: 'warning' },
  [ReconciliationStatus.Completed]: { label: 'status.completed', type: 'success' },
}

export function buildReconciliationColumns(t: (key: string) => string): ColumnDef<ReconciliationRow>[] {
  return [
    { key: 'accountName', title: t('columns.account'), minWidth: 180, primary: true, render: (r) => r.accountName ?? r.accountId ?? EMPTY_DASH },
    { key: 'currency', title: t('columns.currency'), width: 90, render: (r) => (r.currency ? h(TStatusBadge, { value: r.currency, type: 'info', label: r.currency }) : EMPTY_DASH) },
    { key: 'statementDate', title: t('columns.statementDate'), width: 130, render: (r) => fmtDate(r.statementDate) },
    { key: 'statementEndingBalance', title: t('columns.endingBalance'), width: 140, render: (r) => h(TMoney, { value: r.statementEndingBalance ?? 0, currency: r.currency }) },
    {
      key: 'status',
      title: t('columns.status'),
      width: 120,
      render: (r) => {
        const meta = STATUS_BADGE[String(r.status ?? '')]
        return meta ? h(TStatusBadge, { value: String(r.status ?? ''), type: meta.type, label: t(meta.label) }) : String(r.status ?? '')
      },
    },
    { key: 'lineCount', title: t('columns.lines'), width: 90, render: (r) => String(r.lineCount ?? 0) },
  ]
}

export const reconciliationFormSchema: FormSchemaItem[] = [
  { key: 'accountId', labelKey: 'form.account', label: 'Account', type: 'finance-account', required: true },
  { key: 'statementDate', labelKey: 'form.statementDate', label: 'Statement Date', type: 'date', required: true },
  { key: 'statementEndingBalance', labelKey: 'form.endingBalance', label: 'Statement Ending Balance', type: 'number', required: true },
  { key: 'note', labelKey: 'form.note', label: 'Note', type: 'textarea' },
]

/** 对账筛选：后端支持状态与科目（科目走关键字之外的独立维度）。 */
export function buildReconciliationSearchFields(t: (key: string) => string): SearchFieldItem[] {
  return [
    {
      key: 'status',
      label: t('columns.status'),
      type: 'select',
      options: [
        { label: t('status.draft'), value: 'Draft' },
        { label: t('status.completed'), value: 'Completed' },
      ],
    },
  ]
}
