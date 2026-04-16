<template>
  <!--
    PaymentRefund page — Phase 3.34
    Admin view of refund requests.
    Row actions "Approve" and "Reject" (with reason prompt) wired to:
      bridge.refunds.approve(id) and bridge.refunds.reject(id, reason).
    Both require confirmation before calling and trigger a table refresh after.
  -->
  <TCrudPage
    :state="crud"
    :all-columns="refundColumns"
    title="Payment Refunds"
    :translate="t"
    :show-create="false"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="refundFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
      />
    </template>

    <template #rowActions="{ row }">
      <Button
        size="small"
        type="success"
        style="margin-right: 4px;"
        @click="openApprove(row as RefundRow)"
      >
        Approve
      </Button>
      <Button
        size="small"
        type="error"
        @click="openReject(row as RefundRow)"
      >
        Reject
      </Button>
    </template>
  </TCrudPage>

  <!-- Approve confirmation dialog -->
  <Modal
    v-if="approveVisible"
    :show="approveVisible"
    title="Approve Refund"
    @update:show="(v: boolean) => { if (!v) approveVisible = false }"
  >
    <p>Are you sure you want to approve this refund request?</p>
    <div style="display: flex; gap: 8px; justify-content: flex-end; margin-top: 16px;">
      <Button @click="approveVisible = false">Cancel</Button>
      <Button type="success" @click="confirmApprove">Approve</Button>
    </div>
  </Modal>

  <!-- Reject dialog with reason input -->
  <Modal
    v-if="rejectVisible"
    :show="rejectVisible"
    title="Reject Refund"
    @update:show="(v: boolean) => { if (!v) rejectVisible = false }"
  >
    <p>Provide a reason for rejection:</p>
    <Input
      v-model:value="rejectReason"
      type="textarea"
      placeholder="Enter rejection reason..."
      style="margin-top: 8px; width: 100%;"
    />
    <div style="display: flex; gap: 8px; justify-content: flex-end; margin-top: 16px;">
      <Button @click="rejectVisible = false">Cancel</Button>
      <Button type="error" :disabled="!rejectReason.trim()" @click="confirmReject">Reject</Button>
    </div>
  </Modal>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { NButton as Button, NModal as Modal, NInput as Input } from 'naive-ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { createPaymentBridge } from '../../services/bridges/payment-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { refundColumns, refundFormSchema } from './payment-refund-config'
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
