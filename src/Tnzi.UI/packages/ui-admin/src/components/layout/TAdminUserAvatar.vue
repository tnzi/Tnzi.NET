<script setup lang="ts">
/**
 * `TAdminUserAvatar` — header user avatar + dropdown.
 *
 * Mirrors `soybean-admin-example/src/layouts/modules/global-header/components/user-avatar.vue`:
 * shows the user's display name next to an avatar icon, with a click
 * dropdown for "User Center" / "Logout".
 *
 * The component is intentionally stateless about *which* auth store it
 * reads — consumers pass `userName` and `onLogout` (and optionally
 * `onUserCenter`) as props. `AdminShellRoot` wires the defaults from
 * `useAdminLoginConfig().userMenu` so the common case (Acme et al.)
 * is one-config-line away.
 */
import { computed, h, ref, watch } from 'vue'
import { NDropdown, NButton, useDialog } from 'naive-ui'
import type { DropdownOption } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { UserPresenceStatus } from '@tnzi/core/services/chat'
import { translatePageKey } from '../../pages/_shared/translate'
import TPresenceDot from '../chat/TPresenceDot.vue'

interface Props {
  /** Display name shown next to the avatar. */
  userName?: string
  /**
   * Resolved avatar image URL. When present (and it loads) the header shows
   * the real picture; on a load error — or when absent — it falls back to the
   * name initial, then to `avatarIcon`. Resolve via `resolveAvatarUrl()` in
   * the container so this component stays stateless about storage/DTO shapes.
   */
  avatarUrl?: string | null
  /** Iconify icon for the avatar. Default `mdi:account-circle-outline`. */
  avatarIcon?: string
  /** Called when the user picks "User Center" from the dropdown. */
  onUserCenter?: () => void | Promise<void>
  /** Called when the user confirms "Logout". */
  onLogout?: () => void | Promise<void>
  /** Translator (e.g. vue-i18n `$t`). */
  translate?: (key: string, fallback?: string) => string
  /** Whether the user is signed in. When false the component renders a "Sign in" button. */
  signedIn?: boolean
  /** Called when an unsigned user clicks "Sign in". */
  onSignIn?: () => void | Promise<void>
  /**
   * Current presence status. When provided together with `onSetPresence`, the
   * avatar shows a status dot and the dropdown gains a "Status" submenu so the
   * user can switch online/away/busy/invisible from the admin header.
   */
  presence?: UserPresenceStatus | null
  /** Called when the user picks a new presence status from the dropdown. */
  onSetPresence?: (status: UserPresenceStatus) => void | Promise<void>
}

const props = withDefaults(defineProps<Props>(), {
  userName: 'User',
  avatarUrl: null,
  avatarIcon: 'mdi:account-circle-outline',
  onUserCenter: undefined,
  onLogout: undefined,
  translate: undefined,
  signedIn: true,
  onSignIn: undefined,
  presence: null,
  onSetPresence: undefined,
})

const dialog = useDialog()

// Presence switching is only offered when both the current status and a setter
// are supplied (i.e. chat is enabled and wired by the shell).
const presenceEnabled = computed(() => props.presence != null && !!props.onSetPresence)

const PRESENCE_OPTIONS: { key: UserPresenceStatus; labelKey: string; fallback: string }[] = [
  { key: UserPresenceStatus.Online, labelKey: 'admin.user.presence.online', fallback: 'Online' },
  { key: UserPresenceStatus.Away, labelKey: 'admin.user.presence.away', fallback: 'Away' },
  { key: UserPresenceStatus.Busy, labelKey: 'admin.user.presence.busy', fallback: 'Busy' },
  { key: UserPresenceStatus.Invisible, labelKey: 'admin.user.presence.invisible', fallback: 'Invisible' },
]

const currentPresenceLabel = computed(() => {
  const o = PRESENCE_OPTIONS.find((x) => x.key === props.presence)
  return o ? t(o.labelKey, o.fallback) : ''
})

// Track image load failures so a broken/expired avatar URL degrades to the
// name initial instead of a broken-image glyph. Reset whenever the URL
// changes (a fresh upload should get a fresh chance to load).
const imgFailed = ref(false)
watch(
  () => props.avatarUrl,
  () => { imgFailed.value = false },
)
const showAvatarImage = computed(() => !!props.avatarUrl && !imgFailed.value)

/** First letter of the display name, used as the image fallback. */
const initial = computed(() => {
  const name = (props.userName ?? '').trim()
  return name ? name.slice(0, 1).toUpperCase() : ''
})

function t(key: string, fallback: string): string {
  if (props.translate) return props.translate(key, fallback)
  // Bundled-locale fallback so the user-avatar dropdown localises out of
  // the box. `translatePageKey('', absoluteKey)` resolves `admin.*` keys
  // against the active locale and humanises misses.
  const bundled = translatePageKey('', key)
  if (bundled && bundled !== key) return bundled
  return fallback
}

const options = computed<DropdownOption[]>(() => {
  const items: DropdownOption[] = []
  if (presenceEnabled.value) {
    items.push({
      key: 'status',
      label: `${t('admin.user.status', 'Status')} · ${currentPresenceLabel.value}`,
      icon: () => h(TPresenceDot, { status: props.presence, size: 10 }),
      children: PRESENCE_OPTIONS.map((o) => ({
        key: `presence:${o.key}`,
        label: o.key === props.presence ? `${t(o.labelKey, o.fallback)} ✓` : t(o.labelKey, o.fallback),
        icon: () => h(TPresenceDot, { status: o.key, size: 10 }),
      })),
    })
    items.push({ type: 'divider', key: 'divider-status' })
  }
  items.push({
    key: 'user-center',
    label: t('admin.user.center', 'User Center'),
    icon: () => h(TSvgIcon, { icon: 'mdi:account-circle', size: 18 }),
  })
  items.push({ type: 'divider', key: 'divider' })
  items.push({
    key: 'logout',
    label: t('admin.user.logout', 'Logout'),
    icon: () => h(TSvgIcon, { icon: 'mdi:logout', size: 18 }),
  })
  return items
})

function handleSelect(key: string | number): void {
  if (typeof key === 'string' && key.startsWith('presence:')) {
    const status = Number(key.slice('presence:'.length)) as UserPresenceStatus
    void props.onSetPresence?.(status)
    return
  }
  if (key === 'logout') {
    confirmLogout()
    return
  }
  if (key === 'user-center') {
    void props.onUserCenter?.()
  }
}

function confirmLogout(): void {
  dialog.info({
    title: t('admin.user.tip', 'Confirm'),
    content: t('admin.user.logoutConfirm', 'Are you sure you want to log out?'),
    positiveText: t('admin.common.confirm', 'Confirm'),
    negativeText: t('admin.common.cancel', 'Cancel'),
    onPositiveClick: () => {
      void props.onLogout?.()
    },
  })
}
</script>

<template>
  <NButton v-if="!signedIn" quaternary class="t-admin-user-avatar__signin" @click="onSignIn?.()">
    {{ t('admin.user.signIn', 'Sign in') }}
  </NButton>
  <NDropdown
    v-else
    placement="bottom"
    trigger="click"
    :options="options"
    @select="handleSelect"
  >
    <button class="t-admin-user-avatar" type="button" :title="userName">
      <span class="t-admin-user-avatar__pic">
        <img
          v-if="showAvatarImage"
          :src="avatarUrl ?? ''"
          :alt="userName"
          class="t-admin-user-avatar__img"
          @error="imgFailed = true"
        />
        <span v-else-if="initial" class="t-admin-user-avatar__initial" aria-hidden="true">{{ initial }}</span>
        <TSvgIcon v-else :icon="avatarIcon" :size="22" class="t-admin-user-avatar__icon" />
        <TPresenceDot v-if="presenceEnabled" :status="presence" :size="9" class="t-admin-user-avatar__dot" />
      </span>
      <span class="t-admin-user-avatar__name">{{ userName }}</span>
    </button>
  </NDropdown>
</template>

<style scoped>
.t-admin-user-avatar {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  height: 36px;
  padding: 0 8px;
  border: none;
  background: transparent;
  cursor: pointer;
  border-radius: var(--tnzi-admin-radius-md, 8px);
  color: var(--tnzi-base-text);
  font-size: 14px;
  font-weight: 500;
  transition: background-color 0.15s ease;
}
.t-admin-user-avatar:hover {
  background-color: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.06);
}
.t-admin-user-avatar__icon {
  color: var(--tnzi-primary);
}
/* Avatar wrapper — anchors the presence status dot. */
.t-admin-user-avatar__pic {
  position: relative;
  display: inline-flex;
  flex-shrink: 0;
}
.t-admin-user-avatar__dot {
  position: absolute;
  right: -1px;
  bottom: -1px;
  /* Border matches the header surface so the dot reads as an overlay badge. */
  border-color: var(--tnzi-container-bg, #fff);
}
/* Real avatar picture — circular, cover-cropped, sized to match the icon. */
.t-admin-user-avatar__img {
  width: 24px;
  height: 24px;
  border-radius: 50%;
  object-fit: cover;
  flex-shrink: 0;
}
/* Name-initial fallback when there's no picture (or it failed to load). */
.t-admin-user-avatar__initial {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  border-radius: 50%;
  background: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.12);
  color: var(--tnzi-primary);
  font-size: 12px;
  font-weight: 600;
  line-height: 1;
  flex-shrink: 0;
}
.t-admin-user-avatar__name {
  max-width: 160px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
/* Mobile: drop the textual name so a long display name doesn't crowd
   out the language switcher + notification slot. Avatar icon + native
   title tooltip keep the affordance discoverable. */
@media (max-width: 640px) {
  .t-admin-user-avatar__name {
    display: none;
  }
  .t-admin-user-avatar {
    padding: 0 4px;
  }
}
</style>
