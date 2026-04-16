import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'

export const tenantColumns: ColumnDef[] = [
  { key: 'name',      title: 'Name' },
  { key: 'code',      title: 'Code' },
  { key: 'status',    title: 'Status' },
  { key: 'plan',      title: 'Plan' },
  { key: 'createdAt', title: 'Created At', visible: false },
]

export const tenantFormSchema: FormSchemaItem[] = [
  { key: 'name',   label: 'Name',   type: 'text',   required: true },
  { key: 'code',   label: 'Code',   type: 'text',   required: true },
  { key: 'plan',   label: 'Plan',   type: 'select', options: [
    { label: 'Free',       value: 'free' },
    { label: 'Pro',        value: 'pro' },
    { label: 'Enterprise', value: 'enterprise' },
  ] },
  { key: 'status', label: 'Status', type: 'select', options: [
    { label: 'Active',    value: 'active' },
    { label: 'Suspended', value: 'suspended' },
  ] },
]
