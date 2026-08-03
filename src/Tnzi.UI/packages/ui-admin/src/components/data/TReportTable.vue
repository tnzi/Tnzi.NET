<template>
  <TResponsiveTable
    v-bind="$attrs"
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
 *
 * Anything else you pass (`remote`, `pagination`, `loading`, `scroll-x`,
 * `row-key`, …) falls through to `TResponsiveTable`, so a server-paged report
 * still works - see the `totals` prop for what that implies about the totals row.
 */
import { computed, h, useAttrs, type VNodeChild } from 'vue'
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
    /**
     * Authoritative totals, keyed by column key. Supply these whenever `rows`
     * is a PAGE of a larger report.
     *
     * ★ Without them the totals row sums only the rows it was given. On a
     * server-paged report that is the current page's total, and it looks
     * exactly like the report's total - a wrong number that nobody queries
     * because nothing about it appears wrong. So when the table is paged and
     * no totals are supplied, the totals row is suppressed rather than
     * guessed at (and a dev-mode warning says why).
     */
    totals?: Record<string, number>
  }>(),
  { totalsLabel: 'Total' },
)

const attrs = useAttrs()

// 该包的 tsconfig 不含 vite/client types，所以 `import.meta.env` 要 cast 探测
// （与 headless/useDetail.ts 同款）。VITEST 下关掉，免得测试输出被警告淹没。
const metaEnv = (import.meta as unknown as { env?: { DEV?: boolean; VITEST?: unknown } }).env

/**
 * Is this table showing a page of a larger set? `remote` (server-driven) or a
 * `pagination` object both mean yes.
 */
const isPaged = computed(() => Boolean(attrs.remote) || Boolean(attrs.pagination))

const emit = defineEmits<{ 'row-click': [row: T] }>()

const fmt = (value: unknown): string => {
  const n = Number(value ?? 0)
  if (props.formatMoney) return props.formatMoney(n)
  return n.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

// `showTotals` is a Boolean prop → absent coerces to `false`, so `||` (not `??`)
// falls through to auto-detection; `showTotals: true` forces the row on.
const showTotalsRow = computed(() => {
  const wanted = props.showTotals || props.columns.some((c) => c.total)
  if (!wanted) return false

  // ★ Paged + no authoritative totals = suppress the row. Summing the page
  //   would print a number that reads as the report's total and is not.
  if (isPaged.value && !props.totals) {
    if (metaEnv?.DEV && !metaEnv.VITEST) {
      console.warn(
        '[TReportTable] The totals row was suppressed: this table is paged but no `totals` ' +
          'prop was supplied, and summing one page would misreport the report total. ' +
          'Pass the server-side totals, or set `showTotals: false` if no totals row is wanted.',
      )
    }
    return false
  }
  return true
})

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
    // Authoritative totals win. They are the only correct answer whenever the
    // table shows a page, and they are still correct when it does not.
    const sum = props.totals?.[c.key] ?? pageData.reduce((s, r) => s + Number(r[c.key] ?? 0), 0)
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
