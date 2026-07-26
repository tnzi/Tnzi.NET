<script setup lang="ts">
/**
 * TStreamLoader - 10-ray fan spinner
 *
 * Accessible loading indicator with graduated opacity rays.
 * Matches DeerFlow's fan design pattern.
 */

const props = withDefaults(defineProps<{
  size?: number;
}>(), {
  size: 16,
});

/** 10 rays, each rotated 36deg apart, with graduated opacity 0.1 -> 1.0 */
const rays = Array.from({ length: 10 }, (_, i) => ({
  rotation: i * 36,
  opacity: ((i + 1) / 10).toFixed(1),
}));
</script>

<template>
  <div class="inline-flex items-center justify-center animate-spin" role="status" aria-label="Loading">
    <svg
      :width="props.size"
      :height="props.size"
      viewBox="0 0 24 24"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
    >
      <title>Loader</title>
      <line
        v-for="(ray, i) in rays"
        :key="i"
        x1="12"
        y1="2"
        x2="12"
        y2="6"
        stroke="currentColor"
        stroke-width="2"
        stroke-linecap="round"
        :opacity="ray.opacity"
        :transform="`rotate(${ray.rotation} 12 12)`"
      />
    </svg>
  </div>
</template>
