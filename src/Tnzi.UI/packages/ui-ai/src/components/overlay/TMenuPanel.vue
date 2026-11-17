<script setup lang="ts">
/**
 * TMenuPanel - a menu that stays where it is: the panel and the entries of
 * the package's menus, without the floating.
 *
 * `TPopoverMenu` (and through it `TDropdownMenu`, `TContextMenu`) draws its
 * menu with this panel and places it, so a page that is itself a menu (a tray
 * window, a side list of actions) gets the same entries at the same sizes:
 * `TMenuItem`, `TMenuSeparator`, and the item classes (`.t-popover-menu__item`
 * with `--danger`, `__item-value`, `__item-hint`, `__item-check`,
 * `__item-badge`, `__sep`, `__row`) are styled inside it.
 *
 * `role="menu"`. Up and Down move between the enabled entries and wrap, Home
 * and End go to the ends (`keyboard`, on by default; `TPopoverMenu` turns it
 * off because its openers handle the keys). `windowKeys` takes them from the
 * whole window while focus is on nothing, for a window that is the menu.
 * Escape and Tab are always the page's.
 *
 * `flat` drops the border and the rounded corners, for a panel that fills a
 * window whose frame is the menu's edge.
 */
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { focusMenuItem, menuItems } from './menu-keys'

const props = withDefaults(
  defineProps<{
    /** Header line at the top of the menu. */
    label?: string
    /** No border and no rounded corners: the panel fills a window that is the menu. */
    flat?: boolean
    /** Up, Down, Home and End move between the entries. */
    keyboard?: boolean
    /**
     * Also take those keys when they are pressed with focus on nothing (the
     * page body), for a window that is the menu: it opens with no focus, and
     * the first Down goes to the first entry, Up to the last. A key pressed in
     * a field or another control elsewhere on the page is left alone, and so
     * are Escape and Tab.
     */
    windowKeys?: boolean
  }>(),
  { label: '', flat: false, keyboard: true, windowKeys: false },
)

const root = ref<HTMLDivElement | null>(null)

/** Moves focus for a menu key; false when `key` is not one. */
function move(key: string): boolean {
  const menu = root.value
  const current = menuItems(menu).indexOf(document.activeElement as HTMLElement)
  switch (key) {
    case 'ArrowDown':
      focusMenuItem(menu, current + 1)
      return true
    case 'ArrowUp':
      focusMenuItem(menu, current < 0 ? -1 : current - 1)
      return true
    case 'Home':
      focusMenuItem(menu, 0)
      return true
    case 'End':
      focusMenuItem(menu, -1)
      return true
    default:
      return false
  }
}

function onKeydown(event: KeyboardEvent): void {
  if (!props.keyboard || event.isComposing) return
  if (move(event.key)) event.preventDefault()
}

/* Focus on nothing: the body (or no element at all). */
function focusIsNowhere(): boolean {
  const active = document.activeElement
  return !active || active === document.body || active === document.documentElement
}

function onWindowKeydown(event: KeyboardEvent): void {
  if (!props.keyboard || event.isComposing || event.defaultPrevented || !focusIsNowhere()) return
  if (move(event.key)) event.preventDefault()
}

function listenWindow(on: boolean): void {
  if (typeof window === 'undefined') return
  if (on) window.addEventListener('keydown', onWindowKeydown)
  else window.removeEventListener('keydown', onWindowKeydown)
}

onMounted(() => listenWindow(props.windowKeys))
watch(
  () => props.windowKeys,
  (on) => listenWindow(on),
)
onBeforeUnmount(() => listenWindow(false))
</script>

<template>
  <div ref="root" class="t-menu-panel" :class="{ 't-menu-panel--flat': flat }" role="menu" @keydown="onKeydown">
    <div v-if="label" class="t-popover-menu__head">{{ label }}</div>
    <slot />
  </div>
</template>

<style scoped>
.t-menu-panel {
  box-sizing: border-box;
  background: var(--tnzi-ai-surface, #ffffff);
  border: 1px solid var(--tnzi-ai-border, #e5e5e5);
  border-radius: var(--tnzi-ai-radius-lg, 12px);
  padding: 6px;
  display: flex;
  flex-direction: column;
  gap: 2px;
  font-family: var(--tnzi-ai-font-body, inherit);
  color: var(--tnzi-ai-text, inherit);
  text-align: left;
}
.t-menu-panel--flat {
  border: none;
  border-radius: 0;
}
.t-popover-menu__head {
  padding: 6px 10px 4px;
  font-size: 11px;
  font-weight: 500;
  color: var(--tnzi-ai-text-tertiary, #9a9a9a);
  letter-spacing: 0.02em;
}

/* Item primitives - exposed via :deep so consumers can use them on raw
   button / label elements inside the default slot without needing to
   wrap them in another component. */
.t-menu-panel :deep(.t-popover-menu__item) {
  display: flex;
  align-items: center;
  gap: 10px;
  height: 34px;
  padding: 0 10px;
  border: none;
  background: transparent;
  border-radius: var(--tnzi-ai-radius-md, 8px);
  color: var(--tnzi-ai-text, #1a1a1a);
  font-family: inherit;
  font-size: 13px;
  /* Its own, not the page reset's (see TSegmented). */
  line-height: 1.5;
  text-align: left;
  cursor: pointer;
  width: 100%;
}
.t-menu-panel :deep(.t-popover-menu__item:hover) {
  background: var(--tnzi-ai-hover, rgba(55, 53, 47, 0.04));
}
.t-menu-panel :deep(.t-popover-menu__item .iconify) {
  color: var(--tnzi-ai-text-secondary, #6a6a6a);
  font-size: 15px;
  flex-shrink: 0;
}
.t-menu-panel :deep(.t-popover-menu__item-check) {
  color: var(--tnzi-ai-accent-text);
  margin-left: auto;
  flex-shrink: 0;
}
.t-menu-panel :deep(.t-popover-menu__sep) {
  height: 1px;
  background: var(--tnzi-ai-divider, #ececec);
  margin: 4px 4px;
}

/* Trailing affordances. All three sit at the right edge via `margin-left:auto`
   so an item can carry a value, a badge or a hint icon without the consumer
   adding a spacer element. */
.t-menu-panel :deep(.t-popover-menu__item-value) {
  margin-left: auto;
  flex-shrink: 0;
  font-size: 13px;
  color: var(--tnzi-ai-text-secondary, #6a6a6a);
}
.t-menu-panel :deep(.t-popover-menu__item-hint) {
  margin-left: auto;
  flex-shrink: 0;
  color: var(--tnzi-ai-text-tertiary, #9a9a9a);
  font-size: 14px;
}
/* A value plus a hint (e.g. "300 ›") - the value already claimed the auto
   margin, so the hint only needs its own gap. */
.t-menu-panel :deep(.t-popover-menu__item-value + .t-popover-menu__item-hint) {
  margin-left: 0;
}
.t-menu-panel :deep(.t-popover-menu__item-badge) {
  margin-left: auto;
  flex-shrink: 0;
  height: 26px;
  padding: 0 12px;
  border: none;
  border-radius: var(--tnzi-ai-radius-pill, 999px);
  background: var(--tnzi-ai-accent-contrast, #1a1a1a);
  color: var(--tnzi-ai-on-accent-contrast, #ffffff);
  font-family: inherit;
  font-size: 12px;
  font-weight: 500;
  cursor: pointer;
}

/* Destructive items (Sign out, Delete). The icon inherits the danger colour
   too, otherwise it reads as a normal item with oddly red text. */
.t-menu-panel :deep(.t-popover-menu__item--danger),
.t-menu-panel :deep(.t-popover-menu__item--danger .iconify) {
  color: var(--tnzi-ai-danger, #e5484d);
}
.t-menu-panel :deep(.t-popover-menu__item--danger:hover) {
  background: var(--tnzi-ai-danger-soft, rgba(229, 72, 77, 0.08));
}

/* Non-interactive label row (e.g. a plan name beside an Upgrade badge). */
.t-menu-panel :deep(.t-popover-menu__row) {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 4px 10px;
  font-size: 14px;
  font-weight: 500;
  color: var(--tnzi-ai-text, #1a1a1a);
}
</style>
