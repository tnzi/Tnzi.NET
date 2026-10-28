<script setup lang="ts">
/**
 * @experimental
 * The frame of a right-side panel: a resizable column on a wide screen, a full-size layer over its
 * positioned parent on a narrow one (with a way back). What goes inside is the default slot's.
 *
 * Open/closed and the width are the owner's (`useAsidePanel`); this component only draws and reports
 * (`update:width`, `close`).
 */
import { onBeforeUnmount, ref } from 'vue'
import { Icon } from '@iconify/vue'
import { useAiI18n } from '../../i18n/index'

const props = withDefaults(
  defineProps<{
    width?: number
    minWidth?: number
    maxWidth?: number
    /** Full-size layer with a back button instead of a column. */
    narrow?: boolean
    /** Accessible name of the region; also the title of the narrow layer's header. */
    label?: string
    resizable?: boolean
  }>(),
  { width: 420, minWidth: 280, maxWidth: 720, narrow: false, label: undefined, resizable: true },
)

const emit = defineEmits<{
  'update:width': [width: number]
  close: []
}>()

const t = useAiI18n()
const KEY_STEP = 16
const KEY_STEP_LARGE = 64

const dragging = ref(false)
let startX = 0
let startWidth = 0

function clamp(w: number): number {
  return Math.round(Math.min(props.maxWidth, Math.max(props.minWidth, w)))
}

function onPointerDown(e: PointerEvent): void {
  if (!props.resizable || e.button > 0) return
  e.preventDefault()
  startX = e.clientX
  startWidth = props.width
  dragging.value = true
  window.addEventListener('pointermove', onPointerMove)
  window.addEventListener('pointerup', stopDrag)
  window.addEventListener('pointercancel', stopDrag)
  document.body.style.cursor = 'ew-resize'
  document.body.style.userSelect = 'none'
}

function onPointerMove(e: PointerEvent): void {
  // The handle is on the left edge: moving left grows the panel.
  emit('update:width', clamp(startWidth + (startX - e.clientX)))
}

function stopDrag(): void {
  if (!dragging.value) return
  dragging.value = false
  window.removeEventListener('pointermove', onPointerMove)
  window.removeEventListener('pointerup', stopDrag)
  window.removeEventListener('pointercancel', stopDrag)
  document.body.style.cursor = ''
  document.body.style.userSelect = ''
}

function onHandleKeydown(e: KeyboardEvent): void {
  const step = e.shiftKey ? KEY_STEP_LARGE : KEY_STEP
  let next: number | null = null
  if (e.key === 'ArrowLeft') next = props.width + step
  else if (e.key === 'ArrowRight') next = props.width - step
  else if (e.key === 'Home') next = props.minWidth
  else if (e.key === 'End') next = props.maxWidth
  if (next === null) return
  e.preventDefault()
  emit('update:width', clamp(next))
}

function onKeydown(e: KeyboardEvent): void {
  if (props.narrow && e.key === 'Escape') {
    e.stopPropagation()
    emit('close')
  }
}

onBeforeUnmount(stopDrag)
</script>

<template>
  <aside
    class="t-aside-panel"
    :class="{ 't-aside-panel--narrow': narrow }"
    :style="narrow ? undefined : { width: `${width}px` }"
    :aria-label="label ?? t.chat.asideLabel"
    @keydown="onKeydown"
  >
    <div
      v-if="resizable && !narrow"
      class="t-aside-panel__resize"
      :class="{ 'is-dragging': dragging }"
      role="separator"
      aria-orientation="vertical"
      tabindex="0"
      :aria-label="t.chat.asideResize"
      :aria-valuenow="width"
      :aria-valuemin="minWidth"
      :aria-valuemax="maxWidth"
      @pointerdown="onPointerDown"
      @keydown="onHandleKeydown"
    >
      <span class="t-aside-panel__resize-pill" aria-hidden="true" />
    </div>

    <header v-if="narrow" class="t-aside-panel__head">
      <button type="button" class="t-aside-panel__back" :aria-label="t.chat.asideBack" @click="emit('close')">
        <Icon icon="lucide:arrow-left" />
      </button>
      <span class="t-aside-panel__title">{{ label ?? t.chat.asideLabel }}</span>
    </header>

    <div class="t-aside-panel__body">
      <slot />
    </div>
  </aside>
</template>

<style scoped>
.t-aside-panel {
  position: relative;
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  min-width: 0;
  max-width: 70%;
  box-sizing: border-box;
  background: var(--tnzi-ai-surface);
  border-left: 1px solid var(--tnzi-ai-border);
  color: var(--tnzi-ai-text);
}
.t-aside-panel--narrow {
  position: absolute;
  inset: 0;
  z-index: 5;
  width: auto;
  max-width: none;
  border-left: none;
  background: var(--tnzi-ai-bg);
}
.t-aside-panel__body {
  flex: 1;
  min-height: 0;
  min-width: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}
.t-aside-panel__head {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  gap: 8px;
  height: 48px;
  padding: 0 8px;
  border-bottom: 1px solid var(--tnzi-ai-border);
}
.t-aside-panel__title {
  font-size: 14px;
  font-weight: 600;
  color: var(--tnzi-ai-text);
}
.t-aside-panel__back {
  width: var(--tnzi-ai-icon-button-size, 32px);
  height: var(--tnzi-ai-icon-button-size, 32px);
  border: none;
  border-radius: 6px;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 18px;
  cursor: pointer;
}
.t-aside-panel__back:hover {
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text);
}
.t-aside-panel__resize {
  position: absolute;
  top: 0;
  bottom: 0;
  left: -4px;
  width: 8px;
  z-index: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: ew-resize;
  touch-action: none;
  outline: none;
}
.t-aside-panel__resize-pill {
  width: 3px;
  height: 36px;
  border-radius: 2px;
  background: var(--tnzi-ai-border-strong);
  opacity: 0;
  transition: opacity var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-aside-panel__resize:hover .t-aside-panel__resize-pill,
.t-aside-panel__resize:focus-visible .t-aside-panel__resize-pill,
.t-aside-panel__resize.is-dragging .t-aside-panel__resize-pill {
  opacity: 1;
}
.t-aside-panel__resize:focus-visible .t-aside-panel__resize-pill {
  background: var(--tnzi-ai-accent);
}
</style>
