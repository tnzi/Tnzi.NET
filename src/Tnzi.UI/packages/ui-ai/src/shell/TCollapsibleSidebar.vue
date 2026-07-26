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
 * Import `useSidebarState` from `@tnzi/ui-ai/composables` (or
 * `@tnzi/ui-ai/shell`) for the state machine.
 */
import { computed } from 'vue'
import type { SidebarMode } from '@/composables/useSidebarState'
import { useBodyScrollLock } from '@/composables/useBodyScrollLock'

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

const currentWidth = computed(() => {
  if (props.modelValue === 'hidden') return 0
  if (props.modelValue === 'icon') return props.railWidth
  return props.width
})

const isDrawer = computed(() => props.isMobile && props.modelValue !== 'hidden')

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

/**
 * In icon (collapsed) mode, clicking an empty area of the rail expands the
 * sidebar - matches Manus's "whole nav is a cursor-e-resize click target"
 * behavior. Clicks that land on an interactive descendant (button / link /
 * input / [role=button]) keep their own semantics and DO NOT expand.
 */
function handleRailClick(e: MouseEvent): void {
  if (props.modelValue !== 'icon') return
  const target = e.target as HTMLElement | null
  if (target?.closest('button,a,input,[role="button"],[data-no-expand]')) return
  emit('update:modelValue', 'expanded')
}

/* The mobile drawer covers the page, so the page behind it must not scroll.
   The shared lock restores whatever overflow the host had and releases on
   unmount: without that, unmounting mid-drawer (a route change while the
   drawer is open) left the page permanently unscrollable. */
useBodyScrollLock(isDrawer)
</script>

<template>
  <transition name="t-sidebar-backdrop">
    <div
      v-if="isDrawer"
      class="t-collapsible-sidebar__backdrop"
      @click="closeFromBackdrop"
    />
  </transition>

  <aside
    class="t-collapsible-sidebar"
    :class="{
      't-collapsible-sidebar--expanded': modelValue === 'expanded',
      't-collapsible-sidebar--icon': modelValue === 'icon',
      't-collapsible-sidebar--hidden': modelValue === 'hidden',
      't-collapsible-sidebar--drawer': isDrawer,
    }"
    :style="{ width: `${currentWidth}px` }"
    :aria-hidden="isFullyHidden"
    :inert="isFullyHidden || undefined"
    @click="handleRailClick"
  >
    <div class="t-collapsible-sidebar__inner">
      <!-- Header / nav / footer slots only render in expanded mode; in icon
           mode the rail slot owns the full height and provides its own
           brand mark / footer icons. -->
      <header
        v-if="$slots.header && modelValue !== 'icon'"
        class="t-collapsible-sidebar__header"
      >
        <slot name="header" />
      </header>

      <div
        v-if="$slots.nav && modelValue !== 'icon'"
        class="t-collapsible-sidebar__nav"
      >
        <slot name="nav" />
      </div>

      <div class="t-collapsible-sidebar__content">
        <template v-if="modelValue === 'icon' && $slots.rail">
          <slot name="rail" :mode="modelValue" />
        </template>
        <template v-else>
          <slot name="content" />
        </template>
      </div>

      <footer
        v-if="$slots.footer && modelValue !== 'icon'"
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
.t-collapsible-sidebar--drawer {
  position: fixed;
  top: 0;
  left: 0;
  height: 100vh;
  width: 280px !important;
  z-index: 50;
  box-shadow: 0 10px 40px rgba(0, 0, 0, 0.25);
}
.t-collapsible-sidebar__backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.35);
  z-index: 49;
}
.t-sidebar-backdrop-enter-active,
.t-sidebar-backdrop-leave-active {
  transition: opacity 150ms ease;
}
.t-sidebar-backdrop-enter-from,
.t-sidebar-backdrop-leave-to {
  opacity: 0;
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
.t-collapsible-sidebar__content {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
}
</style>
