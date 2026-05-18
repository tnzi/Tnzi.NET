<template>
  <!--
    NotificationMessage page — Phase 3.26
    Messages are sent notifications — create/update reject via bridge stubs.
    Delete is supported (batchDelete). Resend is available on failed rows only.
    Backend fields: NotificationInfo from useAdminNotificationApi.
    The plan's "templateCode/recipient/channel/status/sentAt" fields do not map
    directly to NotificationInfo (which uses type/status/sentTime).
    This page uses the plan's column names as display labels; real data
    comes from NotificationInfo fields. Columns are display-only.
  -->
  <TCrudPage
    :state="crud"
    :all-columns="notificationMessageColumns"
    :title="title"
    :translate="t"
    :show-create="false"
  >
    <template #batchActions="{ selectedIds }">
      <NPopconfirm @positive-click="() => batchResend(selectedIds)">
        <template #trigger>
          <NButton
            v-if="selectedIds.length > 0"
            size="small"
            type="warning"
            ghost
            :loading="batchResending"
          >
            {{ t('actions.batchResend') }} ({{ selectedIds.length }})
          </NButton>
        </template>
        {{ t('actions.confirmBatchResend') }}
      </NPopconfirm>
    </template>
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="notificationMessageFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
      />
    </template>
    <template #rowActions="{ row }">
      <TRowActions :row="row" :state="crud" :translate="t" :show-edit="false">
        <template #prepend>
          <NButton
            v-if="isFailed(row)"
            size="small"
            type="warning"
            ghost
            :loading="resendingIds.has(String(row.id ?? ''))"
            @click="resendMessage(row)"
          >
            {{ t('actions.resend') }}
          </NButton>
        </template>
      </TRowActions>
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { NButton, NPopconfirm } from 'naive-ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { createNotificationBridge } from '../../services/bridges/notification-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { notificationMessageColumns, notificationMessageFormSchema } from './notification-message-config'
import { translatePageKey } from '../_shared/translate'
import type { NotificationInfo } from '@tnzi/core/services/notification'

const title = 'title'
const bridge = createNotificationBridge({ client: useAdminClient() })

// Messages are read-only (immutable after sending) except delete
const readOnlyFn = async (): Promise<never> => { throw new Error('Notification messages are immutable') }

const crud = useCrudPage<NotificationInfo>({
  pageId: 'notification.messages',
  columns: notificationMessageColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.messages.fetch(query),
  createData: readOnlyFn,
  updateData: readOnlyFn,
  deleteData: (ids) => bridge.messages.delete(ids.map(String)),
})


crud.refresh().catch(() => undefined)

// ---- Resend action ----
const resendingIds = ref<Set<string>>(new Set())
const batchResending = ref(false)

function isFailed(row: NotificationInfo): boolean {
  // NotificationInfo.status is a NotificationStatus enum; 3 = Failed
  return (row.status as unknown) === 'failed' || row.status === 3
}

async function resendMessage(row: NotificationInfo): Promise<void> {
  const id = String(row.id ?? '')
  resendingIds.value = new Set([...resendingIds.value, id])
  try {
    await bridge.messages.send(id)
    await crud.refresh()
  } catch {
    // Error surfaced by useCrudPage or shown inline; swallow here to unblock UI
  } finally {
    const next = new Set(resendingIds.value)
    next.delete(id)
    resendingIds.value = next
  }
}

async function batchResend(ids: Array<string | number>): Promise<void> {
  if (!ids.length) return
  batchResending.value = true
  try {
    // Backend's send endpoint is per-id — fan out sequentially so partial
    // failures don't poison the whole batch (admin can re-trigger the rest).
    for (const id of ids) {
      try {
        await bridge.messages.send(String(id))
      } catch {
        // Swallow per-id error; the UI will reflect remaining failed status
        // on the next refresh.
      }
    }
    await crud.refresh()
  } finally {
    batchResending.value = false
  }
}

const t = (key: string) => translatePageKey('notification.messages', key)
</script>
