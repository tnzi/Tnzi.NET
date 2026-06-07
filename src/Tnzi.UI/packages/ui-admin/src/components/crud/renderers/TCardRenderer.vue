<template>
  <div class="t-card-renderer">
    <div v-if="props.state.loading.value && !hasItems" class="t-card-renderer__grid" :style="gridStyle">
      <div v-for="n in skeletonCount" :key="`sk-${n}`" class="t-card-renderer__skeleton" />
    </div>

    <div v-else-if="!hasItems" class="t-card-renderer__empty">
      <slot name="empty">
        <TSvgIcon icon="mdi:inbox-outline" :size="40" />
        <span class="t-card-renderer__empty-text">{{ t('admin.crud.empty') }}</span>
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
import { TSvgIcon } from '@tnzi/ui'
import type { UseCrudPageReturn } from '../../../headless/useCrudPage'
import { useBreakpoint } from '../../../headless/useBreakpoint'

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
.t-card-renderer__grid { display: grid; }
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
.t-card-renderer__empty {
  display: flex; flex-direction: column; align-items: center; justify-content: center;
  gap: 8px; padding: 48px 0; color: var(--tnzi-base-text-muted, #9ca3af);
}
.t-card-renderer__empty-text { font-size: 13px; }
</style>
