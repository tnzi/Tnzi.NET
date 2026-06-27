<template>
  <!--
    Chat Overview — system-level IM dashboard (TContentPage, no back). Merges
    statistics with the presence monitor so the page is information-dense:
    · conversation breakdown KPIs (total / direct / group / system)
    · activity KPIs (total messages / today / active members / online users)
    · presence section: effective-status distribution + per-user online table.
    Data: bridge.statistics() + bridge.presence().
  -->
  <TContentPage :title="t('title')" :translate="t" scroll="fill">
    <template #actions>
      <NButton size="small" :loading="loading" @click="refresh">
        <template #icon><TSvgIcon icon="mdi:refresh" :size="16" /></template>
        {{ t('refresh') }}
      </NButton>
    </template>

    <TKpiRow cols="1 s:2 m:4">
      <TKpiCard :label="t('kpi.totalConversations')" :value="stats ? stats.totalConversations : null" icon="mdi:forum-outline" />
      <TKpiCard :label="t('kpi.directConversations')" :value="stats ? stats.directConversations : null" icon="mdi:account-multiple-outline" />
      <TKpiCard :label="t('kpi.groupConversations')" :value="stats ? stats.groupConversations : null" icon="mdi:account-group-outline" />
      <TKpiCard :label="t('kpi.systemConversations')" :value="stats ? stats.systemConversations : null" icon="mdi:bullhorn-outline" />
    </TKpiRow>

    <TKpiRow cols="1 s:2 m:4">
      <TKpiCard :label="t('kpi.totalMessages')" :value="stats ? stats.totalMessages : null" icon="mdi:message-text-outline" />
      <TKpiCard :label="t('kpi.messagesToday')" :value="stats ? stats.messagesToday : null" icon="mdi:message-flash-outline" />
      <TKpiCard :label="t('kpi.activeMembers')" :value="stats ? stats.activeMembers : null" tone="success" icon="mdi:account-check-outline" />
      <TKpiCard :label="t('kpi.onlineUsers')" :value="stats ? stats.onlineUsers : null" tone="success" icon="mdi:wifi" />
    </TKpiRow>

    <div class="t-presence-head">
      <span class="t-presence-title">{{ t('presence.title') }}</span>
      <NSwitch v-model:value="onlineOnly" size="small" @update:value="loadPresence">
        <template #checked>{{ t('presence.onlineOnly') }}</template>
        <template #unchecked>{{ t('presence.all') }}</template>
      </NSwitch>
    </div>

    <TKpiRow cols="2 s:4">
      <TKpiCard :label="t('presence.online')" :value="presence ? presence.online : null" tone="success" icon="mdi:circle" />
      <TKpiCard :label="t('presence.away')" :value="presence ? presence.away : null" tone="warning" icon="mdi:clock-outline" />
      <TKpiCard :label="t('presence.busy')" :value="presence ? presence.busy : null" tone="error" icon="mdi:minus-circle-outline" />
      <TKpiCard :label="t('presence.offline')" :value="presence ? presence.offline : null" icon="mdi:circle-outline" />
    </TKpiRow>

    <NCard size="small" :bordered="false" class="t-table-card">
      <TResponsiveTable
        :columns="presenceColumns"
        :data="presence?.users ?? []"
        :loading="loading"
        :row-key="(r: PresenceUserDto) => r.userId"
        :pagination="false"
        size="small"
        :empty-text="presence ? t('presence.empty') : t('presence.loadFirst')"
      />
    </NCard>
  </TContentPage>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { NButton, NCard, NSwitch } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TContentPage from '../../components/layout/TContentPage.vue'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import TKpiRow from '../../components/data/TKpiRow.vue'
import TKpiCard from '../../components/data/TKpiCard.vue'
import { createChatBridge } from '../../services/bridges/chat-bridge'
import { useAdminClient } from '../../plugin/client'
import { translatePageKey } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safeMessage'
import { buildPresenceColumns } from './presence-config'
import type { ChatStatisticsDto, PresenceOverviewDto, PresenceUserDto } from '@tnzi/core/services/chat'

const t = (key: string) => translatePageKey('chat.overview', key)
const message = useSafeMessage()
const bridge = createChatBridge({ client: useAdminClient() })

const loading = ref(false)
const onlineOnly = ref(false)
const stats = ref<ChatStatisticsDto | null>(null)
const presence = ref<PresenceOverviewDto | null>(null)
const presenceColumns = buildPresenceColumns(t)

async function loadPresence(): Promise<void> {
  presence.value = await bridge.presence({ onlineOnly: onlineOnly.value })
}

async function refresh(): Promise<void> {
  loading.value = true
  try {
    const [s] = await Promise.all([bridge.statistics(), loadPresence()])
    stats.value = s
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    loading.value = false
  }
}

onMounted(refresh)
</script>

<style scoped>
.t-presence-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-top: 4px;
}
.t-presence-title {
  font-size: 14px;
  font-weight: 600;
  color: var(--tnzi-text-color-1, #333);
}
</style>
