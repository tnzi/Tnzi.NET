<script setup lang="ts">
/**
 * TaskItem — Subtask display with left vertical border and file badges
 */

import { ref } from 'vue';
import { Icon } from '@iconify/vue';

const props = withDefaults(defineProps<{
  /** Task title. */
  title: string;
  /** Files associated with this task. */
  files?: string[];
  /** Whether the task is collapsible. */
  collapsible?: boolean;
  /** Default open state. */
  defaultOpen?: boolean;
}>(), {
  files: () => [],
  collapsible: true,
  defaultOpen: true,
});

const isOpen = ref(props.defaultOpen);
</script>

<template>
  <div v-if="collapsible">
    <button
      type="button"
      class="t-task-item__trigger"
      @click="isOpen = !isOpen"
    >
      <Icon icon="lucide:search" class="size-3.5 shrink-0 t-task-item__muted" />
      <span class="flex-1 text-left font-medium truncate">{{ title }}</span>
      <Icon
        icon="lucide:chevron-down"
        class="size-3.5 shrink-0 t-task-item__muted t-task-item__chevron"
        :class="{ 't-task-item__chevron--open': isOpen }"
      />
    </button>
    <div v-show="isOpen" class="t-task-item__body">
      <slot />
      <div v-if="files.length > 0" class="flex flex-wrap gap-1 mt-1">
        <span
          v-for="file in files"
          :key="file"
          class="t-task-item__file-badge"
        >{{ file }}</span>
      </div>
    </div>
  </div>
  <div v-else class="flex items-start gap-2 py-1 text-sm t-task-item__muted">
    <slot />
    <div v-if="files.length > 0" class="flex flex-wrap gap-1">
      <span
        v-for="file in files"
        :key="file"
        class="t-task-item__file-badge"
      >{{ file }}</span>
    </div>
  </div>
</template>

<style scoped>
.t-task-item__trigger {
  display: flex;
  width: 100%;
  align-items: center;
  gap: 8px;
  padding: 6px 8px;
  font-size: 14px;
  border-radius: 6px;
  background: none;
  border: none;
  cursor: pointer;
  transition: background-color 0.15s;
}
.t-task-item__trigger:hover { background-color: var(--tnzi-hover-bg, rgba(0,0,0,0.04)); }
.t-task-item__muted { color: var(--tnzi-base-text-muted); }
.t-task-item__chevron { transition: transform 0.2s; }
.t-task-item__chevron--open { transform: rotate(180deg); }
.t-task-item__body {
  margin-left: 8px;
  border-left: 2px solid var(--tnzi-border);
  padding: 8px 16px;
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.t-task-item__file-badge {
  display: inline-block;
  font-family: monospace;
  font-size: 11px;
  padding: 1px 6px;
  border-radius: 4px;
  background-color: var(--tnzi-container-bg);
  border: 1px solid var(--tnzi-border);
  color: var(--tnzi-base-text-muted);
}
</style>
