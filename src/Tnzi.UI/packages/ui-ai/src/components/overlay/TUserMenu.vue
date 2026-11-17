<script setup lang="ts">
/**
 * @experimental
 * TUserMenu - account status bar with a popover menu.
 *
 * The bar itself (avatar + name + optional icon buttons) is the trigger; the
 * menu rises above it, which is what a sidebar footer needs.
 *
 * ## Extending vs replacing
 *
 * `extraItems` appends to the built-in set and is the common case:
 *
 * ```vue
 * <TUserMenu :extra-items="[{ id: 'billing', label: 'Billing', icon: 'lucide:credit-card' }]" />
 * ```
 *
 * They land *above* sign-out, not after it, because a destructive action
 * belongs at the bottom of the menu no matter what a consumer adds. `items`
 * replaces the non-destructive built-ins outright when the defaults are wrong
 * rather than merely incomplete. There is no exported constant of default
 * items to spread: the labels come from the active locale, so a frozen array
 * would ship untranslatable English.
 *
 * `UserMenuItem` is intentionally richer than "icon + label" because real
 * account menus are not uniform lists: a plan row carries an action badge, a
 * credits row carries a value, help links carry an external marker, and sign
 * out is destructive. Expressing those as data keeps consumers out of the
 * markup; anything the type cannot express goes through `menu-header` /
 * `menu-footer`, or replaces the whole surface via the `menu` slot.
 *
 * ## Submenus
 *
 * An item with `children` opens a submenu instead of being selected: a
 * language or theme picker in the account menu. `checked` marks the current
 * choice. Selecting a child emits `select` with the child's id and closes the
 * whole menu, so ids must be unique across the whole tree (`lang:en`,
 * `theme:dark`).
 *
 * Where it opens depends on the device:
 *
 * - With a mouse (`(hover: hover) and (pointer: fine)`, 768px wide or more)
 *   the submenu flies out beside the menu, which stays as it is. Resting the
 *   pointer on the item opens it, and so does a click. Only one is open at a
 *   time; the pointer can cut diagonally across other rows into it, and it
 *   closes a moment after the pointer has left both (`headless/submenu-intent`).
 *   It covers the conversation beside the sidebar, which is what an account
 *   menu does on the desktop. A level below that drills in place inside the
 *   fly-out, under a back row.
 * - With a finger, or on a narrow screen, the submenu opens in place of the
 *   list under a back row: a phone has nowhere to fly out to, and a tap is not
 *   a hover. A tap on a hybrid laptop's screen drills in the same way.
 *
 * From the keyboard the item announces a submenu (`aria-haspopup`,
 * `aria-expanded`); Enter, Space or ArrowRight open it and move focus to its
 * checked choice (else its first item); ArrowUp / ArrowDown / Home / End move
 * within it; ArrowLeft or Escape go back to the item that opened it, with the
 * rest of the menu still open.
 *
 * ```vue
 * <TUserMenu :extra-items="[
 *   { id: 'language', label: 'Language', value: 'English', children: [
 *     { id: 'lang:en', label: 'English', checked: locale === 'en' },
 *     { id: 'lang:fr', label: 'Français', checked: locale === 'fr' },
 *   ] },
 * ]" />
 * ```
 */
import { computed, nextTick, onBeforeUnmount, onMounted, ref, useId, watch } from 'vue'
import { Icon } from '@iconify/vue'
import { TAvatar } from '@tnzi/ui/components'
import { useOverlayLayer } from '@tnzi/ui/headless'
import TPopoverMenu from './TPopoverMenu.vue'
import { useAiI18n, formatAiMessage } from '../../i18n/index'
import { opensSubmenu, resolveMenuLevel } from './user-menu'
import { vTooltip } from '../../headless/tooltip'
import { placeBeside } from '../../headless/place-beside'
import { createSubmenuIntent, type MenuPoint } from '../../headless/submenu-intent'
import { useViewportBelow } from '../../headless/useViewportBelow'

export interface UserMenuItem {
  readonly id: string
  readonly label: string
  /** Iconify name rendered at the left edge. */
  readonly icon?: string
  /** Read-only value pinned right (e.g. a credit balance). */
  readonly value?: string
  /** Action pill pinned right (e.g. "Upgrade"). Emits `select` with this
   *  item's id, same as clicking the row. */
  readonly badge?: string
  /** Marks the item as leaving the app; renders an outbound arrow. */
  readonly external?: boolean
  /** Renders a chevron, signalling the item opens a sub-surface. */
  readonly chevron?: boolean
  /** Destructive styling (sign out, delete). */
  readonly danger?: boolean
  /** Draw a separator above this item. This is how a flat array expresses
   *  grouping without a nested structure that every consumer would have to
   *  build even for two items. */
  readonly dividerBefore?: boolean
  /** Render as a non-interactive label row. Use with `badge` for a plan row. */
  readonly static?: boolean
  /** A submenu: beside the menu with a mouse, in the menu's place on a phone. */
  readonly children?: ReadonlyArray<UserMenuItem>
  /** The current choice in a picker submenu (a check mark at the right edge). */
  readonly checked?: boolean
}

/** Trailing icon buttons on the status bar itself (an apps launcher,
 *  notifications, feedback), flush right of the account button. `label` is
 *  the tooltip and the accessible name. */
export interface UserBarAction {
  readonly id: string
  readonly icon: string
  readonly label: string
}

const props = withDefaults(
  defineProps<{
    /** Display name. */
    name?: string
    /** Secondary line in the menu header (email, workspace, plan tier). */
    subtitle?: string
    /** Avatar image. Falls back to initials derived from `name`. */
    avatarSrc?: string | null
    /** Replaces the non-destructive built-in items (account, settings). */
    items?: ReadonlyArray<UserMenuItem>
    /** Appended after `items`, before sign-out. The usual way to extend. */
    extraItems?: ReadonlyArray<UserMenuItem>
    /** Render the built-in sign-out row at the bottom. */
    showSignOut?: boolean
    /** Icon buttons rendered on the bar, right of the name. */
    actions?: ReadonlyArray<UserBarAction>
    /** Show the account-switcher affordance in the menu header. */
    switchable?: boolean
    /** Hide the menu header block entirely. */
    showHeader?: boolean
    /**
     * Avatar-only trigger, for a collapsed icon rail. `actions` are dropped in
     * this mode: a 56px rail has no room for them, and a rail that needs its
     * own affordances should compose them itself rather than have this
     * component guess at a vertical layout.
     */
    compact?: boolean
    /** Which way the menu opens. Defaults to `top` for sidebar-footer use. */
    placement?: 'top' | 'bottom'
    /**
     * Tooltip of the avatar button (`vTooltip`, beside it on the right). By
     * default `compact` shows the display name there, since the rail shows no
     * name, and the full bar shows none. A string replaces the text, `false`
     * turns it off. The button's accessible name stays "Account menu".
     */
    tooltip?: string | false
  }>(),
  {
    name: '',
    subtitle: '',
    avatarSrc: null,
    items: undefined,
    extraItems: () => [],
    showSignOut: true,
    actions: () => [],
    switchable: false,
    showHeader: true,
    compact: false,
    placement: 'top',
    tooltip: undefined,
  },
)

const emit = defineEmits<{
  /** A menu item (or its badge) was activated. */
  select: [id: string]
  /** The account-switcher affordance was clicked. */
  'switch-account': []
  /** A status-bar icon button was clicked. */
  action: [id: string]
}>()

const t = useAiI18n()
const open = ref(false)

const builtinItems = computed<ReadonlyArray<UserMenuItem>>(() => [
  { id: 'account', label: t.value.account.accountLabel, icon: 'lucide:circle-user-round' },
  { id: 'settings', label: t.value.sidebar.settings, icon: 'lucide:settings-2' },
])

const signOutItem = computed<UserMenuItem>(() => ({
  id: 'sign-out',
  label: t.value.account.signOut,
  icon: 'lucide:log-out',
  danger: true,
  dividerBefore: true,
}))

/* Sign-out is appended last rather than being part of the base set, so
   `extraItems` cannot land underneath it. */
const effectiveItems = computed<ReadonlyArray<UserMenuItem>>(() => [
  ...(props.items ?? builtinItems.value),
  ...props.extraItems,
  ...(props.showSignOut ? [signOutItem.value] : []),
])

const displayName = computed(() => props.name || t.value.account.fallbackName)

/* Off while the menu is open: the menu's header already names the account. */
const triggerTooltip = computed(() => {
  if (props.tooltip === false) return null
  const text = props.tooltip ?? (props.compact ? displayName.value : '')
  return text ? { text, side: 'right' as const, disabled: open.value } : null
})

/* ---- Submenus: in place (drill-down) ---- */

/* The ids of the submenus opened in place, outermost first; empty at the top level. */
const path = ref<string[]>([])
const level = computed(() => resolveMenuLevel(effectiveItems.value, path.value))
const parentItem = computed(() => level.value.trail[level.value.trail.length - 1] ?? null)

/* ---- Submenus: beside the menu (fly-out) ---- */

/** Below this width a submenu drills in place: the shell's phone layout. */
const FLYOUT_MIN_WIDTH = 768
/** Space between the menu and a fly-out. */
const FLYOUT_GAP = 4
/** The menu's padding plus its border: a fly-out's first row lines up with its item. */
const FLYOUT_ROW_OFFSET = 7
const FINE_HOVER_QUERY = '(hover: hover) and (pointer: fine)'

const host = ref<HTMLDivElement | null>(null)
const flyoutEl = ref<HTMLDivElement | null>(null)
const flyoutDomId = `t-user-menu-sub-${useId()}`
const narrowViewport = useViewportBelow(FLYOUT_MIN_WIDTH)
const fineHover = ref(false)
let hoverQuery: MediaQueryList | null = null
const onHoverQueryChange = (event: MediaQueryListEvent): void => {
  fineHover.value = event.matches
}

onMounted(() => {
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return
  hoverQuery = window.matchMedia(FINE_HOVER_QUERY)
  fineHover.value = hoverQuery.matches
  hoverQuery.addEventListener?.('change', onHoverQueryChange)
})

/* Whether submenus fly out on this device. A tap still drills in place even
   then (`lastPointerType`): a hybrid laptop reports a fine hovering pointer
   while its user is using the touch screen. */
const flyoutMode = computed(() => fineHover.value && !narrowViewport.value)
let lastPointerType = ''

const flyoutId = ref<string | null>(null)
const intent = createSubmenuIntent({
  submenuRect: () => flyoutEl.value?.getBoundingClientRect() ?? null,
  onChange: (id) => (flyoutId.value = id),
})

const flyoutItem = computed(() =>
  flyoutId.value === null ? null : level.value.items.find((i) => i.id === flyoutId.value && opensSubmenu(i)) ?? null,
)
/* Levels below the fly-out drill in place inside it. */
const flyoutPath = ref<string[]>([])
const flyoutLevel = computed(() => resolveMenuLevel(flyoutItem.value?.children ?? [], flyoutPath.value))
const flyoutBack = computed(() => flyoutLevel.value.trail[flyoutLevel.value.trail.length - 1] ?? null)
const flyoutPlaced = ref(false)

// Closing the menu returns it to the top: the next open starts where it always does.
watch(open, (isOpen) => {
  if (isOpen) return
  path.value = []
  intent.close()
})
watch(flyoutMode, () => intent.close())
watch(flyoutId, (id) => {
  flyoutPath.value = []
  flyoutPlaced.value = false
  if (id === null) unlistenFlyout()
  else listenFlyout()
})

function itemElement(id: string): HTMLElement | null {
  const rows = host.value?.querySelectorAll<HTMLElement>('[data-user-menu-item]') ?? []
  return Array.from(rows).find((el) => el.dataset.userMenuItem === id && !flyoutEl.value?.contains(el)) ?? null
}

/**
 * Beside the menu, its first row level with the item that opened it; on the
 * left when the right has no room, and kept inside the window (a menu rising
 * from the bottom of the sidebar pushes it up). It is `position: fixed` inside
 * the menu, so neither the menu's scrolling nor the sidebar's overflow clips
 * it; it is measured at the origin first in case an ancestor is transformed.
 */
function placeFlyout(): void {
  const panel = flyoutEl.value
  const item = flyoutId.value === null ? null : itemElement(flyoutId.value)
  const menu = item?.closest<HTMLElement>('.t-popover-menu')
  if (!panel || !item || !menu || typeof window === 'undefined') return
  panel.style.top = '0px'
  panel.style.left = '0px'
  const origin = panel.getBoundingClientRect()
  const menuBox = menu.getBoundingClientRect()
  const itemBox = item.getBoundingClientRect()
  const position = placeBeside(
    { top: itemBox.top - FLYOUT_ROW_OFFSET, left: menuBox.left, width: menuBox.width, height: itemBox.height },
    { width: panel.offsetWidth, height: panel.offsetHeight },
    { width: window.innerWidth, height: window.innerHeight },
    { side: 'right', align: 'start', gap: FLYOUT_GAP, edge: 8 },
  )
  panel.style.top = `${position.top - origin.top}px`
  panel.style.left = `${position.left - origin.left}px`
  panel.dataset.side = position.side
  flyoutPlaced.value = true
}

let frame = 0
function schedulePlaceFlyout(): void {
  if (frame || typeof window === 'undefined') return
  frame = window.requestAnimationFrame(() => {
    frame = 0
    placeFlyout()
  })
}

function listenFlyout(): void {
  if (typeof window === 'undefined') return
  window.addEventListener('resize', schedulePlaceFlyout)
  window.addEventListener('scroll', schedulePlaceFlyout, true)
  void nextTick(placeFlyout)
}

function unlistenFlyout(): void {
  if (typeof window === 'undefined') return
  window.removeEventListener('resize', schedulePlaceFlyout)
  window.removeEventListener('scroll', schedulePlaceFlyout, true)
  if (frame) window.cancelAnimationFrame(frame)
  frame = 0
}

onBeforeUnmount(() => {
  intent.destroy()
  unlistenFlyout()
  hoverQuery?.removeEventListener?.('change', onHoverQueryChange)
})

/* Pointer events, mouse only: a finger has no hover, and its tap is a click. */
const hovers = (event: PointerEvent): boolean => flyoutMode.value && event.pointerType !== 'touch'
const pointOf = (event: PointerEvent): MenuPoint => ({ x: event.clientX, y: event.clientY })

function onRowEnter(item: UserMenuItem, event: PointerEvent): void {
  if (hovers(event)) intent.enterItem(opensSubmenu(item) ? item.id : null)
}
function onRowLeave(item: UserMenuItem, event: PointerEvent): void {
  if (hovers(event)) intent.leaveItem(item.id, pointOf(event))
}
function onMenuPointerDown(event: PointerEvent): void {
  lastPointerType = event.pointerType
}
function onMenuEnter(event: PointerEvent): void {
  if (hovers(event)) intent.enterMenus()
}
function onMenuMove(event: PointerEvent): void {
  if (hovers(event)) intent.move(pointOf(event))
}
function onMenuLeave(event: PointerEvent): void {
  if (hovers(event)) intent.leaveMenus()
}
function onFlyoutEnter(event: PointerEvent): void {
  if (hovers(event)) intent.enterSubmenu()
}

/* ---- Focus and keys ---- */

function rowsIn(container: HTMLElement | null | undefined): HTMLElement[] {
  return Array.from(container?.querySelectorAll<HTMLElement>('[data-user-menu-item]:not([disabled])') ?? [])
}

/* The checked choice when there is one, as a native select does; else the
   first item after the back row. */
function focusFirstIn(container: HTMLElement | null | undefined): void {
  const rows = rowsIn(container)
  const target =
    rows.find((el) => el.getAttribute('aria-checked') === 'true') ??
    rows.find((el) => el.dataset.userMenuItem !== '__back') ??
    rows[0]
  target?.focus()
}

function focusRow(id: string): void {
  itemElement(id)?.focus()
}

async function openFlyout(item: UserMenuItem, moveFocus: boolean): Promise<void> {
  intent.openNow(item.id)
  if (!moveFocus) return
  await nextTick()
  placeFlyout()
  await nextTick()
  focusFirstIn(flyoutEl.value)
}

function closeFlyout(returnFocus: boolean): void {
  const id = flyoutId.value
  intent.close()
  if (returnFocus && id !== null) void nextTick(() => focusRow(id))
}

/** A click from a key (Enter / Space on a button) has no pointer position. */
const fromKeyboard = (event: MouseEvent): boolean => event.detail === 0

function selectItem(item: UserMenuItem, event: MouseEvent): void {
  if (item.static) return
  if (opensSubmenu(item)) {
    if (flyoutMode.value && lastPointerType !== 'touch') {
      void openFlyout(item, fromKeyboard(event))
      return
    }
    intent.close()
    path.value = [...path.value, item.id]
    if (fromKeyboard(event)) void nextTick(() => focusFirstIn(host.value?.querySelector('.t-popover-menu')))
    return
  }
  open.value = false
  emit('select', item.id)
}

function selectFlyoutItem(item: UserMenuItem, event: MouseEvent): void {
  if (item.static) return
  if (opensSubmenu(item)) {
    flyoutPath.value = [...flyoutPath.value, item.id]
    if (fromKeyboard(event)) void nextTick(() => focusFirstIn(flyoutEl.value))
    return
  }
  open.value = false
  emit('select', item.id)
}

function goBack(): void {
  const leaving = path.value[path.value.length - 1]
  path.value = path.value.slice(0, -1)
  if (leaving) void nextTick(() => focusRow(leaving))
}

function flyoutGoBack(): void {
  const leaving = flyoutPath.value[flyoutPath.value.length - 1]
  flyoutPath.value = flyoutPath.value.slice(0, -1)
  if (leaving) void nextTick(() => rowsIn(flyoutEl.value).find((el) => el.dataset.userMenuItem === leaving)?.focus())
}

/* Back one level: inside the fly-out first, then out of it to its item, then
   up from a level opened in place. Never the whole menu. */
function stepBack(): boolean {
  if (flyoutId.value !== null) {
    if (flyoutPath.value.length > 0) flyoutGoBack()
    else closeFlyout(true)
    return true
  }
  if (path.value.length > 0) {
    goBack()
    return true
  }
  return false
}

function moveFocusIn(container: HTMLElement, key: string): void {
  const rows = rowsIn(container)
  if (rows.length === 0) return
  const current = rows.indexOf(document.activeElement as HTMLElement)
  const next =
    key === 'Home' ? 0
      : key === 'End' ? rows.length - 1
        : key === 'ArrowDown' ? (current + 1) % rows.length
          : (current <= 0 ? rows.length : current) - 1
  rows[next]!.focus()
}

function onMenuKeydown(event: KeyboardEvent): void {
  const target = event.target instanceof HTMLElement ? event.target : null
  const inFlyout = !!target && !!flyoutEl.value?.contains(target)
  if (inFlyout && ['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) {
    event.preventDefault()
    moveFocusIn(flyoutEl.value!, event.key)
    return
  }
  if (event.key === 'ArrowLeft') {
    if ((inFlyout || flyoutId.value === null) && stepBack()) event.preventDefault()
    return
  }
  if (event.key !== 'ArrowRight' || !target) return
  const id = target.dataset.userMenuItem
  const list = inFlyout ? flyoutLevel.value.items : level.value.items
  const item = list.find((i) => i.id === id)
  if (!item || !opensSubmenu(item)) return
  event.preventDefault()
  if (inFlyout) {
    flyoutPath.value = [...flyoutPath.value, item.id]
    void nextTick(() => focusFirstIn(flyoutEl.value))
  } else if (flyoutMode.value) {
    void openFlyout(item, true)
  } else {
    path.value = [...path.value, item.id]
    void nextTick(() => focusFirstIn(host.value?.querySelector('.t-popover-menu')))
  }
}

/* Escape goes back a level while one is open, through the page's overlay
   stack, so it is not also taken by a dialog or a drawer underneath. */
useOverlayLayer(() => open.value && (flyoutId.value !== null || path.value.length > 0), {
  onEscape: () => void stepBack(),
})

function selectBadge(item: UserMenuItem): void {
  open.value = false
  emit('select', item.id)
}

function onSwitchAccount(): void {
  open.value = false
  emit('switch-account')
}

/* Bar action buttons must not fall through to the bar's own click handler,
   otherwise pressing "notifications" would also toggle the account menu. */
function onAction(id: string, event: MouseEvent): void {
  event.stopPropagation()
  emit('action', id)
}
</script>

<template>
  <div ref="host" class="t-user-menu" :class="{ 't-user-menu--compact': compact }">
    <div class="t-user-menu__bar">
      <button
        type="button"
        v-tooltip="triggerTooltip"
        class="t-user-menu__trigger"
        :aria-label="t.account.userMenu"
        :aria-expanded="open"
        aria-haspopup="menu"
        @click="open = !open"
      >
        <TAvatar
          class="t-user-menu__avatar"
          :src="avatarSrc ?? undefined"
          :name="displayName"
          :size="compact ? 28 : 26"
          :max-initials="2"
        />
        <span v-if="!compact" class="t-user-menu__name">{{ displayName }}</span>
      </button>

      <div v-if="actions.length > 0 && !compact" class="t-user-menu__actions">
        <button
          v-for="action in actions"
          :key="action.id"
          type="button"
          class="t-user-menu__action"
          :aria-label="action.label"
          :title="action.label"
          @click="onAction(action.id, $event)"
        >
          <Icon :icon="action.icon" />
        </button>
      </div>
    </div>

    <!-- Expanded: the menu takes the account row's width, so it reads as that
         row opening upward rather than as a panel pinned to the window edge.
         In the 56px rail there is no width to match, so it keeps its own. -->
    <TPopoverMenu
      v-model="open"
      align="left"
      :placement="placement"
      :full-width="!compact"
      :min-width="264"
      :max-width="300"
      @pointerdown="onMenuPointerDown"
      @pointerenter="onMenuEnter"
      @pointermove="onMenuMove"
      @pointerleave="onMenuLeave"
      @keydown="onMenuKeydown"
    >
      <slot name="menu" :close="() => (open = false)">
        <!-- Inside a submenu the back row replaces the account header: the
             header belongs to the top of the menu, the back row says where
             you are and how to get out. -->
        <template v-if="parentItem">
          <button
            type="button"
            class="t-popover-menu__item t-user-menu__back"
            role="menuitem"
            data-user-menu-item="__back"
            :aria-label="formatAiMessage(t.account.backTo, { label: parentItem.label })"
            @click="goBack"
          >
            <Icon icon="lucide:chevron-left" />
            <span class="t-user-menu__item-label">{{ parentItem.label }}</span>
          </button>
          <div class="t-popover-menu__sep" />
        </template>
        <slot v-else name="menu-header">
          <div v-if="showHeader" class="t-user-menu__head">
            <TAvatar
              :src="avatarSrc ?? undefined"
              :name="displayName"
              :size="34"
              :max-initials="2"
            />
            <div class="t-user-menu__head-text">
              <span class="t-user-menu__head-name">{{ displayName }}</span>
              <span v-if="subtitle" class="t-user-menu__head-sub">{{ subtitle }}</span>
            </div>
            <button
              v-if="switchable"
              type="button"
              class="t-user-menu__switch"
              :aria-label="t.account.switchAccount"
              @click="onSwitchAccount"
            >
              <Icon icon="lucide:chevrons-up-down" />
            </button>
          </div>
          <div v-if="showHeader" class="t-popover-menu__sep" />
        </slot>

        <template v-for="item in level.items" :key="item.id">
          <div v-if="item.dividerBefore" class="t-popover-menu__sep" />

          <div
            v-if="item.static"
            class="t-popover-menu__row"
            @pointerenter="onRowEnter(item, $event)"
          >
            <Icon v-if="item.icon" :icon="item.icon" />
            <span>{{ item.label }}</span>
            <button
              v-if="item.badge"
              type="button"
              class="t-popover-menu__item-badge"
              @click="selectBadge(item)"
            >{{ item.badge }}</button>
          </div>

          <button
            v-else
            type="button"
            class="t-popover-menu__item"
            :class="{
              't-popover-menu__item--danger': item.danger,
              't-user-menu__item--open': flyoutItem?.id === item.id,
            }"
            :role="item.checked === undefined ? 'menuitem' : 'menuitemradio'"
            :aria-checked="item.checked === undefined ? undefined : item.checked"
            :aria-haspopup="item.children?.length ? 'menu' : undefined"
            :aria-expanded="item.children?.length ? flyoutItem?.id === item.id : undefined"
            :aria-controls="flyoutItem?.id === item.id ? flyoutDomId : undefined"
            :data-user-menu-item="item.id"
            @click="selectItem(item, $event)"
            @pointerenter="onRowEnter(item, $event)"
            @pointerleave="onRowLeave(item, $event)"
          >
            <Icon v-if="item.icon" :icon="item.icon" />
            <span class="t-user-menu__item-label">{{ item.label }}</span>
            <span v-if="item.value" class="t-popover-menu__item-value">{{ item.value }}</span>
            <Icon v-if="item.checked" class="t-popover-menu__item-check" icon="lucide:check" />
            <Icon
              v-else-if="item.external"
              class="t-popover-menu__item-hint"
              icon="lucide:arrow-up-right"
            />
            <Icon
              v-else-if="item.chevron || item.children?.length"
              class="t-popover-menu__item-hint"
              icon="lucide:chevron-right"
            />
          </button>
        </template>

        <!-- The open fly-out. A child of the menu in the DOM, so the pointer
             moving into it never leaves the menu and a click in it is the
             menu's; `position: fixed` puts it beside the menu on screen. -->
        <div
          v-if="flyoutItem"
          :id="flyoutDomId"
          ref="flyoutEl"
          class="t-user-menu__flyout t-ai-scroll"
          :class="{ 't-user-menu__flyout--placing': !flyoutPlaced }"
          role="menu"
          :aria-label="(flyoutBack ?? flyoutItem).label"
          @pointerenter="onFlyoutEnter"
        >
          <template v-if="flyoutBack">
            <button
              type="button"
              class="t-popover-menu__item t-user-menu__back"
              role="menuitem"
              data-user-menu-item="__back"
              :aria-label="formatAiMessage(t.account.backTo, { label: flyoutBack.label })"
              @click="flyoutGoBack"
            >
              <Icon icon="lucide:chevron-left" />
              <span class="t-user-menu__item-label">{{ flyoutBack.label }}</span>
            </button>
            <div class="t-popover-menu__sep" />
          </template>
          <template v-for="child in flyoutLevel.items" :key="child.id">
            <div v-if="child.dividerBefore" class="t-popover-menu__sep" />
            <div v-if="child.static" class="t-popover-menu__row">
              <Icon v-if="child.icon" :icon="child.icon" />
              <span>{{ child.label }}</span>
              <button
                v-if="child.badge"
                type="button"
                class="t-popover-menu__item-badge"
                @click="selectBadge(child)"
              >{{ child.badge }}</button>
            </div>
            <button
              v-else
              type="button"
              class="t-popover-menu__item"
              :class="{ 't-popover-menu__item--danger': child.danger }"
              :role="child.checked === undefined ? 'menuitem' : 'menuitemradio'"
              :aria-checked="child.checked === undefined ? undefined : child.checked"
              :aria-haspopup="child.children?.length ? 'menu' : undefined"
              :data-user-menu-item="child.id"
              @click="selectFlyoutItem(child, $event)"
            >
              <Icon v-if="child.icon" :icon="child.icon" />
              <span class="t-user-menu__item-label">{{ child.label }}</span>
              <span v-if="child.value" class="t-popover-menu__item-value">{{ child.value }}</span>
              <Icon v-if="child.checked" class="t-popover-menu__item-check" icon="lucide:check" />
              <Icon
                v-else-if="child.external"
                class="t-popover-menu__item-hint"
                icon="lucide:arrow-up-right"
              />
              <Icon
                v-else-if="child.chevron || child.children?.length"
                class="t-popover-menu__item-hint"
                icon="lucide:chevron-right"
              />
            </button>
          </template>
        </div>

        <slot name="menu-footer" />
      </slot>
    </TPopoverMenu>
  </div>
</template>

<style scoped>
.t-user-menu {
  position: relative;
}
.t-user-menu__bar {
  display: flex;
  align-items: center;
  gap: 4px;
  /* Small: the sidebar footer supplies the column inset. Padding sized to sit
     inside it as one more row, not to inset the row a second time. */
  padding: 2px 4px;
}
.t-user-menu__trigger {
  /* Sized to its content, not the row: the fill under the cursor outlines
     the avatar and name, which is the thing being pressed, and leaves the
     rest of the row to the icon buttons at its right end. */
  flex: 0 1 auto;
  min-width: 0;
  display: flex;
  align-items: center;
  gap: 8px;
  /* Row tokens: a shell sets touch sizes on a phone (see TChatApp). */
  height: var(--tnzi-ai-row-height, 32px);
  /* The avatar sits almost on the left edge; the name gets the breathing room. */
  padding: 0 8px 0 2px;
  border: none;
  background: transparent;
  border-radius: 8px;
  color: var(--tnzi-ai-text);
  font-family: inherit;
  font-size: var(--tnzi-ai-row-font-size, 14px);
  text-align: left;
  cursor: pointer;
  transition: background var(--tnzi-ai-duration-fast, 120ms) var(--tnzi-ai-easing, ease);
}
/* The same subtle fill as every other sidebar row, and kept while the menu is
   open so the row reads as the menu's origin. Hover only where the pointer can
   hover: a touch browser would otherwise leave the fill stuck after a tap. */
@media (hover: hover) {
  .t-user-menu__trigger:hover {
    background: var(--tnzi-ai-sidebar-hover);
  }
}
.t-user-menu__trigger[aria-expanded='true'] {
  background: var(--tnzi-ai-sidebar-hover);
}
.t-user-menu__trigger:focus-visible,
.t-user-menu__action:focus-visible {
  background: var(--tnzi-ai-sidebar-hover);
  outline: var(--tnzi-ai-focus-outline);
  outline-offset: -2px;
}
.t-user-menu__avatar {
  flex-shrink: 0;
}
.t-user-menu__name {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-user-menu__actions {
  display: flex;
  align-items: center;
  gap: 2px;
  flex-shrink: 0;
  /* Flush right: the trigger only takes its content's width. */
  margin-left: auto;
}

/* Rail mode: avatar only, centred. The menu still opens at its full width -
   it escapes the rail rather than being squeezed into it. */
.t-user-menu--compact .t-user-menu__bar {
  padding: 4px;
  justify-content: center;
}
.t-user-menu--compact .t-user-menu__trigger {
  flex: none;
  width: 40px;
  height: 40px;
  padding: 0;
  justify-content: center;
}
.t-user-menu__action {
  width: var(--tnzi-ai-icon-button-size, 30px);
  height: var(--tnzi-ai-icon-button-size, 30px);
  border: none;
  background: transparent;
  border-radius: 8px;
  color: var(--tnzi-ai-text-secondary);
  font-size: 17px;
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  transition: background var(--tnzi-ai-duration-fast, 120ms) var(--tnzi-ai-easing, ease);
}
.t-user-menu__action:hover {
  background: var(--tnzi-ai-sidebar-hover);
  color: var(--tnzi-ai-text);
}

/* -- Menu header -- */
.t-user-menu__head {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px 10px 10px;
}
.t-user-menu__head-text {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
}
.t-user-menu__head-name {
  font-size: 14px;
  font-weight: 500;
  color: var(--tnzi-ai-text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-user-menu__head-sub {
  font-size: 12px;
  color: var(--tnzi-ai-text-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-user-menu__switch {
  flex-shrink: 0;
  width: 26px;
  height: 26px;
  border: none;
  background: transparent;
  border-radius: 6px;
  color: var(--tnzi-ai-text-tertiary);
  font-size: 15px;
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
}
.t-user-menu__switch:hover {
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text);
}

/* Label takes the slack so `margin-left:auto` on the trailing affordance is
   never the only thing holding the layout together. */
.t-user-menu__item-label {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

/* The item whose fly-out is open keeps the hover fill, so the fly-out reads
   as coming out of it while the pointer is over the fly-out. */
.t-popover-menu__item.t-user-menu__item--open {
  background: var(--tnzi-ai-hover, rgba(55, 53, 47, 0.04));
}

/* -- Fly-out submenu --
   The same surface as the menu it comes out of (TPopoverMenu's panel), on the
   same popover layer. Fixed and placed by script (`placeFlyout`). */
.t-user-menu__flyout {
  position: fixed;
  top: 0;
  left: 0;
  z-index: var(--tnzi-ai-z-popover, 2000);
  box-sizing: border-box;
  min-width: 200px;
  max-width: 300px;
  max-height: calc(100vh - 16px);
  overflow-y: auto;
  background: var(--tnzi-ai-surface, #ffffff);
  border: 1px solid var(--tnzi-ai-border, #e5e5e5);
  border-radius: 12px;
  box-shadow: 0 12px 32px rgba(0, 0, 0, 0.08);
  padding: 6px;
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.t-user-menu__flyout--placing {
  visibility: hidden;
}

/* NOTE: `.t-popover-menu__item` / `__sep` / `__row` / `__item-*` used in the
   template above are styled by TPopoverMenu's `:deep()` rules, which compile
   to `.t-popover-menu[data-v-popover] .t-popover-menu__item` and therefore
   match slot content regardless of which component's scope id it carries.
   Do NOT copy those rules here: a second definition is exactly how the two
   drift apart. */
</style>
