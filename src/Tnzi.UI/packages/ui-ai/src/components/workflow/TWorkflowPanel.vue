<script setup lang="ts">
/**
 * TWorkflowPanel — Canvas floating panel
 */

defineProps<{
  position?: 'top-left' | 'top-right' | 'bottom-left' | 'bottom-right';
}>();
</script>

<template>
  <div
    class="t-wf-panel"
    :class="`t-wf-panel--${position ?? 'top-right'}`"
  >
    <slot />
  </div>
</template>

<style scoped>
.t-wf-panel {
  position: absolute;
  z-index: 10;
}
.t-wf-panel--top-left { top: 12px; left: 12px; }
.t-wf-panel--top-right { top: 12px; right: 12px; }
.t-wf-panel--bottom-left { bottom: 12px; left: 12px; }
.t-wf-panel--bottom-right { bottom: 12px; right: 12px; }
</style>
