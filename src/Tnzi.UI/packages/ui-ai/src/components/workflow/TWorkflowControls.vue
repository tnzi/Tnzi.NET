<script setup lang="ts">
/**
 * TWorkflowControls - Zoom/pan control buttons
 */

import { useVueFlow } from '@vue-flow/core';
import { useAiI18n } from '../../i18n/index';
import TIconButton from '../controls/TIconButton.vue';
const t = useAiI18n();
const { zoomIn, zoomOut, fitView } = useVueFlow();
</script>

<template>
  <div class="t-wf-controls">
    <TIconButton icon="lucide:plus" :label="t.workflow.zoomIn" @click="zoomIn()" />
    <TIconButton icon="lucide:minus" :label="t.workflow.zoomOut" @click="zoomOut()" />
    <TIconButton icon="lucide:maximize-2" :label="t.workflow.fitView" @click="fitView()" />
  </div>
</template>

<style scoped>
.t-wf-controls {
  display: flex;
  align-items: center;
  gap: 2px;
  padding: 3px;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 10px;
  background: var(--tnzi-ai-surface);
  box-shadow: var(--tnzi-ai-popover-shadow);
}
</style>
