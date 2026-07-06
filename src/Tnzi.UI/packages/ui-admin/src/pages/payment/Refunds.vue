<template>
  <!--
    Refunds — admin view of refund requests. Read-only list + lifecycle actions:
      • View    → read-only quick preview (crud view modal)
      • Approve → declarative RowAction with an inline confirm (POST approve, approved=true)
      • Reject  → useDetail(modal) + TDetailHost with a reason textarea (approved=false)
    Approve/Reject only surface while the refund is still awaiting review.
  -->
  <TCrudPage
    :state="crud"
    :all-columns="columns"
    :show-default-search="false"
    :title="t('title')"
    :translate="t"
    :row-actions="rowActions"
  >
    <!-- Read-only quick preview (right drawer) — reached via the row View
         action. A #detail slot (not #form) is required so the view surface
         mounts on this create/update-free page. -->
    <template #detail="{ data }">
      <TFormSchemaRenderer
        :schema="refundFormSchema"
        :model="(data ?? {}) as Record<string, unknown>"
        readonly
        :translate="t"
        :columns="2"
      />
    </template>
  </TCrudPage>

  <!-- Reject overlay — reason required. Deep-linkable as `?reject=edit:<id>`. -->
  <TDetailHost :state="rejectDetail" :title="t('rejectTitle')" :width="480" :translate="t">
    <template #default>
      <NForm>
        <NFormItem :label="t('form.reason')" required>
          <NInput
            v-model:value="rejectReason"
            type="textarea"
            :rows="3"
            :placeholder="t('rejectReasonPlaceholder')"
          />
        </NFormItem>
      </NForm>
    </template>
    <template #footer="{ close }">
      <NButton @click="close">{{ t('admin.crud.cancel') }}</NButton>
      <NButton type="error" :loading="rejectSaving" :disabled="!rejectReason.trim()" @click="submitReject">
        {{ t('actions.reject') }}
      </NButton>
    </template>
  </TDetailHost>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import { NButton, NForm, NFormItem, NInput } from 'naive-ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TDetailHost from '../../components/detail/TDetailHost.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { useDetail } from '../../headless/useDetail'
import { viewAction, type RowAction } from '../../headless/rowActions'
import { createPaymentBridge } from '../../services/bridges/payment-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { buildRefundColumns, refundFormSchema } from './refund-config'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safeMessage'
import type { RefundDto } from '@tnzi/core/services/payment'

const t = makePageTranslator('payment.refunds')
const message = useSafeMessage()

const bridge = createPaymentBridge({ client: useAdminClient() })

const columns = buildRefundColumns(t)

const crud = useCrudPage<RefundDto, string>({
  pageId: 'payment.refunds',
  columns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.refunds.fetch(query),
  // Refund records are immutable — lifecycle moves via approve/reject only.
})

// Only pending / in-review refunds can be approved or rejected.
const REVIEWABLE = new Set(['Pending', 'Processing'])
const isReviewable = (row: RefundDto): boolean => REVIEWABLE.has(String(row.status ?? ''))

async function approve(id: string): Promise<void> {
  try {
    await bridge.refunds.approve(id)
    message.success(t('toast.approved'))
    await crud.refresh()
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
  }
}

// ─── Reject overlay ────────────────────────────────────────────────────────
const rejectDetail = useDetail<RefundDto>({ mode: 'modal', url: 'reject', source: crud })
const rejectReason = ref('')
const rejectSaving = ref(false)
// Reset the reason whenever the overlay (re)binds to a refund (open OR deep-link).
watch(() => rejectDetail.data.value, (refund) => {
  if (refund) rejectReason.value = ''
})

async function submitReject(): Promise<void> {
  const refund = rejectDetail.data.value
  const reason = rejectReason.value.trim()
  if (!refund || !reason) return
  rejectSaving.value = true
  try {
    await bridge.refunds.reject(refund.id, reason)
    message.success(t('toast.rejected'))
    rejectDetail.close()
    await crud.refresh()
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
  } finally {
    rejectSaving.value = false
  }
}

const rowActions: RowAction<RefundDto>[] = [
  viewAction(crud),
  {
    key: 'approve',
    label: 'actions.approve',
    type: 'success',
    icon: 'mdi:check',
    show: isReviewable,
    confirm: 'approvePrompt',
    onClick: (row) => void approve(row.id),
  },
  {
    key: 'reject',
    label: 'actions.reject',
    type: 'error',
    icon: 'mdi:close',
    show: isReviewable,
    onClick: (row) => void rejectDetail.open('edit', row),
  },
]
</script>
