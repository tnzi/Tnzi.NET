<template>
  <div class="t-card-renderer">
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

    <div v-else class="t-card-renderer__grid" :style="gridStyle">
      <div
        v-for="(item, index) in items"
        :key="keyOf(item)"
        class="t-card-renderer__cell"
        :class="{ 't-card-renderer__cell--selected': showSelection && isSelected(item) }"
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
          :toggleSelect="() => toggle(item)"
        />
      </div>
    </div>
  </div>
</template>

<script setup lang="ts" generic="T, TId extends string | number = string | number">
import { computed } from 'vue'
import { NButton } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TEmpty from '../../data/TEmpty.vue'
import type { UseCrudPageReturn } from '../../../headless/useCrudPage'
import { useBreakpoint } from '../../../headless/useBreakpoint'
import { useEmptyCreateCta } from '../../../headless/useEmptyCreateCta'

export interface TCardRendererProps<T, TId extends string | number = string | number> {
  state: UseCrudPageReturn<T, TId>
  /** Column count: a fixed number, or a responsive map keyed by breakpoint. */
  cols?: number | { xs?: number; sm?: number; md?: number; lg?: number; xl?: number }
  gap?: number
  cardKey?: (row: T) => string | number
  showSelection?: boolean
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<TCardRendererProps<T, TId>>(), {
  cols: () => ({ xs: 1, sm: 2, md: 3, lg: 4 }),
  gap: 16,
  cardKey: undefined,
  showSelection: false,
  translate: undefined,
})

defineSlots<{
  card?: (props: { item: T; index: number; selected: boolean; toggleSelect: () => void }) => unknown
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

const gridStyle = computed(() => ({
  gridTemplateColumns: `repeat(${currentCols.value}, minmax(0, 1fr))`,
  gap: `${props.gap}px`,
}))

const skeletonCount = computed(() => currentCols.value * 2)

function keyOf(row: T): string | number {
  return props.cardKey ? props.cardKey(row) : props.state.rowKey(row)
}
function isSelected(row: T): boolean {
  return props.state.batchActions.isSelected(props.state.rowKey(row) as TId)
}
function toggle(row: T): void {
  props.state.batchActions.toggle(props.state.rowKey(row) as TId)
}
</script>

<style scoped>
.t-card-renderer { width: 100%; }
/* In page mode the shell gives the renderer a bounded height via the flex chain
   (`.t-list-shell--page .t-list-shell__body` is `flex:1; min-height:0; overflow:hidden`).
   The table renderer scrolls its own NDataTable, but the card grid has no internal
   scroller — so it must fill the body and own the scroll itself, or the overflowing
   cards get clipped. Scoped to page mode only (ancestor class match) so container-mode
   card grids stay content-height and let the outer page scroll. */
.t-list-shell--page .t-card-renderer {
  flex: 1 1 auto;
  min-height: 0;
  overflow-y: auto;
}
/* padding-top gives the first row's cards headroom for the card hover
   `translateY(-1px)` lift. The page-mode renderer is an `overflow-y:auto`
   scroll container whose padding-box top edge is the clip boundary; without
   this headroom the top row's cards get their top edge clipped on hover. */
.t-card-renderer__grid { display: grid; padding-top: 4px; }
.t-card-renderer__cell { position: relative; }
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
