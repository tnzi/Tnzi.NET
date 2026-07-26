import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem, FormSchemaSection } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { TRelativeTime } from '@tnzi/ui'

/**
 * Tenant search fields - align with backend `TenantQueryDto`:
 * `keyword` (free text on name/code) + `isEnabled` (true/false filter).
 */
export const tenantSearchFields: FormSchemaItem[] = [
  { key: 'keyword', labelKey: 'form.keyword', label: 'Keyword', type: 'text', placeholder: 'columns.name' },
  {
    key: 'isEnabled',
    labelKey: 'form.isEnabled', label: 'Enabled',
    type: 'select',
    options: [
      { label: 'Enabled', value: 'true' },
      { label: 'Disabled', value: 'false' },
    ],
  },
]

export interface TenantRow {
  id?: string
  name?: string
  code?: string
  isEnabled?: boolean
  expiredAt?: string
  remark?: string
  creationTime?: string
}

export const tenantColumns: ColumnDef<TenantRow>[] = [
  { key: 'name', title: 'columns.name', minWidth: 150 },
  { key: 'code', title: 'columns.code', minWidth: 130 },
  {
    key: 'isEnabled',
    title: 'columns.isEnabled',
    width: 110,
    render: (row) =>
      h(TStatusBadge, {
        value: row.isEnabled ?? false,
        mapping: {
          true: { type: 'success', labelKey: 'admin.shared.status.enabled' },
          false: { type: 'warning', labelKey: 'admin.shared.status.disabled' },
        },
      }),
  },
  {
    key: 'expiredAt',
    title: 'columns.expiredAt',
    width: 160,
    render: (row) => h(TRelativeTime, { value: row.expiredAt }),
  },
  { key: 'remark', title: 'columns.remark', minWidth: 160 },
  {
    key: 'creationTime',
    title: 'columns.creationTime',
    width: 150,
    render: (row) => h(TRelativeTime, { value: row.creationTime }),
  },
]

/** Who the tenant is, then how long they stay live. */
export const tenantFormSections: FormSchemaSection[] = [
  { key: 'basics', labelKey: 'admin.shared.formSections.basics', label: 'Basics', icon: 'mdi:domain' },
  { key: 'status', labelKey: 'admin.shared.formSections.status', label: 'Status', icon: 'mdi:calendar-check-outline' },
]

export const tenantFormSchema: FormSchemaItem[] = [
  { key: 'name', labelKey: 'form.name', label: 'Tenant Name', type: 'text', required: true, section: 'basics' },
  { key: 'code', labelKey: 'form.code', label: 'Code', type: 'text', required: true, section: 'basics' },
  { key: 'remark', labelKey: 'form.remark', label: 'Remark', type: 'textarea', section: 'basics' },
  { key: 'isEnabled', labelKey: 'form.isEnabled', label: 'Enabled', type: 'switch', section: 'status' },
  { key: 'expiredAt', labelKey: 'form.expiredAt', label: 'Expires At', type: 'date', section: 'status' },
]
