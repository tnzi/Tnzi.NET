<script setup lang="ts">
/**
 * TAgentHandoff - Agent switch animation (A → B)
 */

import { Icon } from '@iconify/vue';

defineProps<{
  fromAgent: string;
  toAgent: string;
  fromIcon?: string;
  toIcon?: string;
}>();
</script>

<template>
  <div class="flex items-center gap-3 rounded-lg border border-ai-handoff-accent/30 bg-ai-handoff-accent/5 px-4 py-2.5 text-sm">
    <div class="flex items-center gap-1.5 text-tnzi-muted">
      <Icon :icon="fromIcon ?? 'lucide:bot'" class="size-4" />
      <span class="font-medium">{{ fromAgent }}</span>
    </div>
    <div class="flex items-center gap-1">
      <span class="h-px w-4 bg-ai-handoff-accent/50" />
      <Icon icon="lucide:arrow-right" class="size-4 text-ai-handoff-accent animate-pulse" />
      <span class="h-px w-4 bg-ai-handoff-accent/50" />
    </div>
    <div class="flex items-center gap-1.5 font-medium text-tnzi-base">
      <Icon :icon="toIcon ?? 'lucide:bot'" class="size-4 text-primary" />
      <span>{{ toAgent }}</span>
    </div>
  </div>
</template>
