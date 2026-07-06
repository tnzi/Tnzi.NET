import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { ItemType } from '../../services/bridges/finance-bridge'
import { amountCell, fmtAmount } from './money'

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
    { key: 'code', title: 'columns.code', width: 110, render: (row) => row.code ?? '—' },
    { key: 'name', title: 'columns.name', minWidth: 180, primary: true },
    {
      key: 'type',
      title: 'columns.type',
      width: 110,
      render: (row) => (row.type === ItemType.Product ? t('type.product') : t('type.service')),
    },
    { key: 'salesPrice', title: 'columns.salesPrice', width: 120, render: (row) => amountCell(row.salesPrice != null ? fmtAmount(row.salesPrice) : '—') },
    { key: 'purchasePrice', title: 'columns.purchasePrice', width: 120, mobileHidden: true, render: (row) => amountCell(row.purchasePrice != null ? fmtAmount(row.purchasePrice) : '—') },
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

export const itemFormSchema: FormSchemaItem[] = [
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'text', required: true },
  { key: 'code', labelKey: 'form.code', label: 'Code', type: 'text' },
  {
    key: 'type',
    labelKey: 'form.type',
    label: 'Type',
    type: 'select',
    options: [
      { label: 'Service', value: ItemType.Service, labelKey: 'type.service' },
      { label: 'Product', value: ItemType.Product, labelKey: 'type.product' },
    ],
  },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'textarea' },
  { key: 'salesPrice', labelKey: 'form.salesPrice', label: 'Sales Price', type: 'number' },
  { key: 'purchasePrice', labelKey: 'form.purchasePrice', label: 'Purchase Price', type: 'number' },
  { key: 'incomeAccountId', labelKey: 'form.incomeAccount', label: 'Income Account', type: 'finance-account' },
  { key: 'expenseAccountId', labelKey: 'form.expenseAccount', label: 'Expense Account', type: 'finance-account' },
  { key: 'defaultTaxCodeId', labelKey: 'form.defaultTaxCode', label: 'Default Tax Code', type: 'finance-tax-code' },
  { key: 'isActive', labelKey: 'form.isActive', label: 'Active', type: 'switch' },
]
