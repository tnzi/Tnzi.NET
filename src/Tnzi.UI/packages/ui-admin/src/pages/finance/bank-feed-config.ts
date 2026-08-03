import { EMPTY_DASH } from '../../utils/placeholders'
import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import {
  BankTransactionStatus,
  type BankTransactionDto,
} from '../../services/bridges/finance-bridge'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { fmtMoney, fmtDate } from './money'

/** All-optional row shape (house pattern) so ColumnDef stays assignable. */
export type BankTransactionRow = Partial<BankTransactionDto>

/** BankTransactionStatus → badge meta (key = backend PascalCase member). */
export const TXN_STATUS_META: Record<string, { type: 'warning' | 'success' | 'default'; label: string }> = {
  [BankTransactionStatus.Pending]: { type: 'warning', label: 'status.pending' },
  [BankTransactionStatus.Matched]: { type: 'success', label: 'status.matched' },
  [BankTransactionStatus.Excluded]: { type: 'default', label: 'status.excluded' },
}

/** Signed amount cell: deposit (positive) in success tone, withdrawal in muted default. */
function amountCellFor(r: BankTransactionRow) {
  const amount = r.amount ?? 0
  const text = fmtMoney(amount, r.currency)
  return h('span', { style: `font-variant-numeric: tabular-nums; color: ${amount >= 0 ? 'var(--tnzi-success, #18a058)' : 'var(--tnzi-text-2, inherit)'};` }, text)
}

/** Suggestion badge: match rule + confidence percentage (only for suggested rows). */
function suggestionCell(t: (key: string) => string, r: BankTransactionRow) {
  if (!r.suggestedJournalLineId || !r.matchRule) return EMPTY_DASH
  const confidence = r.matchConfidence != null ? ` ${Math.round(r.matchConfidence * 100)}%` : ''
  return h(TStatusBadge, { value: r.matchRule, type: 'info', label: `${r.matchRule}${confidence}` })
}

export function buildBankTransactionColumns(t: (key: string) => string): ColumnDef<BankTransactionRow>[] {
  return [
    { key: 'txnDate', title: t('columns.date'), width: 110, render: (r) => fmtDate(r.txnDate) },
    {
      key: 'description',
      title: t('columns.description'),
      minWidth: 200,
      primary: true,
      render: (r) => r.description ?? r.payee ?? EMPTY_DASH,
    },
    { key: 'reference', title: t('columns.reference'), minWidth: 120, mobileHidden: true, render: (r) => r.reference ?? EMPTY_DASH },
    { key: 'amount', title: t('columns.amount'), width: 140, render: (r) => amountCellFor(r) },
    {
      key: 'status',
      title: t('columns.status'),
      width: 110,
      render: (r) => {
        const meta = TXN_STATUS_META[String(r.status ?? '')]
        return meta ? h(TStatusBadge, { value: String(r.status ?? ''), type: meta.type, label: t(meta.label) }) : String(r.status ?? '')
      },
    },
    { key: 'suggestion', title: t('columns.suggestion'), width: 150, mobileHidden: true, render: (r) => suggestionCell(t, r) },
  ]
}

/**
 * How to READ the file (shown in the import modal when source = CSV).
 *
 * Separate from the column mapping below because these drive the client-side
 * preview: change the delimiter or skip a row and the preview re-parses, so a
 * wrong setting is visible before importing a few hundred junk rows rather than
 * after.
 */
export const csvParseFormSchema: FormSchemaItem[] = [
  { key: 'hasHeader', labelKey: 'import.hasHeader', label: 'Has Header Row', type: 'switch' },
  { key: 'delimiter', labelKey: 'import.delimiter', label: 'Delimiter', type: 'text', placeholderKey: 'import.delimiterPlaceholder' },
  { key: 'skipRows', labelKey: 'import.skipRows', label: 'Skip Rows', type: 'number', min: 0 },
  { key: 'dateFormat', labelKey: 'import.dateFormat', label: 'Date Format', type: 'text', placeholderKey: 'import.dateFormatPlaceholder' },
  { key: 'decimalSeparator', labelKey: 'import.decimalSeparator', label: 'Decimal Separator', type: 'text', placeholderKey: 'import.decimalSeparatorPlaceholder' },
]

/**
 * Which column is which. Values stay 0-based indexes on the wire (that is what
 * `CsvMappingDto` carries), but they are picked BY NAME from the previewed
 * header row - the `finance-csv-column` renderer supplies the options.
 *
 * Use a single signed `amountColumn` OR the `debitColumn` + `creditColumn` pair;
 * `guessColumns` resolves which shape a file uses. The page persists the last
 * mapping per account in localStorage.
 */
export const csvColumnFormSchema: FormSchemaItem[] = [
  { key: 'dateColumn', labelKey: 'import.dateColumn', label: 'Date Column', type: 'finance-csv-column', required: true },
  { key: 'descriptionColumn', labelKey: 'import.descriptionColumn', label: 'Description Column', type: 'finance-csv-column' },
  { key: 'referenceColumn', labelKey: 'import.referenceColumn', label: 'Reference Column', type: 'finance-csv-column' },
  { key: 'amountColumn', labelKey: 'import.amountColumn', label: 'Amount Column', type: 'finance-csv-column' },
  { key: 'debitColumn', labelKey: 'import.debitColumn', label: 'Debit Column', type: 'finance-csv-column' },
  { key: 'creditColumn', labelKey: 'import.creditColumn', label: 'Credit Column', type: 'finance-csv-column' },
]
