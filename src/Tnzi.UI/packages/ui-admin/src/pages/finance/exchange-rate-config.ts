import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import { TRelativeTime } from '@tnzi/ui'
import { formatDateOnly } from '@tnzi/core'
import { amountCell } from './money'

/** All-optional row shape (house pattern). */
export interface RateRow {
  id?: string
  fromCurrency?: string
  toCurrency?: string
  rate?: number
  rateDate?: string
  source?: string | null
  creationTime?: string
}

export const exchangeRateColumns: ColumnDef<RateRow>[] = [
  { key: 'fromCurrency', title: 'columns.fromCurrency', width: 110, primary: true },
  { key: 'toCurrency', title: 'columns.toCurrency', width: 110 },
  {
    key: 'rate',
    title: 'columns.rate',
    width: 140,
    render: (row) => amountCell(String(row.rate), true),
  },
  { key: 'rateDate', title: 'columns.rateDate', width: 130, render: (row) => formatDateOnly(row.rateDate, { utc: true }) },
  { key: 'source', title: 'columns.source', minWidth: 120, render: (row) => row.source ?? '—' },
  {
    key: 'creationTime',
    title: 'columns.creationTime',
    width: 140,
    mobileHidden: true,
    render: (row) => h(TRelativeTime, { value: row.creationTime }),
  },
]

export const exchangeRateFormSchema: FormSchemaItem[] = [
  { key: 'fromCurrency', labelKey: 'form.fromCurrency', label: 'From Currency', type: 'text', required: true },
  { key: 'toCurrency', labelKey: 'form.toCurrency', label: 'To Currency', type: 'text', required: true },
  { key: 'rate', labelKey: 'form.rate', label: 'Rate', type: 'number', required: true },
  { key: 'rateDate', labelKey: 'form.rateDate', label: 'Rate Date', type: 'date', required: true },
  { key: 'source', labelKey: 'form.source', label: 'Source', type: 'text' },
]
