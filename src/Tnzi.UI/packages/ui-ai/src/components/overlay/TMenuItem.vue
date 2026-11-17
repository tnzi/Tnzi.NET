<script setup lang="ts">
/**
 * TMenuItem - one entry of a menu: an icon, a label, and at the end a value, a
 * shortcut, a chevron for a group it opens, or a check mark. `#leading` takes
 * the icon's place for an image or a badged icon, `#trailing` adds to the end;
 * the entry's size, hover and keys stay the panel's. It is styled inside a `TMenuPanel`, which is what
 * `TPopoverMenu` (`TDropdownMenu`, `TContextMenu`) draws; a menu that does not
 * float (a tray window) puts it in a `TMenuPanel` directly.
 *
 * It wears the item classes the panel styles (`.t-popover-menu__item` and its
 * `--danger`, `__item-value`, `__item-check`), so a page never names them.
 * `role="menuitem"` by default; a list that is not a menu (a `TSelect`'s
 * options) passes its own `role` and `aria-*`, which win.
 */
import { Icon } from '@iconify/vue'

withDefaults(
  defineProps<{
    icon?: string
    danger?: boolean
    disabled?: boolean
    /** Read-only text pinned right, e.g. the current choice. */
    value?: string
    /** A keyboard shortcut (or a short hint) pinned right, in quieter type: `Ctrl+Q`. */
    shortcut?: string
    /**
     * Shows a chevron at the end that turns down while `true`: the entry opens
     * a group of entries below it. Also sets `aria-expanded`. Leave unset for an
     * entry that opens nothing.
     */
    expanded?: boolean
    /** Shows a check mark: the item is the current choice. */
    checked?: boolean
  }>(),
  { icon: '', danger: false, disabled: false, value: '', shortcut: '', expanded: undefined, checked: false },
)

defineSlots<{
  /** The label. */
  default?: () => unknown
  /**
   * In place of `icon`: an application's own image, or an icon with a count on
   * it (a `TBadge` with an `icon` and a `count`). Drawn in the icon's 15px
   * box, so labels line up with entries that have an `icon`.
   */
  leading?: () => unknown
  /** More at the end, before the chevron and the check mark: a tag, a status dot. */
  trailing?: () => unknown
}>()
</script>

<template>
  <button
    type="button"
    role="menuitem"
    class="t-popover-menu__item t-menu-item"
    :class="{ 't-popover-menu__item--danger': danger }"
    :disabled="disabled"
    :aria-expanded="expanded === undefined ? undefined : expanded"
  >
    <span v-if="$slots.leading" class="t-menu-item__leading"><slot name="leading" /></span>
    <Icon v-else-if="icon" :icon="icon" aria-hidden="true" />
    <span class="t-menu-item__label"><slot /></span>
    <span v-if="value" class="t-popover-menu__item-value">{{ value }}</span>
    <kbd v-if="shortcut" class="t-menu-item__shortcut">{{ shortcut }}</kbd>
    <span v-if="$slots.trailing" class="t-menu-item__trailing"><slot name="trailing" /></span>
    <Icon
      v-if="expanded !== undefined"
      icon="lucide:chevron-right"
      class="t-menu-item__chevron"
      :class="{ 't-menu-item__chevron--open': expanded }"
      aria-hidden="true"
    />
    <Icon v-if="checked" icon="lucide:check" class="t-popover-menu__item-check" aria-hidden="true" />
  </button>
</template>

<style scoped>
/* The icon's box, whatever is in it: an image fills it, a TBadge's count sits
   on its corner. */
.t-menu-item__leading {
  position: relative;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  /* The size of an entry's `icon` (the panel sets icons to 15px), so labels
     line up whichever the entry has. */
  font-size: 15px;
  width: 1em;
  height: 1em;
  flex-shrink: 0;
}
.t-menu-item__leading :slotted(img),
.t-menu-item__leading :slotted(svg) {
  width: 100%;
  height: 100%;
  object-fit: contain;
}
.t-menu-item__shortcut {
  flex-shrink: 0;
  font-family: inherit;
  font-size: 12px;
  color: var(--tnzi-ai-text-tertiary, #9a9a9a);
  letter-spacing: 0.02em;
}
.t-menu-item__trailing {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  flex-shrink: 0;
}
.t-menu-item__chevron {
  flex-shrink: 0;
  transition: transform 0.15s ease;
}
.t-menu-item__chevron--open {
  transform: rotate(90deg);
}
@media (prefers-reduced-motion: reduce) {
  .t-menu-item__chevron {
    transition: none;
  }
}
.t-menu-item__label {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-menu-item:focus-visible {
  outline: var(--tnzi-ai-focus-outline);
  outline-offset: 2px;
}
.t-menu-item:disabled {
  opacity: 0.45;
  cursor: default;
}
.t-menu-item:disabled:hover {
  background: transparent;
}
</style>
