import { EMPTY_DASH } from '../../utils/placeholders'
import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import type { TransferDto } from '../../services/bridges/finance-bridge'
import { amountCell, fmtMoney, fmtDate } from './money'

import { docStatusBadge } from './document-config'
import TMoney from '../../components/finance/TMoney.vue'
import type { SearchFieldItem } from '../../components/crud/TCrudSearchAdvanced.vue'

/** All-optional row shape (house pattern) so ColumnDef stays assignable. */
export type TransferRow = Partial<TransferDto>

/** True when the row is a cross-currency exchange transfer (target currency differs). */
export function isCrossCurrencyTransfer(r: TransferRow): boolean {
  return !!r.targetCurrency && r.targetCurrency !== r.currency
}

/**
 * 金额单元格：同币种一个金额；跨币种显示「出币 → 入币」。
 *
 * 两侧都走 TMoney —— 括号负数与屏幕阅读器的真负号是金额的语义，不该因为
 * 这一格要拼两个数字就退回纯文本。
 */
function transferAmountCell(r: TransferRow) {
  const out = h(TMoney, { value: r.amount, currency: r.currency })
  if (!isCrossCurrencyTransfer(r)) return out
  return h('span', { class: 'inline-flex items-center gap-4px' }, [
    out,
    h('span', { 'aria-hidden': 'true' }, '→'),
    h(TMoney, { value: r.targetAmount, currency: r.targetCurrency }),
  ])
}

export function buildTransferColumns(t: (key: string) => string): ColumnDef<TransferRow>[] {
  return [
    { key: 'number', title: t('columns.number'), width: 130, render: (r) => r.number ?? EMPTY_DASH },
    // Transfer 与五单据共用 FinanceDocumentStatus：徽章走 document-config 单一真值源
    { key: 'status', title: t('columns.status'), width: 110, render: (r) => docStatusBadge(t, r.status) },
    { key: 'transferDate', title: t('columns.date'), width: 120, render: (r) => fmtDate(r.transferDate) },
    { key: 'fromAccountName', title: t('columns.from'), minWidth: 160, render: (r) => r.fromAccountName ?? r.fromAccountId ?? EMPTY_DASH },
    { key: 'toAccountName', title: t('columns.to'), minWidth: 160, render: (r) => r.toAccountName ?? r.toAccountId ?? EMPTY_DASH },
    { key: 'amount', title: t('columns.amount'), minWidth: 160, render: (r) => transferAmountCell(r) },
    { key: 'reference', title: t('columns.reference'), minWidth: 120, mobileHidden: true, render: (r) => r.reference ?? EMPTY_DASH },
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

/** 划转筛选：后端 `TransferQueryDto` 支持状态与日期区间（`from` / `to`）。 */
export function buildTransferSearchFields(t: (key: string) => string): SearchFieldItem[] {
  return [
    {
      key: 'status',
      label: t('columns.status'),
      type: 'select',
      options: [
        { label: t('status.draft'), value: 'Draft' },
        { label: t('status.posted'), value: 'Posted' },
        { label: t('status.voided'), value: 'Voided' },
      ],
    },
    { key: 'from', label: t('search.dateFrom'), type: 'date' },
    { key: 'to', label: t('search.dateTo'), type: 'date' },
  ]
}
