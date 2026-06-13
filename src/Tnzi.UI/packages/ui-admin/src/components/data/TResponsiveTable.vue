<template>
  <!-- Mobile (<768px) card mode: derive a stacked card list from the naive
       column defs. The detected action column(s) render into each card's
       footer; everything else becomes a label/value row. -->
  <div v-if="useCards" class="t-responsive-table t-responsive-table--cards">
    <TDataCardList
      :items="rows"
      :columns="cardColumns"
      :row-key="cardRowKey"
      :loading="loading"
      :empty-text="emptyText"
    >
      <template v-if="actionColumns.length" #actions="{ row, index }">
        <ActionCell :row="row" :index="index" />
      </template>
      <template v-if="$slots.empty" #empty><slot name="empty" /></template>
    </TDataCardList>

    <div v-if="paginationConfig" class="t-responsive-table__pager">
      <NPagination
        :page="paginationConfig.page"
        :page-size="paginationConfig.pageSize"
        :item-count="paginationConfig.itemCount"
        simple
        @update:page="paginationConfig.onUpdatePage"
        @update:page-size="paginationConfig.onUpdatePageSize"
      />
    </div>
  </div>

  <!-- Desktop / tablet (or mobile="scroll"): pass straight through to
       NDataTable. All extra attrs (size, bordered, flex-height, remote,
       checked-row-keys, …) flow via $attrs untouched. `scroll-x` is added
       so wide tables scroll horizontally instead of crushing columns. -->
  <NDataTable
    v-else
    class="t-responsive-table"
    :data="ndtData"
    :columns="ndtColumns"
    :loading="loading"
    :row-key="ndtRowKey"
    :pagination="pagination"
    :scroll-x="scrollX"
    v-bind="$attrs"
  >
    <!-- Forward consumer slots (#empty, #loading, …) to the underlying
         NDataTable so near-drop-in migrations keep their custom empty
         states. The cards branch wires #empty into TDataCardList above. -->
    <template v-for="(_, name) in forwardedSlots" :key="name" #[name]="slotProps">
      <slot :name="name" v-bind="slotProps ?? {}" />
    </template>
  </NDataTable>
</template>

<script setup lang="ts" generic="T = any">
import { computed, defineComponent, h, useSlots, type PropType, type VNodeChild } from 'vue'
import { NDataTable, NPagination, type DataTableColumns } from 'naive-ui'
import { useBreakpoint } from '../../headless/useBreakpoint'
import TDataCardList, { type CardColumn } from './TDataCardList.vue'

export interface TResponsivePagination {
  page?: number
  pageSize?: number
  itemCount?: number
  onUpdatePage?: (page: number) => void
  onUpdatePageSize?: (size: number) => void
  // Pages pass full naive pagination configs (showSizePicker, pageSizes, …);
  // accept and ignore the extras.
  [key: string]: unknown
}

export interface TResponsiveTableProps<T = unknown> {
  /** Naive `DataTableColumns` (forwarded verbatim on desktop). */
  columns: DataTableColumns<T>
  data: T[]
  loading?: boolean
  rowKey?: (row: T) => string | number
  pagination?: false | TResponsivePagination
  /**
   * Column keys treated as the row-action cluster (rendered into the card
   * footer on mobile). Defaults cover the framework conventions.
   */
  actionKeys?: string[]
  /** `cards` (default) → stacked cards on phones; `scroll` → keep the table
   *  with horizontal scroll (use for dense dashboards / selection tables). */
  mobile?: 'cards' | 'scroll'
  emptyText?: string
}

// $slots 的 Readonly 推断类型在声明产物（vite-plugin-dts）下无法用动态键
// 索引（TS7053）——经 useSlots() 显式放宽为字符串记录后再转发。
const forwardedSlots = useSlots() as Record<string, ((props: Record<string, unknown>) => unknown) | undefined>

type Row = Record<string, unknown>
/** A loose superset of naive's column shape — we only read a handful of
 *  fields when deriving the mobile cards. */
interface LooseColumn {
  key?: string | number
  type?: string
  title?: unknown
  width?: number
  minWidth?: number
  render?: (row: Row, index: number) => VNodeChild
}

const props = withDefaults(defineProps<TResponsiveTableProps<T>>(), {
  loading: false,
  rowKey: undefined,
  pagination: undefined,
  actionKeys: () => ['actions', 'action', 'operation', '__row_actions__'],
  mobile: 'cards',
  emptyText: 'No data',
})

// Pass-through extra attrs to NDataTable (size, bordered, flex-height, etc.).
defineOptions({ inheritAttrs: false })

const bp = useBreakpoint()
const useCards = computed(() => props.mobile === 'cards' && bp.isSm.value)

const rows = computed<Row[]>(() => (props.data ?? []) as unknown as Row[])

// Casts for the desktop NDataTable pass-through — the public props are typed
// to the consumer's row type `T`; naive's NDataTable wants its own RowData.
const ndtData = computed(() => props.data as unknown as Row[])
const ndtColumns = computed(() => props.columns as unknown as DataTableColumns)
const ndtRowKey = computed(
  () => props.rowKey as unknown as ((row: Row) => string | number) | undefined,
)

const looseColumns = computed<LooseColumn[]>(
  () => (props.columns ?? []) as unknown as LooseColumn[],
)

const cardRowKey = computed<(row: Row) => string | number>(() => {
  const rk = props.rowKey as ((row: Row) => string | number) | undefined
  return rk ?? ((row: Row) => (row.id ?? row.key ?? '') as string | number)
})

const actionKeySet = computed(() => new Set(props.actionKeys))

function isActionCol(col: LooseColumn): boolean {
  return col.key != null && actionKeySet.value.has(String(col.key))
}
function isStructuralCol(col: LooseColumn): boolean {
  return col.type === 'selection' || col.type === 'expand'
}

/** Resolve a naive column title into a card label. Plain strings pass
 *  through; render-fn titles (icon + text headers) become a `labelNode`. */
function resolveTitle(col: LooseColumn): { title: string; labelNode?: VNodeChild } {
  const tt = col.title
  if (typeof tt === 'function') {
    return { title: String(col.key ?? ''), labelNode: (tt as (c: LooseColumn) => VNodeChild)(col) }
  }
  if (tt == null) return { title: String(col.key ?? '') }
  return { title: String(tt) }
}

/** Body columns → card label/value rows (excludes selection + action cols). */
const cardColumns = computed<CardColumn[]>(() =>
  looseColumns.value
    .filter((c) => !isStructuralCol(c) && !isActionCol(c))
    .map((c) => {
      const { title, labelNode } = resolveTitle(c)
      return {
        key: String(c.key ?? ''),
        title,
        labelNode,
        render: c.render ? (row: Row, index: number) => c.render!(row, index) : undefined,
      }
    }),
)

/** The action column(s), rendered into each card's footer. */
const actionColumns = computed<LooseColumn[]>(() => looseColumns.value.filter(isActionCol))

/** Renders the action column(s)' own `render(row)` output into the card
 *  footer. A functional component because `<component :is>` can't host a
 *  raw VNode array. */
const ActionCell = defineComponent({
  name: 'TResponsiveActionCell',
  props: {
    row: { type: Object as PropType<Row>, required: true },
    index: { type: Number, default: 0 },
  },
  setup(p) {
    return () => actionColumns.value.map((c) => (c.render ? c.render(p.row, p.index) : null))
  },
})

// Only surface a mobile pager for *controlled remote* pagination (has an
// item count + a page handler). Client-side `{ pageSize }`-only configs are
// dropped on mobile — the card list just shows every row and scrolls.
const paginationConfig = computed<TResponsivePagination | null>(() => {
  const p = props.pagination
  if (!p || typeof p !== 'object') return null
  if (p.itemCount == null || typeof p.onUpdatePage !== 'function') return null
  return p
})

/** Horizontal-scroll min-width for the desktop / scroll-mobile table. */
const scrollX = computed<number | undefined>(() => {
  let total = 0
  for (const c of looseColumns.value) total += c.width ?? c.minWidth ?? 140
  return total > 720 ? total : undefined
})
</script>

<style scoped>
.t-responsive-table { width: 100%; }
.t-responsive-table--cards {
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-height: 0;
}
.t-responsive-table__pager {
  display: flex;
  justify-content: center;
  flex-shrink: 0;
}
</style>
