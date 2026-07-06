<template>
  <TCrudPage :state="crud" :all-columns="columns" :title="title" :row-actions="rowActions" :translate="t">
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="vendorFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { editAction, deleteAction, type RowAction } from '../../headless/rowActions'
import { createFinanceBridge, type UpdateVendorDto } from '../../services/bridges/finance-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { makePageTranslator } from '../_shared/translate'
import { buildVendorColumns, vendorFormSchema, type VendorRow } from './vendor-config'

const bridge = createFinanceBridge({ client: useAdminClient() })
const t = makePageTranslator('finance.vendors')

const columns = buildVendorColumns(t)

function toPayload(d: Record<string, unknown>): UpdateVendorDto {
  return {
    name: String(d.name ?? '').trim(),
    code: (d.code as string | null) || null,
    email: (d.email as string | null) || null,
    phone: (d.phone as string | null) || null,
    address: (d.address as string | null) || null,
    currency: typeof d.currency === 'string' && d.currency.trim() ? d.currency.trim().toUpperCase() : null,
    paymentTermsDays: d.paymentTermsDays == null ? null : Number(d.paymentTermsDays),
    notes: (d.notes as string | null) || null,
    isActive: d.isActive !== false,
  }
}

const crud = useCrudPage<VendorRow>({
  pageId: 'finance.vendors',
  columns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => bridge.vendors.fetch(q),
  createData: (d) => bridge.vendors.create(toPayload(d)),
  updateData: (id, d) => bridge.vendors.update(String(id), toPayload(d)),
  deleteData: (ids) => bridge.vendors.delete(ids.map(String)),
})

const title = 'tnzi.admin.modules.finance.vendors.title'
const rowActions: RowAction<VendorRow>[] = [editAction(crud), deleteAction(crud)]
</script>
