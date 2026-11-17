<script setup lang="ts">
/**
 * TPopover - a small floating panel of content beside a trigger: the details
 * of a merge's state, a definition, a short form. Not a menu: for a list of
 * actions use `TDropdownMenu`.
 *
 * ```vue
 * <TPopover label="Merge status">
 *   <template #trigger="{ toggle, attrs }">
 *     <TButton variant="ghost" v-bind="attrs" @click="toggle">Checks passed</TButton>
 *   </template>
 *   <MergeDetails />
 * </TPopover>
 * ```
 *
 * The panel is a non-modal dialog (`role="dialog"`, named by `label`, or by
 * an element of its content through `labelledBy`) on the popover layer
 * (`--tnzi-ai-z-popover`), `position: fixed`, so a scrolling list or a
 * dialog's body does not clip it. It goes on `side` of the trigger (`bottom`
 * by default) lined up by `align` (`start`, `center`, `end`), flipping to the
 * other side when there is no room, and follows the trigger on scroll and
 * resize.
 *
 * It stays in the DOM right after the trigger, so the Tab order runs from the
 * trigger into the panel and on to the page after it. The `#trigger` slot
 * gives `attrs` (`aria-haspopup="dialog"`, `aria-expanded`, `aria-controls`)
 * for the trigger button.
 *
 * Keyboard and closing: opening moves focus into the panel (to an `autofocus`
 * / `data-autofocus` element, else the panel itself, so a screen reader reads
 * it and Tab goes on to its first control). Escape closes it and focus goes
 * back to the trigger; Tab out of it, a press outside it, or `close()` from
 * the default slot close it too. On a phone the back gesture closes it.
 */
import { computed, nextTick, onBeforeUnmount, ref, useId, watch } from 'vue'
import { placeBeside, type FloatingAlign, type FloatingSide } from '../../headless/place-beside'
import { useBackLayer } from '../../headless/back-layers'
import { useViewportBelow } from '../../headless/useViewportBelow'
import { afterMenuPlaced } from './menu-keys'

/* `class`, `data-*` and the like go on the panel, not on the wrapper. */
defineOptions({ inheritAttrs: false })

const props = withDefaults(
  defineProps<{
    /** Accessible name of the panel. */
    label?: string
    /** Id of an element in the content that names the panel, instead of `label`. */
    labelledBy?: string
    side?: FloatingSide
    align?: FloatingAlign
    /** Width of the panel (number = px); by default it fits its content, up to 360px. */
    width?: number | string
    /** Space between trigger and panel. */
    gap?: number
  }>(),
  { label: '', labelledBy: undefined, side: 'bottom', align: 'start', width: undefined, gap: 6 },
)

const emit = defineEmits<{ 'update:open': [open: boolean] }>()

defineSlots<{
  trigger?: (props: { open: boolean; toggle: () => void; attrs: Record<string, string | boolean> }) => unknown
  default?: (props: { close: () => void }) => unknown
}>()

/** Below this width back closes it: the shell's phone layout. */
const NARROW_BREAKPOINT = 768
const EDGE = 8

const open = ref(false)
const placed = ref(false)
const root = ref<HTMLElement | null>(null)
const trigger = ref<HTMLElement | null>(null)
const panel = ref<HTMLElement | null>(null)
const panelId = `t-popover-${useId()}`

const triggerAttrs = computed(() => ({
  'aria-haspopup': 'dialog',
  'aria-expanded': open.value,
  'aria-controls': panelId,
}))

const panelStyle = computed(() => {
  if (props.width === undefined || props.width === '') return undefined
  return { width: typeof props.width === 'number' ? `${props.width}px` : props.width }
})

function triggerElement(): HTMLElement | null {
  return trigger.value?.querySelector<HTMLElement>('button, a[href], [tabindex]') ?? null
}

function place(): void {
  const el = panel.value
  const anchor = triggerElement() ?? trigger.value
  if (!el || !anchor || typeof window === 'undefined') return
  el.style.top = '0px'
  el.style.left = '0px'
  /* Measured at the origin: a transformed ancestor moves a fixed element's
     origin, and the difference is taken back out below. */
  const origin = el.getBoundingClientRect()
  const box = anchor.getBoundingClientRect()
  const position = placeBeside(
    { top: box.top, left: box.left, width: box.width, height: box.height },
    { width: el.offsetWidth, height: el.offsetHeight },
    { width: window.innerWidth, height: window.innerHeight },
    { side: props.side, align: props.align, gap: props.gap, edge: EDGE },
  )
  el.style.top = `${position.top - origin.top}px`
  el.style.left = `${position.left - origin.left}px`
  el.dataset.side = position.side
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

function close(returnFocus = true): void {
  if (!open.value) return
  open.value = false
  if (returnFocus) void nextTick(() => triggerElement()?.focus())
}

function toggle(): void {
  if (open.value) close(false)
  else open.value = true
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key !== 'Escape' || !open.value || event.isComposing) return
  event.preventDefault()
  event.stopPropagation()
  close()
}

/* Focus that leaves both the trigger and the panel (Tab past the end) closes it. */
function onFocusOut(event: FocusEvent): void {
  const to = event.relatedTarget
  if (!open.value || !(to instanceof Node)) return
  if (root.value?.contains(to)) return
  close(false)
}

function onPointerDown(event: PointerEvent): void {
  if (event.target instanceof Node && root.value?.contains(event.target)) return
  close(false)
}

function initialFocus(): HTMLElement | null {
  return panel.value?.querySelector<HTMLElement>('[autofocus], [data-autofocus]') ?? panel.value
}

function listen(): void {
  document.addEventListener('pointerdown', onPointerDown, true)
  window.addEventListener('resize', schedulePlace)
  window.addEventListener('scroll', schedulePlace, true)
}
function unlisten(): void {
  document.removeEventListener('pointerdown', onPointerDown, true)
  window.removeEventListener('resize', schedulePlace)
  window.removeEventListener('scroll', schedulePlace, true)
  if (frame) window.cancelAnimationFrame(frame)
  frame = 0
  placed.value = false
}

watch(open, async (isOpen) => {
  emit('update:open', isOpen)
  if (!isOpen) {
    unlisten()
    return
  }
  listen()
  await nextTick()
  place()
  /* Focus on the next frame: the panel is hidden until placed, and a browser
     does not focus what it cannot see. */
  afterMenuPlaced(() => {
    if (open.value) initialFocus()?.focus({ preventScroll: true })
  })
})

const narrowViewport = useViewportBelow(NARROW_BREAKPOINT)
useBackLayer(() => open.value, () => close(), { enabled: narrowViewport })

onBeforeUnmount(unlisten)

defineExpose({ close: () => close(false), open: () => (open.value = true), place })
</script>

<template>
  <div ref="root" class="t-popover-root" @keydown="onKeydown" @focusout="onFocusOut">
    <div ref="trigger" class="t-popover-root__trigger">
      <slot name="trigger" :open="open" :toggle="toggle" :attrs="triggerAttrs" />
    </div>
    <div
      v-if="open"
      :id="panelId"
      ref="panel"
      v-bind="$attrs"
      class="t-popover t-ai-scroll"
      :class="{ 't-popover--placing': !placed }"
      :style="panelStyle"
      role="dialog"
      :aria-label="labelledBy ? undefined : label || undefined"
      :aria-labelledby="labelledBy"
      tabindex="-1"
      data-popover
    >
      <slot :close="() => close()" />
    </div>
  </div>
</template>

<style scoped>
.t-popover-root {
  display: inline-flex;
}
.t-popover-root__trigger {
  display: inline-flex;
}
.t-popover {
  position: fixed;
  top: 0;
  left: 0;
  z-index: var(--tnzi-ai-z-popover, 2000);
  box-sizing: border-box;
  max-width: min(360px, calc(100vw - 16px));
  max-height: calc(100vh - 16px);
  overflow-y: auto;
  padding: 12px 14px;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: var(--tnzi-ai-radius-lg, 12px);
  background: var(--tnzi-ai-surface);
  box-shadow: 0 12px 32px rgba(0, 0, 0, 0.08);
  color: var(--tnzi-ai-text);
  font-family: var(--tnzi-ai-font-body, inherit);
  font-size: 13px;
  line-height: 1.5;
  text-align: left;
}
.t-popover:focus {
  outline: none;
}
.t-popover--placing {
  visibility: hidden;
}
</style>
