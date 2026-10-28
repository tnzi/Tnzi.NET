<script setup lang="ts">
/**
 * `TReloadButton` - refresh icon with a spin-during-loading affordance.
 *
 * Self-contained: clicks emit `reload`, the consumer flips `loading` (or
 * passes a promise via `onReload`) and the icon rotates 360 degrees once
 * per spin cycle.
 *
 * `@reload="fn"` and `:on-reload="fn"` are the same vnode prop, so a handler
 * given either way arrives as the `onReload` prop and is run (and awaited)
 * exactly once per click.
 */
import { ref, computed } from 'vue'
import TButtonIcon from '../display/TButtonIcon.vue'

interface Props {
  /** External loading flag (controlled mode). */
  loading?: boolean
  /** Async callback - when set, click awaits it and spins automatically. */
  onReload?: () => Promise<void>
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<Props>(), {
  loading: undefined,
  onReload: undefined,
  translate: undefined,
})

const emit = defineEmits<{
  reload: []
}>()

const internal = ref(false)
const spinning = computed(() => props.loading ?? internal.value)

async function handleClick(): Promise<void> {
  if (props.onReload) {
    // Not emitted as well: `emit('reload')` would call this same handler a
    // second time, since Vue resolves it from the vnode's `onReload`.
    internal.value = true
    try {
      await props.onReload()
    } finally {
      internal.value = false
    }
  } else {
    emit('reload')
    // Brief visual confirmation for fire-and-forget callers.
    internal.value = true
    setTimeout(() => {
      internal.value = false
    }, 600)
  }
}

const tooltip = computed(() =>
  props.translate ? props.translate('admin.reload') : 'Refresh',
)
</script>

<template>
  <TButtonIcon
    icon="mdi:refresh"
    :tooltip="tooltip"
    :class="['t-reload-btn', { 't-reload-btn--spinning': spinning }]"
    @click="handleClick"
  />
</template>

<style scoped>
.t-reload-btn :deep(.iconify) {
  transition: transform 0.6s cubic-bezier(0.4, 0, 0.2, 1);
}
.t-reload-btn--spinning :deep(.iconify) {
  transform: rotate(360deg);
}
</style>
