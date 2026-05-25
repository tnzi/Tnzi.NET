import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'

/**
 * Payment Order page config — aligned with PaymentDto (2026-04-14 Plan C unstub).
 *
 * `status` is the PaymentStatus enum (0=Pending / 1=Processing / 2=Succeeded /
 *  3=Failed / 4=Closed / 5=Cancelled / 6=Expired / 7=Refunding / 8=Refunded).
 * Backend serialises it as an int — the previous string-keyed map never
 * matched any row, so the badge silently fell through to the raw number.
 *
 * Page is read-only; state transitions flow through close/sync admin endpoints.
 */
const ORDER_STATUS_MAP: Record<number, { type: 'info' | 'success' | 'warning' | 'error' | 'default'; label?: string; labelKey?: string }> = {
  0: { type: 'warning', labelKey: 'admin.shared.status.pending' },
  1: { type: 'info',    label: 'Processing' },
  2: { type: 'success', label: 'Succeeded' },
  3: { type: 'error',   labelKey: 'admin.shared.status.failed' },
  4: { type: 'default', label: 'Closed' },
  5: { type: 'default', labelKey: 'admin.shared.status.cancelled' },
  6: { type: 'default', label: 'Expired' },
  7: { type: 'info',    label: 'Refunding' },
  8: { type: 'default', label: 'Refunded' },
}

export const orderColumns: ColumnDef[] = [
  { key: 'paymentNo',       title: 'columns.paymentNo' },
  { key: 'businessOrderNo', title: 'columns.businessOrderNo' },
  { key: 'userId',          title: 'columns.userId' },
  { key: 'amount',          title: 'columns.amount' },
  { key: 'currency',        title: 'columns.currency' },
  {
    key: 'status',
    title: 'columns.status',
    width: 130,
    render: (row) => {
      const v = typeof row.status === 'number' ? row.status : Number(row.status)
      const m = ORDER_STATUS_MAP[v]
      if (m) {
        return h(TStatusBadge, { value: v, type: m.type, label: m.label, labelKey: m.labelKey })
      }
      return h(TStatusBadge, { value: v, type: 'default', label: String(row.status ?? '—') })
    },
  },
  { key: 'paymentMethod',   title: 'columns.paymentMethod' },
  { key: 'paidTime',        title: 'columns.paidTime' },
  { key: 'creationTime',    title: 'columns.creationTime', visible: false },
  { key: 'refundAmount',    title: 'columns.refundAmount',   visible: false },
]

export const orderFormSchema: FormSchemaItem[] = [
  { key: 'paymentNo',       labelKey: 'form.paymentNo', label: 'Payment No',     type: 'text' },
  { key: 'businessOrderNo', labelKey: 'form.businessOrderNo', label: 'Business Order', type: 'text' },
  { key: 'userId',          labelKey: 'form.userId', label: 'Customer ID',    type: 'text' },
  { key: 'amount',          labelKey: 'form.amount', label: 'Amount',         type: 'number' },
  { key: 'currency',        labelKey: 'form.currency', label: 'Currency',       type: 'select', options: [
    { label: 'CNY', value: 'CNY' },
    { label: 'USD', value: 'USD' },
    { label: 'EUR', value: 'EUR' },
  ] },
  { key: 'status',          labelKey: 'form.status', label: 'Status',         type: 'text' },
  { key: 'paymentMethod',   labelKey: 'form.paymentMethod', label: 'Method',         type: 'text' },
]
