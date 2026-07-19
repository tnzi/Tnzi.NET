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
        :schema="vendorFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
      />
    </template>

    <!-- Remit-to bank accounts (structured EFT / wire details) for this vendor. -->
    <template #detail>
      <PartyBankAccountsPanel
        v-if="viewedParty?.id"
        :bridge="bridge"
        :party-type="FinancePartyType.Vendor"
        :party-id="String(viewedParty.id)"
        :t="t"
        :can-manage="canManageRemit"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { editAction, deleteAction, type RowAction } from '../../headless/rowActions'
import { createFinanceBridge, FinancePartyType, type UpdateVendorDto } from '../../services/bridges/finance-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer from '../_shared/form-schema'
import { makePageTranslator } from '../_shared/translate'
import PartyBankAccountsPanel from './components/PartyBankAccountsPanel.vue'
import { buildVendorColumns, vendorFormSchema, type VendorRow } from './vendor-config'

const bridge = createFinanceBridge({ client: useAdminClient() })
const t = makePageTranslator('finance.vendors')
const { can } = usePermissionGuard()

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

// Remit-to panel opens through the read-only detail drawer.
const viewedParty = ref<VendorRow | null>(null)
const canManageRemit = computed(() => can('finance.partyBank.create') || can('finance.partyBank.update'))

const crud = useCrudPage<VendorRow>({
  pageId: 'finance.vendors',
  permission: 'finance.vendor',
  columns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => bridge.vendors.fetch(q),
  createData: (d) => bridge.vendors.create(toPayload(d)),
  updateData: (id, d) => bridge.vendors.update(String(id), toPayload(d)),
  deleteData: (ids) => bridge.vendors.delete(ids.map(String)),
  onView: (row) => {
    viewedParty.value = row
  },
})

const title = 'tnzi.admin.modules.finance.vendors.title'
const detailTitle = (d: VendorRow) => d.name ?? t('remitTo.title')
const rowActions: RowAction<VendorRow>[] = [
  { key: 'remitTo', label: 'remitTo.action', onClick: (row) => crud.openView(row) },
  editAction(crud),
  deleteAction(crud),
]
</script>
