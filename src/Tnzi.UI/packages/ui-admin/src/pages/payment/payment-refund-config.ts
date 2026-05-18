import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import TRelativeTime from '../../components/display/TRelativeTime.vue'

interface RefundRow {
  id?: string
  refundNo?: string
  tradeNo?: string
  paymentId?: string
  paymentNo?: string
  refundAmount?: number
  currency?: string
  reason?: string
  status?: 'pending' | 'approved' | 'rejected' | 'completed' | 'failed' | string
  creationTime?: string
  completedTime?: string
  approverId?: string
  approveTime?: string
}

const STATUS_MAP: Record<string, { type: 'info' | 'success' | 'warning' | 'error' | 'default'; label: string }> = {
  pending: { type: 'warning', label: 'Pending Review' },
  approved: { type: 'info', label: 'Approved' },
  rejected: { type: 'error', label: 'Rejected' },
  completed: { type: 'success', label: 'Completed' },
  failed: { type: 'error', label: 'Failed' },
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
  { key: 'refundNo', title: 'columns.refundNo', width: 200, fixed: 'left' },
  { key: 'paymentNo', title: 'columns.paymentNo', width: 200 },
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
  { key: 'reason', title: 'columns.reason' },
  {
    key: 'status',
    title: 'columns.status',
    width: 140,
    render: (row) => {
      const m = STATUS_MAP[row.status ?? ''] ?? { type: 'default' as const, label: row.status ?? '—' }
      return h(TStatusBadge, { value: row.status ?? 'unknown', type: m.type, label: m.label })
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
    width: 140,
    fixed: 'right',
    render: (row) => h(TRelativeTime, { value: row.completedTime }),
  },
]

export const refundFormSchema: FormSchemaItem[] = [
  { key: 'refundNo',     label: 'Refund No',    type: 'text' },
  { key: 'paymentNo',    label: 'Payment No',   type: 'text' },
  { key: 'refundAmount', label: 'Amount',       type: 'number', required: true },
  { key: 'reason',       label: 'Reason',       type: 'textarea', required: true },
  { key: 'status',       label: 'Status',       type: 'text' },
]
