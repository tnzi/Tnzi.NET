import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { formatDateOnly, formatDateTime } from '@tnzi/core'

/** All-optional row shape (house pattern). */
export interface FiscalYearRow {
  id?: string
  name?: string
  startDate?: string
  endDate?: string
  isClosed?: boolean
  closedTime?: string | null
}

export function buildFiscalYearColumns(t: (key: string) => string): ColumnDef<FiscalYearRow>[] {
  return [
    { key: 'name', title: 'columns.name', minWidth: 140, primary: true },
    { key: 'startDate', title: 'columns.startDate', width: 130, render: (row) => formatDateOnly(row.startDate, { utc: true }) },
    { key: 'endDate', title: 'columns.endDate', width: 130, render: (row) => formatDateOnly(row.endDate, { utc: true }) },
    {
      key: 'isClosed',
      title: 'columns.isClosed',
      width: 110,
      render: (row) =>
        h(TStatusBadge, {
          value: row.isClosed ? 1 : 0,
          type: row.isClosed ? 'default' : 'success',
          label: row.isClosed ? t('status.closed') : t('status.open'),
        }),
    },
    { key: 'closedTime', title: 'columns.closedTime', width: 150, mobileHidden: true, render: (row) => formatDateTime(row.closedTime) },
  ]
}

export const fiscalYearFormSchema: FormSchemaItem[] = [
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'text', required: true },
  { key: 'startDate', labelKey: 'form.startDate', label: 'Start Date', type: 'date', required: true },
  { key: 'endDate', labelKey: 'form.endDate', label: 'End Date', type: 'date', required: true },
]
