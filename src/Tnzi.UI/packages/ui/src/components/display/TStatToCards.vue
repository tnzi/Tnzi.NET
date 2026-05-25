<script setup lang="ts">
/**
 * `TStatToCards` — declarative statistics-card grid for admin dashboards.
 *
 * Replaces the hand-rolled `<Grid><Card><Statistic /></Card>...</Grid>`
 * boilerplate that PaymentOrder and AgentDetail repeat. Auto-formats
 * values, supports trend deltas, and exposes a refresh button.
 *
 * @example
 * ```vue
 * <TStatToCards
 *   :stats="[
 *     { label: 'Total Revenue', value: stats.totalRevenue, unit: 'USD' },
 *     { label: 'Order Count', value: stats.totalTransactions },
 *     { label: 'Avg Amount', value: avg, precision: 2 },
 *     { label: 'Paid %', value: percent, suffix: '%' },
 *   ]"
 *   :loading="loadingStats"
 *   @refresh="refreshStats"
 * />
 * ```
 */
import { computed } from 'vue'
import { NCard, NStatistic, NGrid, NGridItem, NButton, NSkeleton } from 'naive-ui'
import { Icon } from '@iconify/vue'

export interface StatCard {
  label: string
  value: number | string | null | undefined
  /** Optional unit prefix (e.g. `'USD'`, `'¥'`). */
  unit?: string
  /** Optional suffix (e.g. `'%'`, `' / sec'`). */
  suffix?: string
  /** Decimal places to round number values to. */
  precision?: number
  /** Optional trend delta (positive=green, negative=red). */
  trend?: number
  /** Optional iconify icon prefixed to label. */
  icon?: string
}

interface Props {
  stats: StatCard[]
  loading?: boolean
  cols?: number
  /** Show refresh button in top-right. */
  showRefresh?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  loading: false,
  cols: 4,
  showRefresh: true,
})

const emit = defineEmits<{ refresh: [] }>()

function formatValue(card: StatCard): string {
  if (card.value === null || card.value === undefined || card.value === '') return '—'
  if (typeof card.value === 'string') return card.value
  if (Number.isFinite(card.value)) {
    const num = card.value as number
    return card.precision !== undefined
      ? num.toFixed(card.precision)
      : num.toLocaleString()
  }
  return String(card.value)
}

const cardCount = computed(() => props.stats.length)
</script>

<template>
  <div class="t-stat-cards">
    <div v-if="showRefresh" class="t-stat-cards__toolbar">
      <NButton
        size="tiny"
        tertiary
        :loading="loading"
        @click="emit('refresh')"
      >
        <template #icon>
          <Icon icon="mdi:refresh" width="14" height="14" />
        </template>
      </NButton>
    </div>
    <NGrid :cols="Math.min(cols, cardCount)" :x-gap="12" :y-gap="12">
      <NGridItem v-for="(card, i) in stats" :key="`${card.label}-${i}`">
        <NCard class="t-stat-cards__card" :bordered="false">
          <NSkeleton v-if="loading" text :repeat="2" />
          <template v-else>
            <NStatistic :label="card.label">
              <template v-if="card.icon" #prefix>
                <Icon :icon="card.icon" width="16" height="16" />
              </template>
              <span class="t-stat-cards__value">{{ formatValue(card) }}</span>
              <template v-if="card.suffix" #suffix>
                <span class="t-stat-cards__suffix">{{ card.suffix }}</span>
              </template>
            </NStatistic>
            <div
              v-if="typeof card.trend === 'number' && Number.isFinite(card.trend)"
              class="t-stat-cards__trend"
              :class="{
                't-stat-cards__trend--up': card.trend > 0,
                't-stat-cards__trend--down': card.trend < 0,
              }"
            >
              <Icon
                :icon="card.trend > 0 ? 'mdi:arrow-up-bold' : card.trend < 0 ? 'mdi:arrow-down-bold' : 'mdi:minus'"
                width="12"
                height="12"
              />
              <span>{{ Math.abs(card.trend).toFixed(1) }}%</span>
            </div>
          </template>
        </NCard>
      </NGridItem>
    </NGrid>
  </div>
</template>

<style scoped>
.t-stat-cards {
  position: relative;
  margin-bottom: 16px;
}
.t-stat-cards__toolbar {
  position: absolute;
  top: -4px;
  right: 0;
  z-index: 1;
}
.t-stat-cards__card {
  background: var(--tnzi-container-bg);
  box-shadow: var(--tnzi-shadow-card, 0 1px 2px rgb(0 0 0 / 4%));
  border-radius: var(--tnzi-admin-radius-md, 8px);
}
.t-stat-cards__value {
  font-weight: 600;
  font-size: 22px;
}
.t-stat-cards__suffix {
  color: var(--tnzi-base-text-muted);
  font-size: 14px;
  margin-left: 2px;
}
.t-stat-cards__trend {
  display: inline-flex;
  align-items: center;
  gap: 2px;
  margin-top: 6px;
  font-size: 11px;
  color: var(--tnzi-base-text-muted);
}
.t-stat-cards__trend--up {
  color: var(--tnzi-success);
}
.t-stat-cards__trend--down {
  color: var(--tnzi-error);
}
</style>
