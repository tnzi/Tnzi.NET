<script setup lang="ts">
/**
 * Shimmer — Gradient sweep animation overlay
 *
 * Wraps slot content with a shimmer gradient animation.
 */

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
    class="t-shimmer"
    :class="props.class"
    :style="{ animationDuration: `${props.duration}s`, WebkitBackgroundClip: 'text' }"
  >
    <slot />
  </span>
</template>

<style scoped>
.t-shimmer {
  display: inline-block;
  background: linear-gradient(
    90deg,
    color-mix(in srgb, var(--tnzi-base-text) 60%, transparent) 0%,
    var(--tnzi-base-text) 50%,
    color-mix(in srgb, var(--tnzi-base-text) 60%, transparent) 100%
  );
  background-size: 200% 100%;
  -webkit-background-clip: text;
  background-clip: text;
  -webkit-text-fill-color: transparent;
  color: transparent;
  animation: t-shimmer-sweep 2s linear infinite;
}

@keyframes t-shimmer-sweep {
  0% { background-position: 200% center; }
  100% { background-position: -200% center; }
}
</style>
