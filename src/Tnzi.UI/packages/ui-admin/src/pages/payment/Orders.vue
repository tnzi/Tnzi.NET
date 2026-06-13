<template>
  <!--
    Orders — Phase 3.32
    Admin view of payment orders. Read-only (payment records are immutable for admin).
    A KPI row (payment statistics) renders between the page header and the
    list card via TCrudPage's #kpis slot (content-page standard: header →
    KPI row → list). Statistics are fetched on mount and refreshed whenever
    filters change.
  -->
  <TCrudPage
    :state="crud"
    :all-columns="orderColumns"
    :title="t('title')"
    :translate="t"
    :form-modal-width="760"
    :row-actions="rowActions"
  >
    <template #kpis>
      <TKpiRow class="orders-page__stats">
        <TStatCard :label="t('kpi.totalRevenue')" :value="totalRevenueDisplay" />
        <TStatCard :label="t('kpi.orderCount')" :value="stats.totalTransactions" />
        <TStatCard :label="t('kpi.averageAmount')" :value="averageAmountDisplay" />
        <TStatCard :label="t('kpi.paidRate')" :value="paidRateDisplay" suffix="%" />
      </TKpiRow>
    </template>

    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="orderFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
        :columns="2"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { computed, ref, watchEffect, onMounted } from 'vue'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TKpiRow from '../../components/data/TKpiRow.vue'
import TStatCard from '../../components/data/TStatCard.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { type RowAction } from '../../headless/rowActions'
import { createPaymentBridge } from '../../services/bridges/payment-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { orderColumns, orderFormSchema } from './order-config'
import { translatePageKey } from '../_shared/translate'
import { formatCurrency } from '@tnzi/core'
import type { PaymentDto, PaymentStatisticsDto } from '@tnzi/core/services/payment'

// Wired to /admin/payments + /admin/payment-statistics via Plan C
// 2026-04-14. Client is injected by createTnziUiAdmin({ client }).
const bridge = createPaymentBridge({ client: useAdminClient() })

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
// KPI display values — money formats through @tnzi/core's formatCurrency
// ("$1,234.56"); the paid rate keeps one decimal place. TStatCard renders
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
  columns: orderColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.orders.fetch(query),
  // Payment orders are an immutable financial ledger — read-only from admin.
})

const rowActions: RowAction<PaymentDto>[] = []


async function refreshStats(): Promise<void> {
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

// Refresh stats whenever the query changes (filters, pagination, search)
watchEffect(() => {
  // Access query to create reactive dependency
  void crud.query.value
  refreshStats().catch(() => undefined)
})

onMounted(() => {
  crud.refresh().catch(() => undefined)
})

const t = (key: string) => translatePageKey('payment.orders', key)
</script>
