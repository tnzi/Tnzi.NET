<script setup lang="ts">
/**
 * TContextMenu - a menu opened where the pointer is: a right click on a row, a
 * file, a message.
 *
 * Put it anywhere (it draws nothing until opened) and open it from the
 * element's `contextmenu` event; its entries are `TMenuItem`s:
 *
 * ```vue
 * <li @contextmenu="menu?.openAt($event)">...</li>
 * <TContextMenu ref="menu" :label="file.name">
 *   <template #default="{ close }">
 *     <TMenuItem icon="lucide:pencil" @click="rename(); close()">Rename</TMenuItem>
 *   </template>
 * </TContextMenu>
 * ```
 *
 * `openAt(event)` takes the event (and stops the browser's own menu) or a
 * point `{ x, y }`. The menu's top left corner goes at the point, and it opens
 * up or to the left when there is no room below or to the right. Opened from
 * the keyboard (the menu key, Shift+F10), the event has no pointer position;
 * the menu then opens under the start of the element that has focus.
 *
 * Keyboard and closing, as `TDropdownMenu`: opening moves focus to the first
 * entry (not on a touch screen); Up / Down / Home / End move; Escape closes it
 * and focus goes back to where it was before it opened; Tab closes it and
 * moves on. A press anywhere outside it (either button), a scroll of the page
 * and the window losing focus close it too.
 */
import { nextTick, onBeforeUnmount, ref, watch } from 'vue'
import TPopoverMenu from './TPopoverMenu.vue'
import { afterMenuPlaced, createMenuKeydown, isTouchScreen, menuItems } from './menu-keys'

const props = withDefaults(
  defineProps<{
    /** Header line at the top of the menu (what it acts on). */
    label?: string
    minWidth?: number
    maxWidth?: number
  }>(),
  { label: '', minWidth: 200, maxWidth: 320 },
)

const emit = defineEmits<{ 'update:open': [open: boolean] }>()

defineSlots<{ default?: (props: { close: () => void }) => unknown }>()

const open = ref(false)
const at = ref<{ x: number; y: number } | null>(null)
const root = ref<HTMLElement | null>(null)
/* Where focus was when the menu opened: it goes back there on Escape and after a choice. */
let returnTo: HTMLElement | null = null

function pointOf(source: MouseEvent | { x: number; y: number }): { x: number; y: number } {
  if (!(source instanceof MouseEvent)) return { x: source.x, y: source.y }
  if (source.clientX !== 0 || source.clientY !== 0) return { x: source.clientX, y: source.clientY }
  /* From the keyboard: no pointer, so under the start of the focused element. */
  const from = (document.activeElement instanceof HTMLElement && document.activeElement !== document.body
    ? document.activeElement
    : source.target) as Element | null
  const box = from?.getBoundingClientRect()
  return box ? { x: box.left, y: box.bottom } : { x: 0, y: 0 }
}

/** Opens the menu at the event's point (stopping the browser's own menu), or at `{ x, y }`. */
function openAt(source: MouseEvent | { x: number; y: number }): void {
  if (source instanceof MouseEvent) source.preventDefault()
  if (!open.value) {
    const active = typeof document === 'undefined' ? null : document.activeElement
    returnTo = active instanceof HTMLElement && active !== document.body ? active : null
  }
  at.value = pointOf(source)
  open.value = true
}

function close(returnFocus = true): void {
  if (!open.value) return
  open.value = false
  const target = returnTo
  returnTo = null
  if (returnFocus && target) void nextTick(() => target.isConnected && target.focus())
}

const onKeydown = createMenuKeydown({ root: () => root.value, isOpen: () => open.value, close })

/* A press outside (a right click elsewhere too), a scroll of what is under it
   or the window losing focus closes it, as the system's menus do. */
function onPointerDown(event: PointerEvent): void {
  const menu = root.value?.querySelector('.t-popover-menu')
  if (menu && event.target instanceof Node && menu.contains(event.target)) return
  close(false)
}
function onScroll(event: Event): void {
  const menu = root.value?.querySelector('.t-popover-menu')
  if (menu && event.target instanceof Node && menu.contains(event.target)) return
  close(false)
}
function onBlur(): void {
  close(false)
}

function listen(): void {
  document.addEventListener('pointerdown', onPointerDown, true)
  window.addEventListener('scroll', onScroll, true)
  window.addEventListener('blur', onBlur)
}
function unlisten(): void {
  document.removeEventListener('pointerdown', onPointerDown, true)
  window.removeEventListener('scroll', onScroll, true)
  window.removeEventListener('blur', onBlur)
}

watch(open, async (isOpen) => {
  emit('update:open', isOpen)
  if (!isOpen) {
    unlisten()
    return
  }
  listen()
  await nextTick()
  if (isTouchScreen()) return
  afterMenuPlaced(() => {
    if (open.value) menuItems(root.value)[0]?.focus()
  })
})

onBeforeUnmount(unlisten)

defineExpose({ openAt, close: () => close(false) })
</script>

<template>
  <div ref="root" class="t-context-menu" @keydown="onKeydown">
    <TPopoverMenu
      v-model="open"
      :at="at"
      align="left"
      :min-width="props.minWidth"
      :max-width="props.maxWidth"
      :label="props.label"
      data-context-menu
    >
      <slot :close="() => close()" />
    </TPopoverMenu>
  </div>
</template>

<style scoped>
.t-context-menu {
  display: contents;
}
</style>
