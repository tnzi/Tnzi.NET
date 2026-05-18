import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'

/**
 * NotificationSubscription page config — aligned with NotificationPreferenceDto
 * (2026-04-14 unstub). Backend fields:
 *   id, userId, channel, category, isEnabled,
 *   quietHoursStart, quietHoursEnd, maxFrequencyPerHour
 *
 * "Subscription" in the admin UI is the Preference entity — keyed by
 * (userId, channel, category). Create/update both upsert through
 * PUT /admin/notification-preferences/user/{userId}.
 */
export const notificationSubscriptionColumns: ColumnDef[] = [
  { key: 'userId',              title: 'columns.userId' },
  { key: 'channel',             title: 'columns.channel' },
  { key: 'category',            title: 'columns.category' },
  {
    key: 'isEnabled',
    title: 'columns.isEnabled',
    width: 110,
    render: (row) =>
      h(TStatusBadge, {
        value: Boolean(row.isEnabled),
        mapping: {
          true: { type: 'success', label: 'Enabled' },
          false: { type: 'warning', label: 'Disabled' },
        },
      }),
  },
  { key: 'quietHoursStart',     title: 'columns.quietHoursStart', visible: false },
  { key: 'quietHoursEnd',       title: 'columns.quietHoursEnd',   visible: false },
  { key: 'maxFrequencyPerHour', title: 'columns.maxFrequencyPerHour',   visible: false },
]

export const notificationSubscriptionFormSchema: FormSchemaItem[] = [
  { key: 'userId',    label: 'User',    type: 'text',   required: true },
  { key: 'channel',   label: 'Channel', type: 'select', required: true, options: [
    { label: 'Email',   value: 'Email' },
    { label: 'SMS',     value: 'Sms' },
    { label: 'InApp',   value: 'InApp' },
    { label: 'Webhook', value: 'Webhook' },
  ] },
  { key: 'category',  label: 'Category (optional)', type: 'text' },
  { key: 'isEnabled', label: 'Enabled', type: 'switch' },
  { key: 'quietHoursStart',     label: 'Quiet Hours Start (HH:mm:ss UTC)', type: 'text' },
  { key: 'quietHoursEnd',       label: 'Quiet Hours End (HH:mm:ss UTC)',   type: 'text' },
  { key: 'maxFrequencyPerHour', label: 'Max Frequency / Hour',             type: 'number' },
]
