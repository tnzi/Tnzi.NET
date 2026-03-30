<script setup lang="ts">
/**
 * ChatHeader — Top bar with agent info, token usage, export, and settings
 */

import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
import { Button } from '@/primitives/ui/button';
import { Tooltip, TooltipContent, TooltipTrigger } from '@/primitives/ui/tooltip';
import { Separator } from '@/primitives/ui/separator';

const t = useAiI18n();

defineProps<{
  /** Current agent name. */
  agentName?: string;
  /** Thread title. */
  title?: string;
}>();

defineEmits<{
  export: [];
  settings: [];
  'toggle-sidebar': [];
}>();
</script>

<template>
  <div class="flex items-center gap-2 border-b px-4 py-2 h-12">
    <slot name="left">
      <Button variant="ghost" size="icon-sm" @click="$emit('toggle-sidebar')">
        <Icon icon="lucide:panel-left" class="size-4" />
      </Button>
      <Separator orientation="vertical" class="h-5" />
      <div class="flex items-center gap-2 min-w-0">
        <Icon v-if="agentName" icon="lucide:bot" class="size-4 text-primary shrink-0" />
        <span class="text-sm font-medium truncate">{{ title ?? agentName ?? '' }}</span>
      </div>
    </slot>

    <div class="flex-1" />

    <slot name="right">
      <Tooltip>
        <TooltipTrigger as-child>
          <Button variant="ghost" size="icon-sm" @click="$emit('export')">
            <Icon icon="lucide:download" class="size-4" />
          </Button>
        </TooltipTrigger>
        <TooltipContent>{{ t.artifact.download }}</TooltipContent>
      </Tooltip>
      <Tooltip>
        <TooltipTrigger as-child>
          <Button variant="ghost" size="icon-sm" @click="$emit('settings')">
            <Icon icon="lucide:settings" class="size-4" />
          </Button>
        </TooltipTrigger>
        <TooltipContent>{{ t.admin.settings }}</TooltipContent>
      </Tooltip>
    </slot>
  </div>
</template>
