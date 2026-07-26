import { EMPTY_DASH } from '../../utils/placeholders'
import { h } from 'vue'
import type { ColumnDef } from '../../headless/useColumnSettings'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { NTag } from 'naive-ui'
import { BankRuleDirection, type BankRuleConditionDto } from '../../services/bridges/finance-bridge'

/** All-optional row shape (house pattern). */
export interface BankRuleRow {
  id?: string
  name?: string
  priority?: number
  isEnabled?: boolean
  accountId?: string | null
  accountName?: string | null
  direction?: BankRuleDirection
  matchMode?: string
  docType?: string
  counterAccountId?: string | null
  counterAccountName?: string | null
  paymentMethod?: string | null
  autoApply?: boolean
  conditions?: BankRuleConditionDto[]
  creationTime?: string
}

/**
 * One-line summary of what a rule looks for.
 *
 * The conditions are the rule; a list that shows only names forces the operator
 * to open every row to answer "which one is catching my Amazon charges".
 */
export function summarizeConditions(row: BankRuleRow, t: (key: string) => string): string {
  const conditions = row.conditions ?? []
  if (conditions.length === 0) return t('summary.noConditions')

  const join = row.matchMode === 'Any' ? t('summary.or') : t('summary.and')
  return conditions
    .map((c) => `${t(`rules.field.${c.field}`)} ${t(`rules.op.${c.operator}`)} "${c.value}"`)
    .join(` ${join} `)
}

export function buildBankRuleColumns(t: (key: string) => string): ColumnDef<BankRuleRow>[] {
  return [
    { key: 'priority', title: 'columns.priority', width: 70, align: 'right', render: (row) => String(row.priority ?? '') },
    { key: 'name', title: 'columns.name', minWidth: 150, primary: true },
    {
      key: 'conditions',
      title: 'columns.conditions',
      minWidth: 260,
      mobileHidden: true,
      render: (row) => h('span', { class: 'fin-rule-summary' }, summarizeConditions(row, t)),
    },
    {
      key: 'accountName',
      title: 'columns.account',
      width: 150,
      mobileHidden: true,
      render: (row) => row.accountName ?? t('form.allAccounts'),
    },
    {
      key: 'counterAccountName',
      title: 'columns.counterAccount',
      minWidth: 160,
      render: (row) => row.counterAccountName ?? EMPTY_DASH,
    },
    {
      key: 'autoApply',
      title: 'columns.autoApply',
      width: 110,
      // A rule that books without asking is the one worth spotting from across
      // the room, so it gets a badge rather than a quiet yes/no.
      render: (row) =>
        row.autoApply
          ? h(NTag, { size: 'small', type: 'warning', bordered: false }, { default: () => t('columns.auto') })
          : EMPTY_DASH,
    },
    {
      key: 'isEnabled',
      title: 'columns.status',
      width: 100,
      render: (row) =>
        h(TStatusBadge, {
          value: row.isEnabled === false ? 0 : 1,
          type: row.isEnabled === false ? 'default' : 'success',
          label: row.isEnabled === false ? t('status.disabled') : t('status.enabled'),
        }),
    },
  ]
}
