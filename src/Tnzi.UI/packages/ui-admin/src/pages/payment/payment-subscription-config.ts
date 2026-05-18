import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'

const SUBSCRIPTION_STATUS_MAP: Record<string, { type: 'info' | 'success' | 'warning' | 'error' | 'default'; label: string }> = {
  active:    { type: 'success', label: 'Active' },
  trialing:  { type: 'info',    label: 'Trialing' },
  pastDue:   { type: 'warning', label: 'Past Due' },
  past_due:  { type: 'warning', label: 'Past Due' },
  paused:    { type: 'warning', label: 'Paused' },
  cancelled: { type: 'default', label: 'Cancelled' },
  expired:   { type: 'default', label: 'Expired' },
  ended:     { type: 'default', label: 'Ended' },
}

/**
 * Payment Subscription page config — aligned with SubscriptionDto
 * (2026-04-14 Plan C unstub).
 *
 * Backend fields (SubscriptionDto):
 *   id, subscriptionNo, userId, planId, planName, status, cycleType,
 *   cycleValue, startTime, endTime, nextBillingTime, originalPrice,
 *   paidAmount, currency, autoRenew, creationTime
 *
 * Row action "Cancel at Period End" maps to POST /admin/subscriptions/{id}/cancel.
 */
export const paymentSubscriptionColumns: ColumnDef[] = [
  { key: 'subscriptionNo',  title: 'columns.subscriptionNo' },
  { key: 'userId',          title: 'columns.userId' },
  { key: 'planName',        title: 'columns.planName' },
  {
    key: 'status',
    title: 'columns.status',
    width: 130,
    render: (row) => {
      const raw = String(row.status ?? '').toLowerCase()
      const m = SUBSCRIPTION_STATUS_MAP[raw] ?? { type: 'default' as const, label: row.status as string ?? '—' }
      return h(TStatusBadge, { value: raw, type: m.type, label: m.label })
    },
  },
  { key: 'cycleType',       title: 'columns.cycleType' },
  { key: 'startTime',       title: 'columns.startTime' },
  { key: 'nextBillingTime', title: 'columns.nextBillingTime' },
  { key: 'paidAmount',      title: 'columns.paidAmount', visible: false },
  { key: 'currency',        title: 'columns.currency', visible: false },
  { key: 'autoRenew',       title: 'columns.autoRenew', visible: false },
]

export const paymentSubscriptionFormSchema: FormSchemaItem[] = [
  { key: 'userId',    label: 'Customer',   type: 'text',   required: true },
  { key: 'planId',    label: 'Plan ID',    type: 'text',   required: true },
  { key: 'planName',  label: 'Plan Name',  type: 'text' },
  { key: 'cycleType', label: 'Cycle Type', type: 'select', options: [
    { label: 'Daily',   value: 'Daily' },
    { label: 'Weekly',  value: 'Weekly' },
    { label: 'Monthly', value: 'Monthly' },
    { label: 'Yearly',  value: 'Yearly' },
  ] },
  { key: 'status',    label: 'Status',     type: 'text' },
]
