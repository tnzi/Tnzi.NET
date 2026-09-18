<template>
  <TUserCenterSection :title="t('sessions.title')" fill :contained="false">
    <template #actions>
      <NPopconfirm @positive-click="revokeAll">
        <template #trigger>
          <NButton size="small" type="error" ghost :disabled="!otherSessions.length">
            {{ t('sessions.revokeAll') }}
          </NButton>
        </template>
        {{ t('sessions.confirmRevokeAll') }}
      </NPopconfirm>
    </template>

    <p class="t-uc-hint">{{ t('sessions.hint') }}</p>
    <TResponsiveTable
      class="t-uc-table"
      :data="sessions"
      :columns="columns"
      :row-key="(r: UserSessionDto) => r.id"
      size="small"
      :bordered="false"
      :loading="loading"
      :flex-height="true"
      :pagination="{ pageSize: 10 }"
    />
  </TUserCenterSection>
</template>

<script setup lang="ts">
import { EMPTY_DASH } from '../../../utils/placeholders'
import { computed, h, onMounted, ref, watch } from 'vue'
import { NButton, NPopconfirm, NTag } from 'naive-ui'
import type { DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { formatDateTime } from '@tnzi/core'
import { readSessionIdClaim } from '@tnzi/core/services/identity'
import type { UserSessionDto } from '@tnzi/core/services/identity'
import TUserCenterSection from './TUserCenterSection.vue'
import TResponsiveTable from '../../../components/data/TResponsiveTable.vue'
import { deviceIconColor, parseDeviceInfo } from '../../_shared/device-info'
import { useAdminClient } from '../../../plugin/client'
import { createGuardedLoader } from '../guarded-loader'
import { useUserCenterContext } from '../user-center-context'

const ctx = useUserCenterContext()
const t = ctx.t
// Optional: an isolated mount has no client and simply cannot mark a row.
const client = useAdminClient(false)

const sessions = ref<UserSessionDto[]>([])
const loading = ref(false)

// The list carries no "this is you" marker; the access token this tab holds
// does (its `session_id` claim). Re-read whenever the list reloads: a refresh
// rotates the token but keeps the session, a re-login changes both. Null when
// it cannot be told (opaque token, deployment without session binding) - then
// nothing is marked and every row keeps its confirmed Revoke.
const currentSessionId = computed(() => {
  void sessions.value
  return readSessionIdClaim(client?.getAccessToken())
})
function isCurrent(row: UserSessionDto): boolean {
  const current = currentSessionId.value
  return current !== null && row.id.toLowerCase() === current.toLowerCase()
}
// What "Sign out other devices" would actually act on. The list always
// includes the caller's own session while signed in, so `sessions.length`
// never disabled the button: it stayed live with nobody else to sign out and
// clicking it did nothing visible.
const otherSessions = computed(() => sessions.value.filter((row) => !isCurrent(row)))

const load = createGuardedLoader<UserSessionDto[]>({
  flag: loading,
  fetch: () => ctx.bridge.me.getSessions(),
  apply: (rows) => {
    sessions.value = rows ?? []
  },
  onError: (e) => ctx.message.error(e instanceof Error ? e.message : String(e)),
  timeoutMessage: t('loadTimeout'),
})

async function revokeOne(row: UserSessionDto): Promise<void> {
  try {
    await ctx.bridge.me.revokeSession(row.id)
    ctx.message.success(t('sessions.revoked'))
    await load()
  } catch (e) {
    ctx.message.error(e instanceof Error ? e.message : String(e))
  }
}

// Sign out every OTHER device and stay signed in here. This is the shape ASVS
// 7.4.3 asks for, and the only shape people actually use: the previous
// behaviour revoked the current session too and bounced to the login page, so
// the button cost you your own session every time you wanted to evict someone
// else's.
async function revokeAll(): Promise<void> {
  try {
    await ctx.bridge.me.revokeAllSessions()
    ctx.message.success(t('sessions.allRevoked'))
    await load()
  } catch (e) {
    ctx.message.error(e instanceof Error ? e.message : String(e))
  }
}

const columns = computed<DataTableColumns<UserSessionDto>>(() => [
  {
    key: 'deviceInfo',
    title: t('sessions.cols.device'),
    render: (row) => {
      const deviceProfile = parseDeviceInfo(row.deviceInfo)
      return h('span', { class: 'inline-flex items-center gap-6px' }, [
        h(TSvgIcon, {
          icon: deviceProfile.icon,
          size: 16,
          style: `color: ${deviceIconColor(deviceProfile.osFamily)}`,
        }),
        h('span', { class: 'text-13px', title: row.deviceInfo ?? '' }, deviceProfile.label),
        isCurrent(row)
          ? h(NTag, { size: 'tiny', type: 'success', bordered: false }, { default: () => t('sessions.thisDevice') })
          : null,
      ])
    },
  },
  { key: 'ipAddress', title: t('sessions.cols.ip') },
  {
    key: 'lastActivityTime',
    title: t('sessions.cols.lastActive'),
    render: (row) => formatDateTime(row.lastActivityTime, { fallback: EMPTY_DASH }),
  },
  {
    key: 'actions',
    title: t('sessions.cols.actions'),
    width: 120,
    // The row this tab is signed in with gets no Revoke: revoking it signs
    // the user out of the page they are standing on. Sign-out lives in the
    // account menu, where it says what it does.
    render: (row) =>
      isCurrent(row)
        ? h('span', { class: 'text-13px text-muted' }, EMPTY_DASH)
        : h(
            NPopconfirm,
            { onPositiveClick: () => revokeOne(row) },
            {
              trigger: () =>
                h(NButton, { size: 'tiny', type: 'error', ghost: true }, { default: () => t('sessions.revoke') }),
              default: () => t('sessions.confirmRevoke'),
            },
          ),
  },
])

// Load on mount + whenever the shell's Refresh bumps the reload bus.
onMounted(() => void load())
watch(() => ctx.reloadKey.value, () => void load())
</script>
