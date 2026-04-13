<script setup lang="ts">
/**
 * QuotaStatus — Quota remaining indicator with progress bar
 */

import { Progress, Tooltip } from '../../primitives';
import { computed } from 'vue';
import { Icon } from '@iconify/vue';
import { cn } from '@/lib/utils';
import { useAiI18n } from '@/locale/index';
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
  <Tooltip>
    <template #trigger>
      <div class="flex items-center gap-2 text-xs">
        <Icon :icon="isExceeded ? 'lucide:alert-triangle' : 'lucide:gauge'" :class="cn('size-3.5', isExceeded ? 'text-destructive' : 'text-muted-foreground')" />
        <Progress :percentage="percentage" :show-indicator="false" :rail-style="{ height: '6px', width: '64px' }" :fill-style="{ height: '6px' }" :status="isExceeded ? 'error' : undefined" />
        <span :class="cn('tabular-nums', isExceeded && 'text-destructive font-medium')">{{ percentage }}%</span>
      </div>
    </template>
    <div class="space-y-1 text-xs">
      <div>{{ t.quota.usage }}: {{ used }} / {{ limit }}</div>
      <div>{{ t.quota.remaining }}: {{ remaining }}</div>
      <div v-if="resetDate">{{ t.quota.resetDate.replace('{date}', resetDate) }}</div>
      <div v-if="isExceeded" class="text-destructive font-medium">{{ t.quota.exceeded }}</div>
    </div>
  </Tooltip>
</template>
