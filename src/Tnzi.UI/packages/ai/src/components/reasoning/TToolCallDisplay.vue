<script setup lang="ts">
/**
 * TToolCallDisplay — Tool call status indicator
 *
 * Shows the current state of an AI tool call with icon,
 * readable name, and optional duration.
 */

import { computed } from 'vue';
import { Icon } from '@iconify/vue';
import { cn } from '@/lib/utils';
import type { ToolCallInfo } from '@/composables/useChat';
import { Badge } from '@/primitives/ui/badge';
import TLoader from '../streaming/TLoader.vue';

const props = defineProps<{
  toolCall: ToolCallInfo;
}>();

type ToolStatus = 'pending' | 'running' | 'completed' | 'failed';

const status = computed<ToolStatus>(() => {
  if (props.toolCall.output === null || props.toolCall.output === undefined) {
    if (props.toolCall.durationMs != null) return 'running';
    return 'pending';
  }
  // If output starts with "Error" or contains error indicators, treat as failed
  if (typeof props.toolCall.output === 'string' && props.toolCall.output.startsWith('Error')) {
    return 'failed';
  }
  return 'completed';
});

/** Convert snake_case / camelCase tool name to readable form. */
const readableName = computed(() => {
  return props.toolCall.name
    .replace(/_/g, ' ')
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .replace(/\b\w/g, (c) => c.toUpperCase());
});

const durationLabel = computed(() => {
  if (!props.toolCall.durationMs) return null;
  const seconds = (props.toolCall.durationMs / 1000).toFixed(1);
  return `${seconds}s`;
});
</script>

<template>
  <div
    :class="cn(
      'flex items-center gap-2 rounded-md px-2.5 py-1.5 text-sm bg-ai-tool-call-bg',
    )"
  >
    <!-- Status icon -->
    <TLoader v-if="status === 'running'" :size="14" />
    <Icon
      v-else-if="status === 'completed'"
      icon="lucide:check-circle-2"
      class="size-3.5 text-ai-node-completed shrink-0"
    />
    <Icon
      v-else-if="status === 'failed'"
      icon="lucide:x-circle"
      class="size-3.5 text-ai-node-failed shrink-0"
    />
    <Icon
      v-else
      icon="lucide:clock"
      class="size-3.5 text-muted-foreground shrink-0"
    />

    <!-- Tool name -->
    <Badge variant="secondary" class="truncate font-medium">{{ readableName }}</Badge>

    <!-- Duration -->
    <span
      v-if="durationLabel"
      class="ml-auto shrink-0 text-xs text-muted-foreground"
    >
      {{ durationLabel }}
    </span>
  </div>
</template>
