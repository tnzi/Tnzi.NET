<script setup lang="ts">
/**
 * TWorkflowToolbar — Node action toolbar
 */

import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
import { Button } from '@/primitives/ui/button';
import { Tooltip, TooltipContent, TooltipTrigger } from '@/primitives/ui/tooltip';

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
      <TooltipTrigger as-child>
        <Button variant="ghost" size="icon-sm" @click="$emit('add')">
          <Icon icon="lucide:plus" class="size-4" />
        </Button>
      </TooltipTrigger>
      <TooltipContent>{{ t.workflow.addNode }}</TooltipContent>
    </Tooltip>
    <Tooltip>
      <TooltipTrigger as-child>
        <Button variant="ghost" size="icon-sm" @click="$emit('delete')">
          <Icon icon="lucide:trash-2" class="size-4" />
        </Button>
      </TooltipTrigger>
      <TooltipContent>{{ t.workflow.deleteNode }}</TooltipContent>
    </Tooltip>
    <Tooltip>
      <TooltipTrigger as-child>
        <Button variant="ghost" size="icon-sm" @click="$emit('connect')">
          <Icon icon="lucide:link" class="size-4" />
        </Button>
      </TooltipTrigger>
      <TooltipContent>{{ t.workflow.connect }}</TooltipContent>
    </Tooltip>
    <Tooltip>
      <TooltipTrigger as-child>
        <Button variant="ghost" size="icon-sm" @click="$emit('run')">
          <Icon icon="lucide:play" class="size-4 text-primary" />
        </Button>
      </TooltipTrigger>
      <TooltipContent>{{ t.workflow.run }}</TooltipContent>
    </Tooltip>
  </div>
</template>
