import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import { ReconciliationStatus, type ReconciliationDto } from '../../services/bridges/finance-bridge'
import { amountCell, fmtAmount } from './money'
import { formatDateOnly } from '@tnzi/core'
import TStatusBadge from '../../components/display/TStatusBadge.vue'

/** All-optional row shape (house pattern) so ColumnDef stays assignable. */
export type ReconciliationRow = Partial<ReconciliationDto>

// Draft 色调与单据页 DOC_STATUS_META 对齐（warning）
const STATUS_BADGE: Record<string, { label: string; type: 'warning' | 'success' }> = {
  [ReconciliationStatus.Draft]: { label: 'status.draft', type: 'warning' },
  [ReconciliationStatus.Completed]: { label: 'status.completed', type: 'success' },
}

export function buildReconciliationColumns(t: (key: string) => string): ColumnDef<ReconciliationRow>[] {
  return [
    { key: 'accountName', title: t('columns.account'), minWidth: 180, primary: true, render: (r) => r.accountName ?? r.accountId ?? '—' },
    { key: 'statementDate', title: t('columns.statementDate'), width: 130, render: (r) => formatDateOnly(r.statementDate, { utc: true }) },
    { key: 'statementEndingBalance', title: t('columns.endingBalance'), width: 140, render: (r) => amountCell(fmtAmount(r.statementEndingBalance ?? 0)) },
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
