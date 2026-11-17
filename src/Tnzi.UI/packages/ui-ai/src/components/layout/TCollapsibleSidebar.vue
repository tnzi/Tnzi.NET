<script setup lang="ts">
/**
 * @experimental
 * TCollapsibleSidebar - three-mode sidebar frame for chat applications.
 *
 * Modes:
 *   - expanded: full-width sidebar with all slots visible
 *   - icon: collapsed rail showing only icons (uses `rail` slot)
 *   - hidden: off-canvas; on mobile renders as an overlay drawer
 *
 * Slot-only design: consumers provide header/nav/content/footer/rail markup.
 * Import `useSidebarState` from `@tnzi/ui-ai/headless` for the state machine.
 */
import { computed, ref } from 'vue'
import type { SidebarMode } from '../../headless/useSidebarState'
import { useBodyScrollLock } from '../../headless/useBodyScrollLock'
import { useBackLayer } from '../../headless/back-layers'
import { closestOnPath, trackEventPaths } from '../../headless/event-path'

const props = withDefaults(
  defineProps<{
    modelValue: SidebarMode
    width?: number
    railWidth?: number
    isMobile?: boolean
  }>(),
  {
    width: 280,
    railWidth: 56,
    isMobile: false,
  },
)

const emit = defineEmits<{
  'update:modelValue': [mode: SidebarMode]
}>()

/* What is drawn. A drawer is never the icon rail: on a narrow screen an open
   sidebar is the full one (header, nav, conversations, account), whatever mode
   a caller hands in. `useSidebarState` already never sets `icon` while narrow;
   this covers a consumer driving the mode itself. */
const shownMode = computed<SidebarMode>(() =>
  props.isMobile && props.modelValue === 'icon' ? 'expanded' : props.modelValue,
)

const currentWidth = computed(() => {
  if (props.isMobile) return undefined
  if (shownMode.value === 'hidden') return 0
  if (shownMode.value === 'icon') return props.railWidth
  return props.width
})

const isDrawer = computed(() => props.isMobile && props.modelValue !== 'hidden')

/* On a narrow screen the sidebar is never part of the page's flow, open or
   closed: it stays `position: fixed` at its full width and slides in and out
   with a transform, which moves nothing else. It used to collapse to
   `width: 0` in the flow and become fixed only while open, so closing it put
   it back in the row for the length of the width animation: the main column
   jumped to 110px wide and reflowed its text on every frame back to full width. */

/* Hidden mode collapses the sidebar to `width: 0` + `overflow: hidden`, which
   hides it visually but leaves its buttons in the tab order. `inert` takes
   them out of the tab order and the a11y tree together, so `aria-hidden` no
   longer contradicts what a keyboard user can reach. */
const isFullyHidden = computed(() => props.modelValue === 'hidden' && !isDrawer.value)

function closeFromBackdrop(): void {
  if (isDrawer.value) {
    emit('update:modelValue', 'hidden')
  }
}

/* The back button or gesture closes the open drawer, as in a native app,
   instead of leaving the page (`useBackLayer`). Only the drawer: on a wide
   screen the sidebar is part of the page. */
useBackLayer(isDrawer, () => {
  emit('update:modelValue', 'hidden')
})

/**
 * In icon (collapsed) mode, clicking an empty area of the rail expands the
 * sidebar - matches Manus's "whole nav is a cursor-e-resize click target"
 * behavior. Clicks that land on an interactive descendant (button / link /
 * input / [role=button]) keep their own semantics and DO NOT expand.
 */
trackEventPaths()

function handleRailClick(e: MouseEvent): void {
  if (shownMode.value !== 'icon') return
  /* The path the click took: a button's handler may have re-rendered it (its
     icon swapped) before this runs, leaving `target` detached. */
  if (closestOnPath(e, 'button,a,input,[role="button"],[data-no-expand]', e.currentTarget as Element)) return
  emit('update:modelValue', 'expanded')
}

/* A pinned `nav` region and the scrolling content read as one column until
   something scrolls under the seam - then the two need separating, or rows
   appear to slide into the pinned block. Only once scrolled: an unconditional
   rule would draw a line across a sidebar with nothing to scroll. The
   scrollbar is hidden here (see `__content` below), so this line is also the
   only remaining hint that there is more below. */
const contentEl = ref<HTMLElement | null>(null)
const contentScrolled = ref(false)

function onContentScroll(): void {
  contentScrolled.value = (contentEl.value?.scrollTop ?? 0) > 0
}

/* The mobile drawer covers the page, so the page behind it must not scroll.
   The shared lock restores whatever overflow the host had and releases on
   unmount: without that, unmounting mid-drawer (a route change while the
   drawer is open) left the page permanently unscrollable. */
useBodyScrollLock(isDrawer)
</script>

<template>
  <!-- Always there on a narrow screen, so it fades in and out in the same
       style change as the drawer's slide (a `<transition>` starts a frame
       later than a class change). Closed, it lets clicks through. -->
  <div
    v-if="isMobile"
    class="t-collapsible-sidebar__backdrop"
    :class="{ 'is-open': isDrawer }"
    aria-hidden="true"
    @click="closeFromBackdrop"
  />

  <aside
    class="t-collapsible-sidebar"
    :class="{
      't-collapsible-sidebar--expanded': shownMode === 'expanded',
      't-collapsible-sidebar--icon': shownMode === 'icon',
      't-collapsible-sidebar--hidden': modelValue === 'hidden',
      't-collapsible-sidebar--mobile': isMobile,
      't-collapsible-sidebar--drawer': isDrawer,
    }"
    :style="currentWidth === undefined ? undefined : { width: `${currentWidth}px` }"
    :aria-hidden="isFullyHidden"
    :inert="isFullyHidden || undefined"
    @click="handleRailClick"
  >
    <div class="t-collapsible-sidebar__inner">
      <!-- Header / nav / footer slots only render in expanded mode; in icon
           mode the rail slot owns the full height and provides its own
           brand mark / footer icons. -->
      <header
        v-if="$slots.header && shownMode !== 'icon'"
        class="t-collapsible-sidebar__header"
      >
        <slot name="header" />
      </header>

      <div
        v-if="$slots.nav && shownMode !== 'icon'"
        class="t-collapsible-sidebar__nav"
        :class="{ 'is-detached': contentScrolled }"
      >
        <slot name="nav" />
      </div>

      <div
        ref="contentEl"
        class="t-collapsible-sidebar__content t-ai-scroll"
        @scroll="onContentScroll"
      >
        <template v-if="shownMode === 'icon' && $slots.rail">
          <slot name="rail" :mode="modelValue" />
        </template>
        <template v-else>
          <slot name="content" />
        </template>
      </div>

      <footer
        v-if="$slots.footer && shownMode !== 'icon'"
        class="t-collapsible-sidebar__footer"
      >
        <slot name="footer" />
      </footer>
    </div>

  </aside>
</template>

<style scoped>
.t-collapsible-sidebar {
  position: relative;
  flex-shrink: 0;
  height: 100%;
  /* Transparent by default so the sidebar inherits the app canvas; consumers
     (e.g. dark/branded themes) can override via --tnzi-ai-sidebar-bg. */
  background: var(--tnzi-ai-sidebar-bg, transparent);
  border-right: var(--tnzi-ai-sidebar-border, none);
  /* Manus-matched easing: width-only transition on the standard Material
     decelerate curve. Only width animates - label nodes are conditionally
     rendered (v-if) so there is nothing else to animate during the swap. */
  transition: width 200ms cubic-bezier(0.4, 0, 0.2, 1);
  overflow: hidden;
}
/* Collapsed rail: whole sidebar reads as "push me east". Clicking any empty
   area (non-interactive target) expands the sidebar - see handleRailClick. */
.t-collapsible-sidebar--icon {
  cursor: e-resize;
}
.t-collapsible-sidebar--icon button,
.t-collapsible-sidebar--icon a,
.t-collapsible-sidebar--icon input,
.t-collapsible-sidebar--icon [role='button'] {
  cursor: pointer;
}
/* Narrow screen: an off-canvas drawer, open or closed. It starts below the
   page's top inset (`--tnzi-overlay-inset-top`, e.g. a frameless window's
   title bar); its backdrop covers the whole viewport, the inset included, so
   nothing behind it is left undimmed.
   Closed, it sits one width to the left and is `visibility: hidden` once the
   slide has finished (the visibility change is delayed by the slide's length
   on the way out and immediate on the way in), so it is neither painted nor
   reachable; `inert` covers the moment in between. Only `transform` and
   `opacity` animate, so no frame of the slide lays anything out. */
.t-collapsible-sidebar--mobile {
  position: fixed;
  top: var(--tnzi-overlay-inset-top, 0px);
  bottom: 0;
  left: 0;
  width: 280px;
  max-width: calc(100vw - 48px);
  height: auto;
  z-index: var(--tnzi-ai-z-drawer, 50);
  box-shadow: 0 10px 40px rgba(0, 0, 0, 0.25);
  transform: translateX(-100%);
  visibility: hidden;
  transition:
    transform var(--t-drawer-duration) var(--t-drawer-easing),
    box-shadow var(--t-drawer-duration) var(--t-drawer-easing),
    visibility 0s linear var(--t-drawer-duration);
}
.t-collapsible-sidebar--mobile.t-collapsible-sidebar--hidden {
  box-shadow: none;
}
.t-collapsible-sidebar--drawer {
  transform: none;
  visibility: visible;
  transition:
    transform var(--t-drawer-duration) var(--t-drawer-easing),
    box-shadow var(--t-drawer-duration) var(--t-drawer-easing),
    visibility 0s linear 0s;
}
.t-collapsible-sidebar__backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.35);
  z-index: calc(var(--tnzi-ai-z-drawer, 50) - 1);
  opacity: 0;
  visibility: hidden;
  pointer-events: none;
  transition:
    opacity var(--t-drawer-duration) var(--t-drawer-easing),
    visibility 0s linear var(--t-drawer-duration);
}
.t-collapsible-sidebar__backdrop.is-open {
  opacity: 1;
  visibility: visible;
  pointer-events: auto;
  transition:
    opacity var(--t-drawer-duration) var(--t-drawer-easing),
    visibility 0s linear 0s;
}
/* One duration and curve for the drawer and its backdrop, so they move as one. */
.t-collapsible-sidebar--mobile,
.t-collapsible-sidebar__backdrop {
  --t-drawer-duration: 240ms;
  --t-drawer-easing: cubic-bezier(0.32, 0.72, 0, 1);
}
@media (prefers-reduced-motion: reduce) {
  .t-collapsible-sidebar--mobile,
  .t-collapsible-sidebar__backdrop {
    --t-drawer-duration: 0ms;
  }
}
.t-collapsible-sidebar__inner {
  display: flex;
  flex-direction: column;
  height: 100%;
  width: 100%;
  min-width: 0;
}
.t-collapsible-sidebar__header,
.t-collapsible-sidebar__nav,
.t-collapsible-sidebar__footer {
  flex-shrink: 0;
}
.t-collapsible-sidebar__nav {
  /* Reserve the seam always, so gaining it costs no layout shift. */
  border-bottom: 1px solid transparent;
  transition: border-color var(--tnzi-ai-duration-fast, 120ms) var(--tnzi-ai-easing, ease);
}
.t-collapsible-sidebar__nav.is-detached {
  border-bottom-color: var(--tnzi-ai-divider);
}
/* A stack, not a single slot: products put a plan card, an upgrade prompt or a
   storage meter beside the account row, and each is its own full-width row.
   Padding lives here rather than on the children so a consumer dropping an
   element in gets the same inset as the built-ins - and so the account
   popover, positioned against its row, lands inside the sidebar instead of
   flush against the window edge. */
.t-collapsible-sidebar__footer {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 6px 8px 8px;
}
.t-collapsible-sidebar__content {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  position: relative;
  /* Scrolls, but without a visible bar. The sidebar is a short, familiar list
     the user scrolls by wheel or trackpad; a permanent bar there costs real
     layout (the package's 8px scrollbar takes a 10px gutter out of a 300px
     sidebar, and it is the nav rows that get narrowed) to report a position
     nobody navigates by. The main thread scroller keeps its bar - there the
     position is the point. Overriding the package-wide rule in
     `styles/index.css`, which is why both properties are set. */
  scrollbar-width: none;
}
.t-collapsible-sidebar__content::-webkit-scrollbar {
  width: 0;
  height: 0;
}
</style>
