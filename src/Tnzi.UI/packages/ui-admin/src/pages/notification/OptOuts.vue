<template>
  <!--
    Opt-outs page - the address-keyed suppression list behind one-click
    unsubscribe (Notification_OptOut). Rows are written by the recipient's own
    click (source "one-click link" / "one-click header") or registered here
    (source "admin:{userId}") for provider complaints, phone calls and imported
    blocklists. Read + create + delete: the (address, channel, category) triple
    is the row's identity, so there is nothing to edit.

    ★ Not the Subscriptions page. A preference is a USER's choice; an opt-out is
    an ADDRESS that said no, and delivery honours it even for addresses that
    belong to no user at all.
  -->
  <TCrudPage
    :state="crud"
    :all-columns="notificationOptOutColumns"
    :title="title"
    :translate="t"
    :search-placeholder="t('searchPlaceholder')"
    :form-modal-width="640"
    :row-actions="rowActions"
  >
    <!-- Channel filter drives the fetch query; it is not navigation, so it is a
         toolbar control rather than a deep-linked tab. -->
    <template #toolbarLeft>
      <NRadioGroup :value="channelFilter" size="small" @update:value="onChannelChange">
        <NRadioButton value="">{{ t('channels.all') }}</NRadioButton>
        <NRadioButton :value="NotificationType.Email">{{ t('channels.email') }}</NRadioButton>
        <NRadioButton :value="NotificationType.Sms">{{ t('channels.sms') }}</NRadioButton>
        <NRadioButton :value="NotificationType.Push">{{ t('channels.push') }}</NRadioButton>
        <NRadioButton :value="NotificationType.Fax">{{ t('channels.fax') }}</NRadioButton>
      </NRadioGroup>
    </template>

    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="notificationOptOutFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
        :columns="1"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { NRadioButton, NRadioGroup } from 'naive-ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { deleteAction, type RowAction } from '../../headless/row-actions'
import { createNotificationBridge } from '../../services/bridges/notification-bridge'
import { useAdminClient } from '../../plugin/client'
import { NotificationType, type OptOutDto } from '@tnzi/core/services/notification'
import TFormSchemaRenderer from '../_shared/form-schema'
import { notificationOptOutColumns, notificationOptOutFormSchema } from './opt-out-config'
import { makePageTranslator } from '../_shared/translate'

const title = 'title'
const client = useAdminClient()
const bridge = createNotificationBridge({ client })

const crud = useCrudPage<OptOutDto>({
  pageId: 'notification.optOuts',
  // The catalogue declares view / create / delete only - see the bridge
  // contract: an opt-out row has no editable fields.
  permission: {
    create: 'notification.optOut.create',
    delete: 'notification.optOut.delete',
  },
  columns: notificationOptOutColumns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (query) => bridge.optOuts.fetch(query),
  createData: (data) => bridge.optOuts.create(data),
  // ★ updateData is deliberately NOT supplied: a write affordance shows when
  // `callback && permission`, and wiring it would put an Edit action on screen
  // that the bridge rejects every single time. Revoke and register instead.
  deleteData: (ids) => bridge.optOuts.delete(ids.map(String)),
})

const channelFilter = ref<string>('')

function onChannelChange(value: string | number): void {
  const channel = String(value)
  channelFilter.value = channel
  crud.setFilters({ ...crud.query.value.filters, channel: channel || undefined })
  crud.refresh().catch(() => undefined)
}

// Labelled "Revoke", not "Delete": the row is a recipient's request, and
// removing it lets the address receive again - that is what the operator is
// deciding, and the button should say so.
const rowActions: RowAction<OptOutDto>[] = [deleteAction(crud, { label: 'actions.delete' })]

const t = makePageTranslator('notification.optOuts')
</script>
