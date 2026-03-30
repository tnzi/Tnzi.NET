<script setup lang="ts">
/**
 * TMessageBranch — Branch navigation
 *
 * Shows branch navigation arrows and current position (e.g. 1/3).
 * Only renders when there are multiple branches.
 */

import { Icon } from '@iconify/vue';
import { Button } from '@/primitives/ui/button';

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
    <Button
      variant="ghost"
      size="icon-sm"
      class="size-5"
      aria-label="Previous branch"
      @click="emit('prev')"
    >
      <Icon icon="lucide:chevron-left" class="size-3" />
    </Button>

    <span class="tabular-nums">{{ currentBranch }}/{{ totalBranches }}</span>

    <Button
      variant="ghost"
      size="icon-sm"
      class="size-5"
      aria-label="Next branch"
      @click="emit('next')"
    >
      <Icon icon="lucide:chevron-right" class="size-3" />
    </Button>
  </div>
</template>
