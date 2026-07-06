<template>
  <!--
    Orders — admin view of payment orders. Read-only (payment records are an
    immutable financial ledger); the only row action is a read-only View.
    A KPI row (payment statistics) renders between the page header and the list
    card via TCrudPage's #kpis slot. Statistics are fetched ONCE on mount (the
    figures are a global overview, independent of list paging/filters).

    The backend `PaymentQueryDto` has no free-text field, so the default
    keyword box is disabled; `status` is exposed as an advanced-search filter.
  -->
  <TCrudPage
    :state="crud"
    :all-columns="columns"
    :search-fields="searchFields"
    :show-default-search="false"
    :title="t('title')"
    :translate="t"
    :form-modal-width="760"
    :row-actions="rowActions"
  >
    <template #kpis>
      <TKpiRow class="orders-page__stats">
        <TKpiCard :label="t('kpi.totalRevenue')" :value="totalRevenueDisplay" />
        <TKpiCard :label="t('kpi.orderCount')" :value="stats.totalTransactions" />
        <TKpiCard :label="t('kpi.averageAmount')" :value="averageAmountDisplay" />
        <TKpiCard :label="t('kpi.paidRate')" :value="paidRateDisplay" suffix="%" />
      </TKpiRow>
    </template>

    <!-- Read-only quick preview (right drawer) — reached via the row View
         action. A #detail slot (not #form) is required so the form modal mounts
         even on this create/update-free page. -->
    <template #detail="{ data }">
      <TFormSchemaRenderer
        :schema="orderFormSchema"
        :model="(data ?? {}) as Record<string, unknown>"
        readonly
        :translate="t"
        :columns="2"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TKpiRow from '../../components/data/TKpiRow.vue'
import TKpiCard from '../../components/data/TKpiCard.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { viewAction, type RowAction } from '../../headless/rowActions'
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
// KPI display values — money formats through @tnzi/core's formatCurrency
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
  // Payment orders are an immutable financial ledger — read-only from admin.
})

// Read-only quick preview only (no create/edit/delete on an immutable ledger).
const rowActions: RowAction<PaymentDto>[] = [viewAction(crud)]

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

// Statistics are a global overview — fetch once on mount, not per page change.
onMounted(() => { void loadStats() })
</script>
