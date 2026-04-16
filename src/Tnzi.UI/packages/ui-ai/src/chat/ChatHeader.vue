<script setup lang="ts">
/**
 * ChatHeader — Top bar with agent info, token usage, export, and settings
 */

import { NButton, NDivider, NTooltip } from 'naive-ui';
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
      <NButton quaternary size="small" @click="$emit('toggle-sidebar')">
        <template #icon><Icon icon="lucide:panel-left" /></template>
      </NButton>
      <NDivider vertical style="height: 20px; margin: 0 4px" />
      <div class="flex items-center gap-2 min-w-0">
        <Icon v-if="agentName" icon="lucide:bot" class="size-4 text-primary shrink-0" />
        <span class="text-sm font-medium truncate">{{ title ?? agentName ?? '' }}</span>
      </div>
    </slot>

    <div class="flex-1" />

    <slot name="right">
      <NTooltip>
        <template #trigger>
          <NButton quaternary size="small" @click="$emit('export')">
            <template #icon><Icon icon="lucide:download" /></template>
          </NButton>
        </template>
        {{ t.artifact.download }}
      </NTooltip>
      <NTooltip>
        <template #trigger>
          <NButton quaternary size="small" @click="$emit('settings')">
            <template #icon><Icon icon="lucide:settings" /></template>
          </NButton>
        </template>
        {{ t.admin.settings }}
      </NTooltip>
    </slot>
  </div>
</template>
