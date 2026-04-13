<script setup lang="ts">
/**
 * ChatHeader — Top bar with agent info, token usage, export, and settings
 */

import { Button, Separator, Tooltip } from '../primitives';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
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
        <template #trigger>
          <Button variant="ghost" size="icon-sm" @click="$emit('export')">
            <Icon icon="lucide:download" class="size-4" />
          </Button>
        </template>
        {{ t.artifact.download }}
      </Tooltip>
      <Tooltip>
        <template #trigger>
          <Button variant="ghost" size="icon-sm" @click="$emit('settings')">
            <Icon icon="lucide:settings" class="size-4" />
          </Button>
        </template>
        {{ t.admin.settings }}
      </Tooltip>
    </slot>
  </div>
</template>
