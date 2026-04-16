import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'

export const notificationTemplateColumns: ColumnDef[] = [
  { key: 'code',    title: 'Code' },
  { key: 'name',    title: 'Name' },
  { key: 'channel', title: 'Channel' },
  { key: 'locale',  title: 'Locale' },
  { key: 'enabled', title: 'Enabled' },
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
