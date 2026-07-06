<template>
  <TCrudPage :state="crud" :all-columns="exchangeRateColumns" :title="title" :row-actions="rowActions" :translate="t">
    <template #toolbarRight>
      <NButton size="small" :loading="refreshing" @click="refreshFromProvider">
        <template #icon>
          <TSvgIcon icon="mdi:cloud-sync-outline" :size="16" />
        </template>
        {{ t('actions.refreshProvider') }}
      </NButton>
    </template>
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="exchangeRateFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { NButton } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { editAction, deleteAction, type RowAction } from '../../headless/rowActions'
import { createFinanceBridge, type UpsertExchangeRateDto } from '../../services/bridges/finance-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safeMessage'
import { exchangeRateColumns, exchangeRateFormSchema, type RateRow } from './exchange-rate-config'
import { tsToIsoDate } from './money'

const bridge = createFinanceBridge({ client: useAdminClient() })
const t = makePageTranslator('finance.rates')
const message = useSafeMessage()

/** Form model → upsert payload. The `date` schema field holds a timestamp. */
function toPayload(d: Record<string, unknown>): UpsertExchangeRateDto {
  const rateDate = d.rateDate
  return {
    fromCurrency: String(d.fromCurrency ?? '').trim().toUpperCase(),
    toCurrency: String(d.toCurrency ?? '').trim().toUpperCase(),
    rate: Number(d.rate ?? 0),
    rateDate: typeof rateDate === 'number' ? tsToIsoDate(rateDate) : String(rateDate ?? ''),
    source: (d.source as string | null) ?? null,
  }
}

const crud = useCrudPage<RateRow>({
  pageId: 'finance.rates',
  columns: exchangeRateColumns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => bridge.rates.fetch(q),
  // Upsert semantics: create and update both go through the idempotent upsert.
  createData: (d) => bridge.rates.upsert(toPayload(d)),
  updateData: (_id, d) => bridge.rates.upsert(toPayload(d)),
  deleteData: (ids) => bridge.rates.delete(ids.map(String)),
})

const title = 'tnzi.admin.modules.finance.rates.title'
const rowActions: RowAction<RateRow>[] = [editAction(crud), deleteAction(crud)]

const refreshing = ref(false)

async function refreshFromProvider() {
  refreshing.value = true
  try {
    const count = await bridge.rates.refresh()
    message.success(t('refreshSuccess', { count }))
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    refreshing.value = false
  }
}
</script>
