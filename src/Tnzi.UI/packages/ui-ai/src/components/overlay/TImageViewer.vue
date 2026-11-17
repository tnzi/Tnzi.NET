<script setup lang="ts">
/**
 * TImageViewer - a picture at full size over the page, with the others of its set one key or
 * click away. It is a modal layer of the page's overlay stack (`useFocusTrap` from `@tnzi/ui`):
 * focus moves into it and Tab stays among its buttons, Escape closes it, focus returns to what
 * opened it, and on a phone the back gesture closes it. The left and right arrow keys step through
 * the set (wrapping round), as do the buttons beside the picture; the count says where it is.
 * A click on the dark area around the picture closes it. A picture that cannot be shown says so.
 *
 * The picture opens fitted to the window and zooms up to eight times: the buttons below it, the
 * `+` / `-` keys and `0` to fit again, the wheel (or a trackpad pinch) and a two-finger pinch
 * around where they point, a double click to zoom in there or fit again. A zoomed picture is
 * dragged around with the mouse or a finger, and the arrow keys move it instead of stepping
 * through the set. It never moves further than its edges; each picture of the set opens fitted.
 *
 * It takes its place in the stack of open dialogs as `TDialog` does, so a viewer opened from a
 * dialog is drawn above it (and Escape closes the viewer first, through the overlay stack).
 *
 * The dark layer covers the whole window; the picture, the close button and the side buttons keep
 * clear of the page's top inset (`--tnzi-overlay-inset-top`, e.g. a frameless window's own title
 * bar buttons), as the dialogs and the toasts do. Without the variable nothing moves.
 *
 * Its words are read through `image-viewer-text.ts` and its styles are in both stylesheets, so it
 * is also exported from `@tnzi/ui-ai/dialogs` for a page that loads only `controls.css`.
 */
import { computed, ref, watch } from 'vue'
import { Icon } from '@iconify/vue'
import { useFocusTrap } from '@tnzi/ui/headless'
import { formatAiMessage } from '../../i18n/format'
import { useBodyScrollLock } from '../../headless/useBodyScrollLock'
import { useBackLayer } from '../../headless/back-layers'
import type { ViewerImage } from '../chat/image-group'
import { useImageViewerText } from './image-viewer-text'
import { useDialogLayer } from './layers'
import {
  DOUBLE_CLICK_SCALE,
  FITTED,
  ZOOM_STEP,
  isZoomed,
  panBy,
  zoomAt,
  type ImageView,
  type Size,
} from './image-viewer-zoom'

const props = defineProps<{
  /** The set, in order. */
  images: readonly ViewerImage[]
  /** The picture shown; null closes the viewer. */
  index: number | null
}>()

const emit = defineEmits<{ 'update:index': [index: number | null] }>()

/** How far a pointer travels before a press becomes a drag (and no longer a click). */
const DRAG_THRESHOLD_PX = 4
/** How far an arrow key moves a zoomed picture, as a share of the area. */
const KEY_PAN_SHARE = 0.1

const t = useImageViewerText()
const root = ref<HTMLElement | null>(null)
const picture = ref<HTMLImageElement | null>(null)
const open = computed(() => props.index !== null && props.images.length > 0)
const current = computed(() => (props.index === null ? null : (props.images[props.index] ?? null)))
const name = computed(() => current.value?.alt || t.value.label)
const many = computed(() => props.images.length > 1)
const counter = computed(() =>
  formatAiMessage(t.value.counter, { index: (props.index ?? 0) + 1, count: props.images.length }),
)

const view = ref<ImageView>(FITTED)
const zoomed = computed(() => isZoomed(view.value))
const level = computed(() => formatAiMessage(t.value.zoomLevel, { percent: Math.round(view.value.scale * 100) }))
const pictureStyle = computed(() => ({
  transform: `translate(${view.value.x}px, ${view.value.y}px) scale(${view.value.scale})`,
}))

watch(() => props.index, () => {
  view.value = FITTED
})

function close(): void {
  emit('update:index', null)
}

function step(by: number): void {
  if (props.index === null || !many.value) return
  emit('update:index', (props.index + by + props.images.length) % props.images.length)
}

/* The dialog layer plus the viewer's depth in the stack of open dialogs. */
const zIndex = useDialogLayer(() => open.value)

useBodyScrollLock(open)
useFocusTrap(root, () => open.value, { onEscape: close, initialFocus: () => root.value })
useBackLayer(open, close)

/** The picture's size as laid out (fitted, before the zoom) and the area it is centred in. */
function geometry(): { picture: Size; area: Size; centre: { x: number; y: number } } | null {
  const layer = root.value
  const image = picture.value
  if (!layer || !image) return null
  const box = layer.getBoundingClientRect()
  const style = getComputedStyle(layer)
  const left = parseFloat(style.paddingLeft) || 0
  const right = parseFloat(style.paddingRight) || 0
  const top = parseFloat(style.paddingTop) || 0
  const bottom = parseFloat(style.paddingBottom) || 0
  const area = { width: Math.max(0, box.width - left - right), height: Math.max(0, box.height - top - bottom) }
  return {
    picture: { width: image.offsetWidth, height: image.offsetHeight },
    area,
    centre: { x: box.left + left + area.width / 2, y: box.top + top + area.height / 2 },
  }
}

/** Zooms to `scale` around a point on the screen, or around the centre without one. */
function zoomTo(scale: number, client?: { x: number; y: number }): void {
  const g = geometry()
  if (!g) return
  const point = client ? { x: client.x - g.centre.x, y: client.y - g.centre.y } : { x: 0, y: 0 }
  view.value = zoomAt(view.value, scale, point, g.picture, g.area)
}

function zoomIn(): void {
  zoomTo(view.value.scale * ZOOM_STEP)
}

function zoomOut(): void {
  zoomTo(view.value.scale / ZOOM_STEP)
}

function fit(): void {
  view.value = FITTED
}

function pan(dx: number, dy: number): void {
  const g = geometry()
  if (g) view.value = panBy(view.value, dx, dy, g.picture, g.area)
}

/** Focus stays inside the viewer, so its own element hears the keys. */
function onKeydown(event: KeyboardEvent): void {
  if (event.ctrlKey || event.metaKey || event.altKey) return
  const g = zoomed.value ? geometry() : null
  const by = g ? { x: g.area.width * KEY_PAN_SHARE, y: g.area.height * KEY_PAN_SHARE } : null
  const handled = (() => {
    switch (event.key) {
      case '+':
      case '=':
        zoomIn()
        return true
      case '-':
      case '_':
        zoomOut()
        return true
      case '0':
        fit()
        return true
      case 'ArrowLeft':
        if (by) pan(by.x, 0)
        else step(-1)
        return true
      case 'ArrowRight':
        if (by) pan(-by.x, 0)
        else step(1)
        return true
      case 'ArrowUp':
        if (!by) return false
        pan(0, by.y)
        return true
      case 'ArrowDown':
        if (!by) return false
        pan(0, -by.y)
        return true
      default:
        return false
    }
  })()
  if (handled) event.preventDefault()
}

/** The wheel, and a trackpad pinch (which the browser sends as a wheel with Ctrl), zoom at the pointer. */
function onWheel(event: WheelEvent): void {
  if (!current.value?.src) return
  event.preventDefault()
  const unit = event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? 400 : 1
  const factor = Math.exp((-event.deltaY * unit) / (event.ctrlKey ? 100 : 400))
  zoomTo(view.value.scale * factor, { x: event.clientX, y: event.clientY })
}

function onDoubleClick(event: MouseEvent): void {
  if (zoomed.value) fit()
  else zoomTo(DOUBLE_CLICK_SCALE, { x: event.clientX, y: event.clientY })
}

/*
 * Pointers on the layer (not on its buttons): one drags a zoomed picture, two pinch. A press that
 * travelled is a drag, and the click it ends in (on the dark layer when it ended off the picture)
 * closes nothing. A press on the picture captures its pointer, so a drag that leaves the picture
 * keeps moving it.
 */
const pointers = new Map<number, { x: number; y: number }>()
const gesturing = ref(false)
let travelled = 0
let pinch: { distance: number; scale: number } | null = null
let swallowClick = false

function pointerDistance(): number {
  const [a, b] = [...pointers.values()]
  return a && b ? Math.hypot(a.x - b.x, a.y - b.y) : 0
}

function onPointerDown(event: PointerEvent): void {
  if (event.button !== 0 || !current.value?.src) return
  if (event.target instanceof Element && event.target.closest('button')) return
  pointers.set(event.pointerId, { x: event.clientX, y: event.clientY })
  if (event.target === picture.value) picture.value?.setPointerCapture?.(event.pointerId)
  travelled = 0
  swallowClick = false
  gesturing.value = true
  pinch = pointers.size === 2 ? { distance: pointerDistance(), scale: view.value.scale } : null
}

function onPointerMove(event: PointerEvent): void {
  const last = pointers.get(event.pointerId)
  if (!last) return
  const dx = event.clientX - last.x
  const dy = event.clientY - last.y
  pointers.set(event.pointerId, { x: event.clientX, y: event.clientY })
  travelled += Math.hypot(dx, dy)
  if (pinch && pointers.size === 2 && pinch.distance > 0) {
    const [a, b] = [...pointers.values()]
    zoomTo(pinch.scale * (pointerDistance() / pinch.distance), { x: (a!.x + b!.x) / 2, y: (a!.y + b!.y) / 2 })
    pan(dx / 2, dy / 2)
  } else if (zoomed.value) {
    pan(dx, dy)
  }
}

function onPointerEnd(event: PointerEvent): void {
  if (!pointers.delete(event.pointerId)) return
  if (travelled > DRAG_THRESHOLD_PX) swallowClick = true
  if (pointers.size < 2) pinch = null
  if (pointers.size === 0) gesturing.value = false
}

/** Only a pointer's click (`detail` > 0) ends a drag: Enter or Space on a button is never swallowed. */
function onClickCapture(event: MouseEvent): void {
  if (!swallowClick || event.detail === 0) return
  swallowClick = false
  event.stopPropagation()
}

function onBackdropClick(): void {
  close()
}
</script>

<template>
  <Teleport to="body">
    <div
      v-if="open"
      ref="root"
      class="t-image-viewer t-ai-scroll-scope"
      :class="{ 't-image-viewer--zoomed': zoomed, 't-image-viewer--gesturing': gesturing }"
      :style="{ zIndex }"
      role="dialog"
      aria-modal="true"
      :aria-label="name"
      tabindex="-1"
      data-image-viewer
      @click.capture="onClickCapture"
      @click.self="onBackdropClick"
      @keydown="onKeydown"
      @wheel="onWheel"
      @pointerdown="onPointerDown"
      @pointermove="onPointerMove"
      @pointerup="onPointerEnd"
      @pointercancel="onPointerEnd"
    >
      <img
        v-if="current?.src"
        ref="picture"
        class="t-image-viewer__image"
        :src="current.src"
        :alt="name"
        :style="pictureStyle"
        draggable="false"
        data-image-viewer-image
        @dblclick="onDoubleClick"
      />
      <p v-else class="t-image-viewer__failed" role="status">{{ t.failed }}</p>
      <button type="button" class="t-image-viewer__btn t-image-viewer__close" :aria-label="t.close" data-image-viewer-close @click="close">
        <Icon icon="lucide:x" aria-hidden="true" />
      </button>
      <template v-if="many">
        <button type="button" class="t-image-viewer__btn t-image-viewer__prev" :aria-label="t.previous" data-image-viewer-prev @click="step(-1)">
          <Icon icon="lucide:chevron-left" aria-hidden="true" />
        </button>
        <button type="button" class="t-image-viewer__btn t-image-viewer__next" :aria-label="t.next" data-image-viewer-next @click="step(1)">
          <Icon icon="lucide:chevron-right" aria-hidden="true" />
        </button>
      </template>
      <div class="t-image-viewer__bar">
        <template v-if="current?.src">
          <button type="button" class="t-image-viewer__btn t-image-viewer__tool" :aria-label="t.zoomOut" data-image-viewer-zoom-out @click="zoomOut">
            <Icon icon="lucide:zoom-out" aria-hidden="true" />
          </button>
          <button type="button" class="t-image-viewer__btn t-image-viewer__tool t-image-viewer__level" :aria-label="t.fit" data-image-viewer-fit @click="fit">
            {{ level }}
          </button>
          <button type="button" class="t-image-viewer__btn t-image-viewer__tool" :aria-label="t.zoomIn" data-image-viewer-zoom-in @click="zoomIn">
            <Icon icon="lucide:zoom-in" aria-hidden="true" />
          </button>
        </template>
        <span v-if="many" class="t-image-viewer__count" aria-live="polite">{{ counter }}</span>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.t-image-viewer {
  position: fixed;
  inset: 0;
  /* Its depth among the open dialogs is added inline (`useDialogLayer`). */
  z-index: var(--tnzi-ai-z-dialog);
  box-sizing: border-box;
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
  /* The picture's area starts below the page's top inset and ends above the zoom bar; the layer itself covers it. */
  padding: calc(48px + var(--tnzi-overlay-inset-top, 0px)) 16px 64px;
  background: var(--tnzi-ai-image-viewer-backdrop);
  font-family: var(--tnzi-ai-font-body, inherit);
  outline: none;
  /* The viewer zooms and pans itself; the browser's pinch and scroll would move the page under it. */
  touch-action: none;
  user-select: none;
  -webkit-user-select: none;
}
.t-image-viewer__image {
  max-width: 100%;
  max-height: 100%;
  object-fit: contain;
  border-radius: 8px;
  box-shadow: var(--tnzi-ai-dialog-shadow);
  transform-origin: center;
  transition: transform var(--tnzi-ai-duration-fast, 120ms) var(--tnzi-ai-easing, ease);
  cursor: zoom-in;
  -webkit-user-drag: none;
}
.t-image-viewer--zoomed .t-image-viewer__image {
  cursor: grab;
}
.t-image-viewer--gesturing .t-image-viewer__image {
  transition: none;
}
.t-image-viewer--zoomed.t-image-viewer--gesturing .t-image-viewer__image {
  cursor: grabbing;
}
@media (prefers-reduced-motion: reduce) {
  .t-image-viewer__image {
    transition: none;
  }
}
.t-image-viewer__failed {
  margin: 0;
  color: var(--tnzi-ai-on-image);
}
.t-image-viewer__btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 40px;
  height: 40px;
  padding: 0;
  border: none;
  border-radius: 50%;
  /* A lighter shade of the backdrop rather than a white tint: the chip stays dark where a zoomed
     picture passes under it, so the white icon reads on the backdrop and on any picture alike. */
  background: color-mix(in srgb, var(--tnzi-ai-on-image) 12%, var(--tnzi-ai-image-viewer-backdrop));
  color: var(--tnzi-ai-on-image);
  font: inherit;
  font-size: 22px;
  cursor: pointer;
}
.t-image-viewer__btn:hover {
  background: color-mix(in srgb, var(--tnzi-ai-on-image) 24%, var(--tnzi-ai-image-viewer-backdrop));
}
.t-image-viewer__btn:focus-visible {
  outline: var(--tnzi-ai-focus-outline);
  outline-offset: 2px;
}
.t-image-viewer__close,
.t-image-viewer__prev,
.t-image-viewer__next {
  position: absolute;
}
.t-image-viewer__close {
  top: calc(12px + var(--tnzi-overlay-inset-top, 0px));
  right: 12px;
}
/* Centred on the picture's area, which is shifted down by the inset and up by the bar. */
.t-image-viewer__prev,
.t-image-viewer__next {
  top: calc(50% + var(--tnzi-overlay-inset-top, 0px) / 2 - 8px);
  transform: translateY(-50%);
}
.t-image-viewer__prev {
  left: 12px;
}
.t-image-viewer__next {
  right: 12px;
}
.t-image-viewer__bar {
  position: absolute;
  bottom: 12px;
  left: 50%;
  transform: translateX(-50%);
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 4px;
  border-radius: 999px;
  background: var(--tnzi-ai-image-viewer-backdrop);
}
.t-image-viewer__bar:empty {
  display: none;
}
.t-image-viewer__tool {
  width: 36px;
  height: 36px;
  font-size: 18px;
}
.t-image-viewer__level {
  width: auto;
  min-width: 56px;
  padding: 0 8px;
  border-radius: 18px;
  font-size: 13px;
  font-variant-numeric: tabular-nums;
}
.t-image-viewer__count {
  padding: 0 10px 0 6px;
  color: color-mix(in srgb, var(--tnzi-ai-on-image) 80%, transparent);
  font-size: 13px;
  font-variant-numeric: tabular-nums;
}
</style>
