<script setup lang="ts">
/**
 * TCheckpoint — Workflow checkpoint marker
 */

import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
import { Separator } from '@/primitives/ui/separator';
import { Button } from '@/primitives/ui/button';
import { Tooltip, TooltipContent, TooltipTrigger } from '@/primitives/ui/tooltip';

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
      <TooltipTrigger as-child>
        <Button variant="ghost" size="icon-sm" @click="$emit('click')">
          <slot>
            <Icon icon="lucide:bookmark" class="size-4 text-muted-foreground" />
          </slot>
          <span class="sr-only">{{ tooltip ?? t.checkpoint.label }}</span>
        </Button>
      </TooltipTrigger>
      <TooltipContent>{{ tooltip ?? t.checkpoint.label }}</TooltipContent>
    </Tooltip>
    <Separator class="flex-1" />
  </div>
</template>
