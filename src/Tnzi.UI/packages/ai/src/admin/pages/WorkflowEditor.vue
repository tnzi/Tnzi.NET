<script setup lang="ts">
/**
 * WorkflowEditor �� Workflow visual editor using TWorkflowCanvas + TWorkflowToolbar
 */

import { useAiI18n } from '@/locale/index';
import type { Node, Edge } from '@vue-flow/core';
import TWorkflowCanvas from '@/components/workflow/TWorkflowCanvas.vue';
import TWorkflowToolbar from '@/components/workflow/TWorkflowToolbar.vue';

const t = useAiI18n();

defineProps<{
  nodes: Node[];
  edges: Edge[];
  isLoading?: boolean;
}>();

const emit = defineEmits<{
  add: [];
  delete: [];
  connect: [connection: unknown];
  run: [];
  save: [];
  'nodes-change': [changes: unknown[]];
  'edges-change': [changes: unknown[]];
}>();
</script>

<template>
  <div class="flex h-full flex-col">
    <div class="flex items-center justify-between border-b px-4 py-2">
      <h2 class="text-sm font-semibold">{{ t.admin.workflows }}</h2>
      <TWorkflowToolbar
        @add="emit('add')"
        @delete="emit('delete')"
        @connect="emit('connect', $event)"
        @run="emit('run')"
      />
    </div>
    <div class="flex-1 relative">
      <div v-if="isLoading" class="absolute inset-0 flex items-center justify-center text-muted-foreground">
        {{ t.common.loading }}
      </div>
      <TWorkflowCanvas
        v-else
        :nodes="nodes"
        :edges="edges"
        @nodes-change="emit('nodes-change', $event)"
        @edges-change="emit('edges-change', $event)"
        @connect="emit('connect', $event)"
      />
    </div>
  </div>
</template>
