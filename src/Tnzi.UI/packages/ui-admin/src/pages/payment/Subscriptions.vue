<template>
  <!--
    Subscriptions — admin view of payment subscriptions. Read-only: there is NO
    admin subscription update endpoint (subscriptions are initiated by users), so
    the page offers a read-only View plus a single lifecycle action,
    "Cancel at period end" (POST /admin/subscriptions/{id}/cancel, immediate=false),
    wired declaratively with an inline confirmation.
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
        :schema="paymentSubscriptionFormSchema"
        :model="(data ?? {}) as Record<string, unknown>"
        readonly
        :translate="t"
        :columns="2"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { viewAction, type RowAction } from '../../headless/rowActions'
import { createPaymentBridge } from '../../services/bridges/payment-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { buildSubscriptionColumns, paymentSubscriptionFormSchema } from './subscription-config'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safeMessage'
import type { SubscriptionDto } from '@tnzi/core/services/payment'

const t = makePageTranslator('payment.subscriptions')
const message = useSafeMessage()

const bridge = createPaymentBridge({ client: useAdminClient() })

const columns = buildSubscriptionColumns(t)

const crud = useCrudPage<SubscriptionDto, string>({
  pageId: 'payment.subscriptions',
  columns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.subscriptions.fetch(query),
  // Read-only: no admin create/update/delete endpoint for subscriptions.
})

// A subscription can only be cancelled while it is still live.
const TERMINAL = new Set(['Cancelled', 'Expired'])

async function cancelAtPeriodEnd(id: string): Promise<void> {
  try {
    await bridge.subscriptions.cancelAtPeriodEnd(id)
    message.success(t('toast.cancelled'))
    await crud.refresh()
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
  }
}

const rowActions: RowAction<SubscriptionDto>[] = [
  viewAction(crud),
  {
    key: 'cancelAtPeriodEnd',
    label: 'cancelAtPeriodEnd',
    type: 'warning',
    icon: 'mdi:calendar-remove-outline',
    show: (row) => !TERMINAL.has(String(row.status ?? '')),
    confirm: 'confirmCancelPrompt',
    onClick: (row) => void cancelAtPeriodEnd(row.id),
  },
]
</script>
