<script setup lang="ts">
/**
 * TQuotaStatus — Quota remaining indicator with progress bar
 */

import { computed } from 'vue';
import { Icon } from '@iconify/vue';
import { cn } from '@/lib/utils';
import { useAiI18n } from '@/locale/index';
import { Progress } from '@/primitives/ui/progress';
import { Tooltip, TooltipContent, TooltipTrigger } from '@/primitives/ui/tooltip';

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
    <TooltipTrigger as-child>
      <div class="flex items-center gap-2 text-xs">
        <Icon :icon="isExceeded ? 'lucide:alert-triangle' : 'lucide:gauge'" :class="cn('size-3.5', isExceeded ? 'text-destructive' : 'text-muted-foreground')" />
        <Progress :model-value="percentage" class="h-1.5 w-16" />
        <span :class="cn('tabular-nums', isExceeded && 'text-destructive font-medium')">{{ percentage }}%</span>
      </div>
    </TooltipTrigger>
    <TooltipContent>
      <div class="space-y-1 text-xs">
        <div>{{ t.quota.usage }}: {{ used }} / {{ limit }}</div>
        <div>{{ t.quota.remaining }}: {{ remaining }}</div>
        <div v-if="resetDate">{{ t.quota.resetDate.replace('{date}', resetDate) }}</div>
        <div v-if="isExceeded" class="text-destructive font-medium">{{ t.quota.exceeded }}</div>
      </div>
    </TooltipContent>
  </Tooltip>
</template>
