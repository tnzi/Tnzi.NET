<script setup lang="ts">
/**
 * `TFullScreen` — toggles document fullscreen. Icon flips on state change.
 *
 * Falls back to a no-op if the browser doesn't expose the Fullscreen API
 * (older Safari versions in some sandboxes).
 */
import { onBeforeUnmount, onMounted, ref, computed } from 'vue'
import TButtonIcon from '../display/TButtonIcon.vue'

interface Props {
  /** Element to fullscreen. Defaults to `document.documentElement`. */
  target?: HTMLElement | null
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<Props>(), {
  target: null,
  translate: undefined,
})

const isFull = ref(false)

function syncState(): void {
  isFull.value = !!document.fullscreenElement
}

async function toggle(): Promise<void> {
  if (typeof document === 'undefined') return
  try {
    if (document.fullscreenElement) {
      await document.exitFullscreen()
    } else {
      const el = props.target ?? document.documentElement
      await el.requestFullscreen()
    }
  } catch {
    // Safari + iframes can reject; swallow.
  }
}

onMounted(() => {
  document.addEventListener('fullscreenchange', syncState)
  syncState()
})

onBeforeUnmount(() => {
  document.removeEventListener('fullscreenchange', syncState)
})

const tooltip = computed(() =>
  props.translate
    ? props.translate(isFull.value ? 'admin.fullscreen.exit' : 'admin.fullscreen.enter')
    : isFull.value
      ? 'Exit fullscreen'
      : 'Enter fullscreen',
)

const icon = computed(() => (isFull.value ? 'mdi:fullscreen-exit' : 'mdi:fullscreen'))
</script>

<template>
  <TButtonIcon
    :icon="icon"
    :tooltip="tooltip"
    class="t-fullscreen"
    @click="toggle"
  />
</template>
