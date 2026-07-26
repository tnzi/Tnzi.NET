<script setup lang="ts">
/**
 * TCheckpoint - Workflow checkpoint marker
 */

import { NButton, NTooltip, NDivider } from 'naive-ui';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
const t = useAiI18n();

defineProps<{
  tooltip?: string;
}>();

defineEmits<{
  click: [];
}>();
</script>

<template>
  <div class="flex items-center gap-2 py-1">
    <NTooltip>
      <template #trigger>
        <NButton text size="small" @click="$emit('click')">
          <template #icon>
            <slot>
              <Icon icon="lucide:bookmark" class="size-4 t-checkpoint__icon" />
            </slot>
          </template>
          <span class="sr-only">{{ tooltip ?? t.checkpoint.label }}</span>
        </NButton>
      </template>
      {{ tooltip ?? t.checkpoint.label }}
    </NTooltip>
    <NDivider class="flex-1" />
  </div>
</template>

<style scoped>
.t-checkpoint__icon { color: var(--tnzi-base-text-muted); }
</style>
