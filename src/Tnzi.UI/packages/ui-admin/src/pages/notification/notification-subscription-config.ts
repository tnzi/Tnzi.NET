import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'

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
  { key: 'userId',              title: 'User' },
  { key: 'channel',             title: 'Channel' },
  { key: 'category',            title: 'Category' },
  { key: 'isEnabled',           title: 'Enabled' },
  { key: 'quietHoursStart',     title: 'Quiet From', visible: false },
  { key: 'quietHoursEnd',       title: 'Quiet To',   visible: false },
  { key: 'maxFrequencyPerHour', title: 'Max/Hour',   visible: false },
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
