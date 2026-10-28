<template>
  <!--
    Orders - admin view of payment orders. Read-only, because payment records are
    an immutable financial ledger: no create, no edit, no delete. Opening a row
    is "View"; the only row button is "Refund", on paid orders, for holders of
    `payment.refund.create`.

    A KPI row (payment statistics) renders between the page header and the list
    via the #kpis slot. Statistics are fetched ONCE on mount (the figures are a
    global overview, independent of list paging/filters).

    The backend `PaymentQueryDto` has no free-text field, so the default keyword
    box is disabled; `status` is exposed as an advanced-search filter.
  -->
  <TItemPage
    :state="crud"
    :search-fields="searchFields"
    :show-default-search="false"
    :title="t('title')"
    :translate="t"
    :form-modal-width="760"
    :show-create="false"
    :show-batch="false"
  >
    <!-- One row per payment: the trade number identifies the record, the state
         and channel are chips, the business order and timing sit underneath, and
         the amount is right-aligned with the discount called out when there is
         one (a paid amount that silently differs from the original is exactly
         the kind of thing a table column hides). -->
    <template #item="{ item }">
      <TItemCard
        :title="item.tradeNo"
        :icon="methodIcon(item.paymentMethod)"
        :icon-tone="statusTone(item.status)"
        :tags="orderTags(item)"
        :muted="isDead(item)"
        clickable
        @click="crud.openView(item)"
      >
        <template #meta>
          <div class="ord-meta">
            <span class="ord-meta__item">
              <TSvgIcon icon="mdi:receipt-text-outline" :size="13" />{{ item.businessOrderNo }}
            </span>
            <span class="ord-meta__item">
              <TSvgIcon icon="mdi:clock-outline" :size="13" />
              <TRelativeTime :value="item.paidTime ?? item.creationTime" />
            </span>
            <span v-if="item.billingAccountType || item.billingAccountId" class="ord-meta__item" data-testid="order-billing-account">
              <TSvgIcon icon="mdi:domain" :size="13" />{{ formatBillingAccount(item) }}
            </span>
            <span v-if="item.description" class="ord-meta__desc" :title="item.description">
              {{ item.description }}
            </span>
          </div>
        </template>

        <template #trailing>
          <div class="ord-amount">
            <span class="ord-amount__value">{{ formatCurrency(orderAmount(item), item.currency) }}</span>
            <span v-if="item.discountAmount > 0" class="ord-amount__label">
              {{ t('admin.shared.card.discount', { amount: formatCurrency(item.discountAmount, item.currency) }) }}
            </span>
          </div>
        </template>

        <!--
          Opening the row is "View". The one other operation is raising a
          refund on a paid order, so only those rows carry an action cluster;
          every other row keeps the chevron affordance.
        -->
        <template v-if="isRefundable(item)" #actions>
          <TRowActions :row="item" :actions="rowActions" :translate="t" />
        </template>
      </TItemCard>
    </template>

    <!-- The statistics endpoint needs payment.statistics.view; this page needs
         only payment.order.view. Without the code the row is not offered: its
         read can only be refused. -->
    <template v-if="canSeeStatistics" #kpis>
      <TKpiRow class="orders-page__stats">
        <TKpiCard :label="t('kpi.totalRevenue')" :value="totalRevenueDisplay" />
        <TKpiCard :label="t('kpi.orderCount')" :value="stats?.totalTransactions ?? null" />
        <TKpiCard :label="t('kpi.averageAmount')" :value="averageAmountDisplay" />
        <TKpiCard :label="t('kpi.paidRate')" :value="paidRateDisplay" suffix="%" />
      </TKpiRow>
    </template>

    <!-- Read-only quick preview (right drawer) - reached via the row View
         action. A #detail slot (not #form) is required so the form modal mounts
         even on this create/update-free page. -->
    <template #detail="{ data }">
      <TFormSchemaRenderer
        :schema="orderFormSchema"
        :model="(data ?? {}) as Record<string, unknown>"
        readonly
        :translate="t"
      />
    </template>
  </TItemPage>

  <!-- Refund overlay: amount + reason. Deep-linkable as `?refund=edit:<id>`. -->
  <TDetailHost :state="refundDetail" :title="t('refund.title')" :width="480" :translate="t">
    <template #default>
      <NForm>
        <NFormItem :label="t('refund.amount')" required>
          <NInputNumber
            v-model:value="refundAmount"
            class="w-full"
            :min="0.01"
            :max="refundDetail.data.value?.paidAmount"
            :precision="2"
            data-testid="refund-amount"
          />
        </NFormItem>
        <NFormItem :label="t('refund.reason')" required>
          <NInput
            v-model:value="refundReason"
            type="textarea"
            :rows="3"
            :maxlength="500"
            :placeholder="t('refund.reasonPlaceholder')"
            data-testid="refund-reason"
          />
        </NFormItem>
      </NForm>
    </template>
    <template #footer="{ close }">
      <NButton @click="close">{{ t('admin.crud.cancel') }}</NButton>
      <NButton
        type="warning"
        :loading="refundSaving"
        :disabled="!canSubmitRefund"
        data-testid="refund-submit"
        @click="submitRefund"
      >
        {{ t('actions.refund') }}
      </NButton>
    </template>
  </TDetailHost>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { NButton, NForm, NFormItem, NInput, NInputNumber } from 'naive-ui'
import { TRelativeTime, TSvgIcon } from '@tnzi/ui'
import TItemPage from '../../components/crud/TItemPage.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import TDetailHost from '../../components/detail/TDetailHost.vue'
import { useDetail } from '../../headless/useDetail'
import type { RowAction } from '../../headless/row-actions'
import { useSafeMessage } from '../_shared/safe-message'
import { billingAccountSearchFields, formatBillingAccount } from './billing-account-config'
import TItemCard, { type ItemCardTag, type ItemCardTone } from '../../components/data/TItemCard.vue'
import TKpiRow from '../../components/data/TKpiRow.vue'
import TKpiCard from '../../components/data/TKpiCard.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { createPaymentBridge } from '../../services/bridges/payment-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import type { FormSchemaItem } from '../_shared/form-schema'
import { buildOrderColumns, orderAmount, orderFormSchema, orderStatusOptions } from './order-config'
import { makePageTranslator } from '../_shared/translate'
import { formatCurrency } from '@tnzi/core'
import type { PaymentDto, PaymentStatisticsDto } from '@tnzi/core/services/payment'

const t = makePageTranslator('payment.orders')

const bridge = createPaymentBridge({ client: useAdminClient() })
const { can } = usePermissionGuard()
const canSeeStatistics = computed(() => can('payment.statistics.view'))

const columns = buildOrderColumns(t)

// Status is the only server-side filter `PaymentQueryDto` supports for this
// page; exposed through the advanced-search drawer (option value = enum member
// name, e.g. 'Succeeded', which the backend binds directly).
const searchFields: FormSchemaItem[] = [
  {
    key: 'status',
    labelKey: 'form.status',
    label: 'Status',
    type: 'select',
    placeholderKey: 'filter.statusAny',
    placeholder: 'Status',
    options: orderStatusOptions.map((o) => ({ value: o.value, label: o.value, labelKey: o.labelKey })),
  },
  ...billingAccountSearchFields(),
]

const defaultStats: PaymentStatisticsDto = {
  startTime: '',
  endTime: '',
  totalRevenue: 0,
  totalTransactions: 0,
  successfulTransactions: 0,
  failedTransactions: 0,
  totalRefunds: 0,
  refundCount: 0,
  refundRate: 0,
  // null = not applicable (this deployment may not load the recurring-billing
  // module at all). This page never renders it; the default just has to match
  // the DTO rather than assert "zero active subscriptions".
  activeSubscriptions: null,
  channelDistribution: [],
}

/**
 * Null until read, and after a failed read: the cards then show the
 * placeholder. Zeros would state "no revenue, no orders" about figures nobody
 * saw (the statistics endpoint needs `payment.statistics.view`, while this page
 * needs only `payment.order.view`).
 */
const stats = ref<PaymentStatisticsDto | null>(null)

// Revenue sums only succeeded payments, so it is averaged over those: dividing
// by every attempt (failed and pending included) understates the order value.
const averageAmount = computed(() => {
  const s = stats.value
  if (!s) return null
  return s.successfulTransactions > 0 ? s.totalRevenue / s.successfulTransactions : 0
})
// KPI display values - money formats through @tnzi/core's formatCurrency
// ("$1,234.56"); the paid rate keeps one decimal place. TKpiCard renders
// string values verbatim (no number animation), which is intended here.
// The strip carries the currency the server reports (CA$, not a bare "$"); with mixed currencies
// the totals are a plain sum and are shown as numbers without a symbol.
function statMoney(amount: number): string {
  const currency = stats.value?.currency
  if (currency === null) return amount.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
  // An older server sends no currency at all: keep its formatting
  return currency ? formatCurrency(amount, currency) : formatCurrency(amount)
}
const totalRevenueDisplay = computed(() => (stats.value ? statMoney(stats.value.totalRevenue) : null))
const averageAmountDisplay = computed(() => (averageAmount.value === null ? null : statMoney(averageAmount.value)))
const paidRateDisplay = computed(() => {
  const s = stats.value
  if (!s) return null
  return s.totalTransactions > 0 ? ((s.successfulTransactions / s.totalTransactions) * 100).toFixed(1) : '0.0'
})

const crud = useCrudPage<PaymentDto>({
  pageId: 'payment.orders',
  columns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.orders.fetch(query),
  // Payment orders are an immutable financial ledger - read-only from admin.
})


async function loadStats(): Promise<void> {
  if (!canSeeStatistics.value) return
  try {
    const result = await bridge.orders.statistics()
    // A body missing some fields keeps the DTO shape; no body at all is no figure.
    stats.value = result ? { ...defaultStats, ...result } : null
  } catch {
    // Stats fetch failure must not block the main table, nor read as zeros.
    stats.value = null
  }
}

// Statistics are a global overview - fetch once on mount, not per page change.
onMounted(() => { void loadStats() })

/** Payment-method glyph, so a mixed channel list is scannable by shape. */
function methodIcon(method?: string): string {
  const m = String(method ?? '').toLowerCase()
  if (m.includes('card') || m.includes('credit')) return 'mdi:credit-card-outline'
  if (m.includes('wechat')) return 'mdi:wechat'
  if (m.includes('alipay')) return 'mdi:alpha-a-circle-outline'
  if (m.includes('transfer') || m.includes('bank')) return 'mdi:bank-transfer'
  if (m.includes('wallet') || m.includes('balance')) return 'mdi:wallet-outline'
  return 'mdi:cash'
}

function statusTone(status?: string): ItemCardTone {
  switch (String(status ?? '')) {
    case 'Succeeded': return 'success'
    case 'Failed': return 'error'
    case 'Pending': return 'warning'
    case 'Refunded':
    case 'PartiallyRefunded': return 'info'
    default: return 'default'
  }
}

/** A closed-without-money order should not read like a live one. */
function isDead(row: PaymentDto): boolean {
  const s = String(row.status ?? '')
  return s === 'Failed' || s === 'Cancelled' || s === 'Expired'
}

// ─── Refund ────────────────────────────────────────────────────────────────
// Raised on the customer's behalf (`POST admin/refunds`): the most common
// refund path, and the user-facing endpoint cannot serve it. Only money that
// was taken can be returned; the server checks the remaining refundable amount.
const message = useSafeMessage()
const REFUNDABLE = new Set(['Succeeded', 'PartiallyRefunded'])
const isRefundable = (row: PaymentDto): boolean => can('payment.refund.create') && REFUNDABLE.has(String(row.status ?? ''))

const refundDetail = useDetail<PaymentDto>({ mode: 'modal', url: 'refund', source: crud })
const refundAmount = ref<number | null>(null)
const refundReason = ref('')
const refundSaving = ref(false)
watch(() => refundDetail.data.value, (payment) => {
  if (!payment) return
  refundAmount.value = payment.paidAmount > 0 ? payment.paidAmount : null
  refundReason.value = ''
})
const canSubmitRefund = computed(
  () => !refundSaving.value && !!refundReason.value.trim() && (refundAmount.value ?? 0) > 0,
)

async function submitRefund(): Promise<void> {
  const payment = refundDetail.data.value
  const amount = refundAmount.value
  const reason = refundReason.value.trim()
  if (!payment || !amount || !reason || refundSaving.value) return
  refundSaving.value = true
  try {
    await bridge.refunds.create(payment.tradeNo, amount, reason)
    message.success(t('refund.created'))
    refundDetail.close()
    await crud.refresh()
  } catch (err) {
    message.error(err instanceof Error ? err.message : String(err))
  } finally {
    refundSaving.value = false
  }
}

const rowActions: RowAction<PaymentDto>[] = [
  {
    key: 'refund',
    label: 'actions.refund',
    type: 'warning',
    icon: 'mdi:cash-refund',
    onClick: (row) => void refundDetail.open('edit', row),
  },
]

function orderTags(row: PaymentDto): ItemCardTag[] {
  const out: ItemCardTag[] = [
    { label: t(`status.${String(row.status ?? '').toLowerCase()}`), type: statusTone(row.status) },
  ]
  if (row.channelCode) out.push({ label: row.channelCode, type: 'default' })
  return out
}
</script>

<style scoped>
.ord-meta {
  display: flex;
  flex-wrap: nowrap;
  min-width: 0;
  gap: 4px 16px;
  font-size: 12.5px;
  color: var(--tnzi-base-text-muted);
}
.ord-meta__item {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  flex-shrink: 0;
}
/* `display: block` so the ellipsis applies: a flex child clips mid-word. */
.ord-meta__desc {
  display: block;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.ord-amount {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 2px;
  text-align: right;
}
.ord-amount__value {
  font-size: 15px;
  font-weight: 700;
  color: var(--tnzi-base-text);
  font-variant-numeric: tabular-nums;
}
.ord-amount__label {
  font-size: 11.5px;
  color: var(--tnzi-base-text-muted);
}
@media (max-width: 660px) {
  .ord-amount {
    align-items: flex-start;
    text-align: left;
  }
}
</style>
