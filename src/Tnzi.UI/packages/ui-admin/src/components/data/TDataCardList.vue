<template>
  <div class="t-data-cards">
    <!-- loading skeletons (only when there's nothing to show yet) -->
    <div v-if="loading && !hasItems" class="t-data-cards__list">
      <div v-for="n in 4" :key="`sk-${n}`" class="t-data-cards__skeleton" />
    </div>

    <!-- empty -->
    <div v-else-if="!hasItems" class="t-data-cards__empty">
      <slot name="empty">
        <TEmpty :text="emptyText" />
      </slot>
    </div>

    <!-- cards -->
    <div v-else class="t-data-cards__list">
      <article
        v-for="(row, index) in items"
        :key="keyOf(row, index)"
        class="t-data-cards__card"
        :class="{
          't-data-cards__card--selected': showSelection && isSelected(row),
          't-data-cards__card--clickable': isCardClickable(row, index),
        }"
        v-bind="cardAttrsOf(row, index)"
      >
        <header class="t-data-cards__head">
          <button
            v-if="showSelection"
            type="button"
            class="t-data-cards__check"
            :aria-pressed="isSelected(row)"
            :aria-label="isSelected(row) ? 'Deselect row' : 'Select row'"
            @click.stop="onToggle(row)"
          >
            <TSvgIcon
              :icon="isSelected(row) ? 'mdi:checkbox-marked' : 'mdi:checkbox-blank-outline'"
              :size="20"
            />
          </button>
          <div class="t-data-cards__title">
            <CellNode v-if="titleColumn" :node="cellOf(titleColumn, row, index)" />
            <span v-else class="t-data-cards__title-fallback">#{{ serialOf(index) }}</span>
          </div>
        </header>

        <dl class="t-data-cards__fields">
          <div v-for="col in bodyColumns" :key="col.key" class="t-data-cards__field">
            <dt class="t-data-cards__label">
              <CellNode v-if="col.labelNode != null" :node="col.labelNode" />
              <template v-else>{{ col.title }}</template>
            </dt>
            <dd class="t-data-cards__value">
              <CellNode :node="cellOf(col, row, index)" />
            </dd>
          </div>
        </dl>

        <!-- .stop so tapping an action never also fires the card-level click
             handler from `cardProps` (drill-in + row action would both run). -->
        <footer v-if="$slots.actions" class="t-data-cards__actions" @click.stop>
          <slot name="actions" :row="row" :index="index" />
        </footer>
      </article>

      <!-- Totals card — the card counterpart of NDataTable's summary row(s),
           rendered once at the bottom of the list. -->
      <article v-if="summaryRows.length" class="t-data-cards__card t-data-cards__card--summary">
        <dl
          v-for="(srow, si) in summaryRows"
          :key="`summary-${si}`"
          class="t-data-cards__fields"
        >
          <div v-for="col in summaryColumnsOf(srow)" :key="col.key" class="t-data-cards__field">
            <dt class="t-data-cards__label">
              <CellNode v-if="col.labelNode != null" :node="col.labelNode" />
              <template v-else>{{ col.title }}</template>
            </dt>
            <dd class="t-data-cards__value">
              <CellNode :node="srow[col.key]" />
            </dd>
          </div>
        </dl>
      </article>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, defineComponent, h, isVNode, type PropType, type VNodeChild } from 'vue'
import { TSvgIcon } from '@tnzi/ui'
import TEmpty from './TEmpty.vue'

/**
 * Column descriptor for the mobile card list. A subset of the table column
 * shape — only what's needed to render a `label: value` pair (or a card
 * title). `render` receives the row and may return a VNode (badges,
 * relative timestamps, links) or a primitive (rendered as text).
 */
export interface CardColumn {
  key: string
  title: string
  /** Pre-rendered label node — used instead of `title` when the source
   *  column's header is a VNode (e.g. an icon + text), so the card label
   *  keeps its rich content. Falls back to the plain `title` string. */
  labelNode?: VNodeChild
  render?: (row: Record<string, unknown>, index: number) => VNodeChild
  /** Use this column's value as the card title rather than a label/value row. */
  primary?: boolean
  /** Omit this column from the mobile card entirely. */
  hidden?: boolean
}

interface Props {
  items: Record<string, unknown>[]
  columns: CardColumn[]
  rowKey: (row: Record<string, unknown>) => string | number
  loading?: boolean
  showSelection?: boolean
  selectedKeys?: (string | number)[]
  emptyText?: string
  /** 1-based serial of the first row on the current page (for the title
   *  fallback when no column qualifies as a title). */
  serialStart?: number
  /** Row-level attrs/handlers spread onto each card — the card counterpart of
   *  naive's `row-props`. A result carrying an `onClick` handler also gives
   *  the card pointer/hover affordance. */
  cardProps?: (row: Record<string, unknown>, index: number) => Record<string, unknown>
  /** Summary rows (column key → rendered value) shown as a totals card at the
   *  bottom of the list — the card counterpart of NDataTable's summary row. */
  summaryRows?: Record<string, VNodeChild>[]
}

const props = withDefaults(defineProps<Props>(), {
  loading: false,
  showSelection: false,
  selectedKeys: () => [],
  emptyText: 'No data',
  serialStart: 1,
  cardProps: undefined,
  summaryRows: () => [],
})

const emit = defineEmits<{ toggle: [key: string | number] }>()

defineSlots<{
  actions?: (props: { row: Record<string, unknown>; index: number }) => unknown
  empty?: () => unknown
}>()

/**
 * Renders a single cell value. Naive's column `render` returns a VNodeChild
 * which may be a VNode (status badge, link) or a primitive — wrap primitives
 * in a span so the markup is uniform, and drop nullish values.
 */
const CellNode = defineComponent({
  name: 'TDataCardCell',
  props: {
    node: {
      type: [Object, String, Number, Boolean, Array] as PropType<VNodeChild>,
      default: null,
    },
  },
  setup(cellProps) {
    return () => {
      const n = cellProps.node
      if (n == null || n === '') return null
      if (Array.isArray(n)) return n
      return isVNode(n) ? n : h('span', String(n))
    }
  },
})

const hasItems = computed(() => props.items.length > 0)

const selectedSet = computed(() => new Set(props.selectedKeys))

/** First non-hidden column flagged `primary`, else the first non-hidden column. */
const titleColumn = computed<CardColumn | undefined>(() => {
  const visible = props.columns.filter((c) => !c.hidden)
  return visible.find((c) => c.primary) ?? visible[0]
})

/** Every non-hidden column except the one promoted to the title. */
const bodyColumns = computed<CardColumn[]>(() => {
  const title = titleColumn.value
  return props.columns.filter((c) => !c.hidden && c !== title)
})

function cellOf(col: CardColumn, row: Record<string, unknown>, index: number): VNodeChild {
  if (col.render) return col.render(row, index)
  const v = row[col.key]
  return v == null ? '' : (v as VNodeChild)
}

function keyOf(row: Record<string, unknown>, index: number): string | number {
  const k = props.rowKey(row)
  return k ?? index
}

function serialOf(index: number): number {
  return props.serialStart + index
}

function isSelected(row: Record<string, unknown>): boolean {
  return selectedSet.value.has(props.rowKey(row))
}

function onToggle(row: Record<string, unknown>): void {
  emit('toggle', props.rowKey(row))
}

function cardAttrsOf(row: Record<string, unknown>, index: number): Record<string, unknown> | undefined {
  return props.cardProps?.(row, index)
}

function isCardClickable(row: Record<string, unknown>, index: number): boolean {
  return typeof cardAttrsOf(row, index)?.onClick === 'function'
}

/** Summary card fields: declared column order, only keys the row provides. */
function summaryColumnsOf(srow: Record<string, VNodeChild>): CardColumn[] {
  return props.columns.filter((c) => !c.hidden && c.key in srow)
}
</script>

<style scoped>
.t-data-cards {
  width: 100%;
  height: 100%;
  min-height: 0;
  overflow-y: auto;
  -webkit-overflow-scrolling: touch;
}
.t-data-cards__list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.t-data-cards__card {
  position: relative;
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 12px;
  background: var(--tnzi-container-bg, #fff);
  border: 1px solid var(--tnzi-border, #e5e7eb);
  border-radius: var(--tnzi-admin-radius-md, 8px);
  box-shadow: 0 1px 2px rgb(0 0 0 / 0.04);
}
.t-data-cards__card--selected {
  border-color: var(--tnzi-primary, #646cff);
  box-shadow: 0 0 0 1px var(--tnzi-primary, #646cff);
}
.t-data-cards__card--clickable {
  cursor: pointer;
  transition:
    border-color 0.2s,
    box-shadow 0.2s;
}
.t-data-cards__card--clickable:hover {
  border-color: var(--tnzi-primary, #646cff);
  box-shadow: 0 2px 8px rgb(0 0 0 / 0.08);
}
.t-data-cards__card--clickable:active {
  box-shadow: 0 1px 2px rgb(0 0 0 / 0.04);
}
.t-data-cards__card--summary {
  background: var(--tnzi-layout-bg, #f7f8fa);
}
.t-data-cards__card--summary .t-data-cards__value {
  font-weight: 600;
}

.t-data-cards__head {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}
.t-data-cards__check {
  flex-shrink: 0;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  padding: 0;
  border: none;
  border-radius: 6px;
  background: transparent;
  color: var(--tnzi-primary, #646cff);
  cursor: pointer;
}
.t-data-cards__check:active {
  background: var(--tnzi-primary-hover-bg, rgb(100 108 255 / 0.1));
}
.t-data-cards__title {
  flex: 1 1 auto;
  min-width: 0;
  font-size: 15px;
  font-weight: 600;
  color: var(--tnzi-base-text, #1f2937);
  word-break: break-word;
}
.t-data-cards__title-fallback {
  color: var(--tnzi-base-text-muted, #9ca3af);
  font-weight: 500;
}

.t-data-cards__fields {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin: 0;
}
.t-data-cards__field {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  min-width: 0;
}
.t-data-cards__label {
  flex-shrink: 0;
  width: 38%;
  max-width: 130px;
  font-size: 13px;
  color: var(--tnzi-base-text-muted, #6b7280);
}
.t-data-cards__value {
  flex: 1 1 auto;
  min-width: 0;
  margin: 0;
  font-size: 13px;
  color: var(--tnzi-base-text, #374151);
  text-align: right;
  word-break: break-word;
}

.t-data-cards__actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 8px;
  padding-top: 8px;
  border-top: 1px solid var(--tnzi-border, #f0f0f0);
}

.t-data-cards__skeleton {
  height: 96px;
  border-radius: var(--tnzi-admin-radius-md, 8px);
  background: linear-gradient(90deg, rgb(0 0 0 / 0.04), rgb(0 0 0 / 0.08), rgb(0 0 0 / 0.04));
  background-size: 200% 100%;
  animation: t-data-cards-skel 1.2s ease-in-out infinite;
}
@keyframes t-data-cards-skel {
  0% { background-position: 200% 0; }
  100% { background-position: -200% 0; }
}

/* Visuals live in TEmpty; this wrapper only centers custom #empty content. */
.t-data-cards__empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
}
</style>
