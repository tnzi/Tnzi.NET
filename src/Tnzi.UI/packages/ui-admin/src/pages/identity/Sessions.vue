<template>
  <TContentPage :title="t('title')" :translate="t" card scroll="fill">
    <template #actions>
      <NButton size="small" :loading="loading" @click="refreshAll">{{ t('toolbar.refresh') }}</NButton>
      <NPopconfirm v-if="can('session.delete')" @positive-click="handleCleanExpired">
        <template #trigger>
          <NButton size="small" type="warning" ghost>{{ t('toolbar.cleanExpired') }}</NButton>
        </template>
        {{ t('toolbar.confirmCleanExpired') }}
      </NPopconfirm>
    </template>

    <div class="t-session-page">
    <!-- KPI cards -->
    <TKpiRow class="t-session-page__stats" cols="1 s:2 m:3" :gap="16">
      <TKpiCard :label="t('stats.activeSessions')" :value="stats?.activeSessionCount ?? 0" />
      <TKpiCard :label="t('stats.onlineUsers')" :value="stats?.onlineUserCount ?? 0" />
      <TKpiCard
        :label="t('stats.topDevice')"
        :value="stats?.topDevices?.[0]?.deviceInfo ?? null"
        :suffix="stats?.topDevices?.[0]?.count ? `× ${stats.topDevices[0].count}` : undefined"
      />
    </TKpiRow>

    <!-- Toolbar: the user picker is an OPTIONAL filter — the page lists all
         active sessions globally; picking a user narrows the list to them. -->
    <NCard size="small" :bordered="false" class="t-session-page__toolbar">
      <NSpace align="center" :wrap-item="false">
        <!-- Active-users picker (optional filter): lets admins narrow to one
             user without pasting a userId. Falls back gracefully when the
             list is empty (e.g. KeepDatabaseAuditLog disabled). The select's
             filter input still allows typing a raw userId for power users.
             Clearing it returns to the global list. -->
        <NSelect
          v-model:value="targetUserId"
          :options="activeUserOptions"
          :placeholder="t('toolbar.userPickerPlaceholder')"
          :loading="loadingActiveUsers"
          :filterable="true"
          :tag="true"
          size="small"
          clearable
          class="w-360px max-w-full"
          @update:value="onFilterChanged"
        />
        <NCheckbox v-model:checked="includeRevoked" size="small" @update:checked="onFilterChanged">
          {{ t('toolbar.includeRevoked') }}
        </NCheckbox>
        <NButton type="primary" size="small" :loading="loading" @click="onFilterChanged">
          {{ t('toolbar.fetch') }}
        </NButton>
      </NSpace>
    </NCard>

    <!-- Session list — global by default, narrowed when a user is picked -->
    <NCard
      size="small"
      :bordered="false"
      :title="listTitle"
      class="t-session-page__list"
    >
      <template #header-extra>
        <!-- Revoke-all is a per-user operation — only offered when filtered. -->
        <NPopconfirm v-if="targetUserId && can('session.delete')" @positive-click="handleRevokeAll">
          <template #trigger>
            <NButton size="small" type="error" ghost :disabled="!sessions.length">
              {{ t('list.revokeAll') }}
            </NButton>
          </template>
          {{ t('list.confirmRevokeAll') }}
        </NPopconfirm>
      </template>
      <TResponsiveTable
        :data="sessions"
        :columns="columns"
        :row-key="(r: UserSessionRow) => r.id"
        :loading="loading"
        :row-actions="rowActions"
        :row-actions-title="t('columns.actions')"
        :translate="t"
        size="small"
        :bordered="false"
        :flex-height="true"
      />
      <div class="t-session-page__pagination">
        <NPagination
          v-model:page="pageIndex"
          :item-count="totalCount"
          :page-size="pageSize"
          :page-sizes="[20, 50, 100]"
          show-size-picker
          @update:page="loadSessions"
          @update:page-size="onPageSizeChange"
        />
      </div>
    </NCard>
    </div>
  </TContentPage>
</template>

<script setup lang="ts">
import { computed, h, ref, onMounted } from 'vue'
import type { DataTableColumns, SelectOption } from 'naive-ui'
import {
  NCard, NSpace, NButton, NSelect, NPagination,
  NCheckbox, NPopconfirm, NTag,
} from 'naive-ui'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import { type RowAction } from '../../headless/rowActions'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import TKpiRow from '../../components/data/TKpiRow.vue'
import TKpiCard from '../../components/data/TKpiCard.vue'
import { useSafeMessage } from '../_shared/safeMessage'
import { createIdentityBridge } from '../../services/bridges/identity-bridge'
import { useAdminClient } from '../../plugin/client'
import { makePageTranslator } from '../_shared/translate'
import { deviceIconColor, parseDeviceInfo } from '../_shared/device-info'
import { TSvgIcon } from '@tnzi/ui'
import TContentPage from '../../components/layout/TContentPage.vue'
import type { ActiveUserSummaryDto, SessionStatisticsDto, UserSessionDto } from '@tnzi/core/services/identity'

type UserSessionRow = UserSessionDto

const bridge = createIdentityBridge({ client: useAdminClient() })
const t = makePageTranslator('identity.sessions')
const { can } = usePermissionGuard()

const message = useSafeMessage()

const stats = ref<SessionStatisticsDto | null>(null)
const sessions = ref<UserSessionRow[]>([])
const targetUserId = ref<string | null>(null)
const includeRevoked = ref(false)
const loading = ref(false)

// Server-side pagination over GET /admin/sessions.
const pageIndex = ref(1)
const pageSize = ref(20)
const totalCount = ref(0)

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

const listTitle = computed<string>(() =>
  targetUserId.value
    ? t('list.titleFor', { userId: selectedUserLabel.value })
    : t('list.titleAll'),
)

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
  await Promise.all([refreshStats(), refreshActiveUsers(), loadSessions()])
}

// Any filter change (user picked/cleared, includeRevoked toggled, explicit
// Fetch click) resets to page 1 and reloads — the list is always live, no
// "pick a user first" gate.
function onFilterChanged(): void {
  pageIndex.value = 1
  void loadSessions()
}

function onPageSizeChange(size: number): void {
  pageSize.value = size
  pageIndex.value = 1
  void loadSessions()
}

async function loadSessions(): Promise<void> {
  loading.value = true
  try {
    const page = await bridge.sessions.list({
      userId: targetUserId.value,
      includeRevoked: includeRevoked.value,
      pageIndex: pageIndex.value,
      pageSize: pageSize.value,
    })
    sessions.value = page.items
    totalCount.value = page.totalCount
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    sessions.value = []
    totalCount.value = 0
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
    await Promise.all([refreshStats(), loadSessions()])
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
  { key: 'id', title: t('columns.sessionId'), minWidth: 150, ellipsis: { tooltip: true } },
  {
    key: 'userName',
    title: t('columns.user'),
    minWidth: 120,
    ellipsis: { tooltip: true },
    // userName is populated by the global list endpoint; fall back to the
    // raw userId so per-user rows (or older payloads) still identify the owner.
    render: (row) => row.userName ?? row.userId ?? '—',
  },
  {
    key: 'deviceInfo',
    title: t('columns.device'),
    minWidth: 160,
    render: (row) => {
      // Parse deviceInfo once per row; the helper handles null/empty
      // values gracefully so we can render unconditionally. Icon is
      // colour-tinted by OS family so a row at a glance reveals the
      // platform mix (Windows blue / iOS dark / Android green / etc.).
      const profile = parseDeviceInfo(row.deviceInfo)
      return h(
        'span',
        { class: 'inline-flex items-center gap-6px' },
        [
          h(TSvgIcon, {
            icon: profile.icon,
            size: 16,
            style: `color: ${deviceIconColor(profile.osFamily)}`,
          }),
          h(
            'span',
            {
              class: 'text-13px',
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
])

// Declarative operation column — revoke is offered only for still-active
// sessions (`show`); revoked rows render no action.
const rowActions: RowAction<UserSessionRow>[] = [
  {
    key: 'revoke',
    label: 'actions.revoke',
    type: 'error',
    confirm: true,
    show: (row) => can('session.delete') && !row.isRevoked,
    onClick: (row) => void handleRevokeSession(row.id),
  },
]

onMounted(() => {
  void refreshAll()
})
</script>

<style scoped>
.t-session-page {
  /* Inner flex column — TContentPage body (scroll="fill") provides the
     full-height container; this div fills it and stacks the cards. */
  display: flex;
  flex-direction: column;
  gap: 16px;
  width: 100%;
  flex: 1 1 auto;
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
/* Pagination pinned below the table; stays visible while the table area
   flexes. Matches the Files.vue footer-row pattern. */
.t-session-page__pagination {
  flex-shrink: 0;
  display: flex;
  justify-content: flex-end;
  padding: 12px 4px 0;
}
</style>
