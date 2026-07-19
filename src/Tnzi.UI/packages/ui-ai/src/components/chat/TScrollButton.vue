<script setup lang="ts">
/**
 * TScrollButton — Scroll to bottom button
 *
 * Floating button that appears when user scrolls up in a chat container.
 * Fades in/out with a smooth transition.
 */

import { NButton } from 'naive-ui';
import { Icon } from '@iconify/vue';
defineProps<{
  visible: boolean;
}>();

const emit = defineEmits<{
  click: [];
}>();
</script>

<template>
  <Transition
    enter-active-class="transition-opacity duration-200 ease-out"
    leave-active-class="transition-opacity duration-150 ease-in"
    enter-from-class="opacity-0"
    leave-to-class="opacity-0"
  >
    <NButton
      v-if="visible"
      secondary
      size="small"
      circle
      class="absolute bottom-4 left-1/2 -translate-x-1/2 z-10 shadow-md"
      aria-label="Scroll to bottom"
      @click="emit('click')"
    >
      <template #icon><Icon icon="lucide:arrow-down" /></template>
    </NButton>
  </Transition>
</template>
