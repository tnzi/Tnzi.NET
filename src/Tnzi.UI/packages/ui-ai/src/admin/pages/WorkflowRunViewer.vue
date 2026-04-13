<script setup lang="ts">
/**
 * WorkflowRunViewer — Read-only workflow canvas showing execution status
 */

import { Badge } from '../../primitives';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
import type { Node, Edge } from '@vue-flow/core';
import WorkflowCanvas from '@/components/workflow/WorkflowCanvas.vue';

const t = useAiI18n();

defineProps<{
  nodes: Node[];
  edges: Edge[];
  runStatus?: 'running' | 'completed' | 'failed' | 'paused';
  isLoading?: boolean;
}>();

const emit = defineEmits<{
  refresh: [];
  'view-logs': [];
}>();
</script>

<template>
  <div class="flex h-full flex-col">
    <div class="flex items-center justify-between border-b px-4 py-2">
      <div class="flex items-center gap-2">
        <h2 class="text-sm font-semibold">{{ t.admin.workflows }}</h2>
        <Badge v-if="runStatus" variant="outline" class="text-[10px]">
          <Icon
            v-if="runStatus === 'running'"
            icon="lucide:loader-2"
            class="mr-1 size-3 animate-spin"
          />
          {{ runStatus }}
        </Badge>
      </div>
      <div class="flex items-center gap-1">
        <button class="text-xs text-muted-foreground hover:underline" @click="emit('view-logs')">
          Logs
        </button>
      </div>
    </div>
    <div class="flex-1 relative">
      <div v-if="isLoading" class="absolute inset-0 flex items-center justify-center text-muted-foreground">
        {{ t.common.loading }}
      </div>
      <WorkflowCanvas
        v-else
        :nodes="nodes"
        :edges="edges"
      />
    </div>
  </div>
</template>
