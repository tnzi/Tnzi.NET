<template>
  <NModal
    :show="state.visible.value"
    :mask-closable="false"
    preset="card"
    :title="title"
    :class="{ 't-form-modal--fullscreen': isFullscreenModal }"
    :style="modalStyle"
    @update:show="onUpdateShow"
  >
    <!-- Long forms scroll inside the card instead of pushing header /
         footer off the viewport. `max-height` is computed from viewport
         height minus reserved space for header (~56px) + footer (~64px)
         + outer modal padding (~80px). Short forms keep their natural
         height since native overflow only kicks in past max-height.
         Using plain `overflow: auto` (instead of NScrollbar) so the
         global `polish.css` macOS-style scrollbar applies — NScrollbar
         renders an overlay thumb that floats over (and occludes) the
         rightmost form widgets. -->
    <div
      class="t-form-modal__scroll"
      :style="{ maxHeight: contentMaxHeight }"
    >
      <slot :formData="state.formData.value" :mode="state.mode.value" />
    </div>
    <template #footer>
      <slot name="footer">
        <div class="t-form-modal__footer">
          <NButton class="t-form-modal__cancel" @click="onCancel">
            {{ t('admin.common.cancel') }}
          </NButton>
          <NButton
            v-if="state.mode.value !== 'view'"
            type="primary"
            class="t-form-modal__confirm"
            @click="onConfirm"
          >
            {{ t('admin.common.confirm') }}
          </NButton>
        </div>
      </slot>
    </template>
  </NModal>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { NModal, NButton } from 'naive-ui'
import { useFormModal } from '../../headless/useFormModal'
import { useBreakpoint } from '../../headless/useBreakpoint'

type FormState = ReturnType<typeof useFormModal<unknown>>

interface Props {
  state: FormState
  title: string
  width?: number
  /**
   * Max viewport height (vh) the inner scroll area is allowed to occupy.
   * Default 65 leaves room for the card header (~56px), footer (~64px),
   * and the modal's outer padding. Override when the form needs more
   * (or less) breathing room.
   */
  contentMaxHeightVh?: number
  /**
   * Force fullscreen layout regardless of viewport size. When unset, the
   * modal auto-switches to fullscreen on viewports narrower than the
   * configured `width` (or `<640px`, whichever is wider).
   */
  fullscreen?: boolean
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<Props>(), {
  width: 560,
  contentMaxHeightVh: 65,
  fullscreen: undefined,
  translate: undefined,
})

const bp = useBreakpoint()

// Auto-fullscreen when the viewport can't fit the configured width with
// breathing room. The threshold is `max(props.width + 32, 640)`:
//   - props.width + 32 → ensures the modal never crops at its sides
//   - 640px (sm) → catches large phones in portrait where even a 560px
//     modal would visually compete with the entire screen
// Consumers can override with `fullscreen` prop (true=force, false=never).
const isFullscreenModal = computed<boolean>(() => {
  if (typeof props.fullscreen === 'boolean') return props.fullscreen
  const threshold = Math.max(props.width + 32, 640)
  return bp.width.value > 0 && bp.width.value < threshold
})

const modalStyle = computed(() => {
  if (isFullscreenModal.value) {
    return { width: '100vw', maxWidth: '100vw' }
  }
  // Cap at 95vw so a too-large `width` prop still leaves a visible mask
  // strip — soybean's modal helper does the same `min(width, 95vw)` cap.
  return { width: `min(${props.width}px, 95vw)` }
})

const contentMaxHeight = computed(() => {
  if (isFullscreenModal.value) {
    // Leave room for header (~56) + footer (~64) + safe area (~24).
    return 'calc(100vh - 144px)'
  }
  return `${props.contentMaxHeightVh}vh`
})

const emit = defineEmits<{
  submit: []
}>()

function t(key: string): string {
  return props.translate ? props.translate(key) : key
}

function onUpdateShow(value: boolean): void {
  if (!value) props.state.close()
}

function onCancel(): void {
  props.state.close()
}

function onConfirm(): void {
  emit('submit')
}
</script>

<style scoped>
.t-form-modal__footer {
  display: flex;
  justify-content: flex-end;
  gap: var(--tnzi-spacing-sm, 8px);
}
.t-form-modal__scroll {
  overflow-y: auto;
  overflow-x: hidden;
}
</style>

<!-- Fullscreen mode targets the teleported modal root, so the rules
     can't be scoped. NModal exposes the outermost wrapper as
     `.n-modal-container` with our custom class added. -->
<style>
.t-form-modal--fullscreen {
  position: fixed !important;
  inset: 0 !important;
  height: 100vh !important;
  max-height: 100vh !important;
  border-radius: 0 !important;
  margin: 0 !important;
}
.t-form-modal--fullscreen .n-card {
  border-radius: 0;
  height: 100vh;
  max-height: 100vh;
  display: flex;
  flex-direction: column;
}
.t-form-modal--fullscreen .n-card__content {
  flex: 1 1 auto;
  min-height: 0;
  overflow: hidden;
}
</style>
