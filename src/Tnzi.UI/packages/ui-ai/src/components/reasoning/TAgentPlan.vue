<script setup lang="ts">
/**
 * TAgentPlan - Collapsible plan display card
 *
 * Shows a plan with shimmer-animated title during streaming. The toggle at
 * the top right shows and hides the body (the default slot and `#footer`,
 * where the plan's actions such as Approve go).
 */

import { ref } from 'vue';
import { useAiI18n } from '../../i18n/index';
import TIconButton from '../controls/TIconButton.vue';
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
  <section class="t-agent-plan">
    <div class="t-agent-plan__head">
      <div class="t-agent-plan__heading">
        <div class="t-agent-plan__title">
          <TShimmer v-if="isStreaming">{{ title }}</TShimmer>
          <template v-else>{{ title }}</template>
        </div>
        <div v-if="description" class="t-agent-plan__desc">
          <TShimmer v-if="isStreaming">{{ description }}</TShimmer>
          <template v-else>{{ description }}</template>
        </div>
      </div>
      <TIconButton
        icon="lucide:chevrons-up-down"
        :label="t.plan.collapse"
        size="xs"
        :aria-expanded="isOpen"
        @click="isOpen = !isOpen"
      />
    </div>
    <div v-show="isOpen" class="t-agent-plan__body">
      <slot />
      <div v-if="$slots.footer" class="t-agent-plan__footer">
        <slot name="footer" />
      </div>
    </div>
  </section>
</template>

<style scoped>
.t-agent-plan {
  overflow: hidden;
  padding: 14px 16px;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 14px;
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text);
  font-size: 13px;
}
.t-agent-plan__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}
.t-agent-plan__heading {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}
.t-agent-plan__title {
  font-size: 14px;
  font-weight: 500;
}
.t-agent-plan__desc {
  font-size: 12px;
  color: var(--tnzi-ai-text-secondary);
}
.t-agent-plan__body {
  margin-top: 8px;
}
.t-agent-plan__footer {
  margin-top: 8px;
}
</style>
