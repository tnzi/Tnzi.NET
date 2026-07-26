<script setup lang="ts">
/**
 * TWorkflowNode - Card-based graph node with handles
 */

import { NCard } from 'naive-ui';
import { Handle, Position } from '@vue-flow/core';

defineProps<{
  label: string;
  description?: string;
  status?: 'pending' | 'active' | 'completed' | 'failed';
  targetHandle?: boolean;
  sourceHandle?: boolean;
}>();
</script>

<template>
  <NCard
    size="small"
    class="t-wf-node"
    :class="`t-wf-node--${status ?? 'pending'}`"
  >
    <Handle v-if="targetHandle" type="target" :position="Position.Left" class="t-wf-handle" />
    <div class="text-xs font-medium">{{ label }}</div>
    <div v-if="description" class="text-[11px] t-wf-node__desc mt-0.5">{{ description }}</div>
    <div v-if="$slots.content || $slots.default" class="mt-2">
      <slot name="content"><slot /></slot>
    </div>
    <div v-if="$slots.footer" class="mt-2">
      <slot name="footer" />
    </div>
    <Handle v-if="sourceHandle" type="source" :position="Position.Right" class="t-wf-handle" />
  </NCard>
</template>

<style scoped>
.t-wf-node {
  min-width: 180px;
  max-width: 250px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.12);
}
.t-wf-node--pending { border-color: var(--tnzi-border); }
.t-wf-node--active {
  border-color: var(--tnzi-ai-node-active);
  box-shadow: 0 0 0 2px color-mix(in srgb, var(--tnzi-ai-node-active) 20%, transparent);
}
.t-wf-node--completed { border-color: var(--tnzi-ai-node-completed); }
.t-wf-node--failed {
  border-color: var(--tnzi-ai-node-failed);
  box-shadow: 0 0 0 2px color-mix(in srgb, var(--tnzi-ai-node-failed) 20%, transparent);
}
.t-wf-node__desc { color: var(--tnzi-base-text-muted); }
.t-wf-handle {
  background-color: var(--tnzi-primary) !important;
  border-color: var(--tnzi-container-bg) !important;
  width: 12px !important;
  height: 12px !important;
}
</style>
