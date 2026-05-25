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
import { computed, h } from 'vue'
import { NDropdown, NButton, useDialog } from 'naive-ui'
import type { DropdownOption } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { translatePageKey } from '../../pages/_shared/translate'

interface Props {
  /** Display name shown next to the avatar. */
  userName?: string
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
}

const props = withDefaults(defineProps<Props>(), {
  userName: 'User',
  avatarIcon: 'mdi:account-circle-outline',
  onUserCenter: undefined,
  onLogout: undefined,
  translate: undefined,
  signedIn: true,
  onSignIn: undefined,
})

const dialog = useDialog()

function t(key: string, fallback: string): string {
  if (props.translate) return props.translate(key, fallback)
  // Bundled-locale fallback so the user-avatar dropdown localises out of
  // the box. `translatePageKey('', absoluteKey)` resolves `admin.*` keys
  // against the active locale and humanises misses.
  const bundled = translatePageKey('', key)
  if (bundled && bundled !== key) return bundled
  return fallback
}

const options = computed<DropdownOption[]>(() => [
  {
    key: 'user-center',
    label: t('admin.user.center', 'User Center'),
    icon: () => h(TSvgIcon, { icon: 'mdi:account-circle', size: 18 }),
  },
  { type: 'divider', key: 'divider' },
  {
    key: 'logout',
    label: t('admin.user.logout', 'Logout'),
    icon: () => h(TSvgIcon, { icon: 'mdi:logout', size: 18 }),
  },
])

function handleSelect(key: string | number): void {
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
      <TSvgIcon :icon="avatarIcon" :size="22" class="t-admin-user-avatar__icon" />
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
