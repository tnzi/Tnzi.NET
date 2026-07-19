import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { formatDateOnly } from '@tnzi/core'
import { PayFrequency, PayRunStatus, type CreatePayRunDto } from '../../services/bridges/payroll-bridge'
import { enumKey } from './setup-config'
import { amountCell, fmtAmount } from '../finance/money'

/** All-optional row shape (house pattern). */
export interface PayRunRow {
  id?: string
  number?: string | null
  status?: PayRunStatus
  periodStart?: string
  periodEnd?: string
  payDate?: string
  frequency?: PayFrequency
  source?: string
  employeeCount?: number
  errorCount?: number
  grossTotal?: number
  netTotal?: number
  creationTime?: string
}

/** naive TStatusBadge type per pay-run status. */
export const RUN_STATUS_TYPE: Record<string, 'default' | 'info' | 'success' | 'warning' | 'error'> = {
  Draft: 'default',
  Calculated: 'info',
  Posted: 'success',
  PartiallyPaid: 'warning',
  Paid: 'success',
  Voided: 'error',
}

export function buildPayRunColumns(t: (key: string) => string): ColumnDef<PayRunRow>[] {
  return [
    { key: 'number', title: 'columns.number', width: 140, primary: true, render: (r) => r.number ?? t('draftLabel') },
    {
      key: 'status',
      title: 'columns.status',
      width: 120,
      render: (r) =>
        h(TStatusBadge, {
          value: 1,
          type: RUN_STATUS_TYPE[String(r.status ?? '')] ?? 'default',
          label: t(`status.${enumKey(r.status)}`),
        }),
    },
    { key: 'period', title: 'columns.period', minWidth: 190, render: (r) => `${formatDateOnly(r.periodStart, { utc: true })} ~ ${formatDateOnly(r.periodEnd, { utc: true })}` },
    { key: 'payDate', title: 'columns.payDate', width: 120, mobileHidden: true, render: (r) => formatDateOnly(r.payDate, { utc: true }) },
    { key: 'frequency', title: 'columns.frequency', width: 120, mobileHidden: true, render: (r) => t(`frequency.${enumKey(r.frequency)}`) },
    { key: 'employeeCount', title: 'columns.employees', width: 100, render: (r) => amountCell(String(r.employeeCount ?? 0)) },
    { key: 'netTotal', title: 'columns.netTotal', width: 130, render: (r) => amountCell(fmtAmount(r.netTotal), true) },
    { key: 'source', title: 'columns.source', width: 120, mobileHidden: true, render: (r) => t(`source.${enumKey(r.source)}`) },
  ]
}

export const payRunFormSchema: FormSchemaItem[] = [
  { key: 'periodStart', labelKey: 'form.periodStart', label: 'Period Start', type: 'date', required: true },
  { key: 'periodEnd', labelKey: 'form.periodEnd', label: 'Period End', type: 'date', required: true },
  { key: 'payDate', labelKey: 'form.payDate', label: 'Pay Date', type: 'date', required: true },
  {
    key: 'frequency',
    labelKey: 'form.frequency',
    label: 'Frequency',
    type: 'select',
    required: true,
    options: [
      { label: 'Monthly', value: PayFrequency.Monthly, labelKey: 'frequency.monthly' },
      { label: 'Semi-monthly', value: PayFrequency.SemiMonthly, labelKey: 'frequency.semiMonthly' },
      { label: 'Bi-weekly', value: PayFrequency.BiWeekly, labelKey: 'frequency.biWeekly' },
      { label: 'Weekly', value: PayFrequency.Weekly, labelKey: 'frequency.weekly' },
    ],
  },
  { key: 'structureId', labelKey: 'form.structure', label: 'Structure Filter', type: 'payroll-structure' },
  { key: 'memo', labelKey: 'form.memo', label: 'Memo', type: 'textarea' },
]

export function toPayRunPayload(d: Record<string, unknown>, toIso: (v: unknown) => string): CreatePayRunDto {
  return {
    periodStart: toIso(d.periodStart),
    periodEnd: toIso(d.periodEnd),
    payDate: toIso(d.payDate),
    frequency: (d.frequency as PayFrequency) ?? PayFrequency.Monthly,
    structureId: (d.structureId as string | null) || null,
    memo: (d.memo as string | null) || null,
  }
}
