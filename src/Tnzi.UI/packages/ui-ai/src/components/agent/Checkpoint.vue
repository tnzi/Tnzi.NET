<script setup lang="ts">
/**
 * Checkpoint — Workflow checkpoint marker
 */

import { Separator, Button, Tooltip } from '../../primitives';
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
    <Tooltip>
      <template #trigger>
        <Button variant="ghost" size="icon-sm" @click="$emit('click')">
          <slot>
            <Icon icon="lucide:bookmark" class="size-4 text-muted-foreground" />
          </slot>
          <span class="sr-only">{{ tooltip ?? t.checkpoint.label }}</span>
        </Button>
      </template>
      {{ tooltip ?? t.checkpoint.label }}
    </Tooltip>
    <Separator class="flex-1" />
  </div>
</template>
