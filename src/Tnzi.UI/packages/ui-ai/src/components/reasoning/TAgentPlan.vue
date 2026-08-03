<script setup lang="ts">
/**
 * TAgentPlan - Collapsible plan display card
 *
 * Shows a plan with shimmer-animated title during streaming.
 */

import { NCard, NButton } from 'naive-ui';
import { ref } from 'vue';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '../../i18n/index';
import TShimmer from '../streaming/TShimmer.vue';

const t = useAiI18n();

const props = withDefaults(defineProps<{
  /** Plan title. */
  title: string;
  /** Plan description. */
  description?: string;
  /** Whether the plan is currently being streamed. */
  isStreaming?: boolean;
  /** Default open state. */
  defaultOpen?: boolean;
}>(), {
  isStreaming: false,
  defaultOpen: true,
});

const isOpen = ref(props.defaultOpen);
</script>

<template>
  <NCard class="overflow-hidden">
    <div class="flex flex-row items-center justify-between gap-2 mb-2">
      <div class="space-y-0.5 flex-1 min-w-0">
        <div class="text-sm font-medium">
          <TShimmer v-if="isStreaming">{{ title }}</TShimmer>
          <template v-else>{{ title }}</template>
        </div>
        <div v-if="description" class="text-xs t-agent-plan__desc">
          <TShimmer v-if="isStreaming">{{ description }}</TShimmer>
          <template v-else>{{ description }}</template>
        </div>
      </div>
      <NButton text size="small" @click="isOpen = !isOpen">
        <template #icon>
          <Icon icon="lucide:chevrons-up-down" class="size-4 t-agent-plan__muted" />
        </template>
        <span class="sr-only">{{ t.plan.collapse }}</span>
      </NButton>
    </div>
    <div v-show="isOpen">
      <slot />
      <div v-if="$slots.footer" class="mt-2">
        <slot name="footer" />
      </div>
    </div>
  </NCard>
</template>

<style scoped>
.t-agent-plan__desc { color: var(--tnzi-base-text-muted); }
.t-agent-plan__muted { color: var(--tnzi-base-text-muted); }
</style>
