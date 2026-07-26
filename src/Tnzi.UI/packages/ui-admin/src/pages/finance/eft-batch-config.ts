import { EMPTY_DASH } from '../../utils/placeholders'
import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { DataTableColumns } from 'naive-ui'
import {
  EftBatchStatus,
  EftFileFormat,
  type EftBatchDto,
  type EftBatchLineDto,
  type EftQueueItemDto,
} from '../../services/bridges/finance-bridge'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { amountCell, fmtMoney, fmtDate } from './money'
import TMoney from '../../components/finance/TMoney.vue'
import type { SearchFieldItem } from '../../components/crud/TCrudSearchAdvanced.vue'

/** All-optional row shape (house pattern) so ColumnDef stays assignable. */
export type EftBatchRow = Partial<EftBatchDto>

/** EftBatchStatus → badge meta (key = backend PascalCase member). */
export const EFT_STATUS_META: Record<string, { type: 'default' | 'success' | 'warning'; label: string }> = {
  [EftBatchStatus.Draft]: { type: 'default', label: 'status.draft' },
  [EftBatchStatus.Generated]: { type: 'success', label: 'status.generated' },
  [EftBatchStatus.Voided]: { type: 'warning', label: 'status.voided' },
}

/** EftFileFormat → short label key. */
export const EFT_FORMAT_LABEL: Record<string, string> = {
  [EftFileFormat.Nacha]: 'format.nacha',
  [EftFileFormat.Cpa005]: 'format.cpa005',
}

/** Masked account number cell ("••••1234"), or a dash when unset. */
function maskedCell(masked?: string | null): string {
  if (!masked) return EMPTY_DASH
  return masked.length <= 4 ? `••••${masked}` : masked
}

/** Batch list columns. */
export function buildEftBatchColumns(t: (key: string) => string): ColumnDef<EftBatchRow>[] {
  return [
    { key: 'number', title: t('columns.number'), width: 130, primary: true, render: (r) => r.number ?? t('draftLabel') },
    { key: 'bankAccountName', title: t('columns.account'), minWidth: 150, mobileHidden: true, render: (r) => r.bankAccountName ?? EMPTY_DASH },
    { key: 'format', title: t('columns.format'), width: 100, render: (r) => (r.format ? t(EFT_FORMAT_LABEL[String(r.format)] ?? '') : EMPTY_DASH) },
    { key: 'currency', title: t('columns.currency'), width: 90, mobileHidden: true, render: (r) => r.currency ?? EMPTY_DASH },
    { key: 'effectiveDate', title: t('columns.effectiveDate'), width: 120, render: (r) => fmtDate(r.effectiveDate) },
    { key: 'totalCount', title: t('columns.count'), width: 90, render: (r) => String(r.totalCount ?? 0) },
    { key: 'totalAmount', title: t('columns.amount'), width: 140, render: (r) => h(TMoney, { value: r.totalAmount, currency: r.currency }) },
    {
      key: 'status',
      title: t('columns.status'),
      width: 110,
      render: (r) => {
        const meta = EFT_STATUS_META[String(r.status ?? '')]
        return meta ? h(TStatusBadge, { value: String(r.status ?? ''), type: meta.type, label: t(meta.label) }) : String(r.status ?? '')
      },
    },
  ]
}

/** EFT queue columns (batchable outbound bank-transfer payments). */
export function buildEftQueueColumns(t: (key: string) => string): DataTableColumns<EftQueueItemDto> {
  return [
    { type: 'selection' },
    { key: 'paymentNumber', title: t('queue.columns.payment'), width: 130, render: (r) => r.paymentNumber ?? EMPTY_DASH },
    { key: 'payeeName', title: t('queue.columns.payee'), minWidth: 160, render: (r) => r.payeeName ?? EMPTY_DASH },
    { key: 'partyBankAccountMasked', title: t('queue.columns.bankAccount'), width: 130, render: (r) => maskedCell(r.partyBankAccountMasked) },
    { key: 'docDate', title: t('queue.columns.date'), width: 110, render: (r) => fmtDate(r.docDate) },
    { key: 'currency', title: t('queue.columns.currency'), width: 90, render: (r) => r.currency },
    { key: 'amount', title: t('queue.columns.amount'), width: 130, render: (r) => h(TMoney, { value: r.amount, currency: r.currency }) },
  ]
}

/** Batch detail line columns. */
export function buildEftLineColumns(t: (key: string) => string): DataTableColumns<EftBatchLineDto> {
  return [
    { key: 'paymentNumber', title: t('lines.payment'), width: 130, render: (r) => r.paymentNumber ?? EMPTY_DASH },
    { key: 'payeeName', title: t('lines.payee'), minWidth: 160, render: (r) => r.payeeName ?? EMPTY_DASH },
    { key: 'partyBankAccountMasked', title: t('lines.bankAccount'), width: 130, render: (r) => maskedCell(r.partyBankAccountMasked) },
    { key: 'amount', title: t('lines.amount'), width: 140, render: (r) => h(TMoney, { value: r.amount }) },
  ]
}

/** EFT 批次筛选：后端支持状态与文件格式。 */
export function buildEftSearchFields(t: (key: string) => string): SearchFieldItem[] {
  return [
    {
      key: 'status',
      label: t('columns.status'),
      type: 'select',
      options: [
        { label: t('status.draft'), value: 'Draft' },
        { label: t('status.generated'), value: 'Generated' },
        { label: t('status.voided'), value: 'Voided' },
      ],
    },
    {
      key: 'format',
      label: t('columns.format'),
      type: 'select',
      options: [
        { label: 'NACHA', value: 'Nacha' },
        { label: 'CPA-005', value: 'Cpa005' },
      ],
    },
  ]
}
