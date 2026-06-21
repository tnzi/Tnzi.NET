import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'

/**
 * SubscriptionStatus enum: 0=Pending / 1=Trial / 2=Active /
 *   3=PendingRenewal / 4=Paused / 5=Cancelled / 6=Expired / 7=PastDue.
 * Backend serialises as int — the previous string-keyed map ('active' /
 * 'trialing' / 'pastDue' / 'ended') never matched any row, leaving the
 * badge with the raw number visible.
 */
const SUBSCRIPTION_STATUS_MAP: Record<number, { type: 'info' | 'success' | 'warning' | 'error' | 'default'; label?: string; labelKey?: string }> = {
  0: { type: 'warning', labelKey: 'admin.shared.status.pending' },
  1: { type: 'info',    label: 'Trial' },
  2: { type: 'success', labelKey: 'admin.shared.status.active' },
  3: { type: 'warning', label: 'Pending Renewal' },
  4: { type: 'warning', label: 'Paused' },
  5: { type: 'default', labelKey: 'admin.shared.status.cancelled' },
  6: { type: 'default', label: 'Expired' },
  7: { type: 'error',   label: 'Past Due' },
}

/**
 * Subscriptions page config — aligned with SubscriptionDto
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
      const v = typeof row.status === 'number' ? row.status : Number(row.status)
      const m = SUBSCRIPTION_STATUS_MAP[v]
      if (m) {
        return h(TStatusBadge, { value: v, type: m.type, label: m.label, labelKey: m.labelKey })
      }
      return h(TStatusBadge, { value: v, type: 'default', label: String(row.status ?? '—') })
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
  { key: 'userId',    labelKey: 'form.userId', label: 'Customer',   type: 'text',   required: true },
  { key: 'planId',    labelKey: 'form.planId', label: 'Plan ID',    type: 'text',   required: true },
  { key: 'planName',  labelKey: 'form.planName', label: 'Plan Name',  type: 'text' },
  { key: 'cycleType', labelKey: 'form.cycleType', label: 'Cycle Type', type: 'select', options: [
    { label: 'Daily',   value: 'Daily' },
    { label: 'Weekly',  value: 'Weekly' },
    { label: 'Monthly', value: 'Monthly' },
    { label: 'Yearly',  value: 'Yearly' },
  ] },
  { key: 'status',    labelKey: 'form.status', label: 'Status',     type: 'text' },
]
