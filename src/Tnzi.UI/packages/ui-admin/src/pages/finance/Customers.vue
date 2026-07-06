<template>
  <TCrudPage :state="crud" :all-columns="columns" :title="title" :row-actions="rowActions" :translate="t">
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="customerFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :field-renderers="fieldRenderers"
        :translate="t"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { onMounted } from 'vue'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { editAction, deleteAction, type RowAction } from '../../headless/rowActions'
import { createFinanceBridge, type UpdateCustomerDto } from '../../services/bridges/finance-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer, { selectRenderer } from '../_shared/form-schema'
import { makePageTranslator } from '../_shared/translate'
import { createFinanceOptionSources } from './options'
import { buildCustomerColumns, customerFormSchema, type CustomerRow } from './customer-config'

const bridge = createFinanceBridge({ client: useAdminClient() })
const t = makePageTranslator('finance.customers')
const sources = createFinanceOptionSources(bridge)

const columns = buildCustomerColumns(t)

function toPayload(d: Record<string, unknown>): UpdateCustomerDto {
  return {
    name: String(d.name ?? '').trim(),
    code: (d.code as string | null) || null,
    email: (d.email as string | null) || null,
    phone: (d.phone as string | null) || null,
    billingAddress: (d.billingAddress as string | null) || null,
    shippingAddress: (d.shippingAddress as string | null) || null,
    currency: typeof d.currency === 'string' && d.currency.trim() ? d.currency.trim().toUpperCase() : null,
    paymentTermsDays: d.paymentTermsDays == null ? null : Number(d.paymentTermsDays),
    defaultTaxCodeId: (d.defaultTaxCodeId as string | null) || null,
    notes: (d.notes as string | null) || null,
    isActive: d.isActive !== false,
  }
}

const crud = useCrudPage<CustomerRow>({
  pageId: 'finance.customers',
  columns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => bridge.customers.fetch(q),
  createData: (d) => bridge.customers.create(toPayload(d)),
  updateData: (id, d) => bridge.customers.update(String(id), toPayload(d)),
  deleteData: (ids) => bridge.customers.delete(ids.map(String)),
})

const title = 'tnzi.admin.modules.finance.customers.title'
const rowActions: RowAction<CustomerRow>[] = [editAction(crud), deleteAction(crud)]

// Default tax code — lazily-loaded tax-code options via the shared factory.
const fieldRenderers = {
  'finance-tax-code': selectRenderer(() => sources.taxCodeOptions.value, { placeholder: t('form.taxCodePlaceholder') }),
}

onMounted(() => {
  void sources.ensureTaxCodes()
})
</script>
