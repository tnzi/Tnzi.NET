<script setup lang="ts">
/**
 * TShimmer — Gradient sweep animation overlay
 *
 * Wraps slot content with a shimmer gradient animation.
 * Uses the `animate-shimmer` keyframe from tailwind config.
 */

import { cn } from '@/lib/utils';

const props = withDefaults(defineProps<{
  /** Animation cycle duration in seconds. */
  duration?: number;
  class?: string;
}>(), {
  duration: 2,
});
</script>

<template>
  <span
    :class="cn(
      'inline-block bg-gradient-to-r from-foreground/60 via-foreground to-foreground/60 bg-[length:200%_100%] bg-clip-text text-transparent animate-shimmer',
      props.class,
    )"
    :style="{ animationDuration: `${props.duration}s`, WebkitBackgroundClip: 'text' }"
  >
    <slot />
  </span>
</template>
