<script setup lang="ts">
/**
 * SubtaskCard — Subtask progress card
 *
 * Displays a subtask with status-based styling, border highlight,
 * and animated indicators for active states.
 */

import { Icon } from '@iconify/vue';
import StreamLoader from '../streaming/StreamLoader.vue';
import Shimmer from '../streaming/Shimmer.vue';

export type SubtaskStatus = 'pending' | 'in_progress' | 'completed' | 'failed';

const props = defineProps<{
  title: string;
  description?: string;
  status: SubtaskStatus;
}>();

const statusLabel: Record<SubtaskStatus, string> = {
  pending: 'Pending',
  in_progress: 'Running',
  completed: 'Done',
  failed: 'Failed',
};
</script>

<template>
  <div
    class="t-subtask"
    :class="`t-subtask--${props.status}`"
  >
    <div class="flex items-start gap-2">
      <!-- Status icon -->
      <div class="mt-0.5 shrink-0">
        <StreamLoader v-if="props.status === 'in_progress'" :size="14" />
        <Icon
          v-else-if="props.status === 'completed'"
          icon="lucide:check-circle-2"
          class="size-3.5 t-subtask__icon--completed"
        />
        <Icon
          v-else-if="props.status === 'failed'"
          icon="lucide:x-circle"
          class="size-3.5 t-subtask__icon--failed"
        />
        <Icon
          v-else
          icon="lucide:circle-dashed"
          class="size-3.5 t-subtask__icon--muted"
        />
      </div>

      <!-- Content -->
      <div class="min-w-0 flex-1">
        <div class="flex items-center gap-1.5">
          <p class="text-sm font-medium leading-tight">
            <Shimmer v-if="props.status === 'in_progress'">{{ props.title }}</Shimmer>
            <template v-else>{{ props.title }}</template>
          </p>
          <span class="t-subtask__badge" :class="`t-subtask__badge--${props.status}`">
            {{ statusLabel[props.status] }}
          </span>
        </div>
        <p
          v-if="props.description"
          class="mt-0.5 text-xs t-subtask__desc"
        >
          {{ props.description }}
        </p>
      </div>
    </div>
  </div>
</template>

<style scoped>
.t-subtask {
  border-radius: 8px;
  border-left: 2px solid var(--tnzi-border);
  background-color: var(--tnzi-container-bg);
  padding: 8px 12px;
  transition: border-color 0.2s;
}
.t-subtask--pending { border-left-color: var(--tnzi-border); }
.t-subtask--in_progress { border-left-color: var(--tnzi-ai-node-active); }
.t-subtask--completed { border-left-color: var(--tnzi-ai-node-completed); }
.t-subtask--failed { border-left-color: var(--tnzi-ai-node-failed); }
.t-subtask__icon--completed { color: var(--tnzi-ai-node-completed); }
.t-subtask__icon--failed { color: var(--tnzi-ai-node-failed); }
.t-subtask__icon--muted { color: var(--tnzi-base-text-muted); }
.t-subtask__desc { color: var(--tnzi-base-text-muted); }
.t-subtask__badge {
  font-size: 10px;
  padding: 1px 5px;
  border-radius: 4px;
}
.t-subtask__badge--pending {
  border: 1px solid var(--tnzi-border);
  color: var(--tnzi-base-text-muted);
}
.t-subtask__badge--in_progress {
  background-color: var(--tnzi-container-bg);
  border: 1px solid var(--tnzi-border);
  color: var(--tnzi-base-text);
}
.t-subtask__badge--completed {
  background-color: var(--tnzi-ai-node-completed);
  color: #fff;
}
.t-subtask__badge--failed {
  background-color: var(--tnzi-ai-node-failed);
  color: #fff;
}
</style>
