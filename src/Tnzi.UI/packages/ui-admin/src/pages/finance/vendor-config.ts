import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { TRelativeTime } from '@tnzi/ui'

/** All-optional row shape (house pattern). */
export interface VendorRow {
  id?: string
  code?: string | null
  name?: string
  email?: string | null
  phone?: string | null
  currency?: string | null
  paymentTermsDays?: number | null
  isActive?: boolean
  creationTime?: string
}

export function buildVendorColumns(t: (key: string) => string): ColumnDef<VendorRow>[] {
  return [
    { key: 'code', title: 'columns.code', width: 110, render: (row) => row.code ?? '—' },
    { key: 'name', title: 'columns.name', minWidth: 180, primary: true },
    { key: 'email', title: 'columns.email', minWidth: 180, mobileHidden: true, render: (row) => row.email ?? '—' },
    { key: 'phone', title: 'columns.phone', width: 130, mobileHidden: true, render: (row) => row.phone ?? '—' },
    { key: 'currency', title: 'columns.currency', width: 90, mobileHidden: true, render: (row) => row.currency ?? '—' },
    {
      key: 'isActive',
      title: 'columns.status',
      width: 100,
      render: (row) =>
        h(TStatusBadge, {
          value: row.isActive === false ? 0 : 1,
          type: row.isActive === false ? 'default' : 'success',
          label: row.isActive === false ? t('status.inactive') : t('status.active'),
        }),
    },
    {
      key: 'creationTime',
      title: 'columns.creationTime',
      width: 140,
      mobileHidden: true,
      render: (row) => h(TRelativeTime, { value: row.creationTime }),
    },
  ]
}

export const vendorFormSchema: FormSchemaItem[] = [
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'text', required: true },
  { key: 'code', labelKey: 'form.code', label: 'Code', type: 'text' },
  { key: 'email', labelKey: 'form.email', label: 'Email', type: 'text' },
  { key: 'phone', labelKey: 'form.phone', label: 'Phone', type: 'text' },
  { key: 'currency', labelKey: 'form.currency', label: 'Currency', type: 'text' },
  { key: 'paymentTermsDays', labelKey: 'form.paymentTermsDays', label: 'Payment Terms (days)', type: 'number' },
  { key: 'address', labelKey: 'form.address', label: 'Address', type: 'textarea' },
  { key: 'notes', labelKey: 'form.notes', label: 'Notes', type: 'textarea' },
  { key: 'isActive', labelKey: 'form.isActive', label: 'Active', type: 'switch' },
]
