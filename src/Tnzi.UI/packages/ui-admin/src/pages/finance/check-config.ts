import { EMPTY_DASH } from '../../utils/placeholders'
import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { DataTableColumns } from 'naive-ui'
import { CheckStatus, type BankCheckDto, type CheckQueueItemDto } from '../../services/bridges/finance-bridge'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { fmtDate } from './money'
import TMoney from '../../components/finance/TMoney.vue'
import type { SearchFieldItem } from '../../components/crud/TCrudSearchAdvanced.vue'

/** All-optional row shape (house pattern) so ColumnDef stays assignable. */
export type BankCheckRow = Partial<BankCheckDto>

/** CheckStatus → badge meta (key = backend PascalCase member). */
export const CHECK_STATUS_META: Record<string, { type: 'success' | 'warning' | 'default'; label: string }> = {
  [CheckStatus.Issued]: { type: 'success', label: 'status.issued' },
  [CheckStatus.Void]: { type: 'warning', label: 'status.void' },
  [CheckStatus.Spoiled]: { type: 'default', label: 'status.spoiled' },
}

/** Register book columns (posted / voided / spoiled checks). */
export function buildCheckColumns(t: (key: string) => string): ColumnDef<BankCheckRow>[] {
  return [
    { key: 'checkNumber', title: t('columns.number'), width: 110, primary: true, render: (r) => String(r.checkNumber ?? EMPTY_DASH) },
    { key: 'bankAccountName', title: t('columns.account'), minWidth: 150, mobileHidden: true, render: (r) => r.bankAccountName ?? EMPTY_DASH },
    { key: 'payeeName', title: t('columns.payee'), minWidth: 150, render: (r) => r.payeeName ?? EMPTY_DASH },
    { key: 'amount', title: t('columns.amount'), width: 130, render: (r) => h(TMoney, { value: r.amount, currency: r.currency }) },
    { key: 'issueDate', title: t('columns.issueDate'), width: 110, render: (r) => fmtDate(r.issueDate) },
    {
      key: 'status',
      title: t('columns.status'),
      width: 110,
      render: (r) => {
        const meta = CHECK_STATUS_META[String(r.status ?? '')]
        return meta ? h(TStatusBadge, { value: String(r.status ?? ''), type: meta.type, label: t(meta.label) }) : String(r.status ?? '')
      },
    },
    {
      key: 'isManual',
      title: t('columns.source'),
      width: 100,
      mobileHidden: true,
      render: (r) => (r.isManual ? t('source.manual') : t('source.printed')),
    },
  ]
}

/** Print-queue columns (posted outbound check payments awaiting print). */
export function buildCheckQueueColumns(t: (key: string) => string): DataTableColumns<CheckQueueItemDto> {
  return [
    { type: 'selection' },
    { key: 'paymentNumber', title: t('queue.columns.payment'), width: 130, render: (r) => r.paymentNumber ?? EMPTY_DASH },
    { key: 'payeeName', title: t('queue.columns.payee'), minWidth: 160, render: (r) => r.payeeName ?? EMPTY_DASH },
    { key: 'bankAccountName', title: t('queue.columns.account'), minWidth: 150, render: (r) => r.bankAccountName ?? EMPTY_DASH },
    { key: 'docDate', title: t('queue.columns.date'), width: 110, render: (r) => fmtDate(r.docDate) },
    { key: 'amount', title: t('queue.columns.amount'), width: 130, render: (r) => h(TMoney, { value: r.amount, currency: r.currency }) },
  ]
}

/** 支票登记簿筛选：后端 `CheckQueryDto` 支持状态 + 银行账户（另有 keyword 走关键字框）。 */
export function buildCheckSearchFields(t: (key: string) => string): SearchFieldItem[] {
  return [
    {
      key: 'status',
      label: t('columns.status'),
      type: 'select',
      options: [
        { label: t('status.issued'), value: 'Issued' },
        { label: t('status.void'), value: 'Void' },
        { label: t('status.spoiled'), value: 'Spoiled' },
      ],
    },
  ]
}
