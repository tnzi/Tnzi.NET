<script setup lang="ts">
/**
 * TImageBlock - AI generated image display
 *
 * Handles both URL and base64-encoded images with lazy loading.
 */

import { computed } from 'vue';

const props = withDefaults(defineProps<{
  src?: string | null;
  /** Base64-encoded data without the data URI prefix. */
  base64?: string | null;
  /** MIME type for base64 images (e.g., "image/png"). */
  mediaType?: string | null;
  alt?: string;
  class?: string;
}>(), {
  src: null,
  base64: null,
  mediaType: 'image/png',
  alt: 'AI generated image',
});

const imageSrc = computed(() => {
  if (props.src) return props.src;
  if (props.base64) {
    const type = props.mediaType ?? 'image/png';
    return `data:${type};base64,${props.base64}`;
  }
  return '';
});
</script>

<template>
  <img
    v-if="imageSrc"
    :src="imageSrc"
    :alt="props.alt"
    loading="lazy"
    class="t-image-block"
    :class="props.class"
  />
</template>

<style scoped>
.t-image-block {
  border-radius: 8px;
  max-width: 100%;
  height: auto;
}
</style>
