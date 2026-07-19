<script setup lang="ts">
/**
 * TToolCallDisplay - Tool call status indicator
 *
 * Shows the current state of an AI tool call with icon,
 * readable name, and optional duration.
 */

import { computed } from 'vue';
import { Icon } from '@iconify/vue';
import type { ToolCallInfo } from '@/composables/useChat';
import TStreamLoader from '../streaming/TStreamLoader.vue';

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
  <div class="t-tool-call">
    <!-- Status icon -->
    <TStreamLoader v-if="status === 'running'" :size="14" />
    <Icon
      v-else-if="status === 'completed'"
      icon="lucide:check-circle-2"
      class="size-3.5 t-tool-call__icon--completed shrink-0"
    />
    <Icon
      v-else-if="status === 'failed'"
      icon="lucide:x-circle"
      class="size-3.5 t-tool-call__icon--failed shrink-0"
    />
    <Icon
      v-else
      icon="lucide:clock"
      class="size-3.5 t-tool-call__icon--muted shrink-0"
    />

    <!-- Tool name -->
    <span class="t-tool-call__name truncate font-medium">{{ readableName }}</span>

    <!-- Duration -->
    <span
      v-if="durationLabel"
      class="ml-auto shrink-0 text-xs t-tool-call__duration"
    >
      {{ durationLabel }}
    </span>
  </div>
</template>

<style scoped>
.t-tool-call {
  display: flex;
  align-items: center;
  gap: 8px;
  border-radius: 6px;
  padding: 6px 10px;
  font-size: 14px;
  background-color: var(--tnzi-ai-tool-call-bg);
}
.t-tool-call__icon--completed { color: var(--tnzi-ai-node-completed); }
.t-tool-call__icon--failed { color: var(--tnzi-ai-node-failed); }
.t-tool-call__icon--muted { color: var(--tnzi-base-text-muted); }
.t-tool-call__name {
  font-size: 12px;
  padding: 2px 6px;
  border-radius: 4px;
  background-color: var(--tnzi-container-bg);
  border: 1px solid var(--tnzi-border);
  color: var(--tnzi-base-text);
}
.t-tool-call__duration { color: var(--tnzi-base-text-muted); }
</style>
