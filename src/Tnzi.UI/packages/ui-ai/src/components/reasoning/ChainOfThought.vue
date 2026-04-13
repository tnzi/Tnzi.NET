<script setup lang="ts">
/**
 * ChainOfThought — Vertical timeline for reasoning steps
 *
 * Displays a sequence of steps with status indicators and
 * vertical connector lines between them.
 */

import { Icon } from '@iconify/vue';
import { cn } from '@/lib/utils';
import Shimmer from '../streaming/Shimmer.vue';

export interface ThoughtStep {
  id: string;
  label: string;
  description?: string;
  status: 'complete' | 'active' | 'pending';
  /** Iconify icon name (e.g., "lucide:search"). */
  icon?: string;
}

defineProps<{
  steps: ThoughtStep[];
}>();
</script>

<template>
  <div class="flex flex-col" role="list">
    <div
      v-for="(step, index) in steps"
      :key="step.id"
      class="relative flex gap-3 pb-4 last:pb-0"
      role="listitem"
    >
      <!-- Vertical connector line -->
      <div
        v-if="index < steps.length - 1"
        class="absolute left-3 top-7 bottom-0 w-px"
        :class="step.status === 'complete' ? 'bg-ai-node-completed/40' : 'bg-border'"
      />

      <!-- Status icon -->
      <div
        :class="cn(
          'relative z-10 flex size-6 shrink-0 items-center justify-center rounded-full',
          step.status === 'complete' && 'bg-ai-node-completed/20 text-ai-node-completed',
          step.status === 'active' && 'bg-ai-node-active/20 text-ai-node-active',
          step.status === 'pending' && 'bg-muted text-muted-foreground',
        )"
      >
        <Icon
          v-if="step.status === 'complete'"
          icon="lucide:check"
          class="size-3.5"
        />
        <Icon
          v-else-if="step.icon"
          :icon="step.icon"
          class="size-3.5"
        />
        <div
          v-else-if="step.status === 'active'"
          class="size-2 rounded-full bg-current animate-pulse"
        />
        <div
          v-else
          class="size-2 rounded-full bg-current opacity-40"
        />
      </div>

      <!-- Content -->
      <div class="flex-1 min-w-0 pt-0.5">
        <p
          :class="cn(
            'text-sm font-medium leading-tight',
            step.status === 'pending' && 'text-muted-foreground',
          )"
        >
          <Shimmer v-if="step.status === 'active'">{{ step.label }}</Shimmer>
          <template v-else>{{ step.label }}</template>
        </p>
        <p
          v-if="step.description"
          class="mt-0.5 text-xs text-muted-foreground"
        >
          {{ step.description }}
        </p>
      </div>
    </div>
  </div>
</template>
