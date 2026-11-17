<script setup lang="ts">
/**
 * TMessageBranch - Branch navigation
 *
 * Shows branch navigation arrows and current position (e.g. 1/3).
 * Only renders when there are multiple branches. The arrows stay pressable at
 * either end; which branch comes next (and whether it wraps) is the parent's.
 */

import TIconButton from '../controls/TIconButton.vue';
import { useAiI18n } from '../../i18n/index';
defineProps<{
  /** 1-based index. */
  currentBranch: number;
  totalBranches: number;
}>();

const emit = defineEmits<{
  prev: [];
  next: [];
}>();

const t = useAiI18n();
</script>

<template>
  <div v-if="totalBranches > 1" class="t-message-branch">
    <TIconButton icon="lucide:chevron-left" size="xs" :label="t.chat.previousVersion" @click="emit('prev')" />
    <span class="t-message-branch__position">{{ currentBranch }}/{{ totalBranches }}</span>
    <TIconButton icon="lucide:chevron-right" size="xs" :label="t.chat.nextVersion" @click="emit('next')" />
  </div>
</template>

<style scoped>
.t-message-branch {
  display: flex;
  align-items: center;
  gap: 2px;
  font-size: 12px;
  color: var(--tnzi-ai-text-secondary);
}
.t-message-branch__position {
  font-variant-numeric: tabular-nums;
}
</style>
