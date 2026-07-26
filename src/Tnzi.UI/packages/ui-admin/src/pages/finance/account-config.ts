import { EMPTY_DASH } from '../../utils/placeholders'
import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem, FormSchemaSection } from '../_shared/form-schema'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { AccountRootType, AccountSystemRole, CashFlowActivity } from '../../services/bridges/finance-bridge'
import { amountCell, fmtAmount } from './money'
import TMoney from '../../components/finance/TMoney.vue'
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
  /** Depth in the chart-of-accounts tree (0 = root) - drives the name-cell indent. */
  _depth?: number
  currency?: string | null
  systemRole?: AccountSystemRole | null
  cashFlowActivity?: CashFlowActivity | null
  isActive?: boolean
  creationTime?: string
  /**
   * Base-currency balance as of today, merged in from `accounts.balances` after
   * the page fetch (not a field of the wire `AccountDto`). `undefined` = not
   * loaded yet / not requested; group accounts never carry one.
   */
  balance?: number
}

/**
 * An account carrying a system role is resolved BY ROLE by the posting pipeline
 * (which also requires it to be active), so deleting or deactivating it breaks
 * the corresponding postings forever - the backend rejects both with a 409.
 * The role itself is the fact the UI gates on; clear it first to release the account.
 */
export const isSystemRoleAccount = (row: AccountRow) => row.systemRole != null

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
  [AccountSystemRole.CurrencyExchangeClearing]: 'Currency Exchange Clearing',
}

export const CASH_FLOW_LABELS: Record<string, string> = {
  [CashFlowActivity.Operating]: 'Operating',
  [CashFlowActivity.Investing]: 'Investing',
  [CashFlowActivity.Financing]: 'Financing',
  [CashFlowActivity.CashEquivalent]: 'Cash Equivalent',
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
  // Tree-indent the name by depth so the chart-of-accounts hierarchy (its defining feature) is visible;
  // group (header) accounts are emphasised.
  {
    key: 'name',
    title: 'columns.name',
    minWidth: 200,
    render: (row) =>
      h('span', { style: { paddingLeft: `${(row._depth ?? 0) * 16}px`, fontWeight: row.isGroup ? 600 : 400 } }, row.name ?? EMPTY_DASH),
  },
  {
    key: 'rootType',
    title: 'columns.rootType',
    width: 110,
    render: (row) =>
      h(TStatusBadge, {
        value: row.rootType ?? '',
        type: ROOT_TYPE_BADGE[row.rootType ?? ''] ?? 'default',
        label: row.rootType ? ROOT_TYPE_LABELS[row.rootType] ?? String(row.rootType) : EMPTY_DASH,
      }),
  },
  { key: 'subType', title: 'columns.subType', width: 120, render: (row) => row.subType ?? EMPTY_DASH },
  {
    key: 'isGroup',
    title: 'columns.isGroup',
    width: 100,
    render: (row) =>
      row.isGroup ? h(TStatusBadge, { value: 1, type: 'default', label: 'Group' }) : EMPTY_DASH,
  },
  {
    key: 'systemRole',
    title: 'columns.systemRole',
    minWidth: 160,
    mobileHidden: true,
    render: (row) => (row.systemRole ? SYSTEM_ROLE_LABELS[row.systemRole] ?? String(row.systemRole) : EMPTY_DASH),
  },
  { key: 'currency', title: 'columns.currency', width: 96, mobileHidden: true, render: (row) => row.currency ?? EMPTY_DASH },
  {
    key: 'balance',
    title: 'columns.balance',
    width: 130,
    // Base currency (an account restricted to EUR still reports its base-currency
    // balance) - so render a plain amount, never the account's own currency symbol.
    // Signed: debit-positive, so liability/equity/income accounts read negative.
    render: (row) => (row.isGroup || row.balance == null ? EMPTY_DASH : h(TMoney, { value: row.balance })),
  },
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
 * `parentId` uses the custom `finance-parent` field type - the page injects a
 * fieldRenderer with the dynamic group-account options (static schema selects
 * cannot carry runtime options).
 *
 * `rootType` / `isGroup` use custom field types so the page can disable them in
 * edit mode: the backend `UpdateAccountDto` carries neither, so editing them
 * would silently no-op. Both keep declaring their options/label here; the page
 * renderer reads `ctx.item.options` and greys the control when editing.
 *
 * `isActive` is a custom field type for the same reason: the backend refuses to
 * deactivate a role-bearing account, so the page greys the switch while a system
 * role is selected (clearing the role in the same edit re-enables it - the
 * backend guard keys on the resulting state, not the original).
 */
/**
 * An account is identified, then classified, then wired into the reports.
 * Keeping those three apart matters more here than on most forms: `rootType` /
 * `parentId` / `isGroup` decide where the account sits in the chart, while
 * `systemRole` / `cashFlowActivity` decide which statements pick it up. Mixing
 * them in one column is how a posting account ends up in the wrong section.
 */
export const accountFormSections: FormSchemaSection[] = [
  { key: 'basics', labelKey: 'admin.shared.formSections.basics', label: 'Basics', icon: 'mdi:tag-outline' },
  { key: 'classification', labelKey: 'admin.shared.formSections.classification', label: 'Classification', icon: 'mdi:file-tree-outline' },
  { key: 'reporting', labelKey: 'admin.shared.formSections.behaviour', label: 'Reporting', icon: 'mdi:chart-box-outline' },
  { key: 'notes', labelKey: 'admin.shared.formSections.notes', label: 'Notes', icon: 'mdi:note-text-outline' },
]

export const accountFormSchema: FormSchemaItem[] = [
  { key: 'code', labelKey: 'form.code', label: 'Code', type: 'text', required: true, section: 'basics' },
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'text', required: true, section: 'basics' },
  { key: 'isActive', labelKey: 'form.isActive', label: 'Active', type: 'finance-is-active', section: 'basics' },
  {
    key: 'rootType',
    labelKey: 'form.rootType',
    label: 'Root Type',
    type: 'finance-root-type',
    required: true,
    section: 'classification',
    options: Object.entries(ROOT_TYPE_LABELS).map(([value, label]) => ({ label, value })),
  },
  { key: 'parentId', labelKey: 'form.parentId', label: 'Parent Account', type: 'finance-parent', section: 'classification' },
  { key: 'isGroup', labelKey: 'form.isGroup', label: 'Group Account', type: 'finance-is-group', section: 'classification' },
  { key: 'subType', labelKey: 'form.subType', label: 'Sub Type', type: 'text', section: 'classification' },
  { key: 'currency', labelKey: 'form.currency', label: 'Currency', type: 'text', section: 'classification' },
  {
    key: 'systemRole',
    labelKey: 'form.systemRole',
    label: 'System Role',
    type: 'select',
    section: 'reporting',
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
    section: 'reporting',
    options: [
      { label: 'None', value: '' },
      ...Object.entries(CASH_FLOW_LABELS).map(([value, label]) => ({ label, value })),
    ],
  },
  { key: 'description', labelKey: 'form.description', label: 'Description', type: 'textarea', section: 'notes' },
]
