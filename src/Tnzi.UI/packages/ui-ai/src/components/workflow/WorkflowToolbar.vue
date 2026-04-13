<script setup lang="ts">
/**
 * WorkflowToolbar — Node action toolbar
 */

import { Button, Tooltip } from '../../primitives';
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
  <div class="flex items-center gap-1 rounded-lg border bg-background p-1 shadow-sm">
    <Tooltip>
      <template #trigger>
        <Button variant="ghost" size="icon-sm" @click="$emit('add')">
          <Icon icon="lucide:plus" class="size-4" />
        </Button>
      </template>
      {{ t.workflow.addNode }}
    </Tooltip>
    <Tooltip>
      <template #trigger>
        <Button variant="ghost" size="icon-sm" @click="$emit('delete')">
          <Icon icon="lucide:trash-2" class="size-4" />
        </Button>
      </template>
      {{ t.workflow.deleteNode }}
    </Tooltip>
    <Tooltip>
      <template #trigger>
        <Button variant="ghost" size="icon-sm" @click="$emit('connect')">
          <Icon icon="lucide:link" class="size-4" />
        </Button>
      </template>
      {{ t.workflow.connect }}
    </Tooltip>
    <Tooltip>
      <template #trigger>
        <Button variant="ghost" size="icon-sm" @click="$emit('run')">
          <Icon icon="lucide:play" class="size-4 text-primary" />
        </Button>
      </template>
      {{ t.workflow.run }}
    </Tooltip>
  </div>
</template>
