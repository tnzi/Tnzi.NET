import { EMPTY_DASH } from '../../utils/placeholders'
import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem, FormSchemaSection } from '../_shared/form-schema'
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

export function buildVendorColumns(
  t: (key: string) => string,
  onOpen?: (row: VendorRow) => void,
): ColumnDef<VendorRow>[] {
  return [
    { key: 'code', title: 'columns.code', width: 110, render: (row) => row.code ?? EMPTY_DASH },
    {
      key: 'name',
      title: 'columns.name',
      minWidth: 180,
      primary: true,
      // The name is the drill-in affordance: a real <button>, so it is
      // keyboard-reachable rather than a div that happens to react to clicks.
      render: (row) =>
        onOpen
          ? h('button', { type: 'button', class: 'fin-party-link', onClick: () => onOpen(row) }, row.name ?? EMPTY_DASH)
          : (row.name ?? EMPTY_DASH),
    },
    { key: 'email', title: 'columns.email', minWidth: 180, mobileHidden: true, render: (row) => row.email ?? EMPTY_DASH },
    { key: 'phone', title: 'columns.phone', width: 130, mobileHidden: true, render: (row) => row.phone ?? EMPTY_DASH },
    { key: 'currency', title: 'columns.currency', width: 90, mobileHidden: true, render: (row) => row.currency ?? EMPTY_DASH },
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

/** Mirrors the customer form's blocks, so the two party pages read alike. */
export const vendorFormSections: FormSchemaSection[] = [
  { key: 'basics', labelKey: 'admin.shared.formSections.basics', label: 'Basics', icon: 'mdi:truck-outline' },
  { key: 'contact', labelKey: 'admin.shared.formSections.contact', label: 'Contact', icon: 'mdi:card-account-mail-outline' },
  { key: 'billing', labelKey: 'admin.shared.formSections.billing', label: 'Billing', icon: 'mdi:cash-multiple' },
  { key: 'address', labelKey: 'admin.shared.formSections.address', label: 'Address', icon: 'mdi:map-marker-outline' },
  { key: 'notes', labelKey: 'admin.shared.formSections.notes', label: 'Notes', icon: 'mdi:note-text-outline' },
]

export const vendorFormSchema: FormSchemaItem[] = [
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'text', required: true, section: 'basics' },
  { key: 'code', labelKey: 'form.code', label: 'Code', type: 'text', section: 'basics' },
  { key: 'isActive', labelKey: 'form.isActive', label: 'Active', type: 'switch', section: 'basics' },
  { key: 'email', labelKey: 'form.email', label: 'Email', type: 'text', section: 'contact' },
  { key: 'phone', labelKey: 'form.phone', label: 'Phone', type: 'text', section: 'contact' },
  { key: 'currency', labelKey: 'form.currency', label: 'Currency', type: 'text', section: 'billing' },
  { key: 'paymentTermsDays', labelKey: 'form.paymentTermsDays', label: 'Payment Terms (days)', type: 'number', section: 'billing' },
  { key: 'address', labelKey: 'form.address', label: 'Address', type: 'textarea', section: 'address' },
  { key: 'notes', labelKey: 'form.notes', label: 'Notes', type: 'textarea', section: 'notes' },
]
