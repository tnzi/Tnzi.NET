<template>
  <!--
    Subscriptions page - wired 2026-04-14 to the canonical paged
    GET /admin/notification-preferences endpoint. The Preference entity IS
    the subscription model in Tnzi.Notification (userId × channel × category).
    Create/update both upsert via PUT /user/{userId}.
  -->
  <TCrudPage
    :state="crud"
    :all-columns="notificationSubscriptionColumns"
    :show-default-search="false"
    :title="title"
    :translate="t"
    :form-modal-width="760"
    :row-actions="rowActions"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="notificationSubscriptionFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
        :columns="2"
        :field-renderers="fieldRenderers"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { h } from 'vue'
import { NInput, NSelect } from 'naive-ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { editAction, deleteAction, type RowAction } from '../../headless/row-actions'
import { createNotificationBridge } from '../../services/bridges/notification-bridge'
import { createIdentityBridge } from '../../services/bridges/identity-bridge'
import { useAdminClient } from '../../plugin/client'
import type { NotificationPreferenceDto } from '@tnzi/core/services/notification'
import TFormSchemaRenderer, { type FieldRenderer } from '../_shared/form-schema'
import TUserSelector from '../../components/forms/TUserSelector.vue'
import type { SelectorOption } from '../../components/forms/_selector-factory'
import {
  notificationSubscriptionColumns,
  notificationSubscriptionFormSchema,
  SUBSCRIPTION_KEY_LOCKED_TYPE,
} from './subscription-config'
import { makePageTranslator } from '../_shared/translate'
import { browserTimeZone, timeZoneIds } from '../../utils/time-zones'

const title = 'title'
const client = useAdminClient()
const bridge = createNotificationBridge({ client })
const identity = createIdentityBridge({ client })

// The `userId` form field (config `type: 'user'`) is rendered with a remote
// user-search selector so admins pick a user instead of pasting a raw GUID.
const userFetcher = async (keyword: string): Promise<SelectorOption[]> => {
  const res = await identity.users.fetch({
    pageIndex: 1,
    pageSize: 20,
    searchText: keyword.trim(),
    sortField: undefined,
    sortOrder: null,
    filters: {},
  })
  return res.items.map((u) => ({
    label: u.email ? `${u.userName} (${u.email})` : u.userName,
    value: u.id,
  }))
}

const fieldRenderers: Record<string, FieldRenderer> = {
  // The row's key (user, channel, category) on edit: shown, not edited.
  [SUBSCRIPTION_KEY_LOCKED_TYPE]: (ctx) =>
    h(NInput, { value: String(ctx.value ?? ''), disabled: true, size: 'small' }),
  user: (ctx) =>
    h(TUserSelector, {
      value: (ctx.value as string | null) ?? null,
      fetcher: userFetcher,
      size: 'small',
      disabled: ctx.readonly,
      placeholder: ctx.translate(ctx.item.placeholderKey, ctx.item.placeholder ?? ''),
      'onUpdate:value': (v: unknown) => ctx.onUpdate(v ?? undefined),
    }),
  // IANA zone for the row's quiet hours. Clearable: empty means the stored
  // times are UTC, which is what every row written before zones existed holds.
  timezone: (ctx) =>
    h(NSelect, {
      value: (ctx.value as string | null) || null,
      options: timeZoneIds([ctx.value as string | null, browserTimeZone()]).map((id) => ({ label: id, value: id })),
      filterable: true,
      clearable: true,
      size: 'small',
      disabled: ctx.readonly,
      placeholder: ctx.translate(ctx.item.placeholderKey, ctx.item.placeholder ?? ''),
      'onUpdate:value': (v: string | null) => ctx.onUpdate(v ?? null),
    }),
}

const crud = useCrudPage<NotificationPreferenceDto>({
  pageId: 'notification.subscriptions',
  // A preference left alone on the switch is saved enabled; the switch says so.
  createDefaults: { isEnabled: true },
  // Preferences are per-user UPSERTS on the backend (SetPreference), so the
  // catalogue declares no `.create` code - "create" in this UI is the same
  // operation as update and gates on the update code.
  permission: {
    create: 'notification.subscription.update',
    update: 'notification.subscription.update',
    delete: 'notification.subscription.delete',
  },
  columns: notificationSubscriptionColumns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (query) => bridge.subscriptions.fetch(query),
  createData: (data) => bridge.subscriptions.create(data),
  updateData: (id, data) => bridge.subscriptions.update(String(id), data),
  deleteData: (ids) => bridge.subscriptions.delete(ids.map(String)),
})

const rowActions: RowAction<NotificationPreferenceDto>[] = [editAction(crud), deleteAction(crud)]

const t = makePageTranslator('notification.subscriptions')
</script>
