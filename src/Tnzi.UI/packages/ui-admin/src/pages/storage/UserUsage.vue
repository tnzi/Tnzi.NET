<template>
  <!--
    UserUsage — per-user storage quota overview (TContentPage, no back).
    Default view = top-N storage consumers (`userUsage.topUsers`). An optional
    user-ID lookup (`userUsage.forUser`) pins a single user's row to the top of
    the table for spot checks.
  -->
  <TContentPage :title="t('title')" :translate="t" scroll="fill">
    <template #actions>
      <NInput
        v-model:value="userIdQuery"
        size="small"
        clearable
        :placeholder="t('search.userId')"
        class="w-220px"
        @keydown.enter="lookupUser"
        @clear="clearLookup"
      >
        <template #prefix><TSvgIcon icon="mdi:account-search-outline" :size="16" /></template>
      </NInput>
      <NButton size="small" type="primary" :loading="lookupLoading" @click="lookupUser">
        {{ t('search.lookup') }}
      </NButton>
      <NInputNumber v-model:value="topN" size="small" :min="1" :max="500" :step="10" class="w-120px" />
      <NButton size="small" :loading="loading" @click="loadTopUsers">
        <template #icon><TSvgIcon icon="mdi:refresh" :size="16" /></template>
        {{ t('actions.refresh') }}
      </NButton>
    </template>

    <NCard
      v-if="pinnedUser"
      size="small"
      :bordered="false"
      class="flex-shrink-0"
      :title="t('pinned.title')"
    >
      <div class="flex flex-wrap gap-24px">
        <div>
          <div class="text-12px text-muted">{{ t('columns.userId') }}</div>
          <div class="font-600">{{ pinnedUser.userId ?? '—' }}</div>
        </div>
        <div>
          <div class="text-12px text-muted">{{ t('columns.fileCount') }}</div>
          <div class="font-600">{{ (pinnedUser.fileCount ?? 0).toLocaleString() }}</div>
        </div>
        <div>
          <div class="text-12px text-muted">{{ t('columns.size') }}</div>
          <div class="font-600">{{ pinnedUser.formattedSize || pinnedUser.totalSize }}</div>
        </div>
      </div>
    </NCard>

    <NCard size="small" :bordered="false" class="t-table-card" :title="t('topUsers.title')">
      <TResponsiveTable
        :columns="usageColumns"
        :data="topUsers"
        :loading="loading"
        :row-key="(r: UserStorageUsageDto) => r.userId ?? ''"
        :pagination="false"
        :flex-height="true"
        size="small"
        :empty-text="t('topUsers.empty')"
      />
    </NCard>
  </TContentPage>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { NButton, NCard, NInput, NInputNumber } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TContentPage from '../../components/layout/TContentPage.vue'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import { createStorageBridge } from '../../services/bridges/storage-bridge'
import { useAdminClient } from '../../plugin/client'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safeMessage'
import { buildUsageColumns } from './userusage-config'
import type { UserStorageUsageDto } from '@tnzi/core/services/storage'

const t = makePageTranslator('storage.userUsage')
const message = useSafeMessage()
const bridge = createStorageBridge({ client: useAdminClient() })

const topN = ref(20)
const loading = ref(false)
const topUsers = ref<UserStorageUsageDto[]>([])

const userIdQuery = ref('')
const lookupLoading = ref(false)
const pinnedUser = ref<UserStorageUsageDto | null>(null)

const usageColumns = buildUsageColumns(t)

// User IDs are GUIDs — reject malformed input client-side so we never fire a
// request the backend would reject (or that would silently return an empty row).
const GUID_RE = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/

async function loadTopUsers(): Promise<void> {
  loading.value = true
  try {
    topUsers.value = await bridge.userUsage.topUsers(topN.value ?? 20)
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    topUsers.value = []
  } finally {
    loading.value = false
  }
}

async function lookupUser(): Promise<void> {
  const id = userIdQuery.value.trim()
  if (!id) return
  if (!GUID_RE.test(id)) {
    message.warning(t('search.invalidUserId'))
    return
  }
  lookupLoading.value = true
  try {
    pinnedUser.value = await bridge.userUsage.forUser(id)
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
    pinnedUser.value = null
  } finally {
    lookupLoading.value = false
  }
}

function clearLookup(): void {
  userIdQuery.value = ''
  pinnedUser.value = null
}

onMounted(() => {
  loadTopUsers().catch(() => undefined)
})
</script>
