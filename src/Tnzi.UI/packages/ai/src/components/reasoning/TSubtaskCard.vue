<script setup lang="ts">
/**
 * TSubtaskCard — Subtask progress card
 *
 * Displays a subtask with status-based styling, border highlight,
 * and animated indicators for active states.
 */

import { Icon } from '@iconify/vue';
import { cn } from '@/lib/utils';
import { Badge } from '@/primitives/ui/badge';
import TLoader from '../streaming/TLoader.vue';
import TShimmer from '../streaming/TShimmer.vue';

export type SubtaskStatus = 'pending' | 'in_progress' | 'completed' | 'failed';

const props = defineProps<{
  title: string;
  description?: string;
  status: SubtaskStatus;
}>();

const borderClass: Record<SubtaskStatus, string> = {
  pending: 'border-border',
  in_progress: 'border-ai-node-active',
  completed: 'border-ai-node-completed',
  failed: 'border-ai-node-failed',
};

const badgeVariant: Record<SubtaskStatus, 'secondary' | 'default' | 'destructive' | 'outline'> = {
  pending: 'outline',
  in_progress: 'secondary',
  completed: 'default',
  failed: 'destructive',
};

const statusLabel: Record<SubtaskStatus, string> = {
  pending: 'Pending',
  in_progress: 'Running',
  completed: 'Done',
  failed: 'Failed',
};
</script>

<template>
  <div
    :class="cn(
      'rounded-lg border-l-2 bg-card px-3 py-2 transition-colors',
      borderClass[props.status],
    )"
  >
    <div class="flex items-start gap-2">
      <!-- Status icon -->
      <div class="mt-0.5 shrink-0">
        <TLoader v-if="props.status === 'in_progress'" :size="14" />
        <Icon
          v-else-if="props.status === 'completed'"
          icon="lucide:check-circle-2"
          class="size-3.5 text-ai-node-completed"
        />
        <Icon
          v-else-if="props.status === 'failed'"
          icon="lucide:x-circle"
          class="size-3.5 text-ai-node-failed"
        />
        <Icon
          v-else
          icon="lucide:circle-dashed"
          class="size-3.5 text-muted-foreground"
        />
      </div>

      <!-- Content -->
      <div class="min-w-0 flex-1">
        <div class="flex items-center gap-1.5">
          <p class="text-sm font-medium leading-tight">
            <TShimmer v-if="props.status === 'in_progress'">{{ props.title }}</TShimmer>
            <template v-else>{{ props.title }}</template>
          </p>
          <Badge :variant="badgeVariant[props.status]" class="text-[10px] px-1.5 py-0">
            {{ statusLabel[props.status] }}
          </Badge>
        </div>
        <p
          v-if="props.description"
          class="mt-0.5 text-xs text-muted-foreground"
        >
          {{ props.description }}
        </p>
      </div>
    </div>
  </div>
</template>
