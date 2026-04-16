<template>
  <!--
    PaymentSubscription page — Phase 3.33
    Admin view of payment subscriptions.
    Row action "Cancel at period end" calls bridge.subscriptions.update(id, { cancelAtPeriodEnd: true })
    with a confirmation dialog.
  -->
  <TCrudPage
    :state="crud"
    :all-columns="paymentSubscriptionColumns"
    title="Payment Subscriptions"
    :translate="t"
    :show-create="false"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="paymentSubscriptionFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
      />
    </template>

    <template #rowActions="{ row }">
      <Button
        size="small"
        type="warning"
        @click="cancelAtPeriodEnd(row as SubscriptionRow)"
      >
        Cancel at Period End
      </Button>
    </template>
  </TCrudPage>

  <!-- Confirmation dialog for cancel at period end -->
  <Modal
    v-if="confirmVisible"
    :show="confirmVisible"
    title="Confirm Cancellation"
    @update:show="(v: boolean) => { if (!v) confirmVisible = false }"
  >
    <p>Cancel subscription at end of current billing period?</p>
    <div style="display: flex; gap: 8px; justify-content: flex-end; margin-top: 16px;">
      <Button @click="confirmVisible = false">No</Button>
      <Button type="error" @click="confirmCancelAtPeriodEnd">Yes, Cancel</Button>
    </div>
  </Modal>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { NButton as Button, NModal as Modal } from 'naive-ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { createPaymentBridge } from '../../services/bridges/payment-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { paymentSubscriptionColumns, paymentSubscriptionFormSchema } from './payment-subscription-config'
import { translatePageKey } from '../_shared/translate'
import type { SubscriptionDto } from '@tnzi/core/services/payment'

type SubscriptionRow = SubscriptionDto & { id: string }

// Wired to /admin/subscriptions via Plan C 2026-04-14.
const bridge = createPaymentBridge({ client: useAdminClient() })

const crud = useCrudPage<SubscriptionDto, string>({
  pageId: 'payment.subscriptions',
  columns: paymentSubscriptionColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.subscriptions.fetch(query),
  createData: async () => { throw new Error('Subscriptions are initiated by users, not admins') },
  updateData: (id, data) => bridge.subscriptions.update(id, data),
  deleteData: async () => { throw new Error('Subscriptions cannot be deleted; use cancel') },
})


// Cancel at period end confirmation
const confirmVisible = ref(false)
const pendingCancelId = ref<string | null>(null)

function cancelAtPeriodEnd(row: SubscriptionRow): void {
  pendingCancelId.value = row.id
  confirmVisible.value = true
}

async function confirmCancelAtPeriodEnd(): Promise<void> {
  const id = pendingCancelId.value
  confirmVisible.value = false
  pendingCancelId.value = null
  if (!id) return
  try {
    await bridge.subscriptions.cancelAtPeriodEnd(id)
    await crud.refresh()
  } catch {
    // Error handling deferred to error boundary / toast in full integration
  }
}

onMounted(() => {
  crud.refresh().catch(() => undefined)
})

const t = (key: string) => translatePageKey('payment.subscriptions', key)
</script>
