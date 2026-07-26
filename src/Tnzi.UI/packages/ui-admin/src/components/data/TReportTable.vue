<template>
  <TResponsiveTable
    :columns="ndColumns"
    :data="rows"
    :summary="summaryProp"
    :row-props="rowProps"
    size="small"
    :bordered="false"
  />
</template>

<script setup lang="ts" generic="T extends Record<string, unknown>">
/**
 * `TReportTable` - the financial-report table pattern collapsed into one wrapper
 * over `TResponsiveTable`: declare columns with `money` / `total` roles and it
 * right-aligns + tabular-nums the money cells, formats them, renders an auto
 * totals row summing the `total` columns, and (optionally) emits a drill-down
 * `row-click`. Replaces the copy-pasted "small responsive table + right-aligned
 * money columns + summary totals row + clickable rowProps" across report pages.
 */
import { computed, h, type VNodeChild } from 'vue'
import type { DataTableColumns } from 'naive-ui'
import TResponsiveTable from './TResponsiveTable.vue'

export interface ReportColumn<R> {
  key: string
  title: string
  /** Right-align + tabular-nums + money formatting. */
  money?: boolean
  /** Sum into the totals row (implies `money`). */
  total?: boolean
  align?: 'left' | 'right' | 'center'
  width?: number
  minWidth?: number
  /** Custom cell renderer (overrides money formatting). */
  render?: (row: R) => VNodeChild
}

const props = withDefaults(
  defineProps<{
    columns: ReportColumn<T>[]
    rows: T[]
    /** Money formatter. Default: `en-US` 2-decimal. */
    formatMoney?: (value: number) => string
    totalsLabel?: string
    /** Force the totals row on. Default (unset): on when any column has `total`. */
    showTotals?: boolean
    /** Rows become clickable and emit `row-click`. */
    clickable?: boolean
  }>(),
  { totalsLabel: 'Total' },
)

const emit = defineEmits<{ 'row-click': [row: T] }>()

const fmt = (value: unknown): string => {
  const n = Number(value ?? 0)
  if (props.formatMoney) return props.formatMoney(n)
  return n.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

// `showTotals` is a Boolean prop → absent coerces to `false`, so `||` (not `??`)
// falls through to auto-detection; `showTotals: true` forces the row on.
const showTotalsRow = computed(() => props.showTotals || props.columns.some((c) => c.total))

const ndColumns = computed<DataTableColumns<T>>(() =>
  props.columns.map((c) => {
    const isMoney = Boolean(c.money || c.total)
    return {
      key: c.key,
      title: c.title,
      align: c.align ?? (isMoney ? 'right' : 'left'),
      width: c.width,
      minWidth: c.minWidth,
      className: isMoney ? 't-report-table__money' : undefined,
      render: c.render
        ? (row: T): VNodeChild => c.render!(row)
        : isMoney
          ? (row: T): VNodeChild => fmt(row[c.key])
          : undefined,
    }
  }),
)

function buildSummary(pageData: readonly T[]): Record<string, { value: VNodeChild }> {
  const row: Record<string, { value: VNodeChild }> = {}
  const firstKey = props.columns[0]?.key
  if (firstKey) row[firstKey] = { value: h('strong', props.totalsLabel) }
  for (const c of props.columns) {
    if (!c.total) continue
    const sum = pageData.reduce((s, r) => s + Number(r[c.key] ?? 0), 0)
    row[c.key] = { value: h('strong', fmt(sum)) }
  }
  return row
}

/** The naive `summary` fn, or undefined when the totals row is off. */
const summaryProp = computed(() => (showTotalsRow.value ? buildSummary : undefined))

const rowProps = computed(() =>
  props.clickable
    ? (row: T) => ({ style: 'cursor: pointer', onClick: () => emit('row-click', row) })
    : undefined,
)
</script>

<style scoped>
:deep(.t-report-table__money) {
  font-variant-numeric: tabular-nums;
}
</style>
