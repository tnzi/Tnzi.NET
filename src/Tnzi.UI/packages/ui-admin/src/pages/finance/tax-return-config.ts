import { h } from 'vue'
import type { DataTableColumns } from 'naive-ui'
import TMoney from '../../components/finance/TMoney.vue'
import type { TaxReturnLineDto } from '../../services/bridges/finance-bridge'

type Translate = (key: string) => string

/**
 * Filing-form line columns.
 *
 * `isCalculated` rows (net tax) are derived, never entered - they carry a
 * modifier class so the sheet reads like the authority's own form, where the
 * computed boxes are visually separated from the ones you fill in.
 */
export function buildTaxReturnColumns(t: Translate, currency?: string | null): DataTableColumns<TaxReturnLineDto> {
  return [
    {
      title: t('columns.line'),
      key: 'line',
      width: 90,
      className: 'fin-tax__c-line',
      primary: true,
    },
    { title: t('columns.label'), key: 'label', minWidth: 220 },
    {
      title: t('columns.amount'),
      key: 'amount',
      width: 160,
      align: 'right',
      render: (row) => h(TMoney, { value: row.amount, currency }),
    },
  ] as DataTableColumns<TaxReturnLineDto>
}

/** Derived rows get a shaded row so they read as "computed, not entered". */
export function taxReturnRowClass(row: TaxReturnLineDto): string {
  return row.isCalculated ? 'fin-tax__row--calc' : ''
}
