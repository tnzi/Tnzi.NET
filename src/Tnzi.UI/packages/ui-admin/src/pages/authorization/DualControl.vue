<template>
  <TItemPage
    :state="crud"
    :title="t('title')"
    :title-help="t('titleHelp')"
    :translate="t"
    :search-fields="searchFields"
    :detail-width="720"
    :detail-title="detailTitle"
    :show-create="false"
  >
    <template #item="{ item }">
      <TItemCard
        :title="item.operation || EMPTY_DASH"
        :subtitle="item.description || undefined"
        icon="mdi:account-multiple-check-outline"
        :icon-tone="dualControlStatusTone(item.status)"
        :tags="rowTags(item)"
        :muted="item.status === DualControlStatus.Cancelled || isSpent(item)"
        clickable
        @click="crud.openView(item)"
      >
        <template #meta>
          <div class="dc-meta">
            <span class="dc-meta__item">
              <TSvgIcon icon="mdi:account-arrow-right-outline" :size="13" />
              {{ item.requesterName || shortId(item.requesterId) }}
            </span>
            <span v-if="item.targetId" class="dc-meta__item">
              <TSvgIcon icon="mdi:target" :size="13" />{{ item.targetId }}
            </span>
            <span class="dc-meta__item">
              <TSvgIcon icon="mdi:clock-outline" :size="13" />
              {{ t('card.requested') }} {{ fmt(item.creationTime) }}
            </span>
            <span v-if="item.status === DualControlStatus.Pending" class="dc-meta__item">
              <TSvgIcon icon="mdi:timer-sand" :size="13" />
              {{ t('card.expires') }} {{ fmt(item.expiresAt) }}
            </span>
            <span v-if="item.approverName || item.approverId" class="dc-meta__item">
              <TSvgIcon icon="mdi:gavel" :size="13" />
              {{ item.approverName || shortId(item.approverId) }}
              <template v-if="item.decidedAt"> · {{ fmt(item.decidedAt) }}</template>
            </span>
          </div>
        </template>

        <template #actions>
          <TRowActions :row="item" :actions="rowActions" :translate="t" />
        </template>
      </TItemCard>
    </template>

    <template #detail="{ data }">
      <div v-if="data" class="dc-detail">
        <TDescriptions :items="detailItems(data as DualControlRequestDto)" :max-columns="2" />

        <!--
          The payload snapshot is the whole point of the screen: the approver is
          agreeing to THESE parameters, and execution later compares against them
          byte for byte. Burying it behind a toggle would invite approving the
          operation name instead of the operation.
        -->
        <div class="dc-payload">
          <div class="dc-payload__head">
            <TSvgIcon icon="mdi:code-json" :size="15" />
            <span>{{ t('detail.payload') }}</span>
          </div>
          <p class="dc-payload__hint">{{ t('detail.payloadHint') }}</p>
          <pre v-if="(data as DualControlRequestDto).payloadJson" class="dc-payload__body">{{
            prettyPayload((data as DualControlRequestDto).payloadJson)
          }}</pre>
          <NAlert v-else type="default" :bordered="false" class="dc-payload__empty">
            {{ t('detail.noPayload') }}
          </NAlert>
        </div>
      </div>
    </template>
  </TItemPage>

  <TModalShell
    v-model:show="decisionShow"
    :title="decisionKind === 'approve' ? t('decision.approveTitle') : t('decision.rejectTitle')"
    :width="520"
  >
    <NAlert
      v-if="decisionKind === 'approve'"
      type="warning"
      :bordered="false"
      class="dc-decision__warn"
    >
      {{ t('decision.approveWarning', { operation: decisionRow?.operation ?? '' }) }}
    </NAlert>
    <NInput
      v-model:value="decisionComment"
      type="textarea"
      :rows="3"
      :placeholder="t('decision.commentPlaceholder')"
    />
    <template #footer>
      <!-- `dismiss`, not `cancel`: cancelling a REQUEST is the withdraw action on
           the row. Sharing one key would make the dialog's escape hatch and a
           state transition read as the same thing. -->
      <NButton size="small" @click="decisionShow = false">{{ t('decision.dismiss') }}</NButton>
      <NButton
        size="small"
        :type="decisionKind === 'approve' ? 'primary' : 'error'"
        :loading="decisionBusy"
        @click="submitDecision"
      >
        {{ decisionKind === 'approve' ? t('decision.approve') : t('decision.reject') }}
      </NButton>
    </template>
  </TModalShell>
</template>

<script setup lang="ts">
/**
 * Dual-control (four-eyes) approval queue.
 *
 * The framework ships the backend, the permission codes and the HTTP client for
 * this; what was missing was the screen, which meant an administrator could
 * grant "Decide Dual-Control Requests" in the permission tree and land nowhere.
 *
 * Three deliberate absences, mirroring the bridge:
 *
 *   Create - a request is raised by a business action, because it has to carry
 *            that action's parameter snapshot. There is no generic endpoint.
 *   Edit   - the approved payload is frozen on purpose.
 *   Delete - a decided request is the evidence that a second person looked.
 *
 * ★ The Approve button being visible does NOT mean the call will succeed. Two
 *   permissions gate it: `authorization.dualControl.approve` reaches the
 *   endpoint, and `{operation}.approve` - resolved server-side per request -
 *   decides that KIND of action. That second one is what stops an approver of
 *   expense reports from also approving account deletions, and the console
 *   cannot evaluate it up front, so a refusal has to read as a normal outcome
 *   rather than a bug.
 */
import { computed, ref } from 'vue'
import { NAlert, NButton, NInput } from 'naive-ui'
import { TDescriptions, TModalShell, TSvgIcon } from '@tnzi/ui'
import { formatDateTime } from '@tnzi/core'
import TItemPage from '../../components/crud/TItemPage.vue'
import TItemCard, { type ItemCardTag } from '../../components/data/TItemCard.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import type { RowAction } from '../../headless/row-actions'
import { createDualControlBridge } from '../../services/bridges/dual-control-bridge'
import { useAdminClient } from '../../plugin/client'
import { useAdminAuthStore } from '../../stores/useAdminAuthStore'
import { EMPTY_DASH } from '../../utils/placeholders'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safe-message'
import {
  DUAL_CONTROL_STATUS_OPTIONS,
  dualControlColumns,
  dualControlStatusTone,
  isSpent,
} from './dual-control-config'
import { DualControlStatus, type DualControlRequestDto } from '@tnzi/core/services/authorization'

const bridge = createDualControlBridge({ client: useAdminClient() })
const t = makePageTranslator('authorization.dualControl')
const message = useSafeMessage()
const { can } = usePermissionGuard()
const auth = useAdminAuthStore()

const crud = useCrudPage<DualControlRequestDto, string>({
  pageId: 'authorization.dualControl',
  columns: dualControlColumns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (query) => bridge.requests.fetch(query),
  loadDetailById: (id) => bridge.requests.getById(id),
  // No createData / updateData / deleteData: omitting them is what makes the
  // shell hide those affordances. See the file header for why each is absent.
})

const searchFields = computed(() => [
  {
    key: 'status',
    label: t('filters.status'),
    type: 'select' as const,
    options: DUAL_CONTROL_STATUS_OPTIONS.map((v) => ({ label: v, value: v })),
  },
  { key: 'operation', label: t('filters.operation'), type: 'input' as const },
  { key: 'targetId', label: t('filters.targetId'), type: 'input' as const },
])

const detailTitle = (row: DualControlRequestDto) => row.operation || t('title')

const fmt = (v?: string | null) => (v ? formatDateTime(v) : EMPTY_DASH)

/** Enough of a GUID to tell two rows apart when the name did not resolve. */
const shortId = (id?: string | null) => (id ? `${id.slice(0, 8)}…` : EMPTY_DASH)

function rowTags(row: DualControlRequestDto): ItemCardTag[] {
  const tags: ItemCardTag[] = [
    { label: t(`status.${row.status}`), type: dualControlStatusTone(row.status) },
  ]
  // An approved permit decays: it expires and it is spent on use. Showing only
  // "Approved" would let an operator read a dead permit as a live one.
  if (isSpent(row)) {
    tags.push({ label: row.isConsumed ? t('tags.consumed') : t('tags.expired'), type: 'default' })
  }
  return tags
}

function detailItems(row: DualControlRequestDto) {
  return [
    { label: t('columns.operation'), value: row.operation || EMPTY_DASH },
    { label: t('columns.targetId'), value: row.targetId || EMPTY_DASH },
    { label: t('columns.status'), value: t(`status.${row.status}`) },
    { label: t('columns.requester'), value: row.requesterName || shortId(row.requesterId) },
    { label: t('columns.approver'), value: row.approverName || shortId(row.approverId) },
    { label: t('columns.requestedAt'), value: fmt(row.creationTime) },
    { label: t('columns.decidedAt'), value: fmt(row.decidedAt) },
    { label: t('columns.expiresAt'), value: fmt(row.expiresAt) },
    { label: t('detail.usable'), value: row.isUsable ? t('detail.yes') : t('detail.no') },
    { label: t('detail.comment'), value: row.decisionComment || EMPTY_DASH },
  ]
}

/**
 * Pretty-print for reading, falling back to the raw string.
 *
 * Presentation only: the backend compares the stored snapshot byte for byte and
 * deliberately does NOT normalise JSON, so nothing here is ever sent back.
 */
function prettyPayload(payload?: string | null): string {
  if (!payload) return ''
  try {
    return JSON.stringify(JSON.parse(payload), null, 2)
  } catch {
    return payload
  }
}

const isMine = (row: DualControlRequestDto) => !!auth.userInfo?.id && row.requesterId === auth.userInfo.id

const decisionShow = ref(false)
const decisionBusy = ref(false)
const decisionKind = ref<'approve' | 'reject'>('approve')
const decisionComment = ref('')
const decisionRow = ref<DualControlRequestDto | null>(null)

function openDecision(row: DualControlRequestDto, kind: 'approve' | 'reject') {
  decisionRow.value = row
  decisionKind.value = kind
  decisionComment.value = ''
  decisionShow.value = true
}

async function submitDecision() {
  const row = decisionRow.value
  if (!row) return

  decisionBusy.value = true
  try {
    if (decisionKind.value === 'approve') {
      await bridge.requests.approve(row.id, decisionComment.value)
      message.success(t('decision.approved'))
    } else {
      await bridge.requests.reject(row.id, decisionComment.value)
      message.success(t('decision.rejected'))
    }
    decisionShow.value = false
    await crud.refresh()
  } catch (err) {
    // Surface the server's own reason. A 403 here is a normal outcome (you are
    // the requester, or you do not hold `{operation}.approve`), and the message
    // is the only thing that tells the two apart.
    message.error(err instanceof Error ? err.message : String(err))
  } finally {
    decisionBusy.value = false
  }
}

async function cancelRequest(row: DualControlRequestDto) {
  try {
    await bridge.requests.cancel(row.id)
    message.success(t('decision.cancelled'))
    await crud.refresh()
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
  }
}

const rowActions: RowAction<DualControlRequestDto>[] = [
  {
    key: 'approve',
    label: 'decision.approve',
    type: 'primary',
    show: (row) => row.status === DualControlStatus.Pending && can('authorization.dualControl.approve'),
    onClick: (row) => openDecision(row, 'approve'),
  },
  {
    key: 'reject',
    label: 'decision.reject',
    type: 'error',
    // Same permission as approving: whoever may say yes may also say no.
    // Giving refusal a looser code would let someone block an action they were
    // never trusted to decide.
    show: (row) => row.status === DualControlStatus.Pending && can('authorization.dualControl.approve'),
    onClick: (row) => openDecision(row, 'reject'),
  },
  {
    key: 'cancel',
    label: 'decision.cancel',
    confirm: 'decision.cancelConfirm',
    // Withdrawing needs no approval right - but it IS requester-only, enforced
    // server-side. Hiding it for everyone else keeps the row from offering a
    // button that always 403s.
    show: (row) => row.status === DualControlStatus.Pending && isMine(row),
    onClick: (row) => void cancelRequest(row),
  },
]
</script>

<style scoped>
.dc-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 14px;
}

.dc-meta__item {
  display: inline-flex;
  align-items: center;
  gap: 4px;
}

.dc-detail {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.dc-payload__head {
  display: flex;
  align-items: center;
  gap: 6px;
  font-weight: 600;
  margin-bottom: 4px;
}

.dc-payload__hint {
  margin: 0 0 8px;
  font-size: 12px;
  color: var(--tnzi-base-text-muted, #999);
}

.dc-payload__body {
  margin: 0;
  padding: 12px;
  border-radius: 6px;
  background: var(--tnzi-bg-deep, #f5f6f8);
  border: 1px solid var(--tnzi-border, #e5e6eb);
  font-size: 12px;
  line-height: 1.6;
  overflow-x: auto;
  white-space: pre;
}

.dc-decision__warn {
  margin-bottom: 12px;
}
</style>
