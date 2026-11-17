<script setup lang="ts">
/**
 * @experimental
 * TPopoverMenu - Floating dropdown menu container.
 *
 * Pure layout primitive for any "click a button to open a small list of
 * actions" pattern. Provides:
 *
 *   - `open` v-model + outside-click auto-close
 *   - Optional header label slot
 *   - Default slot for menu items (use `.t-popover-menu__item` class)
 *   - Anchored to the closest positioned ancestor, like an absolutely
 *     positioned menu, but drawn `position: fixed` on the popover layer
 *     (`--tnzi-ai-z-popover`): a menu inside a scrolling list or a dialog's
 *     body is not clipped by them and sits above the dialog. It stays where it
 *     is in the DOM, so a consumer that finds its items or handles keys from
 *     an ancestor keeps working. `teleport` moves it to `<body>` for the rare
 *     ancestor that traps a fixed element (a stacking context below the
 *     content it must cover)
 *
 * On a narrow screen (below 768px, the shell's phone layout) the back button
 * or gesture closes the menu instead of leaving the page (`useBackLayer`).
 *
 * The menu itself is a `TMenuPanel`, which this places: the entries are
 * `TMenuItem` / `TMenuSeparator`, or raw buttons wearing the item classes
 * (`.t-popover-menu__item` / `__item-value` / `__item-check` ...). A menu
 * that does not float (a tray window) uses `TMenuPanel` directly and looks
 * the same.
 *
 * The component does NOT manage trigger button state - wrap your trigger
 * + this menu in a positioned parent and toggle `open` from the trigger's
 * click handler.
 */
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import TMenuPanel from './TMenuPanel.vue'
import { useBackLayer } from '../../headless/back-layers'
import { enableTouchPress } from '../../headless/touch-press'
import { useViewportBelow } from '../../headless/useViewportBelow'

const props = withDefaults(
  defineProps<{
    /** Whether the menu is currently visible. v-model. */
    modelValue: boolean
    /** Optional header label shown at the top of the menu. */
    label?: string
    /** Min-width of the menu. Default 200px. */
    minWidth?: number
    /** Max-width - wider for menus with longer item text. Default 320px. */
    maxWidth?: number
    /**
     * Anchor side. `'right'` aligns the menu's right edge with the
     * anchor's right edge; `'left'` aligns the left edges. Default
     * `'right'` since most action menus open from a right-side trigger.
     */
    align?: 'left' | 'right'
    /**
     * Which way the menu opens. `'bottom'` (default) drops below the
     * anchor; `'top'` rises above it, which is what a trigger pinned to
     * the bottom of a sidebar needs. Either flips when there is no room on
     * its side and there is on the other.
     */
    placement?: 'top' | 'bottom'
    /**
     * Span the anchor's full width instead of sizing to `minWidth` /
     * `maxWidth`, which then do not apply. This is what a menu anchored to a
     * full-width row wants: sized to its trigger it reads as part of that
     * column, whereas a fixed width anchored to one edge reads as pinned to
     * whatever is behind it - in a sidebar footer, the window edge.
     */
    fullWidth?: boolean
    /**
     * Whether clicking outside closes the menu. Default `true`. Set
     * `false` if the consumer wants to manage close behavior manually
     * (e.g. nested menus that need a click-through pattern).
     */
    closeOnOutsideClick?: boolean
    /**
     * Render the menu on `<body>`. Off by default, so the menu stays inside
     * the component that opened it. Turn it on only when an ancestor makes a
     * stacking context lower than what the menu must cover; the menu's items
     * are then no longer inside that component's element.
     */
    teleport?: boolean
    /**
     * Open at a point of the viewport (`clientX` / `clientY` of a right click)
     * instead of beside the anchor's box: the menu's top left corner goes
     * there, and it opens up or to the left of the point when it does not fit
     * below or to the right. `TContextMenu` sets it.
     */
    at?: { x: number; y: number } | null
  }>(),
  {
    label: '',
    minWidth: 200,
    maxWidth: 320,
    align: 'right',
    placement: 'bottom',
    fullWidth: false,
    closeOnOutsideClick: true,
    teleport: false,
    at: null,
  },
)

const emit = defineEmits<{
  'update:modelValue': [open: boolean]
}>()

/* `class` and the like belong on the menu, not on the zero-size anchor. */
defineOptions({ inheritAttrs: false })

const anchor = ref<HTMLSpanElement | null>(null)
const panel = ref<InstanceType<typeof TMenuPanel> | null>(null)
const root = computed<HTMLDivElement | null>(() => (panel.value?.$el as HTMLDivElement | undefined) ?? null)
const placed = ref(false)

const GAP = 6
const EDGE = 8
/** Below this width back closes the menu: the shell's phone layout (`sidebarMobileBreakpoint`). */
const NARROW_BREAKPOINT = 768

/* The box the menu lines up with: the anchor's offset parent, the box an
   absolutely positioned menu used to be placed in. */
type ReferenceBox = Pick<DOMRect, 'left' | 'right' | 'top' | 'bottom' | 'width'>

function referenceRect(): ReferenceBox | null {
  if (props.at) return { left: props.at.x, right: props.at.x, top: props.at.y, bottom: props.at.y, width: 0 }
  const parent = anchor.value?.offsetParent ?? anchor.value?.parentElement
  return parent instanceof HTMLElement ? parent.getBoundingClientRect() : null
}

/**
 * Lines the menu up with its reference box, in viewport coordinates, flipped
 * to the other side when it does not fit and kept inside the window. A fixed
 * element is placed relative to its containing block, which is the viewport
 * unless an ancestor is transformed or filtered (a dialog's backdrop blur is);
 * the menu is measured at the origin first, so either way it lands where
 * intended.
 */
function place(): void {
  const menu = root.value
  const box = referenceRect()
  if (!menu || !box || typeof window === 'undefined') return
  menu.style.top = '0px'
  menu.style.left = '0px'
  if (props.fullWidth) menu.style.width = `${box.width}px`
  const origin = menu.getBoundingClientRect()
  const width = menu.offsetWidth
  const height = menu.offsetHeight
  const viewportW = window.innerWidth
  const viewportH = window.innerHeight

  let left = props.fullWidth || props.align === 'left' ? box.left : box.right - width
  /* At a point: to the left of it when there is no room to its right. */
  if (props.at && left + width > viewportW - EDGE && box.left - width >= EDGE) left = box.left - width
  left = Math.min(Math.max(EDGE, left), Math.max(EDGE, viewportW - width - EDGE))

  const gap = props.at ? 0 : GAP
  const below = box.bottom + gap
  const above = box.top - gap - height
  const fitsBelow = below + height <= viewportH - EDGE
  const fitsAbove = above >= EDGE
  const wantsTop = props.placement === 'top'
  let top = wantsTop ? (fitsAbove || !fitsBelow ? above : below) : fitsBelow || !fitsAbove ? below : above
  top = Math.max(EDGE, Math.min(top, viewportH - height - EDGE))

  menu.style.top = `${top - origin.top}px`
  menu.style.left = `${left - origin.left}px`
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

function onDocumentClick(e: MouseEvent): void {
  if (!props.modelValue) return
  if (!props.closeOnOutsideClick) return
  const target = e.target as Node | null
  if (root.value && target && !root.value.contains(target)) {
    emit('update:modelValue', false)
  }
}

function listen(): void {
  // Defer one tick so the click that OPENED the menu doesn't
  // immediately bubble up and close it on the same event loop.
  setTimeout(() => document.addEventListener('click', onDocumentClick), 0)
  window.addEventListener('resize', schedulePlace)
  // Capture: a scroll in any ancestor moves the anchor.
  window.addEventListener('scroll', schedulePlace, true)
  void nextTick(place)
}

function unlisten(): void {
  document.removeEventListener('click', onDocumentClick)
  window.removeEventListener('resize', schedulePlace)
  window.removeEventListener('scroll', schedulePlace, true)
  if (frame) window.cancelAnimationFrame(frame)
  frame = 0
  placed.value = false
}

watch(
  () => props.modelValue,
  (open) => {
    if (open) listen()
    else unlisten()
  },
)
/* Opened again at another point while open: move there. */
watch(
  () => props.at,
  () => {
    if (props.modelValue) void nextTick(place)
  },
)

onMounted(() => {
  if (props.modelValue) listen()
  /* A press on an item shows on iOS too (`styles/touch.css`). */
  enableTouchPress()
})

/* Back closes the menu on a phone, where back is how a layer is dismissed. */
const narrowViewport = useViewportBelow(NARROW_BREAKPOINT)
useBackLayer(() => props.modelValue, () => emit('update:modelValue', false), { enabled: narrowViewport })

onBeforeUnmount(unlisten)

defineExpose({ place })
</script>

<template>
  <span ref="anchor" class="t-popover-menu-anchor">
    <Teleport to="body" :disabled="!teleport">
      <TMenuPanel
        v-if="modelValue"
        ref="panel"
        v-bind="$attrs"
        class="t-popover-menu t-ai-scroll"
        :class="{ 't-popover-menu--placing': !placed }"
        :style="fullWidth ? undefined : { minWidth: `${minWidth}px`, maxWidth: `${maxWidth}px` }"
        :label="label"
        :keyboard="false"
        @click.stop
      >
        <slot />
      </TMenuPanel>
    </Teleport>
  </span>
</template>

<style scoped>
/* A zero-size box where the component sits. Its offset parent (the closest
   positioned ancestor) is the box the menu lines up with, as an absolutely
   positioned menu used to. */
.t-popover-menu-anchor {
  position: absolute;
  width: 0;
  height: 0;
  overflow: visible;
  pointer-events: none;
}
/* Fixed, not absolute: an absolutely positioned menu was clipped by any
   scrolling or `overflow: hidden` ancestor between it and its anchor (a menu
   near the bottom of a dialog's body was cut off by the body and the panel).
   Placed by script, on the popover layer. */
/* The panel and its entries are `TMenuPanel`'s; this adds the floating. */
.t-popover-menu {
  position: fixed;
  top: 0;
  left: 0;
  z-index: var(--tnzi-ai-z-popover, 2000);
  max-height: calc(100vh - 16px);
  overflow-y: auto;
  pointer-events: auto;
  box-shadow: var(--tnzi-ai-popover-shadow);
}
.t-popover-menu--placing {
  visibility: hidden;
}
</style>
