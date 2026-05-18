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
      <NSpace align="center">
        <NInput
          v-model:value="targetUserId"
          :placeholder="t('toolbar.userIdPlaceholder')"
          style="width: 320px"
          clearable
        />
        <NCheckbox v-model:checked="includeRevoked">{{ t('toolbar.includeRevoked') }}</NCheckbox>
        <NButton type="primary" :loading="loading" :disabled="!targetUserId" @click="loadSessions">
          {{ t('toolbar.fetch') }}
        </NButton>
        <NButton @click="refreshStats">{{ t('toolbar.refreshStats') }}</NButton>
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
      :title="t('list.titleFor', { userId: targetUserId })"
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
      />
    </NCard>
  </div>
</template>

<script setup lang="ts">
import { computed, h, ref, onMounted } from 'vue'
import type { DataTableColumns } from 'naive-ui'
import {
  NCard, NGrid, NGi, NStatistic, NNumberAnimation, NSpace, NButton, NInput,
  NCheckbox, NPopconfirm, NDataTable, NTag, useMessage,
} from 'naive-ui'
import { createIdentityBridge } from '../../services/bridges/identity-bridge'
import { useAdminClient } from '../../plugin/client'
import { makePageTranslator } from '../_shared/translate'
import type { SessionStatisticsDto, UserSessionDto } from '@tnzi/core/services/identity'

type UserSessionRow = UserSessionDto

const bridge = createIdentityBridge({ client: useAdminClient() })
const t = makePageTranslator('identity.sessions')

let message: { success(s: string): void; error(s: string): void; info(s: string): void }
try {
  message = useMessage()
} catch {
  message = { success: () => {}, error: () => {}, info: () => {} }
}

const stats = ref<SessionStatisticsDto | null>(null)
const sessions = ref<UserSessionRow[]>([])
const targetUserId = ref('')
const includeRevoked = ref(false)
const loading = ref(false)
const hasFetched = ref(false)

async function refreshStats(): Promise<void> {
  try {
    stats.value = await bridge.sessions.statistics()
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}

async function loadSessions(): Promise<void> {
  if (!targetUserId.value) return
  loading.value = true
  try {
    sessions.value = await bridge.sessions.listForUser(targetUserId.value, includeRevoked.value)
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
  try {
    await bridge.sessions.revokeAllForUser(targetUserId.value)
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
  { key: 'deviceInfo', title: t('columns.device'), width: 180 },
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
  void refreshStats()
})
</script>

<style scoped>
.t-session-page {
  padding: 16px;
}
.t-session-page__stats {
  margin-bottom: 16px;
}
.t-session-page__toolbar {
  margin-bottom: 16px;
}
.t-session-page__list {
  margin-top: 16px;
}
</style>
