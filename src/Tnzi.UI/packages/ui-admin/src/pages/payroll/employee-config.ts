import { EMPTY_DASH } from '../../utils/placeholders'
import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem, FormSchemaSection } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { formatDateOnly } from '@tnzi/core'
import { TRelativeTime } from '@tnzi/ui'

/** All-optional row shape (house pattern). */
export interface EmployeeRow {
  id?: string
  code?: string
  name?: string
  email?: string | null
  phone?: string | null
  hireDate?: string | null
  terminationDate?: string | null
  vendorId?: string | null
  userId?: string | null
  attributesJson?: string | null
  isActive?: boolean
  notes?: string | null
  creationTime?: string
}

export function buildEmployeeColumns(t: (key: string) => string): ColumnDef<EmployeeRow>[] {
  return [
    { key: 'code', title: 'columns.code', width: 120, render: (row) => row.code ?? EMPTY_DASH },
    { key: 'name', title: 'columns.name', minWidth: 160, primary: true },
    { key: 'email', title: 'columns.email', minWidth: 180, mobileHidden: true, render: (row) => row.email ?? EMPTY_DASH },
    { key: 'phone', title: 'columns.phone', width: 130, mobileHidden: true, render: (row) => row.phone ?? EMPTY_DASH },
    { key: 'hireDate', title: 'columns.hireDate', width: 120, mobileHidden: true, render: (row) => formatDateOnly(row.hireDate, { utc: true, fallback: EMPTY_DASH }) },
    {
      key: 'vendorId',
      title: 'columns.payee',
      width: 100,
      mobileHidden: true,
      render: (row) =>
        h(TStatusBadge, {
          value: row.vendorId ? 1 : 0,
          type: row.vendorId ? 'success' : 'default',
          label: row.vendorId ? t('payee.linked') : t('payee.none'),
        }),
    },
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

/**
 * Who they are, how to reach them, their employment dates, then the technical
 * bits (linked account, free-form attributes) that only a payroll administrator
 * touches. Putting the JSON blob next to the phone number is how a business
 * form starts looking like a database console.
 */
export const employeeFormSections: FormSchemaSection[] = [
  { key: 'basics', labelKey: 'admin.shared.formSections.basics', label: 'Basics', icon: 'mdi:card-account-details-outline' },
  { key: 'contact', labelKey: 'admin.shared.formSections.contact', label: 'Contact', icon: 'mdi:card-account-mail-outline' },
  { key: 'employment', labelKey: 'admin.shared.formSections.period', label: 'Employment', icon: 'mdi:calendar-account-outline' },
  { key: 'advanced', labelKey: 'admin.shared.formSections.advanced', label: 'Advanced', icon: 'mdi:cog-outline' },
]

export const employeeFormSchema: FormSchemaItem[] = [
  { key: 'code', labelKey: 'form.code', label: 'Code', type: 'text', required: true, section: 'basics' },
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'text', required: true, section: 'basics' },
  { key: 'isActive', labelKey: 'form.isActive', label: 'Active', type: 'switch', section: 'basics', visible: (m) => !!m.id },
  { key: 'email', labelKey: 'form.email', label: 'Email', type: 'text', section: 'contact' },
  { key: 'phone', labelKey: 'form.phone', label: 'Phone', type: 'text', section: 'contact' },
  { key: 'hireDate', labelKey: 'form.hireDate', label: 'Hire Date', type: 'date', section: 'employment' },
  { key: 'terminationDate', labelKey: 'form.terminationDate', label: 'Termination Date', type: 'date', section: 'employment' },
  // Linked account: pick a real user (remote-search) instead of pasting a raw GUID (Business-audience iron-law).
  { key: 'userId', labelKey: 'form.userId', label: 'Linked User', type: 'employee-user', section: 'advanced' },
  { key: 'attributesJson', labelKey: 'form.attributesJson', label: 'Attributes (JSON)', type: 'json', span: 'full', section: 'advanced' },
  { key: 'notes', labelKey: 'form.notes', label: 'Notes', type: 'textarea', section: 'advanced' },
]

/** Assignment editor fields (add form inside the assignments drawer). */
export const assignmentFormSchema: FormSchemaItem[] = [
  { key: 'structureId', labelKey: 'form.structure', label: 'Structure', type: 'payroll-structure', required: true },
  { key: 'effectiveFrom', labelKey: 'form.effectiveFrom', label: 'Effective From', type: 'date', required: true },
  { key: 'baseAmount', labelKey: 'form.baseAmount', label: 'Base Amount', type: 'number', required: true },
  { key: 'notes', labelKey: 'form.notes', label: 'Notes', type: 'textarea' },
]
