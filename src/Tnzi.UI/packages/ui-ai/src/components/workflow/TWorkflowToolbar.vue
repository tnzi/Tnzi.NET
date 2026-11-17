<script setup lang="ts">
/**
 * TWorkflowToolbar - Node action toolbar
 */

import { useAiI18n } from '../../i18n/index';
import TIconButton from '../controls/TIconButton.vue';
const t = useAiI18n();

defineEmits<{
  add: [];
  delete: [];
  connect: [];
  run: [];
}>();
</script>

<template>
  <div class="t-wf-toolbar">
    <TIconButton icon="lucide:plus" :label="t.workflow.addNode" @click="$emit('add')" />
    <TIconButton icon="lucide:trash-2" :label="t.workflow.deleteNode" danger @click="$emit('delete')" />
    <TIconButton icon="lucide:link" :label="t.workflow.connect" @click="$emit('connect')" />
    <TIconButton icon="lucide:play" :label="t.workflow.run" class="t-wf-toolbar__run" @click="$emit('run')" />
  </div>
</template>

<style scoped>
.t-wf-toolbar {
  display: flex;
  align-items: center;
  gap: 2px;
  padding: 3px;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 10px;
  background: var(--tnzi-ai-surface);
  box-shadow: var(--tnzi-ai-popover-shadow);
}
/* Run is the toolbar's main action: the accent, readable as an icon colour. */
.t-wf-toolbar .t-wf-toolbar__run,
.t-wf-toolbar .t-wf-toolbar__run:hover:not(:disabled) {
  color: var(--tnzi-ai-accent-text);
}
</style>
