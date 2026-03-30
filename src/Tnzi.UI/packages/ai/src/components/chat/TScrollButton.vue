<script setup lang="ts">
/**
 * TScrollButton — Scroll to bottom button
 *
 * Floating button that appears when user scrolls up in a chat container.
 * Fades in/out with a smooth transition.
 */

import { Icon } from '@iconify/vue';
import { Button } from '@/primitives/ui/button';

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
    <Button
      v-if="visible"
      variant="outline"
      size="icon-sm"
      class="absolute bottom-4 left-1/2 -translate-x-1/2 z-10 rounded-full shadow-md"
      aria-label="Scroll to bottom"
      @click="emit('click')"
    >
      <Icon icon="lucide:arrow-down" class="size-4 text-muted-foreground" />
    </Button>
  </Transition>
</template>
