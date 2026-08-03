import { EMPTY_DASH } from '../../utils/placeholders'
import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem, FormSchemaSection } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { ItemType } from '../../services/bridges/finance-bridge'

import TMoney from '../../components/finance/TMoney.vue'
import type { SearchFieldItem } from '../../components/crud/TCrudSearchAdvanced.vue'

/** All-optional row shape (house pattern). */
export interface ItemRow {
  id?: string
  code?: string | null
  name?: string
  type?: ItemType
  salesPrice?: number | null
  purchasePrice?: number | null
  isActive?: boolean
}

export function buildItemColumns(t: (key: string) => string): ColumnDef<ItemRow>[] {
  return [
    { key: 'code', title: 'columns.code', width: 110, render: (row) => row.code ?? EMPTY_DASH },
    { key: 'name', title: 'columns.name', minWidth: 180, primary: true },
    {
      key: 'type',
      title: 'columns.type',
      width: 110,
      render: (row) => (row.type === ItemType.Product ? t('type.product') : t('type.service')),
    },
    { key: 'salesPrice', title: 'columns.salesPrice', width: 120, render: (row) => (row.salesPrice == null ? EMPTY_DASH : h(TMoney, { value: row.salesPrice })) },
    { key: 'purchasePrice', title: 'columns.purchasePrice', width: 120, mobileHidden: true, render: (row) => (row.purchasePrice == null ? EMPTY_DASH : h(TMoney, { value: row.purchasePrice })) },
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
  ]
}

/** What it is, what it costs, and which accounts it posts to. */
export const itemFormSections: FormSchemaSection[] = [
  { key: 'basics', labelKey: 'admin.shared.formSections.basics', label: 'Basics', icon: 'mdi:package-variant-closed' },
  { key: 'pricing', labelKey: 'admin.shared.formSections.pricing', label: 'Pricing', icon: 'mdi:tag-multiple-outline' },
  { key: 'posting', labelKey: 'admin.shared.formSections.placement', label: 'Posting', icon: 'mdi:bank-outline' },
]

export const itemFormSchema: FormSchemaItem[] = [
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'text', required: true, section: 'basics' },
  { key: 'code', labelKey: 'form.code', label: 'Code', type: 'text', section: 'basics' },
  {
    key: 'type',
    labelKey: 'form.type',
    label: 'Type',
    type: 'select',
    section: 'basics',
    options: [
      { label: 'Service', value: ItemType.Service, labelKey: 'type.service' },
      { label: 'Product', value: ItemType.Product, labelKey: 'type.product' },
    ],
  },
  { key: 'isActive', labelKey: 'form.isActive', label: 'Active', type: 'switch', section: 'basics' },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'textarea', section: 'basics' },
  { key: 'salesPrice', labelKey: 'form.salesPrice', label: 'Sales Price', type: 'number', section: 'pricing' },
  { key: 'purchasePrice', labelKey: 'form.purchasePrice', label: 'Purchase Price', type: 'number', section: 'pricing' },
  { key: 'incomeAccountId', labelKey: 'form.incomeAccount', label: 'Income Account', type: 'finance-account', section: 'posting' },
  { key: 'expenseAccountId', labelKey: 'form.expenseAccount', label: 'Expense Account', type: 'finance-account', section: 'posting' },
  { key: 'defaultTaxCodeId', labelKey: 'form.defaultTaxCode', label: 'Default Tax Code', type: 'finance-tax-code', section: 'posting' },
]

/** 目录项筛选：后端支持类型与启用状态。 */
export function buildItemSearchFields(t: (key: string) => string): SearchFieldItem[] {
  return [
    {
      key: 'type',
      label: t('columns.type'),
      type: 'select',
      options: [
        { label: t('type.service'), value: 'Service' },
        { label: t('type.product'), value: 'Product' },
      ],
    },
    {
      key: 'isActive',
      label: t('columns.isActive'),
      type: 'select',
      options: [
        { label: t('search.active'), value: 'true' },
        { label: t('search.inactive'), value: 'false' },
      ],
    },
  ]
}
