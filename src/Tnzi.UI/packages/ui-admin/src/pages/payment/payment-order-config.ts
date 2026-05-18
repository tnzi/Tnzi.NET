import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'

/**
 * Payment Order page config — aligned with PaymentDto (2026-04-14 Plan C unstub).
 *
 * Backend fields (PaymentDto):
 *   id, paymentNo, tradeNo, businessOrderNo, businessType, amount,
 *   currency, status, channelCode, paymentMethod, paidTime, userId,
 *   discountAmount, finalAmount, refundAmount, creationTime
 *
 * The page is read-only — payment records are immutable once created.
 * State transitions flow through close/sync admin endpoints.
 */
const ORDER_STATUS_MAP: Record<string, { type: 'info' | 'success' | 'warning' | 'error' | 'default'; label: string }> = {
  pending:    { type: 'warning', label: 'Pending' },
  processing: { type: 'info',    label: 'Processing' },
  paid:       { type: 'success', label: 'Paid' },
  completed:  { type: 'success', label: 'Completed' },
  cancelled:  { type: 'default', label: 'Cancelled' },
  refunded:   { type: 'default', label: 'Refunded' },
  failed:     { type: 'error',   label: 'Failed' },
  closed:     { type: 'default', label: 'Closed' },
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
      const raw = String(row.status ?? '').toLowerCase()
      const m = ORDER_STATUS_MAP[raw] ?? { type: 'default' as const, label: row.status as string ?? '—' }
      return h(TStatusBadge, { value: raw, type: m.type, label: m.label })
    },
  },
  { key: 'paymentMethod',   title: 'columns.paymentMethod' },
  { key: 'paidTime',        title: 'columns.paidTime' },
  { key: 'creationTime',    title: 'columns.creationTime', visible: false },
  { key: 'refundAmount',    title: 'columns.refundAmount',   visible: false },
]

export const orderFormSchema: FormSchemaItem[] = [
  { key: 'paymentNo',       label: 'Payment No',     type: 'text' },
  { key: 'businessOrderNo', label: 'Business Order', type: 'text' },
  { key: 'userId',          label: 'Customer ID',    type: 'text' },
  { key: 'amount',          label: 'Amount',         type: 'number' },
  { key: 'currency',        label: 'Currency',       type: 'select', options: [
    { label: 'CNY', value: 'CNY' },
    { label: 'USD', value: 'USD' },
    { label: 'EUR', value: 'EUR' },
  ] },
  { key: 'status',          label: 'Status',         type: 'text' },
  { key: 'paymentMethod',   label: 'Method',         type: 'text' },
]
