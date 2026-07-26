<template>
  <div class="t-aging" role="img" :aria-label="ariaLabel">
    <div v-if="total > 0" class="t-aging__track">
      <div
        v-for="seg in segments"
        :key="seg.key"
        class="t-aging__seg"
        :class="`t-aging__seg--${seg.tone}`"
        :style="{ width: `${seg.percent}%` }"
        :title="`${seg.label}: ${formatMoney(seg.value, { currency })}`"
      />
    </div>
    <div v-else class="t-aging__track t-aging__track--empty" />

    <ul class="t-aging__legend">
      <li v-for="seg in segments" :key="seg.key" class="t-aging__legend-item">
        <span class="t-aging__dot" :class="`t-aging__seg--${seg.tone}`" aria-hidden="true" />
        <span class="t-aging__legend-label">{{ seg.label }}</span>
        <TMoney class="t-aging__legend-value" :value="seg.value" :currency="currency" size="sm" zero-dash />
      </li>
    </ul>
  </div>
</template>

<script setup lang="ts">
/**
 * `TAgingBar` - how a party's outstanding balance is distributed across the
 * aging buckets.
 *
 * A single "owes 12,400" number does not tell a collections person anything;
 * "9,000 of it is 90+ days" does. The bar is the shape, the legend is the
 * verifiable figures (the bar alone would be colour-as-sole-information, which
 * fails AODA).
 *
 * Buckets are passed in rather than computed here: the caller gets them from
 * the party summary, which shares the aging report's calculation. This
 * component must never re-derive them.
 */
import { computed } from 'vue'
import TMoney from './TMoney.vue'
import { formatMoney } from '../../utils/finance-format'

export interface AgingBuckets {
  current: number
  days1To30: number
  days31To60: number
  days61To90: number
  over90: number
  total: number
}

const props = withDefaults(
  defineProps<{
    buckets: AgingBuckets
    currency?: string | null
    /** i18n lookup relative to `finance.aging.*`. */
    translate?: (key: string) => string
  }>(),
  {},
)

const FALLBACK: Record<string, string> = {
  current: 'Current',
  d1to30: '1-30',
  d31to60: '31-60',
  d61to90: '61-90',
  over90: '90+',
}

function label(key: string): string {
  const translated = props.translate?.(`aging.${key}`)
  if (translated && !translated.includes(`aging.${key}`)) return translated
  return FALLBACK[key] ?? key
}

/** Only positive buckets take width; a credit balance has no bar to draw. */
const total = computed(() =>
  [props.buckets.current, props.buckets.days1To30, props.buckets.days31To60, props.buckets.days61To90, props.buckets.over90]
    .reduce((sum, v) => sum + Math.max(0, v), 0),
)

const segments = computed(() => {
  const raw = [
    { key: 'current', tone: 'ok', value: props.buckets.current },
    { key: 'd1to30', tone: 'warn1', value: props.buckets.days1To30 },
    { key: 'd31to60', tone: 'warn2', value: props.buckets.days31To60 },
    { key: 'd61to90', tone: 'warn3', value: props.buckets.days61To90 },
    { key: 'over90', tone: 'bad', value: props.buckets.over90 },
  ]
  return raw.map((s) => ({
    ...s,
    label: label(s.key),
    percent: total.value > 0 ? (Math.max(0, s.value) / total.value) * 100 : 0,
  }))
})

const currency = computed(() => props.currency)

const ariaLabel = computed(() =>
  segments.value.map((s) => `${s.label} ${formatMoney(s.value, { currency: props.currency, accounting: false })}`).join(', '),
)
</script>

<style scoped>
.t-aging {
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-width: 0;
}

.t-aging__track {
  display: flex;
  height: 8px;
  border-radius: 4px;
  overflow: hidden;
  background: var(--tnzi-border);
}

.t-aging__track--empty {
  opacity: 0.5;
}

.t-aging__seg {
  height: 100%;
}

.t-aging__seg--ok {
  background: var(--tnzi-success);
}

.t-aging__seg--warn1 {
  background: color-mix(in srgb, var(--tnzi-warning) 55%, var(--tnzi-success));
}

.t-aging__seg--warn2 {
  background: var(--tnzi-warning);
}

.t-aging__seg--warn3 {
  background: color-mix(in srgb, var(--tnzi-error) 55%, var(--tnzi-warning));
}

.t-aging__seg--bad {
  background: var(--tnzi-error);
}

.t-aging__legend {
  display: flex;
  gap: 14px;
  flex-wrap: wrap;
  margin: 0;
  padding: 0;
  list-style: none;
}

.t-aging__legend-item {
  display: flex;
  align-items: baseline;
  gap: 5px;
  font-size: 12px;
}

.t-aging__dot {
  width: 8px;
  height: 8px;
  border-radius: 2px;
  display: inline-block;
  align-self: center;
}

.t-aging__legend-label {
  color: var(--tnzi-base-text-muted);
}

.t-aging__legend-value {
  font-weight: 600;
}
</style>
