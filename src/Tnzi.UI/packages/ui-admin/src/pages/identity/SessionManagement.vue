<template>
  <div class="t-session-page t-page-scroll">
    <!-- KPI cards -->
    <NGrid :cols="3" :x-gap="16" :y-gap="16" class="t-session-page__stats">
      <NGi>
        <NCard size="small" :bordered="false">
          <NStatistic :label="t('stats.activeSessions')">
            <NNumberAnimation :from="0" :to="stats?.activeSessionCount ?? 0" />
          </NStatistic>
        </NCard>
      </NGi>
      <NGi>
        <NCard size="small" :bordered="false">
          <NStatistic :label="t('stats.onlineUsers')">
            <NNumberAnimation :from="0" :to="stats?.onlineUserCount ?? 0" />
          </NStatistic>
        </NCard>
      </NGi>
      <NGi>
        <NCard size="small" :bordered="false">
          <NStatistic :label="t('stats.topDevice')">
            <span style="font-size: 16px">
              {{ stats?.topDevices?.[0]?.deviceInfo ?? '—' }}
              <small v-if="stats?.topDevices?.[0]?.count" style="color: var(--tnzi-base-text-muted); margin-left: 4px">
                × {{ stats.topDevices[0].count }}
              </small>
            </span>
          </NStatistic>
        </NCard>
      </NGi>
    </NGrid>

    <!-- Toolbar -->
    <NCard size="small" :bordered="false" class="t-session-page__toolbar">
      <NSpace align="center" :wrap-item="false">
        <!-- Active-users picker: lets admins pick from currently-signed-in
             users without pasting a userId. Falls back gracefully when the
             list is empty (e.g. KeepDatabaseAuditLog disabled). The select's
             filter input still allows typing a raw userId for power users. -->
        <NSelect
          v-model:value="targetUserId"
          :options="activeUserOptions"
          :placeholder="t('toolbar.userPickerPlaceholder')"
          :loading="loadingActiveUsers"
          :filterable="true"
          :tag="true"
          clearable
          style="width: 360px"
          @update:value="onUserPicked"
        />
        <NCheckbox v-model:checked="includeRevoked">{{ t('toolbar.includeRevoked') }}</NCheckbox>
        <NButton type="primary" :loading="loading" :disabled="!targetUserId" @click="loadSessions">
          {{ t('toolbar.fetch') }}
        </NButton>
        <NButton @click="refreshAll">{{ t('toolbar.refresh') }}</NButton>
        <NPopconfirm @positive-click="handleCleanExpired">
          <template #trigger>
            <NButton type="warning" ghost>{{ t('toolbar.cleanExpired') }}</NButton>
          </template>
          {{ t('toolbar.confirmCleanExpired') }}
        </NPopconfirm>
      </NSpace>
    </NCard>

    <!-- Session list for chosen user -->
    <NCard
      v-if="targetUserId && (sessions.length || hasFetched)"
      size="small"
      :bordered="false"
      :title="t('list.titleFor', { userId: selectedUserLabel })"
      class="t-session-page__list"
    >
      <template #header-extra>
        <NPopconfirm @positive-click="handleRevokeAll">
          <template #trigger>
            <NButton size="small" type="error" ghost :disabled="!sessions.length">
              {{ t('list.revokeAll') }}
            </NButton>
          </template>
          {{ t('list.confirmRevokeAll') }}
        </NPopconfirm>
      </template>
      <NDataTable
        :data="sessions"
        :columns="columns"
        :row-key="(r: UserSessionRow) => r.id"
        size="small"
        :bordered="false"
        :flex-height="true"
      />
    </NCard>
  </div>
</template>

<script setup lang="ts">
import { computed, h, ref, onMounted } from 'vue'
import type { DataTableColumns, SelectOption } from 'naive-ui'
import {
  NCard, NGrid, NGi, NStatistic, NNumberAnimation, NSpace, NButton, NSelect,
  NCheckbox, NPopconfirm, NDataTable, NTag,
} from 'naive-ui'
import { useSafeMessage } from '../_shared/safeMessage'
import { createIdentityBridge } from '../../services/bridges/identity-bridge'
import { useAdminClient } from '../../plugin/client'
import { makePageTranslator } from '../_shared/translate'
import { deviceIconColor, parseDeviceInfo } from '../_shared/device-info'
import { TSvgIcon } from '@tnzi/ui'
import type { ActiveUserSummaryDto, SessionStatisticsDto, UserSessionDto } from '@tnzi/core/services/identity'

type UserSessionRow = UserSessionDto

const bridge = createIdentityBridge({ client: useAdminClient() })
const t = makePageTranslator('identity.sessions')

const message = useSafeMessage()

const stats = ref<SessionStatisticsDto | null>(null)
const sessions = ref<UserSessionRow[]>([])
const targetUserId = ref<string | null>(null)
const includeRevoked = ref(false)
const loading = ref(false)
const hasFetched = ref(false)

// Active users picker — loaded once on mount + manually refreshable.
const activeUsers = ref<ActiveUserSummaryDto[]>([])
const loadingActiveUsers = ref(false)

const activeUserOptions = computed<SelectOption[]>(() =>
  activeUsers.value.map((u) => ({
    label: `${u.userName ?? '(unknown)'} — ${u.sessionCount} ${t('toolbar.sessionsSuffix')}`,
    value: u.userId,
  })),
)

// Resolve the selected user's display name (falls back to raw userId for
// power users who typed an id manually instead of picking from the list).
const selectedUserLabel = computed<string>(() => {
  const id = targetUserId.value
  if (!id) return ''
  const match = activeUsers.value.find((u) => u.userId === id)
  return match?.userName ?? id
})

async function refreshStats(): Promise<void> {
  try {
    stats.value = await bridge.sessions.statistics()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}

async function refreshActiveUsers(): Promise<void> {
  loadingActiveUsers.value = true
  try {
    activeUsers.value = await bridge.sessions.activeUsers(50)
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    activeUsers.value = []
  } finally {
    loadingActiveUsers.value = false
  }
}

async function refreshAll(): Promise<void> {
  await Promise.all([refreshStats(), refreshActiveUsers()])
}

// Picking from the dropdown immediately loads that user's sessions —
// removes the extra "Fetch" click for the common case.
function onUserPicked(value: string | null): void {
  if (value) void loadSessions()
  else {
    sessions.value = []
    hasFetched.value = false
  }
}

async function loadSessions(): Promise<void> {
  const userId = targetUserId.value
  if (!userId) return
  loading.value = true
  try {
    sessions.value = await bridge.sessions.listForUser(userId, includeRevoked.value)
    hasFetched.value = true
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    sessions.value = []
  } finally {
    loading.value = false
  }
}

async function handleRevokeSession(sessionId: string): Promise<void> {
  try {
    await bridge.sessions.revoke(sessionId)
    message.success(t('revokeSuccess'))
    await loadSessions()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}

async function handleRevokeAll(): Promise<void> {
  const userId = targetUserId.value
  if (!userId) return
  try {
    await bridge.sessions.revokeAllForUser(userId)
    message.success(t('revokeAllSuccess'))
    await loadSessions()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}

async function handleCleanExpired(): Promise<void> {
  try {
    const n = await bridge.sessions.cleanExpired()
    message.success(t('cleanExpiredSuccess', { count: n }))
    await refreshStats()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}

function formatTime(v: string | Date | null | undefined): string {
  if (!v) return '—'
  try {
    return new Date(v).toLocaleString()
  } catch {
    return String(v)
  }
}

const columns = computed<DataTableColumns<UserSessionRow>>(() => [
  { key: 'id', title: t('columns.sessionId'), width: 180, ellipsis: { tooltip: true } },
  {
    key: 'deviceInfo',
    title: t('columns.device'),
    width: 220,
    render: (row) => {
      // Parse deviceInfo once per row; the helper handles null/empty
      // values gracefully so we can render unconditionally. Icon is
      // colour-tinted by OS family so a row at a glance reveals the
      // platform mix (Windows blue / iOS dark / Android green / etc.).
      const profile = parseDeviceInfo(row.deviceInfo)
      return h(
        'span',
        { style: 'display: inline-flex; align-items: center; gap: 6px' },
        [
          h(TSvgIcon, {
            icon: profile.icon,
            size: 16,
            style: `color: ${deviceIconColor(profile.osFamily)}`,
          }),
          h(
            'span',
            {
              style: 'font-size: 13px',
              title: row.deviceInfo ?? '',
            },
            profile.label,
          ),
        ],
      )
    },
  },
  { key: 'ipAddress', title: t('columns.ip'), width: 140 },
  {
    key: 'creationTime',
    title: t('columns.loginTime'),
    width: 160,
    render: (row) => formatTime(row.creationTime),
  },
  {
    key: 'lastActivityTime',
    title: t('columns.lastActive'),
    width: 160,
    render: (row) => formatTime(row.lastActivityTime),
  },
  {
    key: 'isRevoked',
    title: t('columns.status'),
    width: 100,
    render: (row) =>
      h(
        NTag,
        { type: row.isRevoked ? 'error' : 'success', size: 'small', bordered: false },
        { default: () => (row.isRevoked ? t('status.revoked') : t('status.active')) },
      ),
  },
  {
    key: 'actions',
    title: t('columns.actions'),
    width: 100,
    render: (row) =>
      row.isRevoked
        ? h('span', { style: 'color: var(--tnzi-base-text-muted)' }, '—')
        : h(
            NButton,
            {
              size: 'small',
              type: 'error',
              ghost: true,
              onClick: () => handleRevokeSession(row.id),
            },
            { default: () => t('actions.revoke') },
          ),
  },
])

onMounted(() => {
  void refreshAll()
})
</script>

<style scoped>
.t-session-page {
  /* no padding — owned by TAdminContent. Flex column so the list card
     can claim the residual height and keep pagination/footer rows
     anchored to the bottom of the page. */
  display: flex;
  flex-direction: column;
  gap: 16px;
  width: 100%;
  height: 100%;
  min-height: 0;
}
.t-session-page__stats {
  flex-shrink: 0;
}
.t-session-page__toolbar {
  flex-shrink: 0;
}
.t-session-page__list {
  flex: 1 1 auto;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
.t-session-page__list :deep(.n-card-content) {
  flex: 1 1 auto;
  min-height: 0;
  display: flex;
  flex-direction: column;
}
.t-session-page__list :deep(.n-data-table) {
  flex: 1 1 auto;
  min-height: 0;
}
</style>
