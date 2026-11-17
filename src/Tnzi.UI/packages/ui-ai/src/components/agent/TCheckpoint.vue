<script setup lang="ts">
/**
 * TCheckpoint - Workflow checkpoint marker
 *
 * A small icon button (its name is also its tooltip) followed by a hairline
 * across the rest of the row. The default slot replaces the bookmark icon.
 */

import { computed } from 'vue';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '../../i18n/index';
import { vTooltip } from '../../headless/tooltip';
const t = useAiI18n();

const props = defineProps<{
  tooltip?: string;
}>();

defineEmits<{
  click: [];
}>();

const label = computed(() => props.tooltip ?? t.value.checkpoint.label);
</script>

<template>
  <div class="t-checkpoint">
    <button
      v-tooltip="label"
      type="button"
      class="t-checkpoint__button"
      :aria-label="label"
      @click="$emit('click')"
    >
      <slot>
        <Icon icon="lucide:bookmark" aria-hidden="true" />
      </slot>
    </button>
    <span class="t-checkpoint__line" aria-hidden="true" />
  </div>
</template>

<style scoped>
.t-checkpoint {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 4px 0;
}
.t-checkpoint__button {
  flex-shrink: 0;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  padding: 0;
  border: none;
  border-radius: 8px;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  font-size: 16px;
  cursor: pointer;
  transition: background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
    color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-checkpoint__button:hover {
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text);
}
.t-checkpoint__button:focus-visible {
  outline: var(--tnzi-ai-focus-outline);
  outline-offset: 2px;
}
.t-checkpoint__line {
  flex: 1;
  height: 1px;
  background: var(--tnzi-ai-border);
}
</style>
