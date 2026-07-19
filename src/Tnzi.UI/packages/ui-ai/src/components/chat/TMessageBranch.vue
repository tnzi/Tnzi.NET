<script setup lang="ts">
/**
 * TMessageBranch — Branch navigation
 *
 * Shows branch navigation arrows and current position (e.g. 1/3).
 * Only renders when there are multiple branches.
 */

import { NButton } from 'naive-ui';
import { Icon } from '@iconify/vue';
const props = defineProps<{
  /** 1-based index. */
  currentBranch: number;
  totalBranches: number;
}>();

const emit = defineEmits<{
  prev: [];
  next: [];
}>();
</script>

<template>
  <div
    v-if="totalBranches > 1"
    class="flex items-center gap-1 text-xs text-muted-foreground"
  >
    <NButton quaternary size="tiny" aria-label="Previous branch" @click="emit('prev')">
      <template #icon><Icon icon="lucide:chevron-left" /></template>
    </NButton>

    <span class="tabular-nums">{{ currentBranch }}/{{ totalBranches }}</span>

    <NButton quaternary size="tiny" aria-label="Next branch" @click="emit('next')">
      <template #icon><Icon icon="lucide:chevron-right" /></template>
    </NButton>
  </div>
</template>
