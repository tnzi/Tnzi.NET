<template>
  <div class="t-item-renderer">
    <div v-if="props.state.loading.value && !hasItems" class="t-item-renderer__list">
      <div v-for="n in 6" :key="`sk-${n}`" class="t-item-renderer__skeleton" />
    </div>

    <div v-else-if="!hasItems" class="t-item-renderer__empty">
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

    <div v-else class="t-item-renderer__list">
      <template v-for="(item, index) in items" :key="keyOf(item)">
        <slot
          name="item"
          :item="item"
          :index="index"
          :selected="showSelection && isSelected(item)"
          :selectable="showSelection"
          :toggleSelect="() => toggle(item)"
          :rowActions="props.rowActions"
        />
      </template>
    </div>
  </div>
</template>

<script setup lang="ts" generic="T, TId extends string | number = string | number">
/**
 * TItemRenderer - the third list renderer, beside TTableRenderer (grid) and
 * TCardRenderer (tile grid).
 *
 * Renders one full-width row per record, which the page fills with a
 * {@link TItemCard} (or any bespoke row). This is the shape for records that
 * read as DOCUMENTS - a title, a status, a date, a figure - where a table
 * would flatten the hierarchy into equal-weight columns and then run out of
 * horizontal room.
 *
 * Selection is exposed to the slot (`selected` / `selectable` /
 * `toggleSelect`) rather than drawn by the renderer, because a row card puts
 * its checkbox inline at the head of the row, not floating over a tile.
 */
import { computed } from 'vue'
import { NButton } from 'naive-ui'
import TEmpty from '../../data/TEmpty.vue'
import type { UseCrudPageReturn } from '../../../headless/useCrudPage'
import type { RowAction } from '../../../headless/rowActions'
import { useEmptyCreateCta } from '../../../headless/useEmptyCreateCta'

export interface TItemRendererProps<T, TId extends string | number = string | number> {
  state: UseCrudPageReturn<T, TId>
  itemKey?: (row: T) => string | number
  showSelection?: boolean
  /**
   * Declarative row operations, handed straight back to the `#item` slot.
   *
   * A row card decides for itself WHERE its operations sit (footer, trailing
   * edge, overflow menu), so the renderer does not draw them; it only carries
   * the one declaration through, so a page keeps a single `RowAction[]` the way
   * the table shell does instead of hand-rolling a second list.
   */
  rowActions?: RowAction<T>[]
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<TItemRendererProps<T, TId>>(), {
  itemKey: undefined,
  showSelection: false,
  rowActions: undefined,
  translate: undefined,
})

defineSlots<{
  item?: (props: {
    item: T
    index: number
    selected: boolean
    selectable: boolean
    toggleSelect: () => void
    rowActions: RowAction<T>[] | undefined
  }) => unknown
  empty?: () => unknown
}>()

function t(key: string): string {
  return props.translate ? props.translate(key) : key
}

/** Translated empty text; without a translator let TEmpty fall back to
 *  'No data' instead of leaking the raw `admin.crud.empty` key. */
const emptyText = computed(() => (props.translate ? t('admin.crud.empty') : undefined))

const { showCreateCta, createCtaLabel, onEmptyCreate } = useEmptyCreateCta<T, TId>(
  () => props.state,
  () => props.translate,
)

const items = computed<T[]>(() => props.state.items.value)
const hasItems = computed(() => items.value.length > 0)

function keyOf(row: T): string | number {
  return props.itemKey ? props.itemKey(row) : props.state.rowKey(row)
}
function isSelected(row: T): boolean {
  return props.state.batchActions.isSelected(props.state.rowKey(row) as TId)
}
function toggle(row: T): void {
  props.state.batchActions.toggle(props.state.rowKey(row) as TId)
}
</script>

<style scoped>
.t-item-renderer {
  width: 100%;
}
/* In page mode the shell hands the renderer a bounded height through the flex
   chain; a row list has no internal scroller of its own, so it must fill the
   body and own the scroll or the overflowing rows get clipped. Container mode
   stays content-height and lets the outer page scroll. */
.t-list-shell--page .t-item-renderer {
  flex: 1 1 auto;
  min-height: 0;
  overflow-y: auto;
}
.t-item-renderer__list {
  display: flex;
  flex-direction: column;
  gap: 10px;
  /* Headroom for the row card's hover shadow inside the scroll container. */
  padding-top: 2px;
}
.t-item-renderer__skeleton {
  height: 66px;
  border-radius: var(--tnzi-admin-radius-md, 8px);
  background: linear-gradient(90deg, rgb(0 0 0 / 0.04), rgb(0 0 0 / 0.08), rgb(0 0 0 / 0.04));
  background-size: 200% 100%;
  animation: t-item-skel 1.2s ease-in-out infinite;
}
@keyframes t-item-skel {
  0% { background-position: 200% 0; }
  100% { background-position: -200% 0; }
}
/* Visuals live in TEmpty; this wrapper only centers custom #empty content. */
.t-item-renderer__empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
}
</style>
