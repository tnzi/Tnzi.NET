<script setup lang="ts">
/**
 * TWorkflowNode - Card-based graph node with handles
 *
 * The default node of TWorkflowCanvas. Its look lives in its own scoped
 * styles, so it renders the same wherever the canvas is loaded.
 */

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
  <div
    class="t-wf-node"
    :class="`t-wf-node--${status ?? 'pending'}`"
  >
    <Handle v-if="targetHandle" type="target" :position="Position.Left" class="t-wf-handle" />
    <div class="t-wf-node__label">{{ label }}</div>
    <div v-if="description" class="t-wf-node__desc">{{ description }}</div>
    <div v-if="$slots.content || $slots.default" class="t-wf-node__section">
      <slot name="content"><slot /></slot>
    </div>
    <div v-if="$slots.footer" class="t-wf-node__section">
      <slot name="footer" />
    </div>
    <Handle v-if="sourceHandle" type="source" :position="Position.Right" class="t-wf-handle" />
  </div>
</template>

<style scoped>
/* The package's card (surface, one hairline), lifted off the canvas. */
.t-wf-node {
  position: relative;
  box-sizing: border-box;
  min-width: 180px;
  max-width: 250px;
  padding: 10px 12px;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 10px;
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text);
  font-family: var(--tnzi-ai-font-body, inherit);
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.12);
  transition: border-color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
              box-shadow var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-wf-node--active {
  border-color: var(--tnzi-ai-node-active);
  box-shadow: 0 0 0 2px color-mix(in srgb, var(--tnzi-ai-node-active) 20%, transparent);
}
.t-wf-node--completed { border-color: var(--tnzi-ai-node-completed); }
.t-wf-node--failed {
  border-color: var(--tnzi-ai-node-failed);
  box-shadow: 0 0 0 2px color-mix(in srgb, var(--tnzi-ai-node-failed) 20%, transparent);
}
.t-wf-node__label {
  font-size: 12px;
  font-weight: 500;
  line-height: 1.4;
  overflow-wrap: anywhere;
}
.t-wf-node__desc {
  margin-top: 2px;
  font-size: 11px;
  line-height: 1.4;
  color: var(--tnzi-ai-text-secondary);
}
.t-wf-node__section { margin-top: 8px; }
.t-wf-handle {
  background-color: var(--tnzi-ai-accent) !important;
  border-color: var(--tnzi-ai-surface) !important;
  width: 12px !important;
  height: 12px !important;
}
</style>
