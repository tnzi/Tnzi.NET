<script setup lang="ts">
/**
 * TQuotaStatus - Quota remaining indicator with progress bar
 */

import { NProgress, NTooltip } from 'naive-ui';
import { computed } from 'vue';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '../../i18n/index';
const t = useAiI18n();

const props = defineProps<{
  used: number;
  limit: number;
  resetDate?: string;
}>();

const percentage = computed(() => props.limit > 0 ? Math.min(100, Math.round((props.used / props.limit) * 100)) : 0);
const remaining = computed(() => Math.max(0, props.limit - props.used));
const isExceeded = computed(() => props.used >= props.limit);
</script>

<template>
  <NTooltip>
    <template #trigger>
      <div class="flex items-center gap-2 text-xs">
        <Icon
          :icon="isExceeded ? 'lucide:alert-triangle' : 'lucide:gauge'"
          class="size-3.5"
          :class="isExceeded ? 't-quota__icon--exceeded' : 't-quota__icon'"
        />
        <NProgress
          type="line"
          :percentage="percentage"
          :show-indicator="false"
          :rail-style="{ height: '6px', width: '64px' }"
          :fill-style="{ height: '6px' }"
          :status="isExceeded ? 'error' : undefined"
        />
        <span
          class="tabular-nums"
          :class="{ 't-quota__pct--exceeded': isExceeded }"
        >{{ percentage }}%</span>
      </div>
    </template>
    <div class="space-y-1 text-xs">
      <div>{{ t.quota.usage }}: {{ used }} / {{ limit }}</div>
      <div>{{ t.quota.remaining }}: {{ remaining }}</div>
      <div v-if="resetDate">{{ t.quota.resetDate.replace('{date}', resetDate) }}</div>
      <div v-if="isExceeded" class="t-quota__exceeded-msg">{{ t.quota.exceeded }}</div>
    </div>
  </NTooltip>
</template>

<style scoped>
.t-quota__icon { color: var(--tnzi-base-text-muted); }
.t-quota__icon--exceeded { color: var(--tnzi-error); }
.t-quota__pct--exceeded { color: var(--tnzi-error); font-weight: 500; }
.t-quota__exceeded-msg { color: var(--tnzi-error); font-weight: 500; }
</style>
