<template>
  <TCrudPage
    :state="crud"
    :all-columns="columns"
    :title="title"
    :row-actions="rowActions"
    :translate="t"
    :detail-width="720"
    :detail-title="detailTitle"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="customerFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :field-renderers="fieldRenderers"
        :translate="t"
      />
    </template>

    <!-- Remit-to bank accounts (structured EFT / wire details) for this customer. -->
    <template #detail>
      <PartyBankAccountsPanel
        v-if="viewedParty?.id"
        :bridge="bridge"
        :party-type="FinancePartyType.Customer"
        :party-id="String(viewedParty.id)"
        :t="t"
        :can-manage="canManageRemit"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { editAction, deleteAction, type RowAction } from '../../headless/rowActions'
import { createFinanceBridge, FinancePartyType, type UpdateCustomerDto } from '../../services/bridges/finance-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer, { selectRenderer } from '../_shared/form-schema'
import { makePageTranslator } from '../_shared/translate'
import { createFinanceOptionSources } from './options'
import PartyBankAccountsPanel from './components/PartyBankAccountsPanel.vue'
import { buildCustomerColumns, customerFormSchema, type CustomerRow } from './customer-config'

const bridge = createFinanceBridge({ client: useAdminClient() })
const t = makePageTranslator('finance.customers')
const { can } = usePermissionGuard()
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

// Remit-to panel opens through the read-only detail drawer.
const viewedParty = ref<CustomerRow | null>(null)
const canManageRemit = computed(() => can('finance.partyBank.create') || can('finance.partyBank.update'))

const crud = useCrudPage<CustomerRow>({
  pageId: 'finance.customers',
  permission: 'finance.customer',
  columns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => bridge.customers.fetch(q),
  createData: (d) => bridge.customers.create(toPayload(d)),
  updateData: (id, d) => bridge.customers.update(String(id), toPayload(d)),
  deleteData: (ids) => bridge.customers.delete(ids.map(String)),
  onView: (row) => {
    viewedParty.value = row
  },
})

const title = 'tnzi.admin.modules.finance.customers.title'
const detailTitle = (d: CustomerRow) => d.name ?? t('remitTo.title')
const rowActions: RowAction<CustomerRow>[] = [
  { key: 'remitTo', label: 'remitTo.action', onClick: (row) => crud.openView(row) },
  editAction(crud),
  deleteAction(crud),
]

// Default tax code — lazily-loaded tax-code options via the shared factory.
const fieldRenderers = {
  'finance-tax-code': selectRenderer(() => sources.taxCodeOptions.value, { placeholder: t('form.taxCodePlaceholder') }),
}

onMounted(() => {
  void sources.ensureTaxCodes()
})
</script>
