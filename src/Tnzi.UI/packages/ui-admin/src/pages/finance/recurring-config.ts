/**
 * Recurring document templates - columns + form schema.
 *
 * The list answers "what is about to happen": next run first, with the money
 * and the auto-post posture visible without opening anything. A template list
 * that only shows names makes you open every row to find the one that is about
 * to bill the wrong customer.
 */
import { EMPTY_DASH } from '../../utils/placeholders'
import { h } from 'vue'
import { NTag } from 'naive-ui'
import type { ColumnDef } from '../../headless/useColumnSettings'
import type { FormSchemaItem } from '../_shared/form-schema'
import { fmtDate } from './money'
import { formatMoney } from '../../utils/finance-format'
import type { RecurringDocumentDto } from '../../services/bridges/finance-bridge'

/** All-optional row shape (house pattern) so ColumnDef stays assignable. */
export type RecurringRow = Partial<RecurringDocumentDto>

type Translate = (key: string) => string

const STATUS_TYPE: Record<string, 'success' | 'warning' | 'default'> = {
  Active: 'success',
  Paused: 'warning',
  Ended: 'default',
}

const lower = (value?: string | null) => (value ? value.charAt(0).toLowerCase() + value.slice(1) : '')

export function buildRecurringColumns(t: Translate): ColumnDef<RecurringRow>[] {
  return [
    { key: 'name', title: t('columns.name'), minWidth: 200, primary: true },
    { key: 'kind', title: t('columns.kind'), width: 110, render: (r) => (r.kind ? t(`kind.${lower(r.kind)}`) : EMPTY_DASH) },
    { key: 'partyName', title: t('columns.party'), minWidth: 160, render: (r) => r.partyName ?? EMPTY_DASH },
    { key: 'frequency', title: t('columns.schedule'), minWidth: 160, render: (r) => scheduleLabel(r, t) },
    {
      // The one date that decides whether this row needs attention today.
      key: 'nextRunDate',
      title: t('columns.nextRun'),
      width: 130,
      render: (r) => (r.status === 'Ended' ? EMPTY_DASH : fmtDate(r.nextRunDate)),
    },
    {
      key: 'estimatedTotal',
      title: t('columns.amount'),
      width: 130,
      align: 'right',
      render: (r) => formatMoney(r.estimatedTotal, { currency: r.currency }),
    },
    {
      key: 'effectiveAutoPost',
      title: t('columns.autoPost'),
      width: 120,
      mobileHidden: true,
      render: (r) =>
        h(
          NTag,
          { size: 'small', bordered: false, type: r.effectiveAutoPost ? 'warning' : 'default' },
          { default: () => t(r.effectiveAutoPost ? 'autoPost.on' : 'autoPost.off') },
        ),
    },
    {
      key: 'status',
      title: t('columns.status'),
      width: 100,
      render: (r) =>
        h(
          NTag,
          { size: 'small', bordered: false, type: STATUS_TYPE[r.status ?? ''] ?? 'default' },
          { default: () => t(`status.${lower(r.status)}`) },
        ),
    },
    { key: 'occurrenceCount', title: t('columns.generated'), width: 100, align: 'right', mobileHidden: true },
  ]
}

/** "Monthly on the 15th" reads; "Monthly / 1 / 15" does not. */
export function scheduleLabel(row: RecurringRow, t: Translate): string {
  if (!row.frequency) return EMPTY_DASH
  const every = (row.interval ?? 1) > 1 ? t('schedule.everyN').replace('{n}', String(row.interval)) : ''
  const base = t(`frequency.${lower(row.frequency)}`)
  if (!row.anchorDay) return [every, base].filter(Boolean).join(' ')
  const anchor =
    row.frequency === 'Weekly'
      ? t(`weekday.${row.anchorDay}`)
      : t('schedule.onDay').replace('{n}', String(row.anchorDay))
  return [every, base, anchor].filter(Boolean).join(' ')
}

/** Expense documents settle on the spot, so they need the account money leaves from. */
const isExpense = (model: Record<string, unknown>) => model.kind === 'Expense'

/** The kind cannot change after creation - the generated history would stop making sense. */
const isCreate = (model: Record<string, unknown>) => !model.id

export const recurringFormSchema: FormSchemaItem[] = [
  { key: 'name', labelKey: 'form.name', label: 'Name', type: 'text', required: true },
  { key: 'kind', labelKey: 'form.kind', label: 'Generates', type: 'recurring-kind', required: true, visible: isCreate },
  { key: 'partyId', labelKey: 'form.party', label: 'Party', type: 'recurring-party', required: true },
  { key: 'paidFromAccountId', labelKey: 'form.paidFrom', label: 'Paid from', type: 'finance-account', visible: isExpense },
  { key: 'currency', labelKey: 'form.currency', label: 'Currency', type: 'text' },

  { key: 'frequency', labelKey: 'form.frequency', label: 'Frequency', type: 'recurring-frequency', required: true },
  { key: 'interval', labelKey: 'form.interval', label: 'Every', type: 'number', min: 1 },
  { key: 'anchorDay', labelKey: 'form.anchorDay', label: 'On day', type: 'number', min: 1, max: 31 },

  { key: 'startDate', labelKey: 'form.startDate', label: 'Starts', type: 'date', required: true },
  { key: 'endDate', labelKey: 'form.endDate', label: 'Ends', type: 'date' },
  { key: 'maxOccurrences', labelKey: 'form.maxOccurrences', label: 'Max occurrences', type: 'number', min: 1 },

  { key: 'dueDays', labelKey: 'form.dueDays', label: 'Due in (days)', type: 'number', min: 0 },
  // Three-state on purpose: "follow the global default" and "this one explicitly
  // must not post" are different decisions, and squashing them into a boolean
  // silently rewrites templates when the global default changes.
  { key: 'autoPost', labelKey: 'form.autoPost', label: 'Auto-post', type: 'recurring-autopost' },

  { key: 'memo', labelKey: 'form.memo', label: 'Memo', type: 'textarea', span: 2 },
  { key: 'lines', labelKey: 'form.lines', label: 'Lines', type: 'recurring-lines', span: 2 },
]
