import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { TRelativeTime } from '@tnzi/ui'

/**
 * RefundDto.status is the RefundStatus enum:
 *   0=Pending / 1=Processing / 2=Approved / 3=Rejected /
 *   4=Refunding / 5=Succeeded / 6=Failed / 7=Cancelled.
 * Backend serialises as int — the previous string-keyed map never matched.
 */
interface RefundRow {
  id?: string
  refundNo?: string
  tradeNo?: string
  paymentId?: string
  paymentNo?: string
  refundAmount?: number
  currency?: string
  reason?: string
  status?: number
  creationTime?: string
  completedTime?: string
  approverId?: string
  approveTime?: string
}

const STATUS_MAP: Record<number, { type: 'info' | 'success' | 'warning' | 'error' | 'default'; label?: string; labelKey?: string }> = {
  0: { type: 'warning', label: 'Pending Review' },
  1: { type: 'info',    label: 'Processing' },
  2: { type: 'info',    labelKey: 'admin.shared.status.approved' },
  3: { type: 'error',   labelKey: 'admin.shared.status.rejected' },
  4: { type: 'info',    label: 'Refunding' },
  5: { type: 'success', label: 'Succeeded' },
  6: { type: 'error',   labelKey: 'admin.shared.status.failed' },
  7: { type: 'default', labelKey: 'admin.shared.status.cancelled' },
}

function fmtMoney(amount?: number, currency?: string): string {
  if (amount === null || amount === undefined) return '—'
  try {
    return new Intl.NumberFormat(undefined, {
      style: 'currency',
      currency: currency ?? 'USD',
    }).format(amount)
  } catch {
    return `${currency ?? ''} ${amount.toFixed(2)}`
  }
}

export const refundColumns: ColumnDef<RefundRow>[] = [
  { key: 'refundNo', title: 'columns.refundNo', minWidth: 150 },
  { key: 'paymentNo', title: 'columns.paymentNo', minWidth: 150 },
  {
    key: 'refundAmount',
    title: 'columns.refundAmount',
    width: 140,
    render: (row) =>
      h(
        'span',
        { style: 'font-variant-numeric: tabular-nums; font-weight: 500' },
        fmtMoney(row.refundAmount, row.currency),
      ),
  },
  { key: 'reason', title: 'columns.reason', minWidth: 160 },
  {
    key: 'status',
    title: 'columns.status',
    width: 140,
    render: (row) => {
      const v = typeof row.status === 'number' ? row.status : Number(row.status)
      const m = STATUS_MAP[v]
      if (m) {
        return h(TStatusBadge, { value: v, type: m.type, label: m.label, labelKey: m.labelKey })
      }
      return h(TStatusBadge, { value: v, type: 'default', label: String(row.status ?? '—') })
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

export const refundFormSchema: FormSchemaItem[] = [
  { key: 'refundNo',     labelKey: 'form.refundNo', label: 'Refund No',    type: 'text' },
  { key: 'paymentNo',    labelKey: 'form.paymentNo', label: 'Payment No',   type: 'text' },
  { key: 'refundAmount', labelKey: 'form.refundAmount', label: 'Amount',       type: 'number', required: true },
  { key: 'reason',       labelKey: 'form.reason', label: 'Reason',       type: 'textarea', required: true },
  { key: 'status',       labelKey: 'form.status', label: 'Status',       type: 'text' },
]
