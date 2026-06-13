<template>
  <!--
    TStatCard — unified KPI statistic card (white NCard, small, borderless).
    Layout: optional icon in a light rounded block on the left; label (12px,
    muted) above the value (24px, semibold). Numbers animate via
    NNumberAnimation (default on); strings render verbatim; `null` renders
    an em dash. `tone` colours the value (and the icon block) with the
    standard status palette. Compose inside `TKpiRow` for the responsive
    grid; pages pass already-translated `label` text (the page owns t()).
  -->
  <NCard size="small" :bordered="false" class="t-stat-card">
    <div class="t-stat-card__inner">
      <span v-if="icon" class="t-stat-card__icon" :class="`t-stat-card__icon--${tone}`">
        <TSvgIcon :icon="icon" :size="20" />
      </span>
      <div class="t-stat-card__main">
        <div class="t-stat-card__label">{{ label }}</div>
        <div class="t-stat-card__value" :class="`t-stat-card__value--${tone}`">
          <span class="t-stat-card__number">
            <template v-if="value == null">—</template>
            <NNumberAnimation
              v-else-if="typeof value === 'number' && animated"
              :from="0"
              :to="value"
              :precision="precision"
            />
            <template v-else>{{ value }}</template>
          </span>
          <span v-if="suffix && value != null" class="t-stat-card__suffix">{{ suffix }}</span>
          <slot name="extra" />
        </div>
      </div>
    </div>
  </NCard>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { NCard, NNumberAnimation } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'

export type TStatCardTone = 'default' | 'success' | 'warning' | 'error'

export interface TStatCardProps {
  /** Already-translated label text (the page side owns t()). */
  label: string
  /** `null` renders an em dash; numbers animate (see `animated`); strings render verbatim. */
  value: number | string | null
  /** Trailing unit, e.g. `'%'`, `'ms'`. Hidden while the value is null. */
  suffix?: string
  /** mdi:* icon rendered in a light rounded block on the left. */
  icon?: string
  /** Value (and icon block) colour. */
  tone?: TStatCardTone
  /** Animate numeric values via NNumberAnimation (default true). */
  animated?: boolean
}

const props = withDefaults(defineProps<TStatCardProps>(), {
  suffix: undefined,
  icon: undefined,
  tone: 'default',
  animated: true,
})

defineSlots<{
  /** Trailing content after the value (e.g. a status NTag). */
  extra?: () => unknown
}>()

/**
 * Preserve the value's decimal places through NNumberAnimation — its default
 * `precision: 0` would round e.g. `99.95` → `100`. Capped at 4 to avoid
 * float-noise tails ever widening a KPI card.
 */
const precision = computed<number>(() => {
  if (typeof props.value !== 'number' || !Number.isFinite(props.value)) return 0
  const text = String(props.value)
  const dot = text.indexOf('.')
  return dot === -1 ? 0 : Math.min(text.length - dot - 1, 4)
})
</script>

<style scoped>
.t-stat-card {
  border-radius: var(--tnzi-admin-radius-md, 8px);
  box-shadow: 0 1px 2px rgb(0 0 0 / 0.05);
}
.t-stat-card__inner {
  display: flex;
  align-items: center;
  gap: 10px;
  min-width: 0;
}
.t-stat-card__icon {
  flex-shrink: 0;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 36px;
  height: 36px;
  border-radius: 8px;
}
.t-stat-card__icon--default {
  color: var(--tnzi-primary, #6d5ce7);
  background: rgb(109 92 231 / 0.1);
}
.t-stat-card__icon--success {
  color: var(--tnzi-success, #18a058);
  background: rgb(24 160 88 / 0.12);
}
.t-stat-card__icon--warning {
  color: var(--tnzi-warning, #f0a020);
  background: rgb(240 160 32 / 0.12);
}
.t-stat-card__icon--error {
  color: var(--tnzi-error, #d03050);
  background: rgb(208 48 80 / 0.12);
}
.t-stat-card__main {
  flex: 1 1 auto;
  min-width: 0;
}
.t-stat-card__label {
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #888);
  margin-bottom: 4px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-stat-card__value {
  display: flex;
  align-items: baseline;
  gap: 6px;
  flex-wrap: wrap;
  min-width: 0;
  font-size: 24px;
  font-weight: 600;
  line-height: 1.1;
  color: var(--tnzi-base-text, #1f2937);
}
.t-stat-card__value--success { color: var(--tnzi-success, #18a058); }
.t-stat-card__value--warning { color: var(--tnzi-warning, #f0a020); }
.t-stat-card__value--error { color: var(--tnzi-error, #d03050); }
.t-stat-card__number {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-stat-card__suffix {
  font-size: 12px;
  font-weight: 400;
  color: var(--tnzi-base-text-muted, #888);
}
</style>
