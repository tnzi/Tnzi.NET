<template>
  <!--
    NotificationSubscription page — wired 2026-04-14 to the canonical paged
    GET /admin/notification-preferences endpoint. The Preference entity IS
    the subscription model in Tnzi.Notification (userId × channel × category).
    Create/update both upsert via PUT /user/{userId}.
  -->
  <TCrudPage
    :state="crud"
    :all-columns="notificationSubscriptionColumns"
    :title="title"
    :translate="t"
    :form-modal-width="760"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="notificationSubscriptionFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
        :columns="2"
      />
    </template>
    <template #rowActions="{ row }">
      <TRowActions :row="row" :state="crud" :translate="t" />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { createNotificationBridge } from '../../services/bridges/notification-bridge'
import { useAdminClient } from '../../plugin/client'
import type { NotificationPreferenceDto } from '@tnzi/core/services/notification'
import TFormSchemaRenderer from '../_shared/form-schema'
import { notificationSubscriptionColumns, notificationSubscriptionFormSchema } from './notification-subscription-config'
import { translatePageKey } from '../_shared/translate'

const title = 'title'
const bridge = createNotificationBridge({ client: useAdminClient() })

const crud = useCrudPage<NotificationPreferenceDto>({
  pageId: 'notification.subscriptions',
  columns: notificationSubscriptionColumns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (query) => bridge.subscriptions.fetch(query),
  createData: (data) => bridge.subscriptions.create(data),
  updateData: (id, data) => bridge.subscriptions.update(String(id), data),
  deleteData: (ids) => bridge.subscriptions.delete(ids.map(String)),
})


crud.refresh().catch(() => undefined)

const t = (key: string) => translatePageKey('notification.subscriptions', key)
</script>
