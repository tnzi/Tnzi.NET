<template>
  <div class="t-metric-bars">
    <template v-if="items.length">
      <div
        v-for="(item, i) in items"
        :key="i"
        class="t-metric-bars__row"
        :class="{ 't-metric-bars__row--clickable': clickable }"
        :role="clickable ? 'button' : undefined"
        :tabindex="clickable ? 0 : undefined"
        @click="onActivate(item, i, $event)"
        @keydown="onKeydown(item, i, $event)"
      >
        <div class="t-metric-bars__head">
          <span class="t-metric-bars__label" :title="item.label">{{ item.label }}</span>
          <span v-if="item.meta" class="t-metric-bars__meta">{{ item.meta }}</span>
          <span class="t-metric-bars__value">{{ item.display ?? item.value }}</span>
        </div>
        <div class="t-metric-bars__track">
          <div
            class="t-metric-bars__fill"
            :style="{ width: `${pct(item.value)}%`, background: item.color ?? defaultColor }"
          />
        </div>
      </div>
    </template>
    <div v-else class="t-metric-bars__empty">
      <slot name="empty">{{ emptyText }}</slot>
    </div>
  </div>
</template>

<script setup lang="ts">
/**
 * `TMetricBars` - horizontal bar-rank widget. One labelled row per item with a
 * value and a bar scaled to the largest value (or an explicit `max`). Fills the
 * gap the admin KPI/pie widgets leave for "top-N by X" breakdowns.
 *
 * Unlike a pie, the bars do not have to add up to anything: use this whenever
 * the rows are independent measurements rather than slices of one total (a
 * matter handled by two lawyers counts in full for both, so a donut of the same
 * data would be lying about the shape of it).
 *
 * ## Drill-down
 *
 * A rank answers "how much per X"; the next question is always "which records
 * make up this row". Opt in with `clickable` and the rows become real controls
 * that emit `row-click`:
 *
 * ```vue
 * <TMetricBars :items="byLawyer" clickable @row-click="openDrilldown" />
 * ```
 * ```ts
 * function openDrilldown(e: MetricBarClickEvent) {
 *   // `e.index` maps back to the original `items` entry even when `label` is a
 *   // display name rather than a stable key.
 *   loadFilesFor(byLawyer.value[e.index])
 *   openMenu({ x: e.clientX, y: e.clientY })
 * }
 * ```
 *
 * `clickable` is off by default and is the ONLY thing that changes the rows:
 * without it there is no pointer cursor, no hover surface, and nothing extra in
 * the tab order - a pure display list stays a pure display list. It is a prop
 * rather than listener detection so that adding, say, an analytics listener
 * cannot silently turn a read-only rank into something that looks interactive.
 */
import { computed } from 'vue'

export interface MetricBarItem {
  label: string
  value: number
  /** Displayed value override, e.g. a formatted currency string. */
  display?: string
  /**
   * Secondary text shown between the label and the value, e.g. a record count
   * behind the figure. Keep it short - it is laid out on the same line and,
   * unlike the label, it does not ellipsise.
   *
   * Prefer this over folding the extra datum into `display`: that column is
   * right-aligned tabular-nums so the numbers line up down the list, and
   * appending anything to it breaks that alignment.
   */
  meta?: string
  /** Bar colour; defaults to the theme primary. */
  color?: string
}

/** Payload of `row-click` - a row was activated by pointer or keyboard. */
export interface MetricBarClickEvent {
  /** The activated entry. */
  item: MetricBarItem
  /**
   * Position of the entry in the `items` prop. Use this rather than `label` to
   * map back to your own records - `label` is often a display name (or a
   * placeholder like "Unassigned"), not a stable key.
   */
  index: number
  /**
   * Viewport coordinates to anchor a menu / popover to. For a pointer this is
   * where the click landed; for keyboard activation it is the bottom-left of
   * the activated row, so a menu still opens next to what the user acted on.
   * Always a number - every emit comes from a real interaction on a real row.
   */
  clientX: number
  clientY: number
  /** The event that activated the row. */
  nativeEvent: MouseEvent | KeyboardEvent
}

interface Props {
  items: MetricBarItem[]
  /** Scale denominator. Default: the largest item value (min 1). */
  max?: number
  emptyText?: string
  /**
   * Rows become click targets (emit `row-click`) and keyboard-reachable.
   * Default false: display-only lists must not grow a tab stop.
   */
  clickable?: boolean
}

const props = withDefaults(defineProps<Props>(), { emptyText: 'No data', clickable: false })

const emit = defineEmits<{
  /** A row was activated - drill into the records behind it. */
  (e: 'row-click', payload: MetricBarClickEvent): void
}>()

const defaultColor = 'var(--tnzi-primary, #2080f0)'
// Clamp to >= 1 so an explicit `max: 0` (or all-zero items) can't divide by zero.
const maxValue = computed(() => Math.max(1, props.max ?? Math.max(1, ...props.items.map((i) => i.value))))
const pct = (v: number): number => Math.max(0, Math.min(100, (v / maxValue.value) * 100))

function onActivate(item: MetricBarItem, index: number, event: MouseEvent): void {
  if (!props.clickable) return
  emit('row-click', { item, index, clientX: event.clientX, clientY: event.clientY, nativeEvent: event })
}

function onKeydown(item: MetricBarItem, index: number, event: KeyboardEvent): void {
  if (!props.clickable) return
  if (event.key !== 'Enter' && event.key !== ' ') return
  // Space would otherwise scroll the page; both keys "activate" the row.
  // `role="button"` gets no synthesised click (only a real <button> does), so
  // this is the whole keyboard path rather than a duplicate of the click one.
  event.preventDefault()
  const rect = (event.currentTarget as HTMLElement | null)?.getBoundingClientRect()
  emit('row-click', {
    item,
    index,
    clientX: rect?.left ?? 0,
    clientY: rect?.bottom ?? 0,
    nativeEvent: event,
  })
}
</script>

<style scoped>
.t-metric-bars {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.t-metric-bars__head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 4px;
}
.t-metric-bars__label {
  /* Grows so the value stays pinned right and the label is the part that
     ellipsises when the row runs out of room. */
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 13px;
  color: var(--tnzi-base-text, currentColor);
}
.t-metric-bars__meta {
  flex-shrink: 0;
  font-size: 12px;
  color: var(--tnzi-base-text-muted, rgba(0, 0, 0, 0.45));
}
.t-metric-bars__value {
  flex-shrink: 0;
  font-size: 13px;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
  color: var(--tnzi-base-text, currentColor);
}
.t-metric-bars__track {
  height: 8px;
  border-radius: 4px;
  background: var(--tnzi-border, rgba(0, 0, 0, 0.08));
  overflow: hidden;
}
.t-metric-bars__fill {
  height: 100%;
  border-radius: 4px;
  transition: width 0.4s ease;
}
.t-metric-bars__empty {
  padding: 16px 0;
  text-align: center;
  color: var(--tnzi-base-text-muted, rgba(0, 0, 0, 0.4));
  font-size: 13px;
}

/* Interactive rows - only reachable when the host opted in with `clickable`. */
.t-metric-bars__row--clickable {
  position: relative;
  /* Own stacking context, so the hover surface below can sit behind the row's
     content without dropping behind the surrounding card's background too. */
  isolation: isolate;
  cursor: pointer;
}
.t-metric-bars__row--clickable::before {
  content: '';
  position: absolute;
  /* Bleeds into the 12px row gap so the feedback reads as a whole row. Doing it
     with padding instead would shift the content of a clickable list relative
     to a display-only one. */
  inset: -6px -8px;
  z-index: -1;
  border-radius: var(--tnzi-radius, 6px);
  background: transparent;
  transition: background var(--tnzi-duration-fast, 120ms) var(--tnzi-easing, ease);
}
.t-metric-bars__row--clickable:hover::before {
  background: rgb(var(--tnzi-base-text-rgb, 51 54 57) / 0.05);
}
.t-metric-bars__row--clickable:focus-visible {
  outline: 2px solid var(--tnzi-primary, #2080f0);
  outline-offset: 2px;
}
</style>
