<script setup lang="ts">
/**
 * TArtifact — Artifact panel container
 */

import { NButton, NTooltip } from 'naive-ui';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
const t = useAiI18n();

defineProps<{
  title: string;
  description?: string;
}>();

defineEmits<{
  close: [];
  download: [];
}>();
</script>

<template>
  <div class="flex h-full flex-col overflow-hidden rounded-lg border shadow-lg">
    <div class="flex items-center justify-between border-b bg-muted/50 px-4 py-2">
      <div class="min-w-0">
        <slot name="header">
          <div class="text-sm font-medium truncate">{{ title }}</div>
          <p v-if="description" class="text-xs text-muted-foreground truncate">{{ description }}</p>
        </slot>
      </div>
      <div class="flex items-center gap-1 shrink-0">
        <slot name="actions">
          <NTooltip>
            <template #trigger>
              <NButton quaternary size="small" @click="$emit('download')">
                <template #icon><Icon icon="lucide:download" /></template>
              </NButton>
            </template>
            {{ t.artifact.download }}
          </NTooltip>
        </slot>
        <NButton quaternary size="small" @click="$emit('close')">
          <template #icon><Icon icon="lucide:x" /></template>
        </NButton>
      </div>
    </div>
    <div class="flex-1 min-h-0 overflow-auto p-4">
      <slot />
    </div>
  </div>
</template>
