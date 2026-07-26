<template>
  <!--
    Orders - admin view of payment orders. Read-only, because payment records are
    an immutable financial ledger: no create, no edit, no delete, and no row
    buttons at all. Opening a row IS the only operation, so the row card carries
    it and a lone "View" button would just duplicate it.

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
            <span v-if="item.description" class="ord-meta__desc" :title="item.description">
              {{ item.description }}
            </span>
          </div>
        </template>

        <template #trailing>
          <div class="ord-amount">
            <span class="ord-amount__value">{{ formatCurrency(item.paidAmount, item.currency) }}</span>
            <span v-if="item.discountAmount > 0" class="ord-amount__label">
              {{ t('admin.shared.card.discount', { amount: formatCurrency(item.discountAmount, item.currency) }) }}
            </span>
          </div>
        </template>

        <!--
          No #actions slot: on an immutable payment ledger the only operation
          was "View", and the row already opens it. Rendering a lone View button
          next to a clickable row would be a second control for the same intent;
          the card shows its chevron affordance instead.
        -->
      </TItemCard>
    </template>

    <template #kpis>
      <TKpiRow class="orders-page__stats">
        <TKpiCard :label="t('kpi.totalRevenue')" :value="totalRevenueDisplay" />
        <TKpiCard :label="t('kpi.orderCount')" :value="stats.totalTransactions" />
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
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { TRelativeTime, TSvgIcon } from '@tnzi/ui'
import TItemPage from '../../components/crud/TItemPage.vue'
import TItemCard, { type ItemCardTag, type ItemCardTone } from '../../components/data/TItemCard.vue'
import TKpiRow from '../../components/data/TKpiRow.vue'
import TKpiCard from '../../components/data/TKpiCard.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { createPaymentBridge } from '../../services/bridges/payment-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import type { FormSchemaItem } from '../_shared/form-schema'
import { buildOrderColumns, orderFormSchema, orderStatusOptions } from './order-config'
import { makePageTranslator } from '../_shared/translate'
import { formatCurrency } from '@tnzi/core'
import type { PaymentDto, PaymentStatisticsDto } from '@tnzi/core/services/payment'

const t = makePageTranslator('payment.orders')

const bridge = createPaymentBridge({ client: useAdminClient() })

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
  activeSubscriptions: 0,
  channelDistribution: [],
}

const stats = ref<PaymentStatisticsDto>({ ...defaultStats })

const averageAmount = computed(() =>
  stats.value.totalTransactions > 0
    ? stats.value.totalRevenue / stats.value.totalTransactions
    : 0,
)
// KPI display values - money formats through @tnzi/core's formatCurrency
// ("$1,234.56"); the paid rate keeps one decimal place. TKpiCard renders
// string values verbatim (no number animation), which is intended here.
const totalRevenueDisplay = computed(() => formatCurrency(stats.value.totalRevenue))
const averageAmountDisplay = computed(() => formatCurrency(averageAmount.value))
const paidRateDisplay = computed(() =>
  stats.value.totalTransactions > 0
    ? ((stats.value.successfulTransactions / stats.value.totalTransactions) * 100).toFixed(1)
    : '0.0',
)

const crud = useCrudPage<PaymentDto>({
  pageId: 'payment.orders',
  columns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.orders.fetch(query),
  // Payment orders are an immutable financial ledger - read-only from admin.
})


async function loadStats(): Promise<void> {
  try {
    const result = await bridge.orders.statistics()
    // Bridge may return null/undefined when the backend endpoint is down or
    // unauthorized; keep the default-stats shape so the template's
    // `stats.totalRevenue` access stays safe.
    stats.value = result ? { ...defaultStats, ...result } : { ...defaultStats }
  } catch {
    // Stats fetch failure must not block the main table
    stats.value = { ...defaultStats }
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
