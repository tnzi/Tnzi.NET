<script setup lang="ts">
/**
 * TWorkflowControls - Zoom/pan control buttons
 */

import { NButton } from 'naive-ui';
import { useVueFlow } from '@vue-flow/core';
import { Icon } from '@iconify/vue';
const { zoomIn, zoomOut, fitView } = useVueFlow();
</script>

<template>
  <div class="t-wf-controls">
    <NButton text size="small" @click="zoomIn()">
      <template #icon><Icon icon="lucide:plus" class="size-4" /></template>
    </NButton>
    <NButton text size="small" @click="zoomOut()">
      <template #icon><Icon icon="lucide:minus" class="size-4" /></template>
    </NButton>
    <NButton text size="small" @click="fitView()">
      <template #icon><Icon icon="lucide:maximize-2" class="size-4" /></template>
    </NButton>
  </div>
</template>

<style scoped>
.t-wf-controls {
  display: flex;
  align-items: center;
  gap: 4px;
  border-radius: 8px;
  border: 1px solid var(--tnzi-border);
  background-color: var(--tnzi-container-bg);
  padding: 4px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.1);
}
</style>
