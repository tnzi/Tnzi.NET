<template>
  <!--
    Horizontal bar-rank widget - a labelled row per item with a value and a bar
    scaled to the largest value (or an explicit `max`). Fills the gap the admin
    KPI/pie widgets leave for "top-N by X" breakdowns. Pure props-driven.
  -->
  <div class="t-metric-bars">
    <template v-if="items.length">
      <div v-for="(item, i) in items" :key="i" class="t-metric-bars__row">
        <div class="t-metric-bars__head">
          <span class="t-metric-bars__label" :title="item.label">{{ item.label }}</span>
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
import { computed } from 'vue'

export interface MetricBarItem {
  label: string
  value: number
  /** Displayed value override, e.g. a formatted currency string. */
  display?: string
  /** Bar colour; defaults to the theme primary. */
  color?: string
}

interface Props {
  items: MetricBarItem[]
  /** Scale denominator. Default: the largest item value (min 1). */
  max?: number
  emptyText?: string
}

const props = withDefaults(defineProps<Props>(), { emptyText: 'No data' })

const defaultColor = 'var(--tnzi-primary, #2080f0)'
// Clamp to >= 1 so an explicit `max: 0` (or all-zero items) can't divide by zero.
const maxValue = computed(() => Math.max(1, props.max ?? Math.max(1, ...props.items.map((i) => i.value))))
const pct = (v: number): number => Math.max(0, Math.min(100, (v / maxValue.value) * 100))
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
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 13px;
  color: var(--tnzi-base-text, currentColor);
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
</style>
