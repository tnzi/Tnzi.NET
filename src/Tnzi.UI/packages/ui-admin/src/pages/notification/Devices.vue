<template>
  <!--
    Push devices page - the registry behind token-addressed push delivery
    (Notification_PushDevice, shipped by the optional Tnzi.Notification.Push
    module). Read + delete only: a device row is written by the device itself
    when the client posts its FCM token, so there is no create/edit action.

    ★ An empty table is not a broken wiring. A deployment that only uses topic
    broadcast (IPushSender.SendToTopicAsync) stores no device identifiers at
    all - that is the whole point of choosing topics.
  -->
  <TCrudPage
    :state="crud"
    :all-columns="notificationDeviceColumns"
    :title="title"
    :translate="t"
    :row-actions="rowActions"
  />
</template>

<script setup lang="ts">
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { deleteAction, type RowAction } from '../../headless/row-actions'
import { createNotificationBridge } from '../../services/bridges/notification-bridge'
import { useAdminClient } from '../../plugin/client'
import type { PushDeviceDto } from '@tnzi/core/services/notification'
import { notificationDeviceColumns } from './device-config'
import { makePageTranslator } from '../_shared/translate'

const title = 'title'
const client = useAdminClient()
const bridge = createNotificationBridge({ client })

const crud = useCrudPage<PushDeviceDto>({
  pageId: 'notification.devices',
  // ★ Only view + delete exist in the catalogue. Registration and refresh are
  // client actions; an admin-created row would match no real device, so pushes
  // to it would fail forever with nobody knowing why.
  permission: {
    delete: 'notification.pushDevice.delete',
  },
  columns: notificationDeviceColumns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (query) => bridge.devices.fetch(query),
  // ★ createData / updateData are deliberately NOT supplied. A write
  // affordance shows when `callback && permission`, and an omitted permission
  // stays ungated - so wiring the callbacks here would put a Create button and
  // an Edit action on screen that the bridge rejects every single time. A
  // control that moves and changes nothing is worse than no control.
  deleteData: (ids) => bridge.devices.delete(ids.map(String)),
})

const rowActions: RowAction<PushDeviceDto>[] = [deleteAction(crud)]

const t = makePageTranslator('notification.devices')
</script>
