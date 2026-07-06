import { h } from 'vue'
import { formatCurrency, formatDateTime } from '@tnzi/core'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import type { StatusType } from '@tnzi/ui'

/**
 * Subscriptions page config — aligned with `SubscriptionDto`
 * (Tnzi.Payment.Dtos.SubscriptionDto).
 *
 * `status` / `cycleType` serialise as member-name strings (global
 * JsonStringEnumConverter):
 *   SubscriptionStatus: Pending / Trial / Active / PendingRenewal / Paused /
 *     Cancelled / Expired / PastDue
 *   BillingCycleType:   Day / Week / Month / Year / OneTime
 *
 * Page is read-only: subscriptions are initiated by users and there is no admin
 * update endpoint. The only lifecycle action is cancel-at-period-end.
 */
const STATUS_TONE: Record<string, StatusType> = {
  Pending: 'warning',
  Trial: 'info',
  Active: 'success',
  PendingRenewal: 'warning',
  Paused: 'warning',
  Cancelled: 'default',
  Expired: 'default',
  PastDue: 'error',
}
// member name → i18n leaf key under `payment.subscriptions.status.*`
const STATUS_KEY: Record<string, string> = {
  Pending: 'pending',
  Trial: 'trial',
  Active: 'active',
  PendingRenewal: 'pendingRenewal',
  Paused: 'paused',
  Cancelled: 'cancelled',
  Expired: 'expired',
  PastDue: 'pastDue',
}
const CYCLE_KEY: Record<string, string> = {
  Day: 'daily',
  Week: 'weekly',
  Month: 'monthly',
  Year: 'yearly',
  OneTime: 'oneTime',
}

/**
 * Build the Subscriptions columns. A factory so the status/cycle labels,
 * currency and dates resolve through the page translator `t`.
 */
export function buildSubscriptionColumns(t: (key: string) => string): ColumnDef[] {
  return [
    { key: 'subscriptionNo', title: 'columns.subscriptionNo', minWidth: 150 },
    { key: 'userId', title: 'columns.userId', minWidth: 140 },
    { key: 'planName', title: 'columns.planName', minWidth: 130 },
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
      key: 'cycleType',
      title: 'columns.cycleType',
      width: 110,
      render: (row) => {
        const v = String(row.cycleType ?? '')
        return CYCLE_KEY[v] ? t(`cycleType.${CYCLE_KEY[v]}`) : v || '—'
      },
    },
    {
      key: 'startTime',
      title: 'columns.startTime',
      width: 160,
      render: (row) => formatDateTime(row.startTime as string | null | undefined),
    },
    {
      key: 'nextBillingTime',
      title: 'columns.nextBillingTime',
      width: 160,
      render: (row) => formatDateTime(row.nextBillingTime as string | null | undefined),
    },
    {
      key: 'paidAmount',
      title: 'columns.paidAmount',
      width: 120,
      align: 'right',
      visible: false,
      render: (row) => formatCurrency(Number(row.paidAmount ?? 0), String(row.currency || 'USD')),
    },
    { key: 'currency', title: 'columns.currency', width: 90, visible: false },
    {
      key: 'autoRenew',
      title: 'columns.autoRenew',
      width: 110,
      visible: false,
      render: (row) => (row.autoRenew ? t('autoRenew.on') : t('autoRenew.off')),
    },
  ]
}

/** View-form schema (read-only quick preview), reachable via the row `view` action. */
export const paymentSubscriptionFormSchema: FormSchemaItem[] = [
  { key: 'subscriptionNo', labelKey: 'form.subscriptionNo', label: 'Subscription No', type: 'text' },
  { key: 'userId', labelKey: 'form.userId', label: 'Customer', type: 'text' },
  { key: 'planName', labelKey: 'form.planName', label: 'Plan Name', type: 'text' },
  { key: 'status', labelKey: 'form.status', label: 'Status', type: 'text' },
  { key: 'cycleType', labelKey: 'form.cycleType', label: 'Cycle Type', type: 'text' },
  { key: 'cycleValue', labelKey: 'form.cycleValue', label: 'Cycle Value', type: 'number' },
  { key: 'originalPrice', labelKey: 'form.originalPrice', label: 'Original Price', type: 'number' },
  { key: 'paidAmount', labelKey: 'form.paidAmount', label: 'Paid Amount', type: 'number' },
  { key: 'currency', labelKey: 'form.currency', label: 'Currency', type: 'text' },
]
