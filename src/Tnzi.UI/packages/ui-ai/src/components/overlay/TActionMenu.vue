<script setup lang="ts">
/**
 * @experimental
 * TActionMenu - a trigger and a menu of actions or choices, usable with a
 * mouse, a finger and a keyboard.
 *
 * Where `TPopoverMenu` is a container anchored inside its parent, this one is
 * rendered on `<body>` at a fixed position next to its trigger. That is what a
 * menu opened from a scrolling list needs: anchored inside the list it is cut
 * off by the list's own edge (a row near the bottom of the sidebar opened a
 * menu nobody could see).
 *
 * ## Items
 *
 * Data, like `TUserMenu`'s: label, icon, a value shown on the right, a check
 * for the current choice, a danger style, a separator above. An item with
 * `children` opens them in place under a back row (the same model as the
 * account menu, for the same reason: a fly-out off a 300px sidebar covers the
 * conversation, and on a phone there is nowhere to fly to). Ids are unique
 * across the tree; `select` carries the chosen item's id.
 *
 * ## Keyboard
 *
 * Opening moves focus into the menu (the checked choice, else the first item).
 * Up / Down / Home / End move, Enter / Space choose, Right opens a submenu and
 * Left goes back, Escape goes back a level and then closes. Closing by keyboard
 * or by choosing returns focus to the trigger; Tab closes and moves on. Keys go
 * through the page's overlay stack (`useOverlayLayer`), so a menu opened over a
 * dialog takes Escape alone.
 *
 * @example
 * ```vue
 * <TActionMenu :items="items" @select="onSelect">
 *   <template #trigger="{ toggle, attrs }">
 *     <button type="button" v-bind="attrs" aria-label="More" @click="toggle">...</button>
 *   </template>
 * </TActionMenu>
 * ```
 */
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { Icon } from '@iconify/vue'
import { useOverlayLayer } from '@tnzi/ui'
import { formatAiMessage, useAiI18n } from '../../i18n/index'
import { opensSubmenu, resolveMenuLevel } from './user-menu'

export interface ActionMenuItem {
  readonly id: string
  readonly label: string
  /** Iconify name at the left edge. */
  readonly icon?: string
  /** The current value, shown on the right (a filter row's setting). */
  readonly value?: string
  /** The current choice in a picker (a check at the right edge). */
  readonly checked?: boolean
  /** Destructive styling (delete). */
  readonly danger?: boolean
  /** Draw a separator above this item. */
  readonly dividerBefore?: boolean
  readonly disabled?: boolean
  /** A submenu: choosing the item opens these in its place, under a back row. */
  readonly children?: ReadonlyArray<ActionMenuItem>
}

const props = withDefaults(
  defineProps<{
    items: ReadonlyArray<ActionMenuItem>
    /** Whether the menu is open. Optional `v-model:open`; the menu also opens and closes itself. */
    open?: boolean
    /** Which edge of the trigger the menu lines up with. */
    align?: 'start' | 'end'
    /** Accessible name of the menu. */
    label?: string
    /** Minimum width in px. */
    minWidth?: number
  }>(),
  {
    open: undefined,
    align: 'end',
    label: undefined,
    minWidth: 200,
  },
)

const emit = defineEmits<{
  select: [id: string]
  'update:open': [open: boolean]
}>()

const t = useAiI18n()

const localOpen = ref(false)
const isOpen = computed(() => props.open ?? localOpen.value)

function setOpen(value: boolean): void {
  localOpen.value = value
  if (props.open !== value) emit('update:open', value)
}

const anchorRef = ref<HTMLElement | null>(null)
const menuRef = ref<HTMLElement | null>(null)

/* The ids of the submenus opened, outermost first. */
const path = ref<string[]>([])
const level = computed(() => resolveMenuLevel(props.items, path.value))
const parentItem = computed(() => level.value.trail[level.value.trail.length - 1] ?? null)

const position = ref<{ top: number; left: number } | null>(null)
const GAP = 4
const EDGE = 8

function triggerElement(): HTMLElement | null {
  const anchor = anchorRef.value
  if (!anchor) return null
  return anchor.querySelector<HTMLElement>('button, [href], [tabindex]') ?? anchor
}

/* Below the trigger, lined up with the chosen edge; above when there is no
   room below; never past the window's edges. Measured after the menu has
   rendered, since its size depends on its items. */
function place(): void {
  const anchor = triggerElement()
  const menu = menuRef.value
  if (!anchor || !menu || typeof window === 'undefined') return
  const a = anchor.getBoundingClientRect()
  const width = menu.offsetWidth
  const height = menu.offsetHeight
  const viewportW = window.innerWidth
  const viewportH = window.innerHeight
  let left = props.align === 'start' ? a.left : a.right - width
  left = Math.min(Math.max(EDGE, left), Math.max(EDGE, viewportW - width - EDGE))
  let top = a.bottom + GAP
  if (top + height > viewportH - EDGE && a.top - GAP - height >= EDGE) top = a.top - GAP - height
  top = Math.max(EDGE, Math.min(top, viewportH - height - EDGE))
  position.value = { top, left }
}

function menuItems(): HTMLElement[] {
  return Array.from(menuRef.value?.querySelectorAll<HTMLElement>('[data-menu-item]:not([disabled])') ?? [])
}

function focusAt(index: number): void {
  const items = menuItems()
  if (items.length === 0) return
  const wrapped = ((index % items.length) + items.length) % items.length
  items[wrapped]!.focus()
}

/* The checked choice when there is one: reopening a picker lands on the
   current setting, as a native select does. */
function focusInitial(preferId?: string): void {
  const items = menuItems()
  const target =
    (preferId && items.find((el) => el.dataset.menuItem === preferId))
    || items.find((el) => el.getAttribute('aria-checked') === 'true')
    || items.find((el) => el.dataset.menuItem !== '__back')
    || items[0]
  target?.focus()
}

/* Focus waits for the render that applies the position: until then the menu is
   `visibility: hidden`, and a hidden element refuses focus. */
async function afterRender(preferId?: string): Promise<void> {
  await nextTick()
  place()
  await nextTick()
  focusInitial(preferId)
}

watch(isOpen, (open) => {
  if (open) {
    path.value = []
    void afterRender()
    window.addEventListener('resize', close)
    window.addEventListener('scroll', onScroll, true)
    document.addEventListener('pointerdown', onPointerDown, true)
  } else {
    position.value = null
    path.value = []
    removeListeners()
  }
})

function removeListeners(): void {
  if (typeof window === 'undefined') return
  window.removeEventListener('resize', close)
  window.removeEventListener('scroll', onScroll, true)
  document.removeEventListener('pointerdown', onPointerDown, true)
}

onBeforeUnmount(removeListeners)

function toggle(): void {
  setOpen(!isOpen.value)
}

function close(): void {
  if (isOpen.value) setOpen(false)
}

function closeAndReturnFocus(): void {
  close()
  void nextTick(() => triggerElement()?.focus())
}

/* A scroll of whatever the trigger sits in moves the trigger away from the
   menu; following it is not worth it for a menu this short-lived. Scrolling
   inside the menu itself is ignored. */
function onScroll(event: Event): void {
  if (menuRef.value && event.target instanceof Node && menuRef.value.contains(event.target)) return
  close()
}

function onPointerDown(event: PointerEvent): void {
  const target = event.target as Node | null
  if (!target) return
  if (menuRef.value?.contains(target) || anchorRef.value?.contains(target)) return
  close()
}

function choose(item: ActionMenuItem): void {
  if (item.disabled) return
  if (opensSubmenu(item)) {
    path.value = [...path.value, item.id]
    void afterRender()
    return
  }
  closeAndReturnFocus()
  emit('select', item.id)
}

function goBack(): void {
  const leaving = path.value[path.value.length - 1]
  path.value = path.value.slice(0, -1)
  void afterRender(leaving)
}

function currentIndex(): number {
  const active = typeof document === 'undefined' ? null : document.activeElement
  return menuItems().findIndex((el) => el === active)
}

function onKeydown(event: KeyboardEvent): void {
  switch (event.key) {
    case 'ArrowDown':
      event.preventDefault()
      focusAt(currentIndex() + 1)
      break
    case 'ArrowUp':
      event.preventDefault()
      focusAt(currentIndex() < 0 ? -1 : currentIndex() - 1)
      break
    case 'Home':
      event.preventDefault()
      focusAt(0)
      break
    case 'End':
      event.preventDefault()
      focusAt(-1)
      break
    case 'ArrowRight': {
      const id = (document.activeElement as HTMLElement | null)?.dataset?.menuItem
      const item = level.value.items.find((i) => i.id === id)
      if (item && opensSubmenu(item)) {
        event.preventDefault()
        choose(item)
      }
      break
    }
    case 'ArrowLeft':
      if (parentItem.value) {
        event.preventDefault()
        goBack()
      }
      break
    case 'Tab':
      close()
      break
  }
}

useOverlayLayer(() => isOpen.value, {
  onEscape: () => (parentItem.value ? goBack() : closeAndReturnFocus()),
  onKeydown,
  root: () => menuRef.value,
})

const triggerAttrs = computed(() => ({
  'aria-haspopup': 'menu' as const,
  'aria-expanded': isOpen.value,
}))

const menuStyle = computed(() =>
  position.value
    ? { top: `${position.value.top}px`, left: `${position.value.left}px`, minWidth: `${props.minWidth}px` }
    : { top: '0px', left: '0px', minWidth: `${props.minWidth}px`, visibility: 'hidden' as const },
)

defineExpose({ open: () => setOpen(true), close, toggle })
</script>

<template>
  <span ref="anchorRef" class="t-action-menu__anchor">
    <slot name="trigger" :open="isOpen" :toggle="toggle" :attrs="triggerAttrs" />
  </span>
  <Teleport to="body">
    <div
      v-if="isOpen"
      ref="menuRef"
      class="t-action-menu t-ai-scroll-scope"
      role="menu"
      :aria-label="parentItem ? parentItem.label : label"
      :style="menuStyle"
    >
      <template v-if="parentItem">
        <button
          type="button"
          class="t-action-menu__item t-action-menu__back"
          role="menuitem"
          data-menu-item="__back"
          :aria-label="formatAiMessage(t.account.backTo, { label: parentItem.label })"
          @click="goBack"
        >
          <Icon icon="lucide:chevron-left" class="t-action-menu__icon" />
          <span class="t-action-menu__label">{{ parentItem.label }}</span>
        </button>
        <div class="t-action-menu__sep" role="separator" />
      </template>
      <template v-for="item in level.items" :key="item.id">
        <div v-if="item.dividerBefore" class="t-action-menu__sep" role="separator" />
        <button
          type="button"
          class="t-action-menu__item"
          :class="{ 't-action-menu__item--danger': item.danger }"
          :role="item.checked === undefined ? 'menuitem' : 'menuitemradio'"
          :aria-checked="item.checked === undefined ? undefined : item.checked"
          :aria-haspopup="item.children?.length ? 'menu' : undefined"
          :disabled="item.disabled"
          :data-menu-item="item.id"
          tabindex="-1"
          @click="choose(item)"
        >
          <Icon v-if="item.icon" :icon="item.icon" class="t-action-menu__icon" />
          <span class="t-action-menu__label">{{ item.label }}</span>
          <span v-if="item.value" class="t-action-menu__value">{{ item.value }}</span>
          <Icon v-if="item.checked" icon="lucide:check" class="t-action-menu__check" />
          <Icon v-else-if="item.children?.length" icon="lucide:chevron-right" class="t-action-menu__hint" />
        </button>
      </template>
    </div>
  </Teleport>
</template>

<style scoped>
.t-action-menu__anchor {
  display: inline-flex;
  align-items: center;
}
/* Above the sidebar drawer (50) and its backdrop: a menu opened from a row in
   the narrow-screen drawer must not open underneath it. */
.t-action-menu {
  position: fixed;
  z-index: 70;
  max-width: min(320px, calc(100vw - 16px));
  box-sizing: border-box;
  padding: 6px;
  display: flex;
  flex-direction: column;
  gap: 1px;
  background: var(--tnzi-ai-surface, #ffffff);
  color: var(--tnzi-ai-text);
  border-radius: 12px;
  /* A 1px ring rather than a border, so the ring and the soft shadow read as
     one edge. The shadow is `@tnzi/ui`'s popover tier, which has its own dark
     value. */
  box-shadow:
    0 0 0 1px var(--tnzi-ai-border, rgba(0, 0, 0, 0.08)),
    var(--tnzi-surface-popover-shadow, 0 6px 20px -2px rgb(0 0 0 / 12%));
}
.t-action-menu__item {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  min-height: 32px;
  padding: 6px 10px;
  box-sizing: border-box;
  border: none;
  border-radius: 8px;
  background: transparent;
  color: var(--tnzi-ai-text);
  font-family: inherit;
  font-size: 14px;
  line-height: 20px;
  text-align: left;
  cursor: pointer;
  outline: none;
}
.t-action-menu__item:hover:not(:disabled),
.t-action-menu__item:focus-visible {
  background: var(--tnzi-ai-hover, rgba(55, 53, 47, 0.06));
}
.t-action-menu__item:focus-visible {
  box-shadow: inset 0 0 0 1px var(--tnzi-ai-border-strong, rgba(0, 0, 0, 0.2));
}
.t-action-menu__item:disabled {
  cursor: default;
  opacity: 0.5;
}
.t-action-menu__icon {
  flex-shrink: 0;
  width: 16px;
  height: 16px;
  color: var(--tnzi-ai-text-secondary);
}
.t-action-menu__label {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-action-menu__value {
  flex-shrink: 0;
  margin-left: 12px;
  font-size: 13px;
  color: var(--tnzi-ai-text-secondary);
}
.t-action-menu__hint {
  flex-shrink: 0;
  width: 16px;
  height: 16px;
  color: var(--tnzi-ai-text-tertiary);
}
.t-action-menu__check {
  flex-shrink: 0;
  width: 16px;
  height: 16px;
  margin-left: auto;
  color: var(--tnzi-ai-accent-text);
}
.t-action-menu__item--danger,
.t-action-menu__item--danger .t-action-menu__icon {
  color: var(--tnzi-ai-danger, #e5484d);
}
.t-action-menu__item--danger:hover:not(:disabled),
.t-action-menu__item--danger:focus-visible {
  background: var(--tnzi-ai-danger-soft, rgba(229, 72, 77, 0.08));
}
.t-action-menu__back {
  color: var(--tnzi-ai-text-secondary);
}
.t-action-menu__sep {
  height: 1px;
  margin: 4px 6px;
  background: var(--tnzi-ai-divider, #ececec);
}
</style>
