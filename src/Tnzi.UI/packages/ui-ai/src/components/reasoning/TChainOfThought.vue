<script setup lang="ts">
/**
 * TChainOfThought - Vertical timeline for reasoning steps
 *
 * Displays a sequence of steps with status indicators and
 * vertical connector lines between them.
 */

import { Icon } from '@iconify/vue';
import TShimmer from '../streaming/TShimmer.vue';

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
        class="t-cot__connector"
        :class="step.status === 'complete' ? 't-cot__connector--done' : 't-cot__connector--default'"
      />

      <!-- Status icon -->
      <div
        class="t-cot__dot"
        :class="{
          't-cot__dot--complete': step.status === 'complete',
          't-cot__dot--active': step.status === 'active',
          't-cot__dot--pending': step.status === 'pending',
        }"
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
          class="text-sm font-medium leading-tight"
          :class="{ 't-cot__label--pending': step.status === 'pending' }"
        >
          <TShimmer v-if="step.status === 'active'">{{ step.label }}</TShimmer>
          <template v-else>{{ step.label }}</template>
        </p>
        <p
          v-if="step.description"
          class="mt-0.5 text-xs t-cot__desc"
        >
          {{ step.description }}
        </p>
      </div>
    </div>
  </div>
</template>

<style scoped>
.t-cot__connector {
  position: absolute;
  left: 12px;
  top: 28px;
  bottom: 0;
  width: 1px;
}
.t-cot__connector--done { background-color: color-mix(in srgb, var(--tnzi-ai-node-completed) 40%, transparent); }
.t-cot__connector--default { background-color: var(--tnzi-border); }
.t-cot__dot {
  position: relative;
  z-index: 10;
  display: flex;
  width: 24px;
  height: 24px;
  flex-shrink: 0;
  align-items: center;
  justify-content: center;
  border-radius: 50%;
}
.t-cot__dot--complete {
  background-color: color-mix(in srgb, var(--tnzi-ai-node-completed) 20%, transparent);
  color: var(--tnzi-ai-node-completed);
}
.t-cot__dot--active {
  background-color: color-mix(in srgb, var(--tnzi-ai-node-active) 20%, transparent);
  color: var(--tnzi-ai-node-active);
}
.t-cot__dot--pending {
  background-color: var(--tnzi-container-bg);
  color: var(--tnzi-base-text-muted);
}
.t-cot__label--pending { color: var(--tnzi-base-text-muted); }
.t-cot__desc { color: var(--tnzi-base-text-muted); }
</style>
