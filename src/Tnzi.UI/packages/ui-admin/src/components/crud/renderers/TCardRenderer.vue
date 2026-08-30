<template>
  <div ref="rootEl" class="t-card-renderer">
    <div v-if="props.state.loading.value && !hasItems" class="t-card-renderer__grid" :style="gridStyle">
      <div v-for="n in skeletonCount" :key="`sk-${n}`" class="t-card-renderer__skeleton" />
    </div>

    <div v-else-if="!hasItems" class="t-card-renderer__empty">
      <slot name="empty">
        <!-- First-load empty on a creatable list → small Create CTA;
             search/filter misses keep the plain empty visual. -->
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
      </slot>
    </div>

    <component
      :is="dragEnabled ? dragContainer : 'div'"
      v-else
      v-bind="dragBindings"
      class="t-card-renderer__grid"
      :class="{ 't-card-renderer__grid--draggable': dragEnabled }"
      :style="gridStyle"
    >
      <div
        v-for="(item, index) in renderItems"
        :key="keyOf(item)"
        class="t-card-renderer__cell"
        :class="{
          't-card-renderer__cell--selected': showSelection && isSelected(item),
          't-card-renderer__cell--draggable': dragEnabled && !isDragDisabled(item),
          't-card-renderer__cell--no-drag': dragEnabled && isDragDisabled(item),
        }"
      >
        <button
          v-if="showSelection"
          type="button"
          class="t-card-renderer__select"
          :aria-pressed="isSelected(item)"
          @click="toggle(item)"
        >
          <TSvgIcon :icon="isSelected(item) ? 'mdi:checkbox-marked' : 'mdi:checkbox-blank-outline'" :size="18" />
        </button>
        <slot
          name="card"
          :item="item"
          :index="index"
          :selected="showSelection && isSelected(item)"
          :toggle-select="() => toggle(item)"
          :row-actions="props.rowActions"
        />
      </div>
    </component>
  </div>
</template>

<script setup lang="ts" generic="T, TId extends string | number = string | number">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { NButton } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { TEmpty } from '@tnzi/ui'
import { useReorderable } from '@tnzi/ui/headless'
import type { UseCrudPageReturn } from '../../../headless/useCrudPage'
import type { RowAction } from '../../../headless/row-actions'
import { useBreakpoint } from '../../../headless/useBreakpoint'
import { useEmptyCreateCta } from '../../../headless/useEmptyCreateCta'
import type { CardReorderPayload } from './card-reorder'

export interface TCardRendererProps<T, TId extends string | number = string | number> {
  state: UseCrudPageReturn<T, TId>
  /** Column count: a fixed number, or a responsive map keyed by breakpoint. */
  cols?: number | { xs?: number; sm?: number; md?: number; lg?: number; xl?: number }
  /**
   * Minimum column width in px. Set it and the grid packs as many columns as
   * fit instead of taking its count from `cols`.
   *
   * ★ This is what a wide screen needs. The breakpoint map stops at `xl`, and
   * `xl` means "1280 and up, forever" - so a 1280 laptop and a 4K monitor get
   * the same column count and the cards on the 4K one grow to whatever is left
   * over. A minimum width says the thing the design actually fixes (how wide a
   * card should be) and lets the count follow the viewport.
   *
   * Takes precedence over `cols` for the layout; `cols` still supplies the
   * skeleton count until the first measurement lands (see `autoCols`).
   */
  minColWidth?: number
  gap?: number
  cardKey?: (row: T) => string | number
  showSelection?: boolean
  /**
   * Declarative row operations, handed straight back to the `#card` slot.
   * A tile places its own operations (footer strip, hover overlay, kebab), so
   * the renderer carries the declaration through rather than drawing it.
   */
  rowActions?: RowAction<T>[]
  /**
   * Let the operator reorder cards by dragging them. Off by default: a card
   * list is display-first, and a list whose order carries no meaning should
   * not offer to change it.
   *
   * The whole card is the drag handle; buttons, links and inputs inside it keep
   * working as controls. Persisting is the consumer's job - listen for
   * `reorder`.
   *
   * **Left mouse button only.** SortableJS returns early on
   * `mousedown`/`pointerdown` with `button !== 0`, so a right- or middle-button
   * press never starts a gesture and the context menu still opens normally.
   * Noted here because it is a guarantee we rely on, not something this
   * component enforces itself.
   */
  draggable?: boolean
  /** Extra CSS selector for elements that must not start a drag (appended to the built-in list). */
  dragFilter?: string
  /** Rows that may not be dragged (pinned / system rows). They still render and still receive drops around them. */
  dragDisabled?: (row: T) => boolean
  /**
   * Allow dragging while the list is sorted by a column. Off by default:
   * a hand-made order means nothing while the server is ordering by
   * "created at", and saving it would silently discard the drag on the next
   * refresh. Turn it on only if the page's sort and its stored order agree.
   */
  dragWhileSorted?: boolean
  translate?: (key: string) => string
}



/**
 * Elements inside a card that must stay clickable rather than starting a drag.
 * Without this a tap on a row action or a link reads as the beginning of a
 * gesture and the card never fires its own handler.
 */
const INTERACTIVE_SELECTOR =
  'button, a, input, textarea, select, [contenteditable], .t-card-renderer__select, .t-card-renderer__cell--no-drag'

const props = withDefaults(defineProps<TCardRendererProps<T, TId>>(), {
  cols: () => ({ xs: 1, sm: 2, md: 3, lg: 4 }),
  minColWidth: undefined,
  gap: 16,
  cardKey: undefined,
  showSelection: false,
  rowActions: undefined,
  draggable: false,
  dragFilter: undefined,
  dragDisabled: undefined,
  dragWhileSorted: false,
  translate: undefined,
})

const emit = defineEmits<{ reorder: [payload: CardReorderPayload<T, TId>] }>()

defineSlots<{
  card?: (props: {
    item: T
    index: number
    selected: boolean
    toggleSelect: () => void
    rowActions: RowAction<T>[] | undefined
  }) => unknown
  empty?: () => unknown
}>()

const bp = useBreakpoint()

function t(key: string): string {
  return props.translate ? props.translate(key) : key
}

/** Translated empty text; without a translator let TEmpty fall back to
 *  'No data' instead of leaking the raw `admin.crud.empty` key. */
const emptyText = computed(() => (props.translate ? t('admin.crud.empty') : undefined))

/** First-load empty on a creatable list → offer a small Create CTA inside
 *  the default empty visual. Search/filter misses stay plain. */
const { showCreateCta, createCtaLabel, onEmptyCreate } = useEmptyCreateCta<T, TId>(
  () => props.state,
  () => props.translate,
)

const items = computed<T[]>(() => props.state.items.value)
const hasItems = computed(() => items.value.length > 0)

const currentCols = computed<number>(() => {
  if (typeof props.cols === 'number') return Math.max(1, props.cols)
  const c = props.cols
  if (bp.isXs.value) return c.xs ?? c.sm ?? c.md ?? c.lg ?? c.xl ?? 1
  if (bp.isSm.value) return c.sm ?? c.md ?? c.lg ?? c.xl ?? c.xs ?? 2
  if (bp.isMd.value) return c.md ?? c.lg ?? c.xl ?? c.sm ?? c.xs ?? 3
  if (bp.isLg.value) return c.lg ?? c.xl ?? c.md ?? c.sm ?? c.xs ?? 4
  return c.xl ?? c.lg ?? c.md ?? c.sm ?? c.xs ?? 4
})

/* ------------------------------------------------- width-driven column count */

/**
 * The renderer's usable width, measured - only while `minColWidth` is set.
 *
 * A call site that has not opted in observes nothing: the point of the
 * measurement is the skeleton count below, and without a minimum width the
 * count comes from the breakpoint map exactly as it always has.
 */
const rootEl = ref<HTMLElement | null>(null)
const availableWidth = ref(0)
let observer: ResizeObserver | undefined

function measure(): void {
  const el = rootEl.value
  if (!el) return
  // `clientWidth` includes the renderer's own card gutter, which the grid does
  // not get to use. Subtract it, or the count is one too many at the boundary.
  const cs = getComputedStyle(el)
  const padding = (parseFloat(cs.paddingLeft) || 0) + (parseFloat(cs.paddingRight) || 0)
  availableWidth.value = Math.max(0, el.clientWidth - padding)
}

function stopObserving(): void {
  observer?.disconnect()
  observer = undefined
  window.removeEventListener('resize', measure)
}

function startObserving(): void {
  measure()
  // No ResizeObserver (old browser, some test environments): fall back to the
  // viewport. Coarser - it misses a sidebar collapsing - but the grid itself
  // still reflows correctly either way; only the skeleton count is at stake.
  if (typeof ResizeObserver === 'undefined') {
    window.addEventListener('resize', measure)
    return
  }
  observer = new ResizeObserver(measure)
  if (rootEl.value) observer.observe(rootEl.value)
}

// Attached only while a minimum width is asked for, and it FOLLOWS the prop
// rather than being decided once at mount: a call site that computes its
// minimum would otherwise be left permanently unmeasured, which does not
// break the grid (`auto-fill` needs no help) but silently pins the skeleton
// count to the breakpoint fallback.
onMounted(() => {
  if (props.minColWidth !== undefined) startObserving()
})

watch(
  () => props.minColWidth === undefined,
  (off) => {
    stopObserving()
    if (off) availableWidth.value = 0
    else startObserving()
  },
)

onBeforeUnmount(stopObserving)

/**
 * How many columns `auto-fill` will actually produce, or `null` when there is
 * nothing to derive it from (no minimum width asked for, or not yet measured).
 *
 * This exists for ONE reason: the skeleton count. The grid itself needs no
 * arithmetic - `auto-fill` does its own - but the loading placeholders are a
 * fixed number of divs, and a number taken from the breakpoint map puts eight
 * of them into a seven-column grid, which reads as a broken row rather than as
 * a list loading. The formula below is the one `auto-fill` uses, so the two
 * agree.
 */
const autoCols = computed<number | null>(() => {
  const min = props.minColWidth
  if (min === undefined || min <= 0) return null
  const width = availableWidth.value
  if (!width) return null
  return Math.max(1, Math.floor((width + props.gap) / (min + props.gap)))
})

const gridStyle = computed(() => ({
  // `min(Npx, 100%)` rather than a bare `Npx`: a bare minimum larger than the
  // container makes the track overflow horizontally, which is exactly what a
  // phone-width grid does with any realistic card width.
  gridTemplateColumns:
    props.minColWidth === undefined
      ? `repeat(${currentCols.value}, minmax(0, 1fr))`
      : `repeat(auto-fill, minmax(min(${props.minColWidth}px, 100%), 1fr))`,
  gap: `${props.gap}px`,
}))

const skeletonCount = computed(() => (autoCols.value ?? currentCols.value) * 2)

function keyOf(row: T): string | number {
  return props.cardKey ? props.cardKey(row) : props.state.rowKey(row)
}
function isSelected(row: T): boolean {
  return props.state.batchActions.isSelected(props.state.rowKey(row) as TId)
}
function toggle(row: T): void {
  props.state.batchActions.toggle(props.state.rowKey(row) as TId)
}

/* ---------------------------------------------------------------- dragging */

/**
 * Sorting by a column and dragging cards are two different answers to
 * "what order is this in", and only one of them can be true. While the list is
 * ordered by "created at" the sequence the operator drags out means nothing:
 * the next refresh re-sorts it away, and persisting it writes an order nobody
 * will ever see. So dragging is off while a column sort is active unless the
 * page says its sort and its stored order agree.
 */
const sortedByColumn = computed(() => Boolean(props.state.query.value?.sortField))
const dragEnabled = computed(() => props.draggable && (props.dragWhileSorted || !sortedByColumn.value))

function isDragDisabled(row: T): boolean {
  return props.dragDisabled?.(row) === true
}

const dragFilterSelector = computed(() =>
  props.dragFilter ? `${INTERACTIVE_SELECTOR}, ${props.dragFilter}` : INTERACTIVE_SELECTOR,
)

/**
 * The local copy, the drag lifecycle and the optimistic apply all come from
 * `@tnzi/ui`'s `useReorderable` - the same implementation `TAttachmentWall` and
 * `TWorkbenchLayout` use. What is local is the two things that are genuinely
 * this component's: which elements must not start a gesture, and the fact that
 * an order lands in a writable store projection rather than in a prop.
 *
 * ★ A card grid filters rather than using a handle, unlike the attachment wall.
 * That holds here because a card's interactive elements (row actions, links,
 * the selection toggle) occupy small parts of a large surface - a filter only
 * becomes a trap when something interactive can FILL the item.
 */
const { dragContainer, renderItems, dragBindings } = useReorderable<T>({
  source: () => items.value,
  enabled: () => dragEnabled.value,
  filter: () => dragFilterSelector.value,
  ghostClass: 't-card-renderer__cell--ghost',
  chosenClass: 't-card-renderer__cell--chosen',
  // `state.items` is a writable computed projecting onto the query controller -
  // assigning it is what it is for, not a prop mutation. Doing it here rather
  // than in every consumer is the whole point of the optimistic update:
  // otherwise each page re-implements apply-then-roll-back.
  // eslint-disable-next-line vue/no-mutating-props
  apply: (next) => { props.state.items.value = next },
  onCommit: (move) => {
    emit('reorder', {
      orderedIds: move.items.map((row) => props.state.rowKey(row) as TId),
      items: move.items,
      from: move.from,
      to: move.to,
      moved: move.moved,
      revert: move.revert,
    })
  },
})

</script>

<style scoped>
/* Clearance. A card's chrome lives outside its border box - the selection
   ring, the drop shadow - so a container that packs cards flush against its
   own edge crops all of it, and in a scroll container the crop lands right
   where the scrollbar is. Tokenised so a consumer can set it to 0.
   (The nested cards' own material is NOT decided here: that is structural and
   lives in `@tnzi/ui/styles/surfaces.css`, keyed off `t-surface-card`.) */
.t-card-renderer {
  width: 100%;
  padding: var(--tnzi-surface-card-gutter);
}
/* In page mode the shell gives the renderer a bounded height via the flex chain
   (`.t-list-shell--page .t-list-shell__body` is `flex:1; min-height:0; overflow:hidden`).
   The table renderer scrolls its own NDataTable, but the card grid has no internal
   scroller - so it must fill the body and own the scroll itself, or the overflowing
   cards get clipped. Scoped to page mode only (ancestor class match) so container-mode
   card grids stay content-height and let the outer page scroll. */
.t-list-shell--page .t-card-renderer {
  flex: 1 1 auto;
  min-height: 0;
  overflow-y: auto;
}
/* The headroom that used to live here as a one-directional `padding-top: 4px`
   is now the four-sided gutter on the renderer itself. One direction was never
   enough: the same crop happens on the left against the container wall and on
   the right against the scrollbar, and it applies to the hairline and the
   selection ring, not only to a hover lift (which no longer exists). */
.t-card-renderer__grid { display: grid; }
.t-card-renderer__cell { position: relative; }
/* The drag cursor appears on PRESS, not on hover. A resting `grab` would claim
   the card is a thing you drag - but a card is very often also a thing you
   click (drill into the record), and its own `cursor: pointer` would be
   overridden by ours. So at rest the card keeps whatever cursor it chose, and
   the gesture announces itself the moment a button goes down. */
.t-card-renderer__cell--draggable:active { cursor: grabbing; }
.t-card-renderer__cell--chosen { cursor: grabbing; }
/* The placeholder left behind at the drop target. Faded rather than hidden so
   the grid does not reflow under the pointer mid-gesture. */
.t-card-renderer__cell--ghost { opacity: 0.35; }
.t-card-renderer__cell--no-drag { cursor: default; }
.t-card-renderer__cell--selected { outline: 2px solid var(--tnzi-primary, #646cff); outline-offset: 2px; border-radius: var(--tnzi-admin-radius-md, 8px); }
.t-card-renderer__select {
  position: absolute; top: 8px; right: 8px; z-index: 1;
  display: inline-flex; align-items: center; justify-content: center;
  width: 24px; height: 24px; padding: 0; border: none; border-radius: 4px;
  background: rgb(255 255 255 / 0.8); color: var(--tnzi-primary, #646cff); cursor: pointer;
}
.t-card-renderer__skeleton {
  height: 120px; border-radius: var(--tnzi-admin-radius-md, 8px);
  background: linear-gradient(90deg, rgb(0 0 0 / 0.04), rgb(0 0 0 / 0.08), rgb(0 0 0 / 0.04));
  background-size: 200% 100%; animation: t-card-skel 1.2s ease-in-out infinite;
}
@keyframes t-card-skel { 0% { background-position: 200% 0; } 100% { background-position: -200% 0; } }
/* Visuals live in TEmpty; this wrapper only centers custom #empty content. */
.t-card-renderer__empty {
  display: flex; flex-direction: column; align-items: center; justify-content: center;
}
</style>
