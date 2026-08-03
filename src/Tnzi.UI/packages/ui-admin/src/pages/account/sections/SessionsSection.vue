<template>
  <TUserCenterSection :title="t('sessions.title')" fill :contained="false">
    <template #actions>
      <NPopconfirm @positive-click="revokeAll">
        <template #trigger>
          <NButton size="small" type="error" ghost :disabled="!sessions.length">
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
import { NButton, NPopconfirm } from 'naive-ui'
import type { DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { formatDateTime } from '@tnzi/core'
import type { UserSessionDto } from '@tnzi/core/services/identity'
import TUserCenterSection from './TUserCenterSection.vue'
import TResponsiveTable from '../../../components/data/TResponsiveTable.vue'
import { deviceIconColor, parseDeviceInfo } from '../../_shared/device-info'
import { createGuardedLoader } from '../guarded-loader'
import { useUserCenterContext } from '../user-center-context'

const ctx = useUserCenterContext()
const t = ctx.t

const sessions = ref<UserSessionDto[]>([])
const loading = ref(false)

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

async function revokeAll(): Promise<void> {
  try {
    await ctx.bridge.me.revokeAllSessions()
    ctx.message.success(t('sessions.allRevoked'))
    // Revoking ALL sessions necessarily kills the current one - bounce to login
    // instead of leaving a half-dead admin shell.
    ctx.logoutAndRedirect()
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
    render: (row) =>
      h(
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
