import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'

interface NotificationTemplateRow {
  id?: string
  code?: string
  name?: string
  channel?: 'email' | 'sms' | 'push' | 'webhook'
  locale?: string
  subject?: string
  enabled?: boolean
}

export const notificationTemplateColumns: ColumnDef<NotificationTemplateRow>[] = [
  { key: 'name', title: 'columns.name', width: 220, fixed: 'left' },
  { key: 'code', title: 'columns.code', width: 200 },
  {
    key: 'channel',
    title: 'columns.channel',
    width: 110,
    render: (row) =>
      h(TStatusBadge, {
        value: row.channel ?? 'unknown',
        mapping: {
          email: { type: 'info', label: 'Email' },
          sms: { type: 'success', label: 'SMS' },
          push: { type: 'warning', label: 'Push' },
          webhook: { type: 'default', label: 'Webhook' },
        },
      }),
  },
  { key: 'locale', title: 'columns.locale', width: 100 },
  { key: 'subject', title: 'columns.subject' },
  {
    key: 'enabled',
    title: 'columns.enabled',
    width: 110,
    fixed: 'right',
    render: (row) =>
      h(TStatusBadge, {
        value: row.enabled ?? false,
        mapping: {
          true: { type: 'success', label: 'Enabled' },
          false: { type: 'warning', label: 'Disabled' },
        },
      }),
  },
]

export const notificationTemplateFormSchema: FormSchemaItem[] = [
  { key: 'code',    label: 'Code',    type: 'text',     required: true },
  { key: 'name',    label: 'Name',    type: 'text',     required: true },
  { key: 'channel', label: 'Channel', type: 'select',   options: [
    { label: 'Email',   value: 'email' },
    { label: 'SMS',     value: 'sms' },
    { label: 'Push',    value: 'push' },
    { label: 'Webhook', value: 'webhook' },
  ] },
  { key: 'locale',  label: 'Locale',  type: 'select',   options: [
    { label: 'English', value: 'en' },
    { label: 'Chinese', value: 'zh-cn' },
  ] },
  { key: 'subject', label: 'Subject', type: 'text' },
  { key: 'body',    label: 'Body',    type: 'textarea', required: true },
  { key: 'enabled', label: 'Enabled', type: 'switch' },
]
