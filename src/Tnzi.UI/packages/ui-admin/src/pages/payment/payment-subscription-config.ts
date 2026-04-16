import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'

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
  { key: 'subscriptionNo',  title: 'Subscription No' },
  { key: 'userId',          title: 'Customer' },
  { key: 'planName',        title: 'Plan' },
  { key: 'status',          title: 'Status' },
  { key: 'cycleType',       title: 'Cycle' },
  { key: 'startTime',       title: 'Start' },
  { key: 'nextBillingTime', title: 'Next Billing' },
  { key: 'paidAmount',      title: 'Paid', visible: false },
  { key: 'currency',        title: 'Currency', visible: false },
  { key: 'autoRenew',       title: 'Auto Renew', visible: false },
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
