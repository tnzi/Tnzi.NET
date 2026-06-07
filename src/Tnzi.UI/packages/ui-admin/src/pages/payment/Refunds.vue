<template>
  <!--
    Refunds — Phase 3.34
    Admin view of refund requests.
    Row actions "Approve" and "Reject" (with reason prompt) wired to:
      bridge.refunds.approve(id) and bridge.refunds.reject(id, reason).
    Both require confirmation before calling and trigger a table refresh after.
  -->
  <TCrudPage
    :state="crud"
    :all-columns="refundColumns"
    :title="t('title')"
    :translate="t"
    :show-create="false"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="refundFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
      />
    </template>

    <template #rowActions="{ row }">
      <!--
        Refunds are immutable (no Edit, no Delete — see deleteData/updateData
        stubs above). The primary action is "Approve" (most common workflow);
        "Reject" + reason prompt is folded into the More dropdown so the
        destructive path requires an extra gesture.
      -->
      <TRowActions
        :row="row as RefundRow"
        :actions="rowActions"
        :translate="t"
      >
        <template #prepend>
          <Button size="small" type="success" ghost @click="openApprove(row as RefundRow)">{{ t('actions.approve') }}</Button>
        </template>
      </TRowActions>
    </template>
  </TCrudPage>

  <!-- Approve confirmation dialog -->
  <Modal
    v-if="approveVisible"
    :show="approveVisible"
    :title="t('approveTitle')"
    @update:show="(v: boolean) => { if (!v) approveVisible = false }"
  >
    <p>{{ t('approvePrompt') }}</p>
    <div class="flex justify-end gap-8px mt-16px">
      <Button @click="approveVisible = false">{{ t('admin.common.cancel') }}</Button>
      <Button type="success" @click="confirmApprove">{{ t('actions.approve') }}</Button>
    </div>
  </Modal>

  <!-- Reject dialog with reason input -->
  <Modal
    v-if="rejectVisible"
    :show="rejectVisible"
    :title="t('rejectTitle')"
    @update:show="(v: boolean) => { if (!v) rejectVisible = false }"
  >
    <p>{{ t('rejectPrompt') }}</p>
    <Input
      v-model:value="rejectReason"
      type="textarea"
      :placeholder="t('rejectReasonPlaceholder')"
      class="mt-8px w-full"
    />
    <div class="flex justify-end gap-8px mt-16px">
      <Button @click="rejectVisible = false">{{ t('admin.common.cancel') }}</Button>
      <Button type="error" :disabled="!rejectReason.trim()" @click="confirmReject">{{ t('actions.reject') }}</Button>
    </div>
  </Modal>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { NButton as Button, NModal as Modal, NInput as Input } from 'naive-ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { type RowAction } from '../../headless/rowActions'
import { createPaymentBridge } from '../../services/bridges/payment-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { refundColumns, refundFormSchema } from './refund-config'
import { translatePageKey } from '../_shared/translate'
import type { RefundDto } from '@tnzi/core/services/payment'

type RefundRow = RefundDto & { id: string }

// Wired to /admin/refunds via Plan C 2026-04-14.
const bridge = createPaymentBridge({ client: useAdminClient() })

const crud = useCrudPage<RefundDto>({
  pageId: 'payment.refunds',
  columns: refundColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.refunds.fetch(query),
  createData: async () => { throw new Error('Refund requests are initiated by users, not admins') },
  updateData: async () => { throw new Error('Refund records are immutable; use approve/reject') },
  deleteData: async () => { throw new Error('Refunds cannot be deleted; use cancel to void') },
})


// Refunds are immutable (no Edit, no Delete). The primary action "Approve" is
// rendered by the page in the #prepend slot; "Reject" + reason prompt folds
// into the More▾ dropdown (destructive path → extra gesture).
const rowActions: RowAction<RefundRow>[] = [
  { key: 'reject', label: 'actions.reject', type: 'error', onClick: (row) => openReject(row) },
]

// Approve dialog state
const approveVisible = ref(false)
const pendingApproveId = ref<string | null>(null)

// Reject dialog state
const rejectVisible = ref(false)
const pendingRejectId = ref<string | null>(null)
const rejectReason = ref('')

function openApprove(row: RefundRow): void {
  pendingApproveId.value = row.id
  approveVisible.value = true
}

function openReject(row: RefundRow): void {
  pendingRejectId.value = row.id
  rejectReason.value = ''
  rejectVisible.value = true
}

async function confirmApprove(): Promise<void> {
  const id = pendingApproveId.value
  approveVisible.value = false
  pendingApproveId.value = null
  if (!id) return
  try {
    await bridge.refunds.approve(id)
    await crud.refresh()
  } catch {
    // Error handling deferred to error boundary / toast in full integration
  }
}

async function confirmReject(): Promise<void> {
  const id = pendingRejectId.value
  const reason = rejectReason.value.trim()
  rejectVisible.value = false
  pendingRejectId.value = null
  rejectReason.value = ''
  if (!id || !reason) return
  try {
    await bridge.refunds.reject(id, reason)
    await crud.refresh()
  } catch {
    // Error handling deferred to error boundary / toast in full integration
  }
}

onMounted(() => {
  crud.refresh().catch(() => undefined)
})

const t = (key: string) => translatePageKey('payment.refunds', key)
</script>
