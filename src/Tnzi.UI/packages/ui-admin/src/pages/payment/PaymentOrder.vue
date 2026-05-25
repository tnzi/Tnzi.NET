<template>
  <!--
    PaymentOrder page — Phase 3.32
    Admin view of payment orders. Read-only (payment records are immutable for admin).
    Includes a statistics banner above the table showing key payment metrics.
    Statistics are fetched on mount and refreshed whenever filters change.
  -->
  <div>
    <!-- Statistics banner -->
    <div class="payment-order-stats" style="margin-bottom: 16px;">
      <Grid :cols="4" style="gap: 12px;">
        <GridItem>
          <Card>
            <Statistic label="Total Revenue" :value="stats.totalRevenue" />
          </Card>
        </GridItem>
        <GridItem>
          <Card>
            <Statistic label="Order Count" :value="stats.totalTransactions" />
          </Card>
        </GridItem>
        <GridItem>
          <Card>
            <Statistic
              label="Average Amount"
              :value="stats.totalTransactions > 0
                ? Math.round((stats.totalRevenue / stats.totalTransactions) * 100) / 100
                : 0"
            />
          </Card>
        </GridItem>
        <GridItem>
          <Card>
            <Statistic
              label="Paid %"
              :value="stats.totalTransactions > 0
                ? Math.round((stats.successfulTransactions / stats.totalTransactions) * 10000) / 100
                : 0"
            />
          </Card>
        </GridItem>
      </Grid>
    </div>

    <TCrudPage
      :state="crud"
      :all-columns="orderColumns"
      :title="t('title')"
      :translate="t"
      :show-create="false"
      :form-modal-width="760"
    >
      <template #form="{ formData, mode }">
        <TFormSchemaRenderer
          :schema="orderFormSchema"
          :model="(formData ?? {}) as Record<string, unknown>"
          :readonly="mode === 'view'"
          :translate="t"
          :columns="2"
        />
      </template>
      <template #rowActions="{ row }">
        <TRowActions :row="row" :state="crud" :translate="t" :show-edit="false" />
      </template>
    </TCrudPage>
  </div>
</template>

<script setup lang="ts">
import { ref, watchEffect, onMounted } from 'vue'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { createPaymentBridge } from '../../services/bridges/payment-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { orderColumns, orderFormSchema } from './payment-order-config'
import { translatePageKey } from '../_shared/translate'
import type { PaymentDto, PaymentStatisticsDto } from '@tnzi/core/services/payment'
import { NCard as Card, NStatistic as Statistic, NGrid as Grid, NGridItem as GridItem } from 'naive-ui'

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

const crud = useCrudPage<PaymentDto>({
  pageId: 'payment.orders',
  columns: orderColumns,
  rowKey: (r) => r.id,
  fetchData: (query) => bridge.orders.fetch(query),
  createData: async () => { throw new Error('Payment orders are read-only from admin') },
  updateData: async () => { throw new Error('Payment orders are read-only from admin') },
  deleteData: async () => { throw new Error('Payment orders cannot be deleted from admin') },
})


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
