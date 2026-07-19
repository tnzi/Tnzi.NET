import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import { ReceiptStatus, type ReceiptDto } from '../../services/bridges/finance-bridge'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { amountCell, fmtMoney } from './money'
import { formatDateOnly } from '@tnzi/core'

/** All-optional row shape (house pattern) so ColumnDef stays assignable. */
export type ReceiptRow = Partial<ReceiptDto>

/** ReceiptStatus → badge meta (key = backend PascalCase member). */
export const RECEIPT_STATUS_META: Record<string, { type: 'default' | 'info' | 'success' | 'error'; label: string }> = {
  [ReceiptStatus.Uploaded]: { type: 'default', label: 'status.uploaded' },
  [ReceiptStatus.Extracted]: { type: 'info', label: 'status.extracted' },
  [ReceiptStatus.Converted]: { type: 'success', label: 'status.converted' },
  [ReceiptStatus.Failed]: { type: 'error', label: 'status.failed' },
}

/** Confidence percentage cell (0-1 → 0-100%), or a dash when unset. */
function confidenceCell(confidence?: number | null): string {
  if (confidence == null) return '—'
  return `${Math.round(confidence * 100)}%`
}

/** Receipt register columns. */
export function buildReceiptColumns(t: (key: string) => string): ColumnDef<ReceiptRow>[] {
  return [
    { key: 'vendorName', title: t('columns.vendor'), minWidth: 160, primary: true, render: (r) => r.vendorName ?? r.matchedVendorName ?? r.originalFileName ?? '—' },
    { key: 'docDate', title: t('columns.docDate'), width: 110, render: (r) => formatDateOnly(r.docDate, { utc: true }) },
    { key: 'total', title: t('columns.total'), width: 130, render: (r) => amountCell(fmtMoney(r.total, r.currency)) },
    { key: 'reference', title: t('columns.reference'), minWidth: 120, mobileHidden: true, render: (r) => r.reference ?? '—' },
    { key: 'confidence', title: t('columns.confidence'), width: 100, mobileHidden: true, render: (r) => confidenceCell(r.confidence) },
    {
      key: 'status',
      title: t('columns.status'),
      width: 110,
      render: (r) => {
        const meta = RECEIPT_STATUS_META[String(r.status ?? '')]
        return meta ? h(TStatusBadge, { value: String(r.status ?? ''), type: meta.type, label: t(meta.label) }) : String(r.status ?? '')
      },
    },
  ]
}

/**
 * Extracted-field correction form (shown in the detail drawer). Amounts and
 * date are editable so the user can fix the vision output before converting.
 */
export const receiptExtractionFormSchema: FormSchemaItem[] = [
  { key: 'vendorName', labelKey: 'form.vendorName', label: 'Vendor Name', type: 'text' },
  { key: 'docDate', labelKey: 'form.docDate', label: 'Document Date', type: 'date' },
  { key: 'currency', labelKey: 'form.currency', label: 'Currency', type: 'text' },
  { key: 'subtotal', labelKey: 'form.subtotal', label: 'Subtotal', type: 'number' },
  { key: 'taxAmount', labelKey: 'form.taxAmount', label: 'Tax Amount', type: 'number' },
  { key: 'total', labelKey: 'form.total', label: 'Total', type: 'number' },
  { key: 'reference', labelKey: 'form.reference', label: 'Reference', type: 'text' },
]
