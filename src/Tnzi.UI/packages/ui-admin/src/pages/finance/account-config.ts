import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { AccountRootType, AccountSystemRole, CashFlowActivity } from '../../services/bridges/finance-bridge'
/** All-optional row shape (house pattern) so ColumnDef<AccountRow> stays
 * assignable to the ColumnDef<Record<string, unknown>> the CRUD shell expects.
 * Enum fields carry the backend PascalCase member string (JsonStringEnumConverter). */
export interface AccountRow {
  id?: string
  code?: string
  name?: string
  description?: string | null
  rootType?: AccountRootType
  subType?: string | null
  parentId?: string | null
  isGroup?: boolean
  currency?: string | null
  systemRole?: AccountSystemRole | null
  cashFlowActivity?: CashFlowActivity | null
  isActive?: boolean
  creationTime?: string
}

// Label maps are keyed by the backend PascalCase enum member name.
export const ROOT_TYPE_LABELS: Record<string, string> = {
  [AccountRootType.Asset]: 'Asset',
  [AccountRootType.Liability]: 'Liability',
  [AccountRootType.Equity]: 'Equity',
  [AccountRootType.Income]: 'Income',
  [AccountRootType.Expense]: 'Expense',
}

export const SYSTEM_ROLE_LABELS: Record<string, string> = {
  [AccountSystemRole.AccountsReceivable]: 'Accounts Receivable',
  [AccountSystemRole.AccountsPayable]: 'Accounts Payable',
  [AccountSystemRole.TaxPayable]: 'Tax Payable',
  [AccountSystemRole.TaxReceivable]: 'Tax Receivable',
  [AccountSystemRole.RetainedEarnings]: 'Retained Earnings',
  [AccountSystemRole.ExchangeGainLoss]: 'Exchange Gain/Loss',
  [AccountSystemRole.RoundingDifference]: 'Rounding Difference',
  [AccountSystemRole.UndepositedFunds]: 'Undeposited Funds',
  [AccountSystemRole.OpeningBalance]: 'Opening Balance',
}

export const CASH_FLOW_LABELS: Record<string, string> = {
  [CashFlowActivity.Operating]: 'Operating',
  [CashFlowActivity.Investing]: 'Investing',
  [CashFlowActivity.Financing]: 'Financing',
}

const ROOT_TYPE_BADGE: Record<string, 'info' | 'success' | 'warning' | 'error' | 'default'> = {
  [AccountRootType.Asset]: 'info',
  [AccountRootType.Liability]: 'warning',
  [AccountRootType.Equity]: 'default',
  [AccountRootType.Income]: 'success',
  [AccountRootType.Expense]: 'error',
}

export const accountColumns: ColumnDef<AccountRow>[] = [
  { key: 'code', title: 'columns.code', width: 110, primary: true },
  { key: 'name', title: 'columns.name', minWidth: 180 },
  {
    key: 'rootType',
    title: 'columns.rootType',
    width: 110,
    render: (row) =>
      h(TStatusBadge, {
        value: row.rootType ?? '',
        type: ROOT_TYPE_BADGE[row.rootType ?? ''] ?? 'default',
        label: row.rootType ? ROOT_TYPE_LABELS[row.rootType] ?? String(row.rootType) : '—',
      }),
  },
  { key: 'subType', title: 'columns.subType', width: 120, render: (row) => row.subType ?? '—' },
  {
    key: 'isGroup',
    title: 'columns.isGroup',
    width: 100,
    render: (row) =>
      row.isGroup ? h(TStatusBadge, { value: 1, type: 'default', label: 'Group' }) : '—',
  },
  {
    key: 'systemRole',
    title: 'columns.systemRole',
    minWidth: 160,
    mobileHidden: true,
    render: (row) => (row.systemRole ? SYSTEM_ROLE_LABELS[row.systemRole] ?? String(row.systemRole) : '—'),
  },
  { key: 'currency', title: 'columns.currency', width: 96, mobileHidden: true, render: (row) => row.currency ?? '—' },
  {
    key: 'isActive',
    title: 'columns.isActive',
    width: 100,
    render: (row) =>
      h(TStatusBadge, {
        value: row.isActive ? 1 : 0,
        type: row.isActive ? 'success' : 'default',
        labelKey: row.isActive ? 'admin.shared.status.active' : 'admin.shared.status.inactive',
      }),
  },
]

/**
 * `parentId` uses the custom `finance-parent` field type — the page injects a
 * fieldRenderer with the dynamic group-account options (static schema selects
 * cannot carry runtime options).
 *
 * `rootType` / `isGroup` use custom field types so the page can disable them in
 * edit mode: the backend `UpdateAccountDto` carries neither, so editing them
 * would silently no-op. Both keep declaring their options/label here; the page
 * renderer reads `ctx.item.options` and greys the control when editing.
 */
export const accountFormSchema: FormSchemaItem[] = [
  { key: 'code', labelKey: 'form.code', label: 'Code', type: 'text', required: true },
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'text', required: true },
  {
    key: 'rootType',
    labelKey: 'form.rootType',
    label: 'Root Type',
    type: 'finance-root-type',
    required: true,
    options: Object.entries(ROOT_TYPE_LABELS).map(([value, label]) => ({ label, value })),
  },
  { key: 'parentId', labelKey: 'form.parentId', label: 'Parent Account', type: 'finance-parent' },
  { key: 'isGroup', labelKey: 'form.isGroup', label: 'Group Account', type: 'finance-is-group' },
  { key: 'subType', labelKey: 'form.subType', label: 'Sub Type', type: 'text' },
  { key: 'currency', labelKey: 'form.currency', label: 'Currency', type: 'text' },
  {
    key: 'systemRole',
    labelKey: 'form.systemRole',
    label: 'System Role',
    type: 'select',
    options: [
      { label: 'None', value: '' },
      ...Object.entries(SYSTEM_ROLE_LABELS).map(([value, label]) => ({ label, value })),
    ],
  },
  {
    key: 'cashFlowActivity',
    labelKey: 'form.cashFlowActivity',
    label: 'Cash Flow Activity',
    type: 'select',
    options: [
      { label: 'None', value: '' },
      ...Object.entries(CASH_FLOW_LABELS).map(([value, label]) => ({ label, value })),
    ],
  },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'textarea' },
  { key: 'isActive', labelKey: 'form.isActive', label: 'Active', type: 'switch' },
]
