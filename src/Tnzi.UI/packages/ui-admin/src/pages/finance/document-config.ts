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
 * 单据列表按往来方筛选时，列表端点绑定的参数名。各单据的 `*QueryDto` 各叫各的：
 * 发票与贷项通知单是 `customerId`，账单与费用是 `vendorId`，只有付款是 `partyId`。
 * ★ 名字不对不会报错：模型绑定器把不认识的参数直接丢掉，列表看起来「已筛选」，
 * 实际返回所有往来方的单据。
 */
export type DocumentPartyFilterKey = 'customerId' | 'vendorId' | 'partyId'

/**
 * 单据列表的高级搜索字段（标准 1「有真实筛选，不是只有关键字」）。
 *
 * 后端 `*QueryDto` 支持 `status` / `dateFrom` / `dateTo` 与往来方
 * （参数名因单据而异，见 {@link DocumentPartyFilterKey}；付款另有 `direction` / `paymentMethod`）。
 * 日期字段交出所选的日历日（`YYYY-MM-DD`，由 `TCrudSearchAdvanced` 换算）。
 * 往来方走 `render` 逃生口挂 `TPartySelect`——它的选项是异步远程搜索，
 * form-schema 的静态 `select` 承载不了；`render` 把选中的 id 写进 `model[key]`。
 */
export function buildDocumentSearchFields(
  t: (key: string) => string,
  options?: {
    party?: {
      key: DocumentPartyFilterKey
      render: (model: Record<string, unknown>, key: DocumentPartyFilterKey) => VNodeChild
    }
  },
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

  const party = options?.party
  if (party) {
    fields.push({
      key: party.key,
      labelKey: 'columns.party',
      label: 'Party',
      type: 'text',
      render: (model) => party.render(model, party.key),
    })
  }

  return fields
}
