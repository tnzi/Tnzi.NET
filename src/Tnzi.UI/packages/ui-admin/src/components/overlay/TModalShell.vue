<template>
  <NModal
    :show="show"
    preset="card"
    :size="size"
    :mask-closable="maskClosable"
    :title="title"
    :class="{ 't-modal-shell--fullscreen': isFullscreen }"
    :style="modalStyle"
    @update:show="(v: boolean) => emit('update:show', v)"
  >
    <!-- Body scrolls inside the card so long content never pushes the header /
         footer off the viewport. `max-height` = viewport height minus reserved
         header (~56px) + footer (~64px) + outer modal padding (~80px). Short
         content keeps its natural height (native overflow only kicks in past
         max-height). Plain `overflow:auto` (not NScrollbar) so the global
         polish.css macOS-style scrollbar applies — NScrollbar renders an
         overlay thumb that floats over (and occludes) the rightmost widgets. -->
    <div class="t-modal-shell__scroll" :style="{ maxHeight: contentMaxHeight }">
      <slot />
    </div>
    <template v-if="$slots.footer" #footer>
      <slot name="footer" />
    </template>
  </NModal>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { NModal } from 'naive-ui'
import { useBreakpoint } from '../../headless/useBreakpoint'

interface Props {
  /** Open state (controlled). */
  show: boolean
  title?: string
  /** Desktop width (px); capped at 95vw so a too-large value still shows a mask strip. */
  width?: number
  /**
   * Card padding scale (forwarded to naive's card preset). Default `small`
   * (12/16/12px) — the admin-compact chrome. naive's own default is `medium`
   * (19/24/20px), which reads as too roomy for dense admin dialogs. Bump to
   * `medium`/`large` for content-heavy modals.
   */
  size?: 'small' | 'medium' | 'large' | 'huge'
  /**
   * Force fullscreen. When unset, auto-switches to fullscreen on viewports
   * narrower than `max(width + 32, 640)`.
   */
  fullscreen?: boolean
  /** Max viewport height (vh) the inner scroll area may occupy. Default 65. */
  contentMaxHeightVh?: number
  /** Allow closing by clicking the mask. Default false (forms shouldn't lose input on a stray click). */
  maskClosable?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  title: undefined,
  width: 560,
  size: 'small',
  fullscreen: undefined,
  contentMaxHeightVh: 65,
  maskClosable: false,
})

const emit = defineEmits<{ 'update:show': [value: boolean] }>()

const bp = useBreakpoint()

// Auto-fullscreen when the viewport can't fit the configured width with room:
//   - width + 32 → never crop at the sides
//   - 640 (sm)  → catch large phones in portrait where even a 560px modal
//                 would visually compete with the whole screen
const isFullscreen = computed<boolean>(() => {
  if (typeof props.fullscreen === 'boolean') return props.fullscreen
  const threshold = Math.max(props.width + 32, 640)
  return bp.width.value > 0 && bp.width.value < threshold
})

const modalStyle = computed(() =>
  isFullscreen.value
    ? { width: '100vw', maxWidth: '100vw' }
    // Cap at 95vw so a too-large `width` still leaves a visible mask strip.
    : { width: `min(${props.width}px, 95vw)` },
)

const contentMaxHeight = computed(() =>
  // Fullscreen leaves room for header (~56) + footer (~64) + safe area (~24).
  isFullscreen.value ? 'calc(100vh - 144px)' : `${props.contentMaxHeightVh}vh`,
)
</script>

<style scoped>
.t-modal-shell__scroll {
  overflow-y: auto;
  overflow-x: hidden;
}
</style>

<!-- Fullscreen targets the teleported modal root, so these rules can't be
     scoped. NModal exposes the outermost wrapper as `.n-modal-container` with
     our custom class merged in. -->
<style>
.t-modal-shell--fullscreen {
  position: fixed !important;
  inset: 0 !important;
  height: 100vh !important;
  max-height: 100vh !important;
  border-radius: 0 !important;
  margin: 0 !important;
}
.t-modal-shell--fullscreen .n-card {
  border-radius: 0;
  height: 100vh;
  max-height: 100vh;
  display: flex;
  flex-direction: column;
}
.t-modal-shell--fullscreen .n-card__content {
  flex: 1 1 auto;
  min-height: 0;
  overflow: hidden;
}
</style>
