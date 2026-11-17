<script setup lang="ts">
/**
 * TSplitButton - an action and a menu of its variants as one control:
 * "Commit" with "Commit and push" and "Undo last commit" behind its chevron.
 *
 * Two buttons side by side, drawn as one: the main part does the action
 * (`click`), the chevron opens a `TDropdownMenu` whose entries are the
 * `#menu` slot's `TMenuItem`s. The menu lines up with the whole control.
 * `variant` is `primary`, `soft` or `secondary` (as `TButton`), `size` is
 * `sm` (30px) or `md` (36px).
 *
 * Keyboard: both parts are buttons in the Tab order. The chevron is named by
 * `menuLabel` (and has it as its tooltip), carries `aria-haspopup` and
 * `aria-expanded`, and opens the menu with Enter, Space or Down; the menu then
 * keeps `TDropdownMenu`'s keys (Up / Down / Home / End, Escape back to the
 * chevron, Tab closes and moves on). `loading` turns the main part into a
 * spinner that refuses clicks and leaves the menu as it is; `mainDisabled`
 * turns off the main part alone (nothing to commit, but "Undo last commit"
 * in the menu still works); `disabled` turns off both. Each menu entry keeps
 * its own `disabled`.
 */
import { ref } from 'vue'
import TButton from './TButton.vue'
import TDropdownMenu from '../overlay/TDropdownMenu.vue'
import { vTooltip } from '../../headless/tooltip'

withDefaults(
  defineProps<{
    /** Accessible name and tooltip of the chevron ("More ways to commit"). */
    menuLabel: string
    variant?: 'primary' | 'soft' | 'secondary'
    size?: 'sm' | 'md'
    /** Iconify name before the main part's label. */
    icon?: string
    loading?: boolean
    /** Turns off the main part only; the menu stays available. */
    mainDisabled?: boolean
    disabled?: boolean
    type?: 'button' | 'submit'
    align?: 'left' | 'right'
    placement?: 'top' | 'bottom'
    minWidth?: number
    maxWidth?: number
  }>(),
  {
    variant: 'primary',
    size: 'sm',
    icon: '',
    loading: false,
    mainDisabled: false,
    disabled: false,
    type: 'button',
    align: 'right',
    placement: 'bottom',
    minWidth: 200,
    maxWidth: 320,
  },
)

const emit = defineEmits<{ click: [event: MouseEvent] }>()

defineSlots<{
  /** The main part's label. */
  default?: () => unknown
  /** The menu's entries (`TMenuItem`, `TMenuSeparator`); `close()` closes it. */
  menu?: (props: { close: () => void }) => unknown
}>()

const menu = ref<InstanceType<typeof TDropdownMenu> | null>(null)

/* Down on the chevron opens the menu, as on a select. */
function onChevronKeydown(event: KeyboardEvent, open: boolean, toggle: () => void): void {
  if (event.key !== 'ArrowDown' || open) return
  event.preventDefault()
  toggle()
}
</script>

<template>
  <div class="t-split-button" :class="[`t-split-button--${variant}`, `t-split-button--${size}`]" data-split-button>
    <TButton
      class="t-split-button__main"
      :variant="variant"
      :size="size"
      :icon="icon"
      :type="type"
      :loading="loading"
      :disabled="disabled || mainDisabled"
      data-split-main
      @click="emit('click', $event)"
    >
      <slot />
    </TButton>
    <TDropdownMenu ref="menu" :align="align" :placement="placement" :min-width="minWidth" :max-width="maxWidth">
      <template #trigger="{ open, toggle, attrs }">
        <TButton
          v-bind="attrs"
          v-tooltip="open ? null : menuLabel"
          class="t-split-button__chevron"
          :variant="variant"
          :size="size"
          icon="lucide:chevron-down"
          :aria-label="menuLabel"
          :disabled="disabled"
          data-split-menu
          @click="toggle"
          @keydown="onChevronKeydown($event, open, toggle)"
        />
      </template>
      <template #default="{ close }">
        <slot name="menu" :close="close" />
      </template>
    </TDropdownMenu>
  </div>
</template>

<style scoped>
/* The control is the menu's reference box: the menu lines up with the whole
   of it, not with the chevron alone. */
.t-split-button {
  position: relative;
  display: inline-flex;
  flex: none;
  min-width: 0;
}
.t-split-button > .t-dropdown-menu {
  position: static;
}
.t-split-button__main {
  min-width: 0;
  border-top-right-radius: 0;
  border-bottom-right-radius: 0;
}
.t-split-button__chevron {
  border-top-left-radius: 0;
  border-bottom-left-radius: 0;
}
.t-split-button--sm .t-split-button__chevron {
  width: 26px;
  padding: 0;
}
.t-split-button--md .t-split-button__chevron {
  width: 30px;
  padding: 0;
}
/* The seam between the parts: a line in the fill's own foreground for the
   filled variants; the outlined one shares a single border. */
.t-split-button--primary .t-split-button__chevron {
  box-shadow: inset 1px 0 0 color-mix(in srgb, var(--tnzi-ai-on-accent) 30%, transparent);
}
.t-split-button--soft .t-split-button__chevron {
  box-shadow: inset 1px 0 0 color-mix(in srgb, var(--tnzi-ai-accent-text) 25%, transparent);
}
.t-split-button--secondary .t-split-button__chevron {
  margin-left: -1px;
}
.t-split-button__chevron[aria-expanded='true'] {
  filter: brightness(0.94);
}
/* A focus line on one part draws over its neighbour. */
.t-split-button__main:focus-visible,
.t-split-button__chevron:focus-visible {
  position: relative;
  z-index: 1;
}
</style>
