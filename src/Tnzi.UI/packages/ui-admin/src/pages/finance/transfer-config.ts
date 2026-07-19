import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import type { TransferDto } from '../../services/bridges/finance-bridge'
import { amountCell, fmtMoney } from './money'
import { formatDateOnly } from '@tnzi/core'
import { docStatusBadge } from './document-config'

/** All-optional row shape (house pattern) so ColumnDef stays assignable. */
export type TransferRow = Partial<TransferDto>

/** True when the row is a cross-currency exchange transfer (target currency differs). */
export function isCrossCurrencyTransfer(r: TransferRow): boolean {
  return !!r.targetCurrency && r.targetCurrency !== r.currency
}

/** Amount cell: same-currency shows one amount; cross-currency shows "out → in". */
function transferAmountLabel(r: TransferRow): string {
  const out = fmtMoney(r.amount, r.currency)
  if (!isCrossCurrencyTransfer(r)) return out
  return `${out} → ${fmtMoney(r.targetAmount, r.targetCurrency)}`
}

export function buildTransferColumns(t: (key: string) => string): ColumnDef<TransferRow>[] {
  return [
    { key: 'number', title: t('columns.number'), width: 130, render: (r) => r.number ?? '—' },
    // Transfer 与五单据共用 FinanceDocumentStatus：徽章走 document-config 单一真值源
    { key: 'status', title: t('columns.status'), width: 110, render: (r) => docStatusBadge(t, r.status) },
    { key: 'transferDate', title: t('columns.date'), width: 120, render: (r) => formatDateOnly(r.transferDate, { utc: true }) },
    { key: 'fromAccountName', title: t('columns.from'), minWidth: 160, render: (r) => r.fromAccountName ?? r.fromAccountId ?? '—' },
    { key: 'toAccountName', title: t('columns.to'), minWidth: 160, render: (r) => r.toAccountName ?? r.toAccountId ?? '—' },
    { key: 'amount', title: t('columns.amount'), minWidth: 160, render: (r) => amountCell(transferAmountLabel(r)) },
    { key: 'reference', title: t('columns.reference'), minWidth: 120, mobileHidden: true, render: (r) => r.reference ?? '—' },
  ]
}

/** Cross-currency mode discriminator for conditional target fields. */
function isCrossForm(model: Record<string, unknown>): boolean {
  const target = String(model.targetCurrency ?? '').trim().toUpperCase()
  if (!target) return false
  return target !== String(model.currency ?? '').trim().toUpperCase()
}

/**
 * Draft create/edit form (accounts through the finance-account renderer).
 *
 * Cross-currency exchange: leave `targetCurrency` blank for a same-currency
 * transfer. Set it (to a code that differs from `currency`) and the target
 * amount + rate fields appear - the backend then posts the exchange as
 * out-currency / in-currency / base-residual vouchers through the clearing
 * account. `amount` is the source (out) amount, `targetAmount` the received
 * (in) amount.
 */
export const transferFormSchema: FormSchemaItem[] = [
  { key: 'fromAccountId', labelKey: 'form.fromAccount', label: 'From Account', type: 'finance-account', required: true },
  { key: 'toAccountId', labelKey: 'form.toAccount', label: 'To Account', type: 'finance-account', required: true },
  { key: 'transferDate', labelKey: 'form.date', label: 'Date', type: 'date', required: true },
  { key: 'amount', labelKey: 'form.amount', label: 'Amount', type: 'number', required: true },
  { key: 'currency', labelKey: 'form.currency', label: 'Currency', type: 'text' },
  { key: 'targetCurrency', labelKey: 'form.targetCurrency', label: 'Target Currency', type: 'text' },
  { key: 'targetAmount', labelKey: 'form.targetAmount', label: 'Target Amount', type: 'number', visible: isCrossForm },
  { key: 'targetExchangeRate', labelKey: 'form.targetExchangeRate', label: 'Target Exchange Rate', type: 'number', visible: isCrossForm },
  { key: 'reference', labelKey: 'form.reference', label: 'Reference', type: 'text' },
  { key: 'memo', labelKey: 'form.memo', label: 'Memo', type: 'textarea' },
]
