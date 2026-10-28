<script setup lang="ts">
/**
 * TToolCallDisplay - Tool call status indicator
 *
 * Shows the current state of an AI tool call with icon,
 * readable name, and optional duration.
 */

import { computed } from 'vue';
import { Icon } from '@iconify/vue';
import type { ToolCallInfo } from '../../headless/useChat';
import TStreamLoader from '../streaming/TStreamLoader.vue';
import { formatToolDuration, readableToolName, toolCallState } from './tool-call-status';

const props = withDefaults(defineProps<{
  toolCall: ToolCallInfo;
  /** The message this call belongs to is still streaming. A call with no
   *  result yet is then running, not waiting. */
  streaming?: boolean;
}>(), {
  streaming: false,
});

/*
 * A live call is a name-only entry: the stream announces the tool when it
 * starts and reports duration and outcome together once it has run. So "no
 * result yet" means running while the turn streams, and never answered once
 * it has ended. The rules live in `tool-call-status.ts`, shared with the
 * generic card.
 */
const status = computed(() => toolCallState(props.toolCall, props.streaming));

/** Convert snake_case / camelCase tool name to readable form. */
const readableName = computed(() => readableToolName(props.toolCall.name));

const durationLabel = computed(() => formatToolDuration(props.toolCall.durationMs));
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

    <!-- Error -->
    <span
      v-if="status === 'failed' && toolCall.error"
      class="min-w-0 truncate text-xs t-tool-call__error"
      :title="toolCall.error"
    >
      {{ toolCall.error }}
    </span>

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
.t-tool-call__error { color: var(--tnzi-ai-node-failed); }
</style>
