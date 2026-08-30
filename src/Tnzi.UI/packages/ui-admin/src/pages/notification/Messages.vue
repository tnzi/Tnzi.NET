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
      <NPopconfirm @positive-click="() => batchCancel(selectedIds)">
        <template #trigger>
          <NButton
            v-if="selectedIds.length > 0 && can('notification.message.update')"
            size="small"
            type="error"
            ghost
            :loading="batchCancelling"
          >
            {{ t('actions.batchCancel') }} ({{ selectedIds.length }})
          </NButton>
        </template>
        {{ t('actions.confirmBatchCancel') }}
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

      <!--
        Per-recipient delivery breakdown. A bulk send's row-level counters answer
        "how many", never "which ones and why" - and "why" is the only question
        anyone opens this drawer with after a partial failure.
      -->
      <div class="nm-report">
        <h4 class="nm-report__title">{{ t('report.title') }}</h4>
        <NSpin :show="reportLoading">
          <TKpiRow v-if="report" :columns="4" class="nm-report__kpis">
            <TKpiCard :label="t('report.total')" :value="report.totalRecipients" />
            <TKpiCard :label="t('report.sent')" :value="report.sentCount" tone="success" />
            <TKpiCard :label="t('report.failed')" :value="report.failedCount" :tone="report.failedCount > 0 ? 'error' : 'default'" />
            <TKpiCard :label="t('report.pending')" :value="report.pendingCount" />
          </TKpiRow>
          <TResponsiveTable
            v-if="report"
            :columns="reportColumns"
            :data="reportRows"
            :row-key="(r: RecipientOutput) => r.id"
            size="small"
            :bordered="false"
          />
          <p v-if="reportTruncated" class="nm-report__truncated">
            {{ t('report.truncated', { shown: REPORT_ROW_CAP, total: report!.recipients.length }) }}
          </p>
          <TEmpty v-else-if="!reportLoading" :text="reportError ?? t('report.empty')" />
        </NSpin>
      </div>
    </template>
  </TItemPage>
</template>

<script setup lang="ts">
import { computed, h, ref } from 'vue'
import { NButton, NPopconfirm, NSpin, type DataTableColumns } from 'naive-ui'
import { TEmpty, TRelativeTime, TSvgIcon } from '@tnzi/ui'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import TKpiRow from '../../components/data/TKpiRow.vue'
import TKpiCard from '../../components/data/TKpiCard.vue'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import type { StatusType } from '@tnzi/ui'
import TItemPage from '../../components/crud/TItemPage.vue'
import TItemCard, { type ItemCardTag, type ItemCardTone } from '../../components/data/TItemCard.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import { EMPTY_DASH } from '../../utils/placeholders'
import { useCrudPage } from '../../headless/useCrudPage'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { deleteAction, type RowAction } from '../../headless/row-actions'
import { createNotificationBridge } from '../../services/bridges/notification-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { notificationMessageColumns, notificationMessageFormSchema } from './message-config'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safe-message'
import {
  NotificationStatus,
  NotificationType,
  type NotificationInfo,
  type DeliveryReportDto,
  type RecipientOutput,
} from '@tnzi/core/services/notification'
import { formatDateTime } from '@tnzi/core'

const title = 'title'
const bridge = createNotificationBridge({ client: useAdminClient() })
const { can } = usePermissionGuard()
const message = useSafeMessage()

const crud = useCrudPage<NotificationInfo>({
  pageId: 'notification.messages',
  permission: 'notification.message',
  columns: notificationMessageColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.messages.fetch(query),
  // Messages are immutable after sending - no create/update; delete stays.
  deleteData: (ids) => bridge.messages.delete(ids.map(String)),
  onView: (row) => void loadReport(row),
})

/**
 * Cancel + Delete are the declarative actions. Edit is impossible (a sent
 * message is immutable), View would duplicate the row click, and Resend is drawn
 * by the page itself in the card's #actions so it can carry its own per-row spinner.
 *
 * ★ Cancel is not the inverse of Resend and it is not Delete: the row survives
 * and its recipients are marked Cancelled, so the record still shows that this
 * notification was created and deliberately stopped. It only offers itself while
 * the send can still be stopped - on a message already out the door the button
 * would be a promise the backend cannot keep.
 */
const rowActions: RowAction<NotificationInfo>[] = [
  {
    key: 'cancel',
    label: 'actions.cancel',
    type: 'error',
    confirm: 'actions.confirmCancel',
    show: (row) => isCancellable(row) && can('notification.message.update'),
    onClick: (row) => cancelMessage(row),
  },
  deleteAction(crud),
]

const CANCELLABLE = new Set<NotificationStatus>([
  NotificationStatus.Pending,
  NotificationStatus.Scheduled,
  NotificationStatus.Sending,
])

function isCancellable(row: NotificationInfo): boolean {
  return row.status != null && CANCELLABLE.has(row.status)
}

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

// ---- Cancel ----
const batchCancelling = ref(false)

async function cancelMessage(row: NotificationInfo): Promise<void> {
  try {
    await bridge.messages.cancel(String(row.id ?? ''))
    await crud.refresh()
  } catch (err) {
    // ★ 服务器拒绝取消是**正常结果**（消息刚刚发出去了、并发被人抢先），
    // 而不是一个可以静静吞掉的意外。原话必须原样透出：只有服务器说得清为什么。
    message.error(errorText(err))
  }
}

async function batchCancel(ids: Array<string | number>): Promise<void> {
  if (!ids.length) return
  batchCancelling.value = true
  try {
    // One call: the backend skips ids that are past cancelling and answers with
    // how many it actually stopped, so partial selections need no client-side
    // pre-filtering to avoid a wall of rejections.
    const stopped = await bridge.messages.batchCancel(ids.map(String))
    await crud.refresh()
    // 只报「停下了几条」：后端跳过来不及的那些，说「全部取消成功」会是一句谎。
    message.success(t('actions.batchCancelResult', { n: stopped, total: ids.length }))
  } catch (err) {
    message.error(errorText(err))
  } finally {
    batchCancelling.value = false
  }
}

// ---- Delivery report ----
const report = ref<DeliveryReportDto | null>(null)
const reportLoading = ref(false)
const reportError = ref<string | null>(null)

/**
 * ★ 请求序号令牌：抽屉可以在上一份报告回来之前就被换到另一条消息上，而慢的那次后到
 * 会把新的一份覆盖掉 —— 屏幕上于是显示着 A 的收件人、标题却是 B。这种错读起来完全
 * 像真的，所以过期的答案一律丢弃。
 */
let reportSeq = 0

async function loadReport(row: NotificationInfo): Promise<void> {
  const id = String(row.id ?? '')
  const seq = ++reportSeq
  report.value = null
  reportError.value = null
  if (!id) return
  reportLoading.value = true
  try {
    const loaded = await bridge.messages.getDeliveryReport(id)
    if (seq !== reportSeq) return
    report.value = loaded
  } catch (err) {
    if (seq !== reportSeq) return
    // 抽屉里其余部分仍然值得读，所以报告失败降级成它自己那一块里的一句话，
    // 而不是把整个面板拖下水。
    reportError.value = errorText(err)
  } finally {
    if (seq === reportSeq) reportLoading.value = false
  }
}

function errorText(err: unknown): string {
  return err instanceof Error ? err.message : String(err)
}

/**
 * ★ 一次群发可以有几千个收件人，而这是一个抽屉不是一个页面。整份渲染出来会让抽屉
 * 卡住，且没人会往下滚三千行 —— 报告要回答的是「哪些人、为什么」，失败的那些排在最前
 * 就已经答完了。超出部分明说被截断，**不装作这就是全部**。
 */
const REPORT_ROW_CAP = 200

const reportRows = computed<RecipientOutput[]>(() => {
  const all = report.value?.recipients ?? []
  if (all.length <= REPORT_ROW_CAP) return all
  const rank = (r: RecipientOutput) =>
    r.status === NotificationStatus.Failed ? 0 : r.status === NotificationStatus.Cancelled ? 1 : 2
  return [...all].sort((a, b) => rank(a) - rank(b)).slice(0, REPORT_ROW_CAP)
})

const reportTruncated = computed(() => (report.value?.recipients?.length ?? 0) > REPORT_ROW_CAP)

const t = makePageTranslator('notification.messages')

/**
 * ★ Cancelled reads as warning, not error: a recipient the framework held back
 * (opted out, channel switched off, hourly limit) is a correct outcome, not a
 * delivery that went wrong.
 */
function recipientTone(status?: NotificationStatus): StatusType {
  switch (status) {
    case NotificationStatus.Sent: return 'success'
    case NotificationStatus.Failed: return 'error'
    case NotificationStatus.Cancelled: return 'warning'
    default: return 'default'
  }
}

const reportColumns: DataTableColumns<RecipientOutput> = [
  { key: 'address', title: () => t('report.columns.address'), minWidth: 200 },
  {
    key: 'status',
    title: () => t('report.columns.status'),
    width: 130,
    // ★ 走 status.* 词典而不是把线缆枚举直接印出来 —— 同一页 48 行之下的行卡片已经
    // 在译同一个枚举了，两处不一致比两处都不译更糟。
    render: (row) => h(TStatusBadge, {
      value: row.status,
      type: recipientTone(row.status),
      label: t(`status.${String(row.status ?? '').toLowerCase()}`),
    }),
  },
  {
    key: 'sentTime',
    title: () => t('report.columns.sentTime'),
    width: 170,
    render: (row) => (row.sentTime ? formatDateTime(row.sentTime) : EMPTY_DASH),
  },
  {
    key: 'failureReason',
    title: () => t('report.columns.failureReason'),
    minWidth: 220,
    render: (row) => row.failureReason || EMPTY_DASH,
  },
]

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
    case NotificationType.Fax: return 'mdi:fax'
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
.nm-report {
  margin-top: 20px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.nm-report__title {
  margin: 0;
  font-size: 13px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--tnzi-base-text-muted);
}
.nm-report__kpis {
  margin-bottom: 12px;
}
.nm-report__truncated {
  margin: 0;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
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
