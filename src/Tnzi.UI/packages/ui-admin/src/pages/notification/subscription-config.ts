import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'

/**
 * Subscriptions page config — aligned with NotificationPreferenceDto
 * (2026-04-14 unstub). Backend fields:
 *   id, userId, channel, category, isEnabled,
 *   quietHoursStart, quietHoursEnd, maxFrequencyPerHour
 *
 * "Subscription" in the admin UI is the Preference entity — keyed by
 * (userId, channel, category). Create/update both upsert through
 * PUT /admin/notification-preferences/user/{userId}.
 */
export const notificationSubscriptionColumns: ColumnDef[] = [
  // Shows the raw userId — a resolved user-name column needs a backend join
  // (NotificationPreferenceDto carries only userId), so that is left out.
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
          true: { type: 'success', labelKey: 'admin.shared.status.enabled' },
          false: { type: 'warning', labelKey: 'admin.shared.status.disabled' },
        },
      }),
  },
  { key: 'quietHoursStart',     title: 'columns.quietHoursStart', visible: false },
  { key: 'quietHoursEnd',       title: 'columns.quietHoursEnd',   visible: false },
  { key: 'maxFrequencyPerHour', title: 'columns.maxFrequencyPerHour',   visible: false },
]

export const notificationSubscriptionFormSchema: FormSchemaItem[] = [
  // `type: 'user'` is a custom field rendered by the page via a TUserSelector
  // fieldRenderer (remote user search) instead of a raw GUID text input.
  { key: 'userId',    labelKey: 'form.userId', label: 'User',    type: 'user',   required: true },
  { key: 'channel',   labelKey: 'form.channel', label: 'Channel', type: 'select', required: true, options: [
    { label: 'Email',   value: 'Email' },
    { label: 'SMS',     value: 'Sms' },
    { label: 'InApp',   value: 'InApp' },
    { label: 'Webhook', value: 'Webhook' },
  ] },
  { key: 'category',  labelKey: 'form.category', label: 'Category (optional)', type: 'text' },
  { key: 'isEnabled', labelKey: 'form.isEnabled', label: 'Enabled', type: 'switch' },
  { key: 'quietHoursStart',     labelKey: 'form.quietHoursStart', label: 'Quiet Hours Start (HH:mm:ss UTC)', type: 'text' },
  { key: 'quietHoursEnd',       labelKey: 'form.quietHoursEnd', label: 'Quiet Hours End (HH:mm:ss UTC)',   type: 'text' },
  { key: 'maxFrequencyPerHour', labelKey: 'form.maxFrequencyPerHour', label: 'Max Frequency / Hour',             type: 'number' },
]
