<template>
  <!--
    TKpiCard — unified KPI statistic card (white NCard, small, borderless).
    Renamed from TStatCard in the 2026-06 component audit to avoid a name
    collision with @tnzi/ui's globally-registered <TStatCard> (a different
    NStatistic-based component). Pairs with TKpiRow for the responsive grid.
    Layout: optional icon in a light rounded block on the left; label (12px,
    muted) above the value (24px, semibold). Numbers animate via
    NNumberAnimation (default on); strings render verbatim; `null` renders
    an em dash. `tone` colours the value (and the icon block) with the
    standard status palette. Pages pass already-translated `label` text
    (the page owns t()).
  -->
  <NCard
    size="small"
    :bordered="false"
    class="t-stat-card"
    :class="{ 't-stat-card--clickable': clickable }"
    :role="clickable ? 'button' : undefined"
    :tabindex="clickable ? 0 : undefined"
    @click="clickable && handleClick()"
    @keydown.enter="clickable && handleClick()"
  >
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
    <!-- Full-width band below the value row — for a progress bar, a status
         line or a mini-chart that a same-baseline `#extra` can't host. Keeps
         "KPI + progress/trend" cards unified instead of hand-rolling NCard. -->
    <div v-if="$slots.footer" class="t-stat-card__footer"><slot name="footer" /></div>
  </NCard>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { NCard, NNumberAnimation } from 'naive-ui'
import { useRouter, type RouteLocationRaw } from 'vue-router'
import { TSvgIcon } from '@tnzi/ui'

export type TKpiCardTone = 'default' | 'success' | 'warning' | 'error'

export interface TKpiCardProps {
  /** Already-translated label text (the page side owns t()). */
  label: string
  /** `null` renders an em dash; numbers animate (see `animated`); strings render verbatim. */
  value: number | string | null
  /** Trailing unit, e.g. `'%'`, `'ms'`. Hidden while the value is null. */
  suffix?: string
  /** mdi:* icon rendered in a light rounded block on the left. */
  icon?: string
  /** Value (and icon block) colour. */
  tone?: TKpiCardTone
  /** Animate numeric values via NNumberAnimation (default true). */
  animated?: boolean
  /**
   * Vue-router target. When set the whole card becomes a click target (cursor +
   * hover lift + `role="button"`) and navigates on click — so a KPI can act as a
   * drill-in link ("Active files → /admin/matters") without hand-rolling a
   * clickable card. Also emits `click` for router-less handling.
   */
  to?: RouteLocationRaw
  /**
   * Force the interactive affordance (cursor + hover lift + `click` emit) without
   * a router target — for consumers wiring their own `@click`. Ignored when `to`
   * is set (that already implies interactive).
   */
  interactive?: boolean
}

const props = withDefaults(defineProps<TKpiCardProps>(), {
  suffix: undefined,
  icon: undefined,
  tone: 'default',
  animated: true,
  to: undefined,
  interactive: false,
})

const emit = defineEmits<{
  /** Fired on click when the card is interactive (`to` set or `interactive`). */
  (e: 'click'): void
}>()

defineSlots<{
  /** Trailing content after the value (e.g. a status NTag), same baseline. */
  extra?: () => unknown
  /** Full-width band below the value (e.g. a progress bar / trend line). */
  footer?: () => unknown
}>()

// `useRouter()` is a plain inject — safe even without a router installed
// (returns undefined); navigation is guarded so router-less mounts (e.g. unit
// tests, non-interactive KPIs) never throw.
const router = useRouter()
const clickable = computed(() => props.to != null || props.interactive)

function handleClick(): void {
  emit('click')
  if (props.to != null && router) void router.push(props.to)
}

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
.t-stat-card--clickable {
  cursor: pointer;
  transition:
    transform var(--tnzi-admin-motion-duration-fast, 0.15s) ease,
    box-shadow var(--tnzi-admin-motion-duration-fast, 0.15s) ease;
}
.t-stat-card--clickable:hover {
  transform: translateY(-2px);
  box-shadow: 0 4px 12px rgb(0 0 0 / 0.08);
}
.t-stat-card--clickable:focus-visible {
  outline: 2px solid var(--tnzi-primary);
  outline-offset: 2px;
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
.t-stat-card__footer {
  margin-top: 10px;
}
</style>
