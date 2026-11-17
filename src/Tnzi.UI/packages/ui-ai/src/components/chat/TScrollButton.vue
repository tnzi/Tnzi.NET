<script setup lang="ts">
/**
 * TScrollButton - Scroll to bottom button
 *
 * Floating button that appears when user scrolls up in a chat container.
 * Fades in/out with a smooth transition. Drawn as the shell's own
 * scroll-to-latest button: a round surface chip with a soft shadow.
 */

import { Icon } from '@iconify/vue';
import { vTooltip } from '../../headless/tooltip';
import { useAiI18n } from '../../i18n/index';
defineProps<{
  visible: boolean;
}>();

const emit = defineEmits<{
  click: [];
}>();

const t = useAiI18n();
</script>

<template>
  <Transition name="t-scroll-button">
    <button
      v-if="visible"
      v-tooltip="{ text: t.chat.scrollToLatest, side: 'top' }"
      type="button"
      class="t-scroll-button"
      :aria-label="t.chat.scrollToLatest"
      @click="emit('click')"
    >
      <Icon icon="lucide:arrow-down" aria-hidden="true" />
    </button>
  </Transition>
</template>

<style scoped>
.t-scroll-button {
  position: absolute;
  bottom: 16px;
  left: 50%;
  transform: translateX(-50%);
  z-index: var(--tnzi-ai-z-sticky, 10);
  width: 36px;
  height: 36px;
  border: 1px solid var(--tnzi-ai-border-strong);
  border-radius: 999px;
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text-secondary);
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.08);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 18px;
  cursor: pointer;
  transition:
    background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
    color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-scroll-button:hover {
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text);
}
.t-scroll-button:focus-visible {
  outline: var(--tnzi-ai-focus-outline);
  outline-offset: 2px;
}
/* A larger target for a finger. */
@media (hover: none) {
  .t-scroll-button {
    width: 40px;
    height: 40px;
  }
}
.t-scroll-button-enter-active {
  transition: opacity 200ms ease-out;
}
.t-scroll-button-leave-active {
  transition: opacity 150ms ease-in;
}
.t-scroll-button-enter-from,
.t-scroll-button-leave-to {
  opacity: 0;
}
</style>
