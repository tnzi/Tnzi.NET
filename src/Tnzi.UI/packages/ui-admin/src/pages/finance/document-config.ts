import { EMPTY_DASH } from '../../utils/placeholders'
import { h } from 'vue'
import type { VNodeChild } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { SearchFieldItem } from '../../components/crud/TCrudSearchAdvanced.vue'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import TMoney from '../../components/finance/TMoney.vue'
import { TRelativeTime } from '@tnzi/ui'

import { FinanceDocumentStatus } from '../../services/bridges/finance-bridge'
import { DOC_STATUS_META, type FinanceDocRow } from '../../components/finance/document-row'
import { fmtDate } from './money'

// Row shape + status vocabulary moved to the component layer - TDocumentCard
// renders them, and a component reaching up into pages/ for the contract it
// displays inverts the layering. Re-exported so the five document pages and
// their configs keep importing them from here.
export { DOC_STATUS_META, type FinanceDocRow } from '../../components/finance/document-row'

export function docStatusBadge(t: (key: string) => string, status?: FinanceDocumentStatus) {
  const meta = DOC_STATUS_META[status ?? '']
  return h(TStatusBadge, {
    value: status ?? '',
    type: meta?.type ?? 'default',
    label: meta ? t(meta.label) : String(status ?? ''),
  })
}

/** 单据列表列（partyKey: 'customerName' | 'vendorName' | 'partyName'；amountKey: 'total' | 'amount'）。 */
export function buildDocumentColumns(
  t: (key: string) => string,
  partyKey: 'customerName' | 'vendorName' | 'partyName',
  options?: { amountKey?: 'total' | 'amount'; showApplied?: boolean; showDueDate?: boolean },
): ColumnDef<FinanceDocRow>[] {
  const amountKey = options?.amountKey ?? 'total'
  const columns: ColumnDef<FinanceDocRow>[] = [
    { key: 'number', title: 'columns.number', width: 130, primary: true, render: (row) => row.number ?? EMPTY_DASH },
    { key: 'status', title: 'columns.status', width: 120, render: (row) => docStatusBadge(t, row.status) },
    { key: 'docDate', title: 'columns.docDate', width: 110, render: (row) => fmtDate(row.docDate) },
    { key: partyKey, title: 'columns.party', minWidth: 160, render: (row) => row[partyKey] ?? EMPTY_DASH },
    { key: 'currency', title: 'columns.currency', width: 90, mobileHidden: true },
    { key: amountKey, title: 'columns.total', width: 130, render: (row) => h(TMoney, { value: row[amountKey], currency: row.currency, class: 'font-600' }) },
  ]

  if (options?.showApplied) {
    columns.push({ key: 'appliedTotal', title: 'columns.applied', width: 120, mobileHidden: true, render: (row) => h(TMoney, { value: row.appliedTotal, currency: row.currency }) })
    // Outstanding (Total − Applied) - the primary collections/payables figure, emphasised right-aligned.
    columns.push({
      key: 'outstanding',
      title: 'columns.outstanding',
      width: 130,
      render: (row) => h(TMoney, { value: (row[amountKey] ?? 0) - (row.appliedTotal ?? 0), currency: row.currency, class: 'font-600' }),
    })
  }
  if (options?.showDueDate) {
    columns.push({ key: 'dueDate', title: 'columns.dueDate', width: 110, mobileHidden: true, render: (row) => fmtDate(row.dueDate) })
  }

  columns.push({
    key: 'creationTime',
    title: 'columns.creationTime',
    width: 140,
    mobileHidden: true,
    render: (row) => h(TRelativeTime, { value: row.creationTime }),
  })
  return columns
}


/**
 * 单据列表的高级搜索字段（标准 1「有真实筛选，不是只有关键字」）。
 *
 * 后端 `*QueryDto` 一直支持 `status` / `dateFrom` / `dateTo` / `partyId`
 * （付款另有 `direction` / `paymentMethod`），此前前端一个都没接出来。
 * 往来方走 `render` 逃生口挂 `TPartySelect`——它的选项是异步远程搜索，
 * form-schema 的静态 `select` 承载不了。
 */
export function buildDocumentSearchFields(
  t: (key: string) => string,
  options?: { party?: 'customer' | 'vendor' | 'both'; renderParty?: (model: Record<string, unknown>) => VNodeChild },
): SearchFieldItem[] {
  const fields: SearchFieldItem[] = [
    {
      key: 'status',
      labelKey: 'columns.status',
      label: 'Status',
      type: 'select',
      options: [
        { label: t('status.draft'), value: FinanceDocumentStatus.Draft },
        { label: t('status.posted'), value: FinanceDocumentStatus.Posted },
        { label: t('status.partiallyPaid'), value: FinanceDocumentStatus.PartiallyPaid },
        { label: t('status.paid'), value: FinanceDocumentStatus.Paid },
        { label: t('status.voided'), value: FinanceDocumentStatus.Voided },
      ],
    },
    { key: 'dateFrom', label: t('search.dateFrom'), type: 'date' },
    { key: 'dateTo', label: t('search.dateTo'), type: 'date' },
  ]

  if (options?.renderParty) {
    fields.push({
      key: 'partyId',
      labelKey: 'columns.party',
      label: 'Party',
      type: 'text',
      render: options.renderParty,
    })
  }

  return fields
}
