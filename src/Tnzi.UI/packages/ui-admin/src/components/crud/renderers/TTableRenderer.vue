<template>
  <!-- Mobile (<768px): stacked card list. Each row becomes a card whose
       title is the primary column and whose body lists the remaining
       columns as label/value pairs (reusing each column's render fn so
       badges/timestamps still work). Row actions move to the card footer. -->
  <TDataCardList
    v-if="useCards"
    class="t-table-renderer t-table-renderer--cards"
    :class="`t-table-renderer--${mode}`"
    :items="dataTableData"
    :columns="cardColumns"
    :row-key="dataTableRowKey"
    :loading="props.state.loading.value"
    :show-selection="props.showSelection"
    :selected-keys="checkedRowKeys"
    :serial-start="serialStart"
    :empty-text="emptyText"
    :card-props="cardPropsFn"
    @toggle="onToggleOne"
  >
    <template v-if="hasRowActions" #actions="{ row }">
      <TRowActions
        v-if="hasDeclarativeActions"
        :row="(row as T)"
        :actions="props.rowActions ?? []"
        :max-inline="props.rowActionsMaxInline"
        :collapse="props.rowActionsCollapse"
        :translate="props.translate"
      />
      <slot v-else name="rowActions" :row="(row as T)" />
    </template>
    <template #empty>
      <TEmpty :text="emptyText">
        <NButton
          v-if="showCreateCta"
          class="t-crud-empty-cta"
          size="small"
          tertiary
          type="primary"
          @click="onEmptyCreate"
        >
          {{ createCtaLabel }}
        </NButton>
      </TEmpty>
    </template>
  </TDataCardList>

  <!-- Desktop / tablet: the data table, with scroll-x so many-column
       tables keep their column widths and scroll horizontally instead of
       squishing into an unreadable grid. -->
  <NDataTable
    v-else
    class="t-table-renderer"
    :class="`t-table-renderer--${mode}`"
    :data="dataTableData"
    :columns="dataTableColumns"
    :loading="props.state.loading.value"
    :row-key="dataTableRowKey"
    :pagination="false"
    :checked-row-keys="checkedRowKeys"
    :flex-height="mode === 'page'"
    :scroll-x="scrollX"
    :row-props="dataTableRowProps"
    remote
    @update:checked-row-keys="onUpdateCheckedRowKeys"
    @update:sorter="onUpdateSorter"
  >
    <!-- Unified empty visual (TEmpty) instead of Naive's default NEmpty.
         A first-load empty list on a creatable page also offers a small
         Create call-to-action (hidden for search/filter misses). -->
    <template #empty>
      <TEmpty :text="emptyText">
        <NButton
          v-if="showCreateCta"
          class="t-crud-empty-cta"
          size="small"
          tertiary
          type="primary"
          @click="onEmptyCreate"
        >
          {{ createCtaLabel }}
        </NButton>
      </TEmpty>
    </template>
  </NDataTable>
</template>

<script setup lang="ts" generic="T, TId extends string | number = string | number">
import { computed, h, useSlots } from 'vue'
import { NButton, NDataTable } from 'naive-ui'
import { useBreakpoint } from '../../../headless/useBreakpoint'
import TDataCardList, { type CardColumn } from '../../data/TDataCardList.vue'
import { TEmpty } from '@tnzi/ui'
import TRowActions from '../TRowActions.vue'
import { estimateRowActionsWidth, type RowAction } from '../../../headless/row-actions'
import { useEmptyCreateCta } from '../../../headless/useEmptyCreateCta'
import type { UseCrudPageReturn } from '../../../headless/useCrudPage'

export interface TTableRendererProps<T, TId extends string | number = string | number> {
  state: UseCrudPageReturn<T, TId>
  mode?: 'page' | 'container'
  showSelection?: boolean
  /** Explicit operation-column width. When set it always wins - including
      over the declarative `rowActions` auto-estimate. Leave unset (default)
      to let declarative actions auto-size the column; the legacy
      `#rowActions` slot falls back to a fixed 150px when unset. */
  rowActionsWidth?: number
  /** Declarative operation actions - when supplied the renderer draws
      `TRowActions` itself (table cell + card footer) and the operation column
      auto-sizes to fit (no fixed width). */
  rowActions?: RowAction<T>[]
  /** Max inline action slots before the tail collapses into More▾ (default 2). */
  rowActionsMaxInline?: number
  /** Disable action collapsing - render every action inline. */
  rowActionsCollapse?: boolean
  /** Optional row-key override (defaults to `state.rowKey`). Mirrors the
      legacy `TCrudPage` `rowKey` prop so the wrapper can pass it through. */
  rowKey?: (row: T) => TId
  /** Disable the mobile card fallback and always render the data table
      (with horizontal scroll). Defaults to false - mobile gets cards. */
  disableMobileCards?: boolean
  /**
   * Per-row attributes (naive `row-props`). The common use is making the whole
   * row open the record: `(row) => ({ style: 'cursor:pointer', onClick: ... })`.
   * The same function drives the mobile card list, so a drill-in works on a
   * phone without the page wiring it twice. Row actions and the selection
   * checkbox stop propagation, so they keep working over a clickable row.
   */
  rowProps?: (row: T) => Record<string, unknown>
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<TTableRendererProps<T, TId>>(), {
  mode: 'page',
  showSelection: true,
  rowActionsWidth: undefined,
  rowActions: undefined,
  rowActionsMaxInline: 2,
  rowActionsCollapse: true,
  rowKey: undefined,
  disableMobileCards: false,
  translate: undefined,
})

defineSlots<{ rowActions?: (props: { row: T }) => unknown }>()

const slots = useSlots()
const bp = useBreakpoint()

/** Collapse to the stacked card list below the `md` breakpoint (<768px). */
const useCards = computed(() => !props.disableMobileCards && bp.isSm.value)

/** Declarative actions take priority; otherwise fall back to the slot. */
const hasDeclarativeActions = computed(() => (props.rowActions?.length ?? 0) > 0)
const hasRowActions = computed(() => hasDeclarativeActions.value || !!slots.rowActions)

/** Legacy fixed width for the `#rowActions` slot when no explicit width is
 *  given - the fixed-right operation column needs a concrete width. */
const LEGACY_ROW_ACTIONS_WIDTH = 150

/** Operation column width: an explicit `rowActionsWidth` always wins (even
 *  over the declarative auto-estimate); without it declarative actions
 *  auto-fit via `estimateRowActionsWidth` and the legacy `#rowActions` slot
 *  keeps its historical fixed 150px. */
const actionColumnWidth = computed<number>(() => {
  if (props.rowActionsWidth != null) return props.rowActionsWidth
  if (hasDeclarativeActions.value) {
    return estimateRowActionsWidth(props.rowActions ?? [], {
      maxInline: props.rowActionsMaxInline,
      collapse: props.rowActionsCollapse,
    })
  }
  return LEGACY_ROW_ACTIONS_WIDTH
})

function renderRowActions(row: T): ReturnType<typeof h> {
  return h(TRowActions, {
    row,
    actions: props.rowActions ?? [],
    maxInline: props.rowActionsMaxInline,
    collapse: props.rowActionsCollapse,
    translate: props.translate,
  } as never)
}

function t(key: string): string {
  return props.translate ? props.translate(key) : key
}

/** Translated empty text; without a translator let TEmpty/TDataCardList fall
 *  back to 'No data' instead of leaking the raw `admin.crud.empty` key. */
const emptyText = computed(() => (props.translate ? t('admin.crud.empty') : undefined))

/** First-load empty on a creatable list → offer a small Create CTA inside the
 *  empty state (table + mobile cards). Search/filter misses stay plain. */
const { showCreateCta, createCtaLabel, onEmptyCreate } = useEmptyCreateCta<T, TId>(
  () => props.state,
  () => props.translate,
)

function maybeTranslate(value: string | undefined, fallback: string): string {
  if (!value) return fallback
  if (/^[a-z][a-zA-Z0-9]*(\.[a-zA-Z0-9]+)*$/.test(value)) return t(value)
  return value
}

function castRow<U>(value: unknown): U {
  return value as U
}
const dataTableData = computed(() => castRow<Record<string, unknown>[]>(props.state.items.value))
const dataTableRowKey = computed(() =>
  castRow<(row: Record<string, unknown>) => string | number>(props.rowKey ?? props.state.rowKey),
)

/**
 * `rowProps` reaches both renderers from one declaration: naive's `row-props`
 * on the table, `card-props` on the mobile card list (which takes an extra
 * `index` the caller does not need). Left undefined when the page declared
 * none, so neither renderer pays for an empty attribute function.
 */
const dataTableRowProps = computed(() =>
  props.rowProps ? (row: Record<string, unknown>) => props.rowProps!(row as T) : undefined,
)
const cardPropsFn = computed(() =>
  props.rowProps ? (row: Record<string, unknown>) => props.rowProps!(row as T) : undefined,
)

/**
 * Cell attributes that keep a click inside the cell instead of letting it reach
 * the row.
 *
 * When a page declares `rowProps` for a whole-row drill-in, the selection
 * checkbox and the operation buttons live INSIDE that click target. Without this
 * stop, ticking a checkbox or pressing "Post" would also navigate away - the
 * action fires and the page changes under the user, which reads as the button
 * being broken. Only applied when a row-level handler actually exists, so tables
 * without a drill-in keep naive's untouched cell behaviour.
 */
const stopRowActivation = computed(() =>
  props.rowProps
    ? () => ({
        onClick: (e: MouseEvent) => e.stopPropagation(),
        // Keyboard activation bubbles the same way (Enter on a focused button).
        onKeydown: (e: KeyboardEvent) => {
          if (e.key === 'Enter' || e.key === ' ') e.stopPropagation()
        },
      })
    : undefined,
)

/** Merge the row-activation guard into a column's own `cellProps`, if any. */
function guardedCellProps(
  own?: (row: unknown, rowIndex: number) => Record<string, unknown>,
): ((row: unknown, rowIndex: number) => Record<string, unknown>) | undefined {
  const guard = stopRowActivation.value
  if (!guard) return own
  return (row, rowIndex) => ({ ...(own?.(row, rowIndex) ?? {}), ...guard() })
}

const dataTableColumns = computed(() => {
  const base: Record<string, unknown>[] = []
  if (props.showSelection) {
    base.push({
      type: 'selection',
      fixed: 'left',
      width: 40,
      cellProps: guardedCellProps((_row: unknown, rowIndex: number) => {
        const q = props.state.query.value
        const serial = (q.pageIndex - 1) * q.pageSize + rowIndex + 1
        return { title: `#${serial}` }
      }),
    })
  }
  for (const c of props.state.columnSettings.visibleColumns.value) {
    base.push({
      key: c.key,
      title: maybeTranslate(c.title, c.title),
      // `width` → fixed size; `minWidth` (no width) → elastic, fills the
      // container down to the floor. Forward only what's set so a minWidth-
      // only column stays elastic instead of being pinned by `width: undefined`.
      ...(c.width !== undefined ? { width: c.width } : {}),
      ...(c.minWidth !== undefined ? { minWidth: c.minWidth } : {}),
      fixed: c.fixed,
      ...(c.align ? { align: c.align } : {}),
      ...(c.ellipsis !== undefined ? { ellipsis: c.ellipsis } : {}),
      // `sorter: true` = "the server sorts this", so naive renders the header
      // control but does not reorder locally. `sortOrder` makes it CONTROLLED:
      // the arrow is derived from the query, so it always agrees with what was
      // actually requested - a programmatic `setSort`/`resetQuery` moves the
      // arrow too, instead of leaving naive's internal state pointing at a
      // column the rows are no longer sorted by.
      // (Sort is NOT persisted to the URL - like `searchText` and `filters` it
      // resets on reload. That is a separate decision from being controlled.)
      ...(c.sortable ? { sorter: true, sortOrder: sortOrderFor(c.key) } : {}),
      ...(c.render ? { render: (row: unknown) => c.render!(row as Record<string, unknown>) } : {}),
    })
  }
  if (hasRowActions.value) {
    base.push({
      key: '__row_actions__',
      title: t('admin.crud.actions'),
      width: actionColumnWidth.value,
      align: 'center',
      fixed: 'right',
      // The operation cell must swallow its own clicks when the row itself is a
      // drill-in target; otherwise pressing an action ALSO navigates.
      cellProps: guardedCellProps(),
      render: (row: unknown) =>
        hasDeclarativeActions.value
          ? renderRowActions(row as T)
          : h('div', slots.rowActions?.({ row: row as T })),
    })
  }
  return base as never[]
})

/** Assumed minimum width (px) for a column that declares neither `width`
 *  nor `minWidth` - used only to estimate {@link scrollX}. */
const DEFAULT_MIN_COL_WIDTH = 120

/**
 * Horizontal-scroll min-width. NDataTable applies this as the inner table's
 * `min-width` while keeping the table `width: 100%`, so:
 *  - container ≥ scrollX → columns stretch to fill (no scrollbar, no gap)
 *  - container < scrollX → the table scrolls horizontally instead of crushing
 * We sum each column's MINIMUM acceptable width - its `minWidth` (elastic
 * floor), falling back to a fixed `width`, else a {@link DEFAULT_MIN_COL_WIDTH}
 * floor - so a horizontal scrollbar appears only when the columns genuinely
 * can't fit, not on every wide-ish or empty table the way the old fixed-width
 * sum did. The selection + actions columns are `fixed`, which forces naive
 * into scroll mode, so scrollX stays set whenever there is content to size.
 * Mobile uses cards, so this only governs tablet/desktop.
 */
const scrollX = computed<number | undefined>(() => {
  const cols = props.state.columnSettings.visibleColumns.value
  let total = props.showSelection ? 40 : 0
  for (const c of cols) total += c.width ?? c.minWidth ?? DEFAULT_MIN_COL_WIDTH
  if (hasRowActions.value) total += actionColumnWidth.value
  return total > 0 ? total : undefined
})

/** Map the table column defs → the lighter card column shape. */
const cardColumns = computed<CardColumn[]>(() =>
  props.state.columnSettings.visibleColumns.value.map((c) => ({
    key: c.key,
    title: maybeTranslate(c.title, c.title),
    render: c.render ? (row) => c.render!(row as Record<string, unknown>) : undefined,
    primary: c.primary,
    hidden: c.mobileHidden,
  })),
)

const serialStart = computed(() => {
  const q = props.state.query.value
  return (q.pageIndex - 1) * q.pageSize + 1
})

// ---------------------------------------------------------------------------
// Server-side sorting
//
// Opt-in per column via `ColumnDef.sortable` - see the warning on that field:
// backends sort by a whitelist and silently ignore anything else, so an
// always-on sort header would quietly reorder rows by the wrong thing.
// ---------------------------------------------------------------------------

/** Query direction → naive's header arrow state for `key`. */
function sortOrderFor(key: string): 'ascend' | 'descend' | false {
  const q = props.state.query.value
  if (q.sortField !== key || !q.sortOrder) return false
  return q.sortOrder === 'desc' ? 'descend' : 'ascend'
}

/**
 * Naive emits `{ columnKey, order }` where `order` is `'ascend' | 'descend' |
 * false`, and `false` means the third click cleared the sort. Multi-sort is
 * not enabled here, but the payload can still be an array, so take the first.
 */
function onUpdateSorter(
  sorter: { columnKey?: string | number; order?: 'ascend' | 'descend' | false } | Array<unknown> | null,
): void {
  const active = (Array.isArray(sorter) ? sorter[0] : sorter) as
    | { columnKey?: string | number; order?: 'ascend' | 'descend' | false }
    | null
    | undefined

  if (!active || !active.order || active.columnKey === undefined) {
    props.state.setSort(undefined, null)
    return
  }
  props.state.setSort(String(active.columnKey), active.order === 'descend' ? 'desc' : 'asc')
}

const checkedRowKeys = computed<(string | number)[]>(
  () => props.state.batchActions.selectedIds.value as (string | number)[],
)
function onUpdateCheckedRowKeys(keys: (string | number)[]): void {
  props.state.batchActions.selectAll(keys as TId[])
}
function onToggleOne(key: string | number): void {
  props.state.batchActions.toggle(key as TId)
}
</script>

<style scoped>
.t-table-renderer { width: 100%; }
/* Page mode: fill the parent flex column so NDataTable's `flex-height` can
   size its scroll area. Without this the table collapses to header height,
   data rows overflow under the footer, and the pagination rides up beneath
   the column header. Mirrors the old monolith's `.t-crud-page__table
   { flex: 1 1 auto; min-height: 0 }`. Container mode keeps natural height. */
.t-table-renderer--page { flex: 1 1 auto; min-height: 0; }
/* The card list owns its own scroll; in page mode let it fill + scroll. */
.t-table-renderer--cards.t-table-renderer--page { overflow: hidden; }
:deep(.n-data-table-th) { padding: 8px 12px; }
:deep(.n-data-table-td) { padding: 9px 12px; }
</style>
