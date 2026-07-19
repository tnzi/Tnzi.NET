import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import {
  BankNumberScheme,
  CheckStockType,
  CheckLayout,
  type BankAccountDto,
} from '../../services/bridges/finance-bridge'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { formatDateOnly } from '@tnzi/core'

/** All-optional row shape (house pattern) so ColumnDef stays assignable. */
export type BankAccountRow = Partial<BankAccountDto>

const SCHEME_BADGE: Record<string, { label: string; type: 'info' | 'success' }> = {
  [BankNumberScheme.UsAba]: { label: 'scheme.usAba', type: 'info' },
  [BankNumberScheme.CaEft]: { label: 'scheme.caEft', type: 'success' },
}

/** Masked account number cell ("••••1234"), or a dash when unset. */
function maskedCell(masked?: string | null): string {
  if (!masked) return '—'
  return masked.length <= 4 ? `••••${masked}` : masked
}

export function buildBankAccountColumns(t: (key: string) => string): ColumnDef<BankAccountRow>[] {
  return [
    { key: 'name', title: t('columns.name'), minWidth: 160, primary: true, render: (r) => r.name ?? '—' },
    { key: 'accountName', title: t('columns.account'), minWidth: 160, render: (r) => r.accountName ?? r.accountId ?? '—' },
    { key: 'bankName', title: t('columns.bankName'), minWidth: 140, mobileHidden: true, render: (r) => r.bankName ?? '—' },
    {
      key: 'scheme',
      title: t('columns.scheme'),
      width: 110,
      render: (r) => {
        const meta = SCHEME_BADGE[String(r.scheme ?? '')]
        return meta ? h(TStatusBadge, { value: String(r.scheme ?? ''), type: meta.type, label: t(meta.label) }) : '—'
      },
    },
    { key: 'accountNumberMasked', title: t('columns.accountNumber'), width: 130, render: (r) => maskedCell(r.accountNumberMasked) },
    { key: 'currency', title: t('columns.currency'), width: 90, mobileHidden: true, render: (r) => r.currency ?? '—' },
    { key: 'nextCheckNumber', title: t('columns.nextCheckNumber'), width: 130, render: (r) => String(r.nextCheckNumber ?? '—') },
    { key: 'lastFeedSyncTime', title: t('columns.lastSync'), width: 140, mobileHidden: true, render: (r) => formatDateOnly(r.lastFeedSyncTime, { utc: true }) },
  ]
}

/** US routing fields show for UsAba (the default); CA transit fields for CaEft. */
function isCaEft(model: Record<string, unknown>): boolean {
  return model.scheme === BankNumberScheme.CaEft
}

function isUsAba(model: Record<string, unknown>): boolean {
  return model.scheme !== BankNumberScheme.CaEft
}

/** New records only (create form) — the starting check number is fixed after creation. */
function isCreate(model: Record<string, unknown>): boolean {
  return model.id == null
}

const SCHEME_OPTIONS = [
  { value: BankNumberScheme.UsAba, label: 'US ABA', labelKey: 'scheme.usAba' },
  { value: BankNumberScheme.CaEft, label: 'Canada EFT', labelKey: 'scheme.caEft' },
]

const STOCK_OPTIONS = [
  { value: CheckStockType.PrePrinted, label: 'Pre-printed', labelKey: 'stock.prePrinted' },
  { value: CheckStockType.Blank, label: 'Blank', labelKey: 'stock.blank' },
]

const LAYOUT_OPTIONS = [
  { value: CheckLayout.Voucher, label: 'Voucher', labelKey: 'layout.voucher' },
  { value: CheckLayout.ThreePerPage, label: 'Three per page', labelKey: 'layout.threePerPage' },
]

/**
 * Bank account create/edit form (flat schema with conditional routing fields).
 *
 * The mounted funds account (`accountId`) is create-only and set through the
 * `finance-account` renderer. Routing fields switch by scheme (US = routing
 * number; CA = institution + transit). `accountNumber` is write-only: on
 * update, leaving it blank keeps the current number; the list only ever shows
 * the masked tail. It is a custom field type because storing it at all is a
 * deployment capability (it must be encrypted at rest, so an unset encryption
 * key means the backend refuses the write) — the page reads that capability and
 * greys the input rather than letting the user type a number and eat a 400.
 * The starting check number is captured at create; afterwards it is read-only
 * in the list and changed through the row action.
 */
export const bankAccountFormSchema: FormSchemaItem[] = [
  { key: 'accountId', labelKey: 'form.account', label: 'Funds Account', type: 'finance-account', required: true, visible: isCreate },
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'text', required: true },
  { key: 'bankName', labelKey: 'form.bankName', label: 'Bank Name', type: 'text' },
  { key: 'scheme', labelKey: 'form.scheme', label: 'Number Scheme', type: 'select', options: SCHEME_OPTIONS },
  { key: 'routingNumber', labelKey: 'form.routingNumber', label: 'Routing Number', type: 'text', visible: isUsAba },
  { key: 'institutionNumber', labelKey: 'form.institutionNumber', label: 'Institution Number', type: 'text', visible: isCaEft },
  { key: 'transitNumber', labelKey: 'form.transitNumber', label: 'Transit Number', type: 'text', visible: isCaEft },
  { key: 'accountNumber', labelKey: 'form.accountNumber', label: 'Account Number', type: 'finance-account-number', placeholderKey: 'form.accountNumberPlaceholder' },
  { key: 'currency', labelKey: 'form.currency', label: 'Currency', type: 'text' },
  { key: 'nextCheckNumber', labelKey: 'form.nextCheckNumber', label: 'Starting Check Number', type: 'number', min: 1, visible: isCreate },
  { key: 'checkStockType', labelKey: 'form.checkStockType', label: 'Check Stock', type: 'select', options: STOCK_OPTIONS },
  { key: 'checkLayout', labelKey: 'form.checkLayout', label: 'Check Layout', type: 'select', options: LAYOUT_OPTIONS },
  { key: 'offsetXMm', labelKey: 'form.offsetXMm', label: 'Print Offset X (mm)', type: 'number' },
  { key: 'offsetYMm', labelKey: 'form.offsetYMm', label: 'Print Offset Y (mm)', type: 'number' },
  { key: 'feedProviderKey', labelKey: 'form.feedProviderKey', label: 'Feed Provider Key', type: 'text' },
  { key: 'externalAccountId', labelKey: 'form.externalAccountId', label: 'External Account Id', type: 'text' },
  { key: 'eftOriginatorId', labelKey: 'form.eftOriginatorId', label: 'EFT Originator Id', type: 'text' },
  { key: 'eftOriginatorName', labelKey: 'form.eftOriginatorName', label: 'EFT Originator Name', type: 'text' },
]
