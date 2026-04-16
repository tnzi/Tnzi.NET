import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'

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
export const orderColumns: ColumnDef[] = [
  { key: 'paymentNo',       title: 'Payment No' },
  { key: 'businessOrderNo', title: 'Business Order' },
  { key: 'userId',          title: 'Customer' },
  { key: 'amount',          title: 'Amount' },
  { key: 'currency',        title: 'Currency' },
  { key: 'status',          title: 'Status' },
  { key: 'paymentMethod',   title: 'Method' },
  { key: 'paidTime',        title: 'Paid At' },
  { key: 'creationTime',    title: 'Created At', visible: false },
  { key: 'refundAmount',    title: 'Refunded',   visible: false },
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
