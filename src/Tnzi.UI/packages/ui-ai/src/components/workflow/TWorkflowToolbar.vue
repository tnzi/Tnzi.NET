<script setup lang="ts">
/**
 * TWorkflowToolbar - Node action toolbar
 */

import { NButton, NTooltip } from 'naive-ui';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
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
    <NTooltip>
      <template #trigger>
        <NButton text size="small" @click="$emit('add')">
          <template #icon><Icon icon="lucide:plus" class="size-4" /></template>
        </NButton>
      </template>
      {{ t.workflow.addNode }}
    </NTooltip>
    <NTooltip>
      <template #trigger>
        <NButton text size="small" @click="$emit('delete')">
          <template #icon><Icon icon="lucide:trash-2" class="size-4" /></template>
        </NButton>
      </template>
      {{ t.workflow.deleteNode }}
    </NTooltip>
    <NTooltip>
      <template #trigger>
        <NButton text size="small" @click="$emit('connect')">
          <template #icon><Icon icon="lucide:link" class="size-4" /></template>
        </NButton>
      </template>
      {{ t.workflow.connect }}
    </NTooltip>
    <NTooltip>
      <template #trigger>
        <NButton text size="small" @click="$emit('run')">
          <template #icon>
            <Icon icon="lucide:play" class="size-4 t-wf-toolbar__run" />
          </template>
        </NButton>
      </template>
      {{ t.workflow.run }}
    </NTooltip>
  </div>
</template>

<style scoped>
.t-wf-toolbar {
  display: flex;
  align-items: center;
  gap: 4px;
  border-radius: 8px;
  border: 1px solid var(--tnzi-border);
  background-color: var(--tnzi-container-bg);
  padding: 4px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.1);
}
.t-wf-toolbar__run { color: var(--tnzi-primary); }
</style>
