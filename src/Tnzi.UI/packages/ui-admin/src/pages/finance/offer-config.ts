import { EMPTY_DASH } from '../../utils/placeholders'
import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { moneyCell, fmtDate } from './money'
import { FinanceOfferStatus } from '../../services/bridges/finance-bridge'

/**
 * Shared table shape for the two non-posting documents.
 *
 * Estimates and purchase orders differ only in the party and the wording, so
 * the column builder is parameterised rather than duplicated - the alternative
 * is two tables that drift.
 */
export interface OfferRow {
  id?: string
  number?: string | null
  status?: FinanceOfferStatus
  customerId?: string
  customerName?: string | null
  vendorId?: string
  vendorName?: string | null
  docDate?: string
  expiryDate?: string | null
  expectedDate?: string | null
  currency?: string
  subTotal?: number
  taxTotal?: number
  total?: number
  memo?: string | null
  internalNote?: string | null
  shipTo?: string | null
  convertedToDocType?: string | null
  convertedToDocId?: string | null
  creationTime?: string
}

/**
 * Status tone.
 *
 * `Declined` is a warning rather than an error: a customer saying no is a
 * normal business outcome, not a system fault. `Converted` is the success
 * terminal - that is the one everybody is aiming for.
 */
export const OFFER_STATUS_META: Record<string, { label: string; type: 'default' | 'info' | 'success' | 'warning' | 'error' }> = {
  [FinanceOfferStatus.Draft]: { label: 'status.draft', type: 'default' },
  [FinanceOfferStatus.Sent]: { label: 'status.sent', type: 'info' },
  [FinanceOfferStatus.Accepted]: { label: 'status.accepted', type: 'success' },
  [FinanceOfferStatus.Declined]: { label: 'status.declined', type: 'warning' },
  [FinanceOfferStatus.Converted]: { label: 'status.converted', type: 'success' },
  [FinanceOfferStatus.Closed]: { label: 'status.closed', type: 'default' },
}

export function offerStatusCell(status: FinanceOfferStatus | undefined, t: (key: string) => string) {
  const meta = OFFER_STATUS_META[status ?? '']
  return h(TStatusBadge, {
    value: String(status ?? ''),
    type: meta?.type ?? 'default',
    label: meta ? t(meta.label) : String(status ?? ''),
  })
}

/**
 * True while the document is still worth chasing AND its validity date has
 * passed. Lapsing is read off the date rather than stored as a status, so there
 * is only ever one answer to "has this expired".
 */
export function isLapsed(row: OfferRow, dateField: 'expiryDate' | 'expectedDate'): boolean {
  const value = row[dateField]
  if (!value) return false
  if (row.status !== FinanceOfferStatus.Sent && row.status !== FinanceOfferStatus.Accepted) return false
  return new Date(value).getTime() < Date.now()
}

export function buildOfferColumns(
  t: (key: string) => string,
  kind: 'estimate' | 'purchaseOrder',
  onOpen?: (row: OfferRow) => void,
): ColumnDef<OfferRow>[] {
  const partyKey = kind === 'estimate' ? 'customerName' : 'vendorName'
  const dateField = kind === 'estimate' ? 'expiryDate' : 'expectedDate'

  return [
    {
      key: 'number',
      title: 'columns.number',
      width: 130,
      primary: true,
      render: (row) =>
        onOpen
          ? h('button', { type: 'button', class: 'fin-party-link', onClick: () => onOpen(row) }, row.number ?? t('columns.draftPlaceholder'))
          : (row.number ?? t('columns.draftPlaceholder')),
    },
    { key: partyKey, title: 'columns.party', minWidth: 180, render: (row) => row[partyKey] ?? EMPTY_DASH },
    { key: 'docDate', title: 'columns.docDate', width: 120, render: (row) => fmtDate(row.docDate) },
    {
      key: dateField,
      title: kind === 'estimate' ? 'columns.expiryDate' : 'columns.expectedDate',
      width: 140,
      mobileHidden: true,
      // A lapsed date is the one thing on this row that needs chasing today.
      render: (row) =>
        row[dateField]
          ? h('span', { class: isLapsed(row, dateField) ? 'fin-offer-lapsed' : undefined }, fmtDate(row[dateField]))
          : EMPTY_DASH,
    },
    { key: 'currency', title: 'columns.currency', width: 90, mobileHidden: true, render: (row) => row.currency ?? EMPTY_DASH },
    { key: 'total', title: 'columns.total', width: 130, align: 'right', render: (row) => moneyCell(row.total, row.currency, true) },
    { key: 'status', title: 'columns.status', width: 120, render: (row) => offerStatusCell(row.status, t) },
  ]
}
