<template>
  <!--
    Messages - the sends that left the system.

    A notification is immutable once sent: no create, no edit. Delete and Resend
    (failed rows only) are the operations; opening a row shows the full record in
    a read-only drawer, so there is no "View" button duplicating the row click.
    Backend shape is `NotificationInfo` (type / status / sentTime - NOT the
    templateCode/recipient/channel triple an earlier plan assumed).
  -->
  <TItemPage
    :state="crud"
    :title="title"
    :translate="t"
    :form-modal-width="760"
    :detail-width="720"
    :detail-title="(d: NotificationInfo) => messageTitle(d)"
    :show-create="false"
    show-batch
  >
    <!-- One row per notification: the subject leads (it is what the recipient
         saw), delivery state and channel are chips, the recipient tally and the
         send time sit underneath, and a failure reason shows inline instead of
         only inside the view drawer. -->
    <template #item="{ item, selected, selectable, toggleSelect }">
      <TItemCard
        :title="messageTitle(item)"
        :icon="channelIcon(item.type)"
        :icon-tone="statusTone(item.status)"
        :tags="messageTags(item)"
        :selectable="selectable"
        :checked="selected"
        :selected="selected"
        clickable
        @update:checked="toggleSelect"
        @click="crud.openView(item)"
      >
        <template #meta>
          <div class="nm-meta">
            <span class="nm-meta__item">
              <TSvgIcon icon="mdi:account-multiple-outline" :size="13" />
              {{ t('admin.shared.card.recipients', { ok: item.successCount, total: item.totalRecipientCount }) }}
            </span>
            <span class="nm-meta__item">
              <TSvgIcon icon="mdi:clock-outline" :size="13" />
              <TRelativeTime :value="item.sentTime ?? item.creationTime" />
            </span>
            <!-- Only when the template is not already doing duty as the title. -->
            <span v-if="item.templateName && item.subject?.trim()" class="nm-meta__item">
              <TSvgIcon icon="mdi:file-document-outline" :size="13" />{{ item.templateName }}
            </span>
          </div>
          <p v-if="item.failureReason" class="nm-error" :title="item.failureReason">
            <TSvgIcon icon="mdi:alert-circle-outline" :size="13" />{{ item.failureReason }}
          </p>
        </template>

        <template #actions>
          <NButton
            v-if="isFailed(item) && can('notification.message.update')"
            size="tiny"
            type="warning"
            ghost
            :loading="resendingIds.has(String(item.id ?? ''))"
            @click="resendMessage(item)"
          >
            {{ t('actions.resend') }}
          </NButton>
          <TRowActions :row="item" :actions="rowActions" :translate="t" />
        </template>
      </TItemCard>
    </template>

    <template #batchActions="{ selectedIds }">
      <NPopconfirm @positive-click="() => batchResend(selectedIds)">
        <template #trigger>
          <NButton
            v-if="selectedIds.length > 0 && can('notification.message.update')"
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
    <!--
      Read-only detail in a right drawer, NOT the create/edit modal: a sent
      notification is immutable, so there is nothing to edit and the page has no
      create/update handler. Before this the View action silently did nothing -
      `TFormModal` only mounts when the page can create/update OR supplies a
      `#detail` slot, and this page had neither.
    -->
    <template #detail="{ data }">
      <TFormSchemaRenderer
        :schema="notificationMessageFormSchema"
        :model="(data ?? {}) as Record<string, unknown>"
        readonly
        :translate="t"
      />
    </template>
  </TItemPage>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { NButton, NPopconfirm } from 'naive-ui'
import { TRelativeTime, TSvgIcon } from '@tnzi/ui'
import TItemPage from '../../components/crud/TItemPage.vue'
import TItemCard, { type ItemCardTag, type ItemCardTone } from '../../components/data/TItemCard.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import { EMPTY_DASH } from '../../utils/placeholders'
import { useCrudPage } from '../../headless/useCrudPage'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { deleteAction, type RowAction } from '../../headless/rowActions'
import { createNotificationBridge } from '../../services/bridges/notification-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { notificationMessageColumns, notificationMessageFormSchema } from './message-config'
import { makePageTranslator } from '../_shared/translate'
import { NotificationStatus, NotificationType, type NotificationInfo } from '@tnzi/core/services/notification'

const title = 'title'
const bridge = createNotificationBridge({ client: useAdminClient() })
const { can } = usePermissionGuard()

const crud = useCrudPage<NotificationInfo>({
  pageId: 'notification.messages',
  permission: 'notification.message',
  columns: notificationMessageColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.messages.fetch(query),
  // Messages are immutable after sending - no create/update; delete stays.
  deleteData: (ids) => bridge.messages.delete(ids.map(String)),
})

// Delete is the only declarative action: Edit is impossible (a sent message is
// immutable), View would duplicate the row click, and Resend is drawn by the
// page itself in the card's #actions so it can carry its own per-row spinner.
const rowActions: RowAction<NotificationInfo>[] = [deleteAction(crud)]

// ---- Resend action ----
const resendingIds = ref<Set<string>>(new Set())
const batchResending = ref(false)

function isFailed(row: NotificationInfo): boolean {
  // NotificationInfo.status is a NotificationStatus enum (string member name).
  return row.status === NotificationStatus.Failed
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
    // Backend's send endpoint is per-id - fan out sequentially so partial
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

const t = makePageTranslator('notification.messages')

/**
 * Row title. Plenty of real sends carry no subject (an SMS has none, and a 2FA
 * code send leaves it empty), and a row whose only identity is a dash tells the
 * reader nothing - fall back to what the send WAS: its template, then its
 * category.
 */
function messageTitle(row: NotificationInfo): string {
  return row.subject?.trim() || row.templateName?.trim() || row.category?.trim() || EMPTY_DASH
}

/** Channel glyph, so a list of mixed email/SMS/push sends is scannable. */
function channelIcon(type?: NotificationType): string {
  switch (type) {
    case NotificationType.Email: return 'mdi:email-outline'
    case NotificationType.Sms: return 'mdi:message-text-outline'
    case NotificationType.Push: return 'mdi:bell-outline'
    default: return 'mdi:send-outline'
  }
}

function statusTone(status?: NotificationStatus): ItemCardTone {
  switch (status) {
    case NotificationStatus.Sent: return 'success'
    case NotificationStatus.Failed: return 'error'
    case NotificationStatus.Pending: return 'warning'
    default: return 'default'
  }
}

function messageTags(row: NotificationInfo): ItemCardTag[] {
  const out: ItemCardTag[] = [
    { label: t(`status.${String(row.status ?? '').toLowerCase()}`), type: statusTone(row.status) },
  ]
  if (row.type) out.push({ label: String(row.type), type: 'default' })
  // A partially-delivered send is neither "sent" nor "failed"; say so on the row.
  if (row.failureCount > 0 && row.successCount > 0) {
    out.push({ label: t('admin.shared.card.partial', { n: row.failureCount }), type: 'warning' })
  }
  return out
}
</script>

<style scoped>
.nm-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 16px;
  font-size: 12.5px;
  color: var(--tnzi-base-text-muted);
}
.nm-meta__item {
  display: inline-flex;
  align-items: center;
  gap: 5px;
}
.nm-error {
  display: flex;
  align-items: flex-start;
  gap: 5px;
  margin: 4px 0 0;
  font-size: 12px;
  line-height: 1.45;
  color: var(--tnzi-error);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
