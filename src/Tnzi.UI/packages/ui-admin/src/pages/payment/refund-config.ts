import { EMPTY_DASH } from '../../utils/placeholders'
import { h } from 'vue'
import { formatCurrency } from '@tnzi/core'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import type { StatusType } from '@tnzi/ui'
import { TRelativeTime } from '@tnzi/ui'

/**
 * Refunds page config - aligned with the real `RefundDto`
 * (Tnzi.Payment.Dtos.RefundDto). The record keys the related payment on
 * `tradeNo` (mapped from `Refund.Payment.TradeNo`); there is no `paymentNo` /
 * `paymentId` / `amount` field - the money is `refundAmount`.
 *
 * `status` serialises as the RefundStatus member name (global
 * JsonStringEnumConverter): Pending / Processing / Approved / Rejected /
 * Refunding / Succeeded / Failed / Cancelled.
 */
interface RefundRow {
  id?: string
  refundNo?: string
  tradeNo?: string
  refundAmount?: number
  currency?: string
  reason?: string
  status?: string
  approverId?: string | null
  approveTime?: string | null
  approveRemark?: string | null
  completedTime?: string | null
  creationTime?: string
}

const STATUS_TONE: Record<string, StatusType> = {
  Pending: 'warning',
  Processing: 'info',
  Approved: 'info',
  Rejected: 'error',
  Refunding: 'info',
  Succeeded: 'success',
  Failed: 'error',
  Cancelled: 'default',
}
// member name → i18n leaf key under `payment.refunds.status.*`
const STATUS_KEY: Record<string, string> = {
  Pending: 'pending',
  Processing: 'processing',
  Approved: 'approved',
  Rejected: 'rejected',
  Refunding: 'refunding',
  Succeeded: 'succeeded',
  Failed: 'failed',
  Cancelled: 'cancelled',
}

function money(amount?: number, currency?: string): string {
  if (amount === null || amount === undefined) return EMPTY_DASH
  return formatCurrency(Number(amount), String(currency || 'USD'))
}

/**
 * Build the Refunds columns. A factory so the status label + currency render
 * resolve through the page translator `t` (finance `buildXColumns(t)` idiom).
 */
export function buildRefundColumns(t: (key: string) => string): ColumnDef<RefundRow>[] {
  return [
    { key: 'refundNo', title: 'columns.refundNo', minWidth: 150 },
    { key: 'tradeNo', title: 'columns.tradeNo', minWidth: 150 },
    {
      key: 'refundAmount',
      title: 'columns.refundAmount',
      width: 140,
      align: 'right',
      render: (row) =>
        h(
          'span',
          { style: 'font-variant-numeric: tabular-nums; font-weight: 500' },
          money(row.refundAmount, row.currency),
        ),
    },
    { key: 'reason', title: 'columns.reason', minWidth: 160 },
    {
      key: 'status',
      title: 'columns.status',
      width: 140,
      render: (row) => {
        const v = String(row.status ?? '')
        return h(TStatusBadge, {
          value: v,
          type: STATUS_TONE[v] ?? 'default',
          label: STATUS_KEY[v] ? t(`status.${STATUS_KEY[v]}`) : v || EMPTY_DASH,
        })
      },
    },
    {
      key: 'creationTime',
      title: 'columns.creationTime',
      width: 140,
      render: (row) => h(TRelativeTime, { value: row.creationTime }),
    },
    {
      key: 'completedTime',
      title: 'columns.completedTime',
      width: 150,
      render: (row) => h(TRelativeTime, { value: row.completedTime }),
    },
  ]
}

/** View-form schema (read-only quick preview), reachable via the row `view` action. */
export const refundFormSchema: FormSchemaItem[] = [
  { key: 'refundNo', labelKey: 'form.refundNo', label: 'Refund No', type: 'text' },
  { key: 'tradeNo', labelKey: 'form.tradeNo', label: 'Trade No', type: 'text' },
  { key: 'refundAmount', labelKey: 'form.refundAmount', label: 'Amount', type: 'number' },
  { key: 'currency', labelKey: 'form.currency', label: 'Currency', type: 'text' },
  { key: 'reason', labelKey: 'form.reason', label: 'Reason', type: 'textarea' },
  { key: 'status', labelKey: 'form.status', label: 'Status', type: 'text' },
  { key: 'approveRemark', labelKey: 'form.approveRemark', label: 'Approval Remark', type: 'textarea' },
]
