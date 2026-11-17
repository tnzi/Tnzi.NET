<script setup lang="ts">
/**
 * @experimental
 * TRailFlyout - a rail button that opens a panel beside the rail.
 *
 * For a destination that is a list rather than a page (the recent
 * conversations, a product's sessions): collapsing the sidebar to its rail
 * must not put that list out of reach, and a rail button cannot hold it.
 *
 * The panel opens on hover (after a short rest), on keyboard focus, or on a
 * click; the rules for when each way closes again are `headless/flyout.ts`.
 * The pointer can travel from the button into the panel: the panel stays in
 * the DOM right after the button, inside the same element, and an invisible
 * bridge covers the gap between them, so neither leaving the button nor
 * crossing the gap counts as leaving; leaving both closes it after a grace
 * period. Escape closes it and puts focus back on the button. ArrowDown or
 * ArrowRight on the button moves focus into the panel, and Tab reaches it
 * from the button in document order.
 *
 * The panel is `position: fixed` (an `overflow: hidden` sidebar cannot clip
 * it) on the popover layer, beside the button and lined up with its top, kept
 * inside the window and scrolling when it is taller than that.
 *
 * The default slot is the panel's content and receives `close()`: call it when
 * the user picks something.
 */
import { computed, nextTick, onBeforeUnmount, ref, useId, watch } from 'vue'
import TRailButton from './TRailButton.vue'
import type { IconMotion } from '../../headless/icon-motion'
import { createFlyoutController, type FlyoutState } from '../../headless/flyout'
import { inputModality, trackInputModality } from '../../headless/input-modality'
import { placeBeside } from '../../headless/place-beside'

const props = withDefaults(
  defineProps<{
    /** The button's accessible name and the panel's heading. */
    label: string
    icon?: string
    iconMotion?: IconMotion
    /** Drawn as the current destination. */
    active?: boolean
    /** Width of the panel in px. */
    width?: number
    /** Pointer rest before a hover opens it, ms. */
    openDelay?: number
    /** Grace period after the pointer leaves the button and the panel, ms. */
    closeDelay?: number
    /** Show `label` as a heading at the top of the panel. */
    showHeading?: boolean
  }>(),
  {
    icon: undefined,
    iconMotion: undefined,
    active: false,
    width: 280,
    openDelay: undefined,
    closeDelay: undefined,
    showHeading: true,
  },
)

const emit = defineEmits<{
  /** The panel opened or closed. */
  'update:open': [open: boolean]
}>()

trackInputModality()

const root = ref<HTMLDivElement | null>(null)
const panel = ref<HTMLDivElement | null>(null)
const trigger = ref<InstanceType<typeof TRailButton> | null>(null)
const state = ref<FlyoutState>('closed')
const placed = ref(false)
const panelId = `t-rail-flyout-${useId()}`

const controller = createFlyoutController({
  openDelay: props.openDelay,
  closeDelay: props.closeDelay,
  onChange: (next) => (state.value = next),
})

const isOpen = computed(() => state.value !== 'closed')

function triggerElement(): HTMLButtonElement | null {
  const el = (trigger.value as { $el?: unknown } | null)?.$el
  return el instanceof HTMLButtonElement ? el : null
}

function focusInside(): boolean {
  return !!panel.value && panel.value.contains(document.activeElement)
}

/* Focus put back on the button by the flyout itself must not reopen it the
   way a user tabbing onto the button does. */
let returningFocus = false
function returnFocus(): void {
  returningFocus = true
  try {
    triggerElement()?.focus()
  } finally {
    returningFocus = false
  }
}

/** Closes the panel; focus inside it goes back to the button. */
function close(): void {
  const restore = focusInside()
  controller.close()
  if (restore) returnFocus()
}

function focusables(): HTMLElement[] {
  if (!panel.value) return []
  return Array.from(
    panel.value.querySelectorAll<HTMLElement>('button:not([disabled]), a[href], input:not([disabled]), [tabindex]:not([tabindex="-1"])'),
  )
}

async function focusFirst(): Promise<void> {
  controller.openPinned()
  await nextTick()
  focusables()[0]?.focus()
}

function place(): void {
  const button = triggerElement()
  const surface = panel.value
  if (!button || !surface || typeof window === 'undefined') return
  surface.style.top = '0px'
  surface.style.left = '0px'
  /* Measured at the origin first: a transformed ancestor would make itself the
     containing block of a fixed element, and this keeps the panel where it is
     meant to be either way. */
  const origin = surface.getBoundingClientRect()
  const box = button.getBoundingClientRect()
  const position = placeBeside(
    { top: box.top, left: box.left, width: box.width, height: box.height },
    { width: surface.offsetWidth, height: surface.offsetHeight },
    { width: window.innerWidth, height: window.innerHeight },
    { side: 'right', align: 'start', gap: 0, edge: 8 },
  )
  surface.style.top = `${position.top - origin.top}px`
  surface.style.left = `${position.left - origin.left}px`
  surface.dataset.side = position.side
  placed.value = true
}

let frame = 0
function schedulePlace(): void {
  if (frame || typeof window === 'undefined') return
  frame = window.requestAnimationFrame(() => {
    frame = 0
    place()
  })
}

function onDocumentPointerDown(event: PointerEvent): void {
  const target = event.target as Node | null
  if (root.value && target && !root.value.contains(target)) controller.close()
}

function listen(): void {
  document.addEventListener('pointerdown', onDocumentPointerDown, true)
  window.addEventListener('resize', schedulePlace)
  window.addEventListener('scroll', schedulePlace, true)
  void nextTick(place)
}

function unlisten(): void {
  document.removeEventListener('pointerdown', onDocumentPointerDown, true)
  window.removeEventListener('resize', schedulePlace)
  window.removeEventListener('scroll', schedulePlace, true)
  if (frame) window.cancelAnimationFrame(frame)
  frame = 0
  placed.value = false
}

watch(isOpen, (open) => {
  if (open) listen()
  else unlisten()
  emit('update:open', open)
})

onBeforeUnmount(() => {
  controller.destroy()
  unlisten()
})

function onPointerEnter(event: PointerEvent): void {
  controller.pointerEnter(event.pointerType)
}

function onFocusIn(): void {
  if (returningFocus) return
  controller.focusIn(inputModality() === 'keyboard')
}

function onFocusOut(event: FocusEvent): void {
  const next = event.relatedTarget as Node | null
  if (next && root.value?.contains(next)) return
  controller.focusOut()
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape' && isOpen.value) {
    event.preventDefault()
    event.stopPropagation()
    controller.close()
    returnFocus()
    return
  }
  const onButton = event.target === triggerElement()
  if (onButton && (event.key === 'ArrowDown' || event.key === 'ArrowRight')) {
    event.preventDefault()
    void focusFirst()
    return
  }
  if (!focusInside() || (event.key !== 'ArrowDown' && event.key !== 'ArrowUp')) return
  const items = focusables()
  const index = items.indexOf(document.activeElement as HTMLElement)
  if (index < 0) return
  event.preventDefault()
  const step = event.key === 'ArrowDown' ? 1 : -1
  items[(index + step + items.length) % items.length]?.focus()
}

defineExpose({ close, place, open: isOpen })
</script>

<template>
  <div
    ref="root"
    class="t-rail-flyout"
    @pointerenter="onPointerEnter"
    @pointerleave="controller.pointerLeave()"
    @focusin="onFocusIn"
    @focusout="onFocusOut"
    @keydown="onKeydown"
  >
    <TRailButton
      ref="trigger"
      :label="label"
      :icon="icon"
      :icon-motion="iconMotion"
      :active="active || isOpen"
      :tooltip="false"
      aria-haspopup="dialog"
      :aria-expanded="isOpen"
      :aria-controls="isOpen ? panelId : undefined"
      @click="controller.click()"
    >
      <slot name="icon" />
    </TRailButton>
    <div
      v-if="isOpen"
      :id="panelId"
      ref="panel"
      class="t-rail-flyout__panel"
      :class="{ 't-rail-flyout__panel--placing': !placed }"
      role="dialog"
      :aria-label="label"
    >
      <div class="t-rail-flyout__surface t-ai-scroll" :style="{ width: `${width}px` }">
        <div v-if="showHeading" class="t-rail-flyout__heading">{{ label }}</div>
        <slot :close="close" />
      </div>
    </div>
  </div>
</template>

<style scoped>
.t-rail-flyout {
  position: relative;
  display: flex;
  justify-content: center;
}
/* The fixed, non-scrolling box placed against the button. Its padding on the
   button's side is the bridge: transparent, but part of the flyout for hit
   testing, so the pointer crossing the gap between the button and the
   surface never leaves the flyout. */
.t-rail-flyout__panel {
  position: fixed;
  top: 0;
  left: 0;
  z-index: var(--tnzi-ai-z-popover, 2000);
  box-sizing: content-box;
  padding-left: var(--t-rail-flyout-gap, 8px);
  cursor: default;
}
.t-rail-flyout__panel[data-side='left'] {
  padding-left: 0;
  padding-right: var(--t-rail-flyout-gap, 8px);
}
.t-rail-flyout__panel--placing {
  visibility: hidden;
}
.t-rail-flyout__surface {
  position: relative;
  box-sizing: border-box;
  max-height: min(70vh, 520px);
  overflow-y: auto;
  overscroll-behavior: contain;
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 6px;
  background: var(--tnzi-ai-surface, #ffffff);
  border: 1px solid var(--tnzi-ai-border, #e5e5e5);
  border-radius: 12px;
  box-shadow: 0 12px 32px rgba(0, 0, 0, 0.08);
  color: var(--tnzi-ai-text, inherit);
  font-family: var(--tnzi-ai-font-body, inherit);
  text-align: left;
}
.t-rail-flyout__heading {
  padding: 6px 10px 4px;
  font-size: 11px;
  font-weight: 500;
  color: var(--tnzi-ai-text-tertiary, #9a9a9a);
  letter-spacing: 0.02em;
}
</style>
