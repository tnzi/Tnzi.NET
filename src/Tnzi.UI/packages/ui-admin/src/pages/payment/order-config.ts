import { h } from 'vue'
import { formatCurrency, formatDateTime } from '@tnzi/core'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import type { StatusType } from '@tnzi/ui'

/**
 * Orders page config — aligned with the real `PaymentDto`
 * (Tnzi.Payment.Dtos.PaymentDto). The record keys on `tradeNo` (there is no
 * `paymentNo` / `userId` / `amount` / `refundAmount`); money is
 * `originalAmount` / `paidAmount` / `discountAmount`.
 *
 * The backend serialises `status` / `paymentMethod` / `businessType` as
 * PascalCase member-name strings (global JsonStringEnumConverter), so all maps
 * key off the member name (e.g. `'Succeeded'`).
 *
 * Page is read-only; state transitions flow through close/sync admin endpoints.
 */
const STATUS_TONE: Record<string, StatusType> = {
  Pending: 'warning',
  Processing: 'info',
  Succeeded: 'success',
  Failed: 'error',
  Closed: 'default',
  Cancelled: 'default',
  Expired: 'default',
  Refunded: 'info',
  PartialRefunded: 'warning',
}
// member name → i18n leaf key under `payment.orders.status.*`
const STATUS_KEY: Record<string, string> = {
  Pending: 'pending',
  Processing: 'processing',
  Succeeded: 'succeeded',
  Failed: 'failed',
  Closed: 'closed',
  Cancelled: 'cancelled',
  Expired: 'expired',
  Refunded: 'refunded',
  PartialRefunded: 'partialRefunded',
}
const METHOD_KEY: Record<string, string> = {
  CreditCard: 'creditCard',
  DebitCard: 'debitCard',
  PayPal: 'payPal',
  ApplePay: 'applePay',
  GooglePay: 'googlePay',
  BankTransfer: 'bankTransfer',
  Offline: 'offline',
}
const BUSINESS_TYPE_KEY: Record<string, string> = {
  Order: 'order',
  Subscription: 'subscription',
  Recharge: 'recharge',
  Other: 'other',
}

/** Status filter options for the Orders advanced search (value = member name). */
export const orderStatusOptions = Object.keys(STATUS_KEY).map((member) => ({
  value: member,
  labelKey: `status.${STATUS_KEY[member]}`,
}))

type Money = number | null | undefined
function money(amount: Money, currency: string | null | undefined): string {
  if (amount === null || amount === undefined) return '—'
  return formatCurrency(Number(amount), String(currency || 'USD'))
}

/**
 * Build the Orders columns. A factory (not a static const) so the enum-label /
 * currency / date renderers resolve through the page translator `t` — mirrors
 * the finance `buildJournalEntryColumns(t)` convention.
 */
export function buildOrderColumns(t: (key: string) => string): ColumnDef[] {
  return [
    { key: 'tradeNo', title: 'columns.tradeNo', minWidth: 170 },
    { key: 'businessOrderNo', title: 'columns.businessOrderNo', minWidth: 150 },
    {
      key: 'businessType',
      title: 'columns.businessType',
      width: 120,
      render: (row) => {
        const v = String(row.businessType ?? '')
        return BUSINESS_TYPE_KEY[v] ? t(`businessType.${BUSINESS_TYPE_KEY[v]}`) : v || '—'
      },
    },
    {
      key: 'paidAmount',
      title: 'columns.amount',
      width: 130,
      align: 'right',
      render: (row) => money(row.paidAmount as Money, row.currency as string),
    },
    {
      key: 'status',
      title: 'columns.status',
      width: 130,
      render: (row) => {
        const v = String(row.status ?? '')
        return h(TStatusBadge, {
          value: v,
          type: STATUS_TONE[v] ?? 'default',
          label: STATUS_KEY[v] ? t(`status.${STATUS_KEY[v]}`) : v || '—',
        })
      },
    },
    {
      key: 'paymentMethod',
      title: 'columns.paymentMethod',
      width: 130,
      render: (row) => {
        const v = String(row.paymentMethod ?? '')
        return METHOD_KEY[v] ? t(`paymentMethod.${METHOD_KEY[v]}`) : v || '—'
      },
    },
    {
      key: 'channelCode',
      title: 'columns.channel',
      width: 120,
      visible: false,
      render: (row) => (row.channelCode ? String(row.channelCode) : '—'),
    },
    {
      key: 'paidTime',
      title: 'columns.paidTime',
      width: 160,
      render: (row) => formatDateTime(row.paidTime as string | null | undefined),
    },
    {
      key: 'originalAmount',
      title: 'columns.originalAmount',
      width: 130,
      align: 'right',
      visible: false,
      render: (row) => money(row.originalAmount as Money, row.currency as string),
    },
    {
      key: 'discountAmount',
      title: 'columns.discountAmount',
      width: 120,
      align: 'right',
      visible: false,
      render: (row) => money(row.discountAmount as Money, row.currency as string),
    },
    {
      key: 'currency',
      title: 'columns.currency',
      width: 90,
      visible: false,
    },
    {
      key: 'creationTime',
      title: 'columns.creationTime',
      width: 160,
      visible: false,
      render: (row) => formatDateTime(row.creationTime as string | null | undefined),
    },
  ]
}

/**
 * View-form schema (read-only quick preview). Reachable via the row `view`
 * action; enum fields render their PascalCase member name in read-only mode.
 */
export const orderFormSchema: FormSchemaItem[] = [
  { key: 'tradeNo', labelKey: 'form.tradeNo', label: 'Trade No', type: 'text' },
  { key: 'businessOrderNo', labelKey: 'form.businessOrderNo', label: 'Business Order', type: 'text' },
  { key: 'businessType', labelKey: 'form.businessType', label: 'Business Type', type: 'text' },
  { key: 'status', labelKey: 'form.status', label: 'Status', type: 'text' },
  { key: 'paymentMethod', labelKey: 'form.paymentMethod', label: 'Method', type: 'text' },
  { key: 'channelCode', labelKey: 'form.channel', label: 'Channel', type: 'text' },
  { key: 'originalAmount', labelKey: 'form.originalAmount', label: 'Original Amount', type: 'number' },
  { key: 'discountAmount', labelKey: 'form.discountAmount', label: 'Discount', type: 'number' },
  { key: 'paidAmount', labelKey: 'form.amount', label: 'Paid Amount', type: 'number' },
  { key: 'currency', labelKey: 'form.currency', label: 'Currency', type: 'text' },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'textarea' },
]
