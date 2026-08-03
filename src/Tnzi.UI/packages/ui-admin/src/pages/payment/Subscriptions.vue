<template>
  <!--
    Subscriptions - admin view of payment subscriptions. The list stays read-only
    (subscriptions are initiated by users); what an operator needs here are the
    lifecycle actions for working a ticket: retry the failed charge, pause /
    resume, cancel at period end, toggle auto-renew.
  -->
  <TCrudPage
    :state="crud"
    :all-columns="columns"
    :show-default-search="false"
    :title="t('title')"
    :translate="t"
    :row-actions="rowActions"
  >
    <!-- Read-only quick preview (right drawer) - reached via the row View
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
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { viewAction, type RowAction } from '../../headless/row-actions'
import { createPaymentBridge } from '../../services/bridges/payment-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { buildSubscriptionColumns, paymentSubscriptionFormSchema } from './subscription-config'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safe-message'
import type { SubscriptionDto } from '@tnzi/core/services/payment'

const t = makePageTranslator('payment.subscriptions')
const message = useSafeMessage()
const { can } = usePermissionGuard()

const bridge = createPaymentBridge({ client: useAdminClient() })

const columns = buildSubscriptionColumns(t)

const crud = useCrudPage<SubscriptionDto, string>({
  pageId: 'payment.subscriptions',
  columns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.subscriptions.fetch(query),
  // Read-only: no admin create/update/delete endpoint for subscriptions.
})

// A subscription can only be acted on while it is still live.
const TERMINAL = new Set(['Cancelled', 'Expired'])
const PAUSABLE = new Set(['Active', 'Trial'])

const canManage = (): boolean => can('payment.subscription.update')
const statusOf = (row: SubscriptionDto): string => String(row.status ?? '')

/** Run a lifecycle action, surface the outcome and refresh the row set. */
async function run(action: () => Promise<void>, successKey: string): Promise<void> {
  try {
    await action()
    message.success(t(successKey))
    await crud.refresh()
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
  }
}

const rowActions: RowAction<SubscriptionDto>[] = [
  viewAction(crud),
  {
    // Past-due tickets are the reason this page gets opened; retrying the charge
    // is the resolution, and waiting for the next background scan is not one.
    key: 'retryBilling',
    label: 'retryBilling',
    type: 'primary',
    icon: 'mdi:credit-card-refresh-outline',
    show: (row) => canManage() && statusOf(row) === 'PastDue',
    onClick: (row) => void run(() => bridge.subscriptions.retryBilling(row.id), 'toast.billingRetried'),
  },
  {
    key: 'pause',
    label: 'pause',
    icon: 'mdi:pause-circle-outline',
    show: (row) => canManage() && PAUSABLE.has(statusOf(row)),
    confirm: 'confirmPausePrompt',
    onClick: (row) => void run(() => bridge.subscriptions.pause(row.id), 'toast.paused'),
  },
  {
    key: 'resume',
    label: 'resume',
    icon: 'mdi:play-circle-outline',
    show: (row) => canManage() && ['Paused', 'Cancelled', 'PendingRenewal'].includes(statusOf(row)),
    onClick: (row) => void run(() => bridge.subscriptions.resume(row.id), 'toast.resumed'),
  },
  {
    key: 'cancelAtPeriodEnd',
    label: 'cancelAtPeriodEnd',
    type: 'warning',
    icon: 'mdi:calendar-remove-outline',
    show: (row) => canManage() && !TERMINAL.has(statusOf(row)),
    confirm: 'confirmCancelPrompt',
    onClick: (row) => void run(() => bridge.subscriptions.cancelAtPeriodEnd(row.id), 'toast.cancelled'),
  },
]
</script>
