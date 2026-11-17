<script setup lang="ts">
/**
 * TDataGrid - rows of data laid out as a data client does: one tight line per
 * row on alternating shades, gridlines, row numbers and the header held in
 * view while the rows scroll under them, numbers to the right, a missing value
 * set apart from an empty one, long values cut to one line.
 *
 * Rows are arrays of values, one per column (a query's result, a CSV); a
 * column says how its cells read (`DataGridColumn`: `name`, `hint`, `kind`,
 * `muted`, `sortable`, `compare`, `format`).
 *
 * - **Sort**: a click on a header (a button) sorts by that column, ascending,
 *   then descending, then as given; the header carries `aria-sort`.
 *   `sortMode="local"` (default) orders the rows here; `"remote"` only reports
 *   the sort (`v-model:sort`) for a server to order them.
 * - **Column widths**: the right edge of a header is a separator: dragged it
 *   resizes the column, focused the Left / Right arrows move it (Shift four
 *   times as far) and Home takes it to the narrowest. The first resize fixes
 *   every column at the width it was laid out at (`v-model:widths`, null
 *   before).
 * - **Choosing a cell** (`selectable`): the grid is then an ARIA grid with one
 *   Tab stop; the arrow keys move between cells, Home / End to the ends of a
 *   row (with Ctrl, of the grid), Page Up / Down by ten rows, Enter or Space
 *   chooses the cell (`v-model:active-cell`, by its row's index in `rows`) or
 *   lets it go, Escape lets the chosen cell go. What a chosen cell shows (its
 *   whole value, a copy button) is the page's.
 *
 * A different set of columns (by name) clears the sort, the widths and the
 * chosen cell; another page of the same keeps them. Every row is rendered:
 * page large results.
 */
import { computed, nextTick, ref, watch } from 'vue'
import { Icon } from '@iconify/vue'
import { vTooltip } from '../../headless/tooltip'
import { useControlsText } from './controls-text'
import {
  DATA_GRID_KEY_STEP,
  DATA_GRID_MIN_COLUMN_WIDTH,
  dataGridCellText,
  nextDataGridSort,
  sortDataGridRows,
  withColumnWidth,
  type DataGridCell,
  type DataGridColumn,
  type DataGridSort,
} from './data-grid'

const props = withDefaults(
  defineProps<{
    columns: ReadonlyArray<DataGridColumn>
    rows: ReadonlyArray<ReadonlyArray<unknown>>
    /** Accessible name of the grid ("Rows of orders"). */
    label: string
    /** Show row numbers in a column of their own, held at the left. */
    rowNumbers?: boolean
    /** The number of the first row (a page after the first starts further on). */
    firstRow?: number
    /** Alternate the rows' shade. */
    zebra?: boolean
    /** Values in the monospace face, as a database writes them out. */
    mono?: boolean
    /** What a missing value (`null` / `undefined`) reads as, set apart in italics. */
    nullText?: string
    /** `local`: the rows are ordered here; `remote`: the sort is only reported. */
    sortMode?: 'local' | 'remote'
    /** Cells can be focused and chosen (an ARIA grid). */
    selectable?: boolean
    /** Language the text of a sort follows; the page's by default. */
    locale?: string
    /** Extra classes for a cell (a value the server cut short, say). */
    cellClass?: (cell: DataGridCell, value: unknown) => string | Record<string, boolean> | undefined
  }>(),
  {
    rowNumbers: true,
    firstRow: 1,
    zebra: true,
    mono: true,
    nullText: 'NULL',
    sortMode: 'local',
    selectable: false,
    locale: undefined,
    cellClass: undefined,
  },
)

const sort = defineModel<DataGridSort | null>('sort', { default: null })
const widths = defineModel<number[] | null>('widths', { default: null })
const activeCell = defineModel<DataGridCell | null>('activeCell', { default: null })

defineSlots<{
  /** A cell's content; `text` is what it would show by default. */
  cell?: (props: { value: unknown; text: string; row: number; column: number; columnDef: DataGridColumn }) => unknown
  /** Shown under the header when there are no rows. */
  empty?: () => unknown
}>()

const text = useControlsText()
const scroller = ref<HTMLElement | null>(null)
const headers = ref<HTMLElement[]>([])

/* The rows in the order shown, as indexes into `rows`. */
const order = computed(() =>
  props.sortMode === 'local' && sort.value
    ? sortDataGridRows(props.rows, sort.value, props.columns[sort.value.column], props.locale)
    : props.rows.map((_, index) => index),
)
const columnCount = computed(() => props.columns.length)
const tableWidth = computed(() => {
  if (!widths.value) return undefined
  const numbers = props.rowNumbers ? numberWidth.value : 0
  return `${numbers + widths.value.reduce((sum, width) => sum + width, 0)}px`
})
/* Wide enough for the largest row number in the 12px tabular face, at least 44px. */
const numberWidth = computed(() => Math.max(44, 16 + 8 * String(props.firstRow + props.rows.length - 1).length))

/* Another set of columns is another result: its sort, widths and chosen cell start over. */
watch(
  () => props.columns.map((column) => column.name).join('\u0000'),
  () => {
    sort.value = null
    widths.value = null
    activeCell.value = null
    cursor.value = null
  },
)

function sortable(column: DataGridColumn): boolean {
  return column.sortable !== false
}

function sortBy(column: number): void {
  sort.value = nextDataGridSort(sort.value, column)
}

function ariaSort(column: number): 'ascending' | 'descending' | 'none' {
  if (sort.value?.column !== column) return 'none'
  return sort.value.direction === 'asc' ? 'ascending' : 'descending'
}

function cellText(value: unknown, column: number): string {
  return dataGridCellText(value, props.columns[column], props.nullText)
}

function isMissing(value: unknown): boolean {
  return value === null || value === undefined
}

/* -- Column widths -- */

/** The widths as laid out now, fixed from here on. */
function fixedWidths(): number[] {
  if (widths.value && widths.value.length === columnCount.value) return widths.value
  const measured = props.columns.map((_, index) =>
    Math.max(DATA_GRID_MIN_COLUMN_WIDTH, Math.round(headers.value[index]?.getBoundingClientRect().width ?? 0)),
  )
  widths.value = measured
  return measured
}

function setWidth(column: number, width: number): void {
  widths.value = withColumnWidth(fixedWidths(), column, width)
}

function startResize(column: number, event: PointerEvent): void {
  if (event.button !== 0) return
  event.preventDefault()
  const handle = event.currentTarget as HTMLElement
  const startX = event.clientX
  const startWidth = fixedWidths()[column] ?? DATA_GRID_MIN_COLUMN_WIDTH
  handle.setPointerCapture?.(event.pointerId)
  const move = (moved: PointerEvent): void => setWidth(column, startWidth + moved.clientX - startX)
  const end = (): void => {
    handle.removeEventListener('pointermove', move)
    handle.removeEventListener('pointerup', end)
    handle.removeEventListener('pointercancel', end)
  }
  handle.addEventListener('pointermove', move)
  handle.addEventListener('pointerup', end)
  handle.addEventListener('pointercancel', end)
}

function resizeByKey(column: number, event: KeyboardEvent): void {
  const current = fixedWidths()[column] ?? DATA_GRID_MIN_COLUMN_WIDTH
  const step = (event.shiftKey ? 4 : 1) * DATA_GRID_KEY_STEP
  let next: number | null = null
  if (event.key === 'ArrowRight') next = current + step
  else if (event.key === 'ArrowLeft') next = current - step
  else if (event.key === 'Home') next = DATA_GRID_MIN_COLUMN_WIDTH
  if (next === null) return
  event.preventDefault()
  setWidth(column, next)
}

function resizeLabel(column: DataGridColumn): string {
  return text.value.resizeColumn.replace('{name}', column.name)
}

/* -- Choosing a cell (selectable): one Tab stop, the arrow keys move it -- */

/** Where keyboard focus is in the grid, by row on screen and column. */
const cursor = ref<{ shown: number; column: number } | null>(null)

const tabStop = computed(() => {
  if (cursor.value) return cursor.value
  const active = activeCell.value
  if (active) {
    const shown = order.value.indexOf(active.row)
    if (shown >= 0) return { shown, column: active.column }
  }
  return { shown: 0, column: 0 }
})

function isTabStop(shown: number, column: number): boolean {
  return tabStop.value.shown === shown && tabStop.value.column === column
}

function isActive(row: number, column: number): boolean {
  return activeCell.value?.row === row && activeCell.value.column === column
}

function focusCell(shown: number, column: number): void {
  const rows = order.value.length
  if (rows === 0 || columnCount.value === 0) return
  const next = {
    shown: Math.min(Math.max(shown, 0), rows - 1),
    column: Math.min(Math.max(column, 0), columnCount.value - 1),
  }
  cursor.value = next
  void nextTick(() => {
    scroller.value
      ?.querySelector<HTMLElement>(`[data-grid-cell="${next.shown}:${next.column}"]`)
      ?.focus()
  })
}

function choose(row: number, column: number): void {
  activeCell.value = isActive(row, column) ? null : { row, column }
}

function onCellClick(shown: number, column: number): void {
  if (!props.selectable) return
  cursor.value = { shown, column }
  choose(order.value[shown]!, column)
}

const PAGE = 10

function onCellKeydown(event: KeyboardEvent, shown: number, column: number): void {
  if (!props.selectable || event.isComposing) return
  const last = order.value.length - 1
  const lastColumn = columnCount.value - 1
  switch (event.key) {
    case 'ArrowRight': focusCell(shown, column + 1); break
    case 'ArrowLeft': focusCell(shown, column - 1); break
    case 'ArrowDown': focusCell(shown + 1, column); break
    case 'ArrowUp': focusCell(shown - 1, column); break
    case 'PageDown': focusCell(shown + PAGE, column); break
    case 'PageUp': focusCell(shown - PAGE, column); break
    case 'Home': focusCell(event.ctrlKey || event.metaKey ? 0 : shown, 0); break
    case 'End': focusCell(event.ctrlKey || event.metaKey ? last : shown, lastColumn); break
    case 'Enter':
    case ' ':
      choose(order.value[shown]!, column)
      break
    case 'Escape':
      if (!activeCell.value) return
      activeCell.value = null
      event.stopPropagation()
      break
    default:
      return
  }
  event.preventDefault()
}

function extraClass(row: number, column: number, value: unknown): string | Record<string, boolean> | undefined {
  return props.cellClass?.({ row, column }, value)
}
</script>

<template>
  <div
    ref="scroller"
    class="t-data-grid t-ai-scroll"
    :class="{ 't-data-grid--zebra': zebra, 't-data-grid--mono': mono, 't-data-grid--selectable': selectable }"
    data-data-grid
  >
    <table
      class="t-data-grid__table"
      :class="{ 'is-sized': widths }"
      :style="tableWidth ? { width: tableWidth } : undefined"
      :role="selectable ? 'grid' : undefined"
      :aria-label="label"
      :aria-rowcount="selectable ? rows.length + 1 : undefined"
    >
      <colgroup v-if="widths">
        <col v-if="rowNumbers" :style="{ width: `${numberWidth}px` }" />
        <col v-for="(width, c) in widths" :key="c" :style="{ width: `${width}px` }" />
      </colgroup>
      <thead>
        <tr>
          <th v-if="rowNumbers" class="t-data-grid__number" aria-hidden="true" />
          <th
            v-for="(column, c) in columns"
            :key="`${c}:${column.name}`"
            ref="headers"
            scope="col"
            class="t-data-grid__head"
            :class="{ 'is-number': column.kind === 'number' }"
            :aria-sort="sortable(column) ? ariaSort(c) : undefined"
          >
            <button
              v-if="sortable(column)"
              v-tooltip="column.hint ?? column.name"
              type="button"
              class="t-data-grid__sort"
              data-grid-sort
              @click="sortBy(c)"
            >
              <span class="t-data-grid__name">{{ column.name }}</span>
              <Icon
                v-if="sort?.column === c"
                :icon="sort.direction === 'asc' ? 'lucide:arrow-up' : 'lucide:arrow-down'"
                class="t-data-grid__sorted"
                aria-hidden="true"
              />
            </button>
            <span v-else v-tooltip="column.hint ?? null" class="t-data-grid__sort t-data-grid__sort--plain">
              <span class="t-data-grid__name">{{ column.name }}</span>
            </span>
            <span
              class="t-data-grid__resize"
              role="separator"
              aria-orientation="vertical"
              tabindex="0"
              :aria-label="resizeLabel(column)"
              :aria-valuenow="widths?.[c]"
              :aria-valuemin="DATA_GRID_MIN_COLUMN_WIDTH"
              data-grid-resize
              @pointerdown="startResize(c, $event)"
              @keydown="resizeByKey(c, $event)"
            />
          </th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="(r, shown) in order" :key="r" :aria-rowindex="selectable ? shown + 2 : undefined">
          <th v-if="rowNumbers" scope="row" class="t-data-grid__number">{{ firstRow + shown }}</th>
          <td
            v-for="(value, c) in rows[r]"
            :key="c"
            :class="[
              {
                'is-number': columns[c]?.kind === 'number',
                'is-muted': columns[c]?.muted,
                'is-null': isMissing(value),
                'is-active': selectable && isActive(r, c),
              },
              extraClass(r, c, value),
            ]"
            :tabindex="selectable ? (isTabStop(shown, c) ? 0 : -1) : undefined"
            :aria-selected="selectable ? isActive(r, c) : undefined"
            :data-grid-cell="`${shown}:${c}`"
            @click="onCellClick(shown, c)"
            @keydown="onCellKeydown($event, shown, c)"
            @focus="selectable && (cursor = { shown, column: c })"
          >
            <slot
              v-if="columns[c]"
              name="cell"
              :value="value"
              :text="cellText(value, c)"
              :row="r"
              :column="c"
              :column-def="columns[c]!"
            >{{ cellText(value, c) }}</slot>
          </td>
        </tr>
      </tbody>
    </table>
    <div v-if="rows.length === 0 && $slots.empty" class="t-data-grid__empty">
      <slot name="empty" />
    </div>
  </div>
</template>

<style scoped>
/* The shades are mixes of the text colour into the surface, so they hold in
   either mode: the header and row numbers 4%, every other row 2.5%. */
/* The scroller is the containing block of what scrolls in it (the sticky
   header, the resize handles): `position: relative`. */
.t-data-grid {
  position: relative;
  --t-data-grid-head: color-mix(in srgb, var(--tnzi-ai-text) 4%, var(--tnzi-ai-surface));
  --t-data-grid-stripe: color-mix(in srgb, var(--tnzi-ai-text) 2.5%, var(--tnzi-ai-surface));
  --t-data-grid-line: var(--tnzi-ai-border);
  min-width: 0;
  min-height: 0;
  overflow: auto;
  overscroll-behavior: contain;
  background: var(--tnzi-ai-surface);
}
.t-data-grid__table {
  border-collapse: separate;
  border-spacing: 0;
  font-size: 12px;
  line-height: 24px;
}
.t-data-grid--mono .t-data-grid__table {
  font-family: var(--tnzi-ai-font-mono);
}
.t-data-grid__table th,
.t-data-grid__table td {
  height: 24px;
  box-sizing: border-box;
  padding: 0 8px;
  white-space: nowrap;
  border-right: 1px solid var(--t-data-grid-line);
  border-bottom: 1px solid var(--t-data-grid-line);
}
/* Once resized, the table keeps the widths and cuts what does not fit. */
.t-data-grid__table.is-sized {
  table-layout: fixed;
}
.t-data-grid__table.is-sized td {
  max-width: none;
}
.t-data-grid__table thead th {
  position: sticky;
  top: 0;
  z-index: 1;
  text-align: left;
  font-weight: 600;
  background: var(--t-data-grid-head);
}
.t-data-grid__head {
  padding: 0;
}
.t-data-grid__table thead th.t-data-grid__head {
  padding: 0;
}
.t-data-grid__sort {
  display: flex;
  align-items: center;
  gap: 4px;
  width: 100%;
  height: 23px;
  box-sizing: border-box;
  padding: 0 12px 0 8px;
  border: none;
  background: transparent;
  color: var(--tnzi-ai-text);
  font: inherit;
  text-align: inherit;
  cursor: pointer;
}
.t-data-grid__sort--plain {
  cursor: default;
}
.t-data-grid__head.is-number .t-data-grid__sort {
  flex-direction: row-reverse;
}
.t-data-grid__sort:hover:not(.t-data-grid__sort--plain) {
  background: var(--tnzi-ai-hover);
}
.t-data-grid__sort:focus-visible,
.t-data-grid__resize:focus-visible,
.t-data-grid__table td:focus-visible {
  outline: var(--tnzi-ai-focus-outline);
  outline-offset: -2px;
}
.t-data-grid__name {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
}
.t-data-grid__sorted {
  flex: none;
  width: 12px;
  height: 12px;
  color: var(--tnzi-ai-accent-text);
}
/* A header's right edge: dragged, the column is resized. */
.t-data-grid__resize {
  position: absolute;
  top: 0;
  right: 0;
  bottom: 0;
  z-index: 1;
  width: 6px;
  cursor: col-resize;
  touch-action: none;
}
.t-data-grid__resize:hover,
.t-data-grid__resize:active,
.t-data-grid__resize:focus-visible {
  background: linear-gradient(var(--tnzi-ai-accent), var(--tnzi-ai-accent)) right / 2px 100% no-repeat;
}
.t-data-grid__table td {
  max-width: 280px;
  overflow: hidden;
  text-overflow: ellipsis;
  color: var(--tnzi-ai-text);
  cursor: default;
}
.t-data-grid--zebra tbody tr:nth-child(even) td {
  background: var(--t-data-grid-stripe);
}
.t-data-grid__table tbody tr:hover td {
  background: var(--tnzi-ai-hover);
}
.t-data-grid--selectable td {
  cursor: pointer;
}
.t-data-grid__table tbody td.is-active,
.t-data-grid__table tbody tr:hover td.is-active {
  background: var(--tnzi-ai-accent-soft);
  box-shadow: inset 0 0 0 1px var(--tnzi-ai-accent);
}
/* Row numbers stay at the left while the columns scroll sideways. */
.t-data-grid__table .t-data-grid__number {
  position: sticky;
  left: 0;
  min-width: 36px;
  padding: 0 6px;
  font-weight: 400;
  color: var(--tnzi-ai-text-tertiary);
  text-align: right;
  font-variant-numeric: tabular-nums;
  background: var(--t-data-grid-head);
  user-select: none;
}
.t-data-grid__table thead th.t-data-grid__number {
  z-index: 2;
}
.t-data-grid__table td.is-number {
  text-align: right;
  font-variant-numeric: tabular-nums;
}
.t-data-grid__table td.is-muted {
  color: var(--tnzi-ai-text-secondary);
}
.t-data-grid__table td.is-null {
  color: var(--tnzi-ai-text-tertiary);
  font-style: italic;
}
.t-data-grid__empty {
  padding: 16px;
  color: var(--tnzi-ai-text-tertiary);
  font-size: 13px;
}
</style>
