<script setup lang="ts">
/**
 * TImagePicker - a picture as the control that changes it, nothing around it
 * taking up room: a click on the picture (or Enter / Space with it focused)
 * picks another, a file can be dropped on it, and while there is one a chip on
 * its corner (shown on hover and focus, always on a touch screen) removes it.
 *
 * The picture itself is the page's (the default slot), which has something to
 * show even with none chosen: a square one for a project, a `round` one for a
 * person. The picker emits the file; reading, cropping and uploading it are the
 * page's, and so is `busy` while that runs (the picture then shows a spinner
 * and takes no clicks).
 */
import { ref } from 'vue'
import { Icon } from '@iconify/vue'

defineProps<{
  /** Whether there is a picture to remove. */
  has: boolean
  busy?: boolean
  round?: boolean
  accept: string
  changeLabel: string
  changeHint?: string
  removeLabel: string
}>()
const emit = defineEmits<{ pick: [file: File]; remove: [] }>()

const input = ref<HTMLInputElement | null>(null)
const over = ref(false)

function onPicked(event: Event): void {
  const element = event.target as HTMLInputElement
  const file = element.files?.[0]
  // So that picking the same file again is a change too.
  element.value = ''
  if (file) emit('pick', file)
}

function onDrop(event: DragEvent): void {
  over.value = false
  const file = event.dataTransfer?.files?.[0]
  if (file) emit('pick', file)
}
</script>

<template>
  <span class="t-image-picker" :class="{ 'is-round': round }">
    <button
      type="button"
      class="t-image-picker__image"
      :class="{ 'is-over': over }"
      :aria-label="changeLabel"
      :aria-busy="busy"
      :disabled="busy"
      :title="changeHint"
      data-image-pick
      @click="input?.click()"
      @dragover.prevent="over = true"
      @dragleave="over = false"
      @drop.prevent="onDrop"
    >
      <slot />
      <span class="t-image-picker__veil" aria-hidden="true">
        <span v-if="busy" class="t-image-picker__busy" />
        <Icon v-else icon="lucide:camera" />
      </span>
    </button>
    <button v-if="has && !busy" type="button" class="t-image-picker__remove" :aria-label="removeLabel" :title="removeLabel" data-image-remove @click="emit('remove')">
      <Icon icon="lucide:x" aria-hidden="true" />
    </button>
    <input ref="input" type="file" class="t-image-picker__file" :accept="accept" tabindex="-1" aria-hidden="true" data-image-file @change="onPicked" />
  </span>
</template>

<style scoped>
.t-image-picker {
  position: relative;
  display: inline-flex;
  flex: none;
}
.t-image-picker__image {
  position: relative;
  display: inline-flex;
  padding: 0;
  overflow: hidden;
  border: 0;
  border-radius: 10px;
  background: none;
  cursor: pointer;
}
.t-image-picker.is-round .t-image-picker__image {
  border-radius: 50%;
}
.t-image-picker__image:disabled {
  cursor: progress;
}
.t-image-picker__image:focus-visible {
  outline: var(--tnzi-ai-focus-outline);
  outline-offset: 2px;
}
.t-image-picker__image.is-over {
  outline: 2px solid var(--tnzi-ai-accent);
}
.t-image-picker__veil {
  position: absolute;
  inset: 0;
  display: grid;
  place-items: center;
  background: var(--tnzi-ai-image-veil);
  color: var(--tnzi-ai-on-image);
  font-size: 18px;
  opacity: 0;
  transition: opacity var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-image-picker__image:hover .t-image-picker__veil,
.t-image-picker__image:focus-visible .t-image-picker__veil,
.t-image-picker__image.is-over .t-image-picker__veil,
.t-image-picker__image:disabled .t-image-picker__veil {
  opacity: 1;
}
.t-image-picker__busy {
  width: 18px;
  height: 18px;
  border: 2px solid color-mix(in srgb, var(--tnzi-ai-on-image) 35%, transparent);
  border-top-color: var(--tnzi-ai-on-image);
  border-radius: 50%;
  animation: t-image-picker-spin 0.8s linear infinite;
}
@keyframes t-image-picker-spin {
  to {
    transform: rotate(360deg);
  }
}
/* On the outer corner: it does not cover the picture it belongs to. */
.t-image-picker__remove {
  position: absolute;
  top: -6px;
  right: -6px;
  display: grid;
  place-items: center;
  width: 20px;
  height: 20px;
  padding: 0;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 50%;
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text-secondary);
  font-size: 12px;
  cursor: pointer;
  opacity: 0;
  transition: opacity var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
/* A circle's corner is further in: the chip stays on its edge. */
.t-image-picker.is-round .t-image-picker__remove {
  top: -2px;
  right: -2px;
}
.t-image-picker:hover .t-image-picker__remove,
.t-image-picker:focus-within .t-image-picker__remove {
  opacity: 1;
}
.t-image-picker__remove:hover {
  border-color: var(--tnzi-ai-danger);
  color: var(--tnzi-ai-danger);
}
.t-image-picker__remove:focus-visible {
  outline: var(--tnzi-ai-focus-outline);
  outline-offset: 1px;
}
.t-image-picker__file {
  display: none;
}
/* No hover on a touch screen: the chip is always there. */
@media (hover: none) {
  .t-image-picker__remove {
    opacity: 1;
  }
}
@media (prefers-reduced-motion: reduce) {
  .t-image-picker__busy {
    animation-duration: 2s;
  }
  .t-image-picker__veil,
  .t-image-picker__remove {
    transition: none;
  }
}
</style>
