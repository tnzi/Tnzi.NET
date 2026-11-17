<script setup lang="ts">
/**
 * TQuotaStatus - Quota remaining indicator with progress bar
 *
 * A gauge icon, a 64px `TProgress` bar and the percentage used. The bar turns
 * `warning` from `QUOTA_WARNING_PERCENT` and `danger` once the limit is
 * reached. The figures (used / limit, remaining, reset date) are its tooltip
 * and, for a screen reader, its visually hidden text.
 */

import { computed } from 'vue';
import { Icon } from '@iconify/vue';
import { formatAiMessage, useAiI18n } from '../../i18n/index';
import { vTooltip } from '../../headless/tooltip';
import TProgress from '../controls/TProgress.vue';
const t = useAiI18n();

/** Share of the limit (percent) from which the bar warns. */
const QUOTA_WARNING_PERCENT = 90;

const props = defineProps<{
  used: number;
  limit: number;
  resetDate?: string;
}>();

const percentage = computed(() => props.limit > 0 ? Math.min(100, Math.round((props.used / props.limit) * 100)) : 0);
const remaining = computed(() => Math.max(0, props.limit - props.used));
const isExceeded = computed(() => props.used >= props.limit);
const tone = computed(() => {
  if (isExceeded.value) return 'danger';
  return percentage.value >= QUOTA_WARNING_PERCENT ? 'warning' : 'accent';
});

const details = computed(() => {
  const m = t.value.quota;
  return [
    `${m.usage}: ${props.used} / ${props.limit}`,
    `${m.remaining}: ${remaining.value}`,
    props.resetDate ? formatAiMessage(m.resetDate, { date: props.resetDate }) : '',
    isExceeded.value ? m.exceeded : '',
  ].filter(Boolean).join(' · ');
});
</script>

<template>
  <div v-tooltip="details" class="t-quota" :class="{ 'is-exceeded': isExceeded }">
    <Icon
      :icon="isExceeded ? 'lucide:alert-triangle' : 'lucide:gauge'"
      class="t-quota__icon"
      aria-hidden="true"
    />
    <TProgress :value="percentage" :tone="tone" width="64px" />
    <span class="t-quota__pct" aria-hidden="true">{{ percentage }}%</span>
    <span class="sr-only">{{ details }}</span>
  </div>
</template>

<style scoped>
.t-quota {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 12px;
  color: var(--tnzi-ai-text-secondary);
}
.t-quota__icon {
  flex-shrink: 0;
  font-size: 14px;
  color: var(--tnzi-ai-text-tertiary);
}
.t-quota__pct {
  font-variant-numeric: tabular-nums;
}
.t-quota.is-exceeded .t-quota__icon,
.t-quota.is-exceeded .t-quota__pct {
  color: var(--tnzi-ai-danger);
}
.t-quota.is-exceeded .t-quota__pct {
  font-weight: 500;
}
</style>
