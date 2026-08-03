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
 */
import { computed, ref } from 'vue'
import { Icon } from '@iconify/vue'
import { TAvatar } from '@tnzi/ui'
import TPopoverMenu from './TPopoverMenu.vue'
import { useAiI18n } from '../../i18n/index'

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
}

/** Trailing icon buttons on the status bar itself (notifications, feedback). */
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

function selectItem(item: UserMenuItem): void {
  if (item.static) return
  open.value = false
  emit('select', item.id)
}

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
  <div class="t-user-menu" :class="{ 't-user-menu--compact': compact }">
    <div class="t-user-menu__bar">
      <button
        type="button"
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
          @click="onAction(action.id, $event)"
        >
          <Icon :icon="action.icon" />
        </button>
      </div>
    </div>

    <TPopoverMenu
      v-model="open"
      align="left"
      :placement="placement"
      :min-width="264"
      :max-width="300"
    >
      <slot name="menu" :close="() => (open = false)">
        <slot name="menu-header">
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

        <template v-for="item in effectiveItems" :key="item.id">
          <div v-if="item.dividerBefore" class="t-popover-menu__sep" />

          <div v-if="item.static" class="t-popover-menu__row">
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
            :class="{ 't-popover-menu__item--danger': item.danger }"
            role="menuitem"
            @click="selectItem(item)"
          >
            <Icon v-if="item.icon" :icon="item.icon" />
            <span class="t-user-menu__item-label">{{ item.label }}</span>
            <span v-if="item.value" class="t-popover-menu__item-value">{{ item.value }}</span>
            <Icon
              v-if="item.external"
              class="t-popover-menu__item-hint"
              icon="lucide:arrow-up-right"
            />
            <Icon
              v-else-if="item.chevron"
              class="t-popover-menu__item-hint"
              icon="lucide:chevron-right"
            />
          </button>
        </template>

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
  gap: 2px;
  padding: 6px 8px;
}
.t-user-menu__trigger {
  flex: 1;
  min-width: 0;
  display: flex;
  align-items: center;
  gap: 8px;
  height: 36px;
  padding: 0 6px;
  border: none;
  background: transparent;
  border-radius: 10px;
  color: var(--tnzi-ai-text);
  font-family: inherit;
  font-size: 14px;
  text-align: left;
  cursor: pointer;
  transition: background var(--tnzi-ai-duration-fast, 120ms) var(--tnzi-ai-easing, ease);
}
.t-user-menu__trigger:hover {
  background: var(--tnzi-ai-hover);
}
.t-user-menu__avatar {
  flex-shrink: 0;
}
.t-user-menu__name {
  flex: 1;
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
  width: 30px;
  height: 30px;
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
  background: var(--tnzi-ai-hover);
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

/* NOTE: `.t-popover-menu__item` / `__sep` / `__row` / `__item-*` used in the
   template above are styled by TPopoverMenu's `:deep()` rules, which compile
   to `.t-popover-menu[data-v-popover] .t-popover-menu__item` and therefore
   match slot content regardless of which component's scope id it carries.
   Do NOT copy those rules here: a second definition is exactly how the two
   drift apart. */
</style>
