<template>
  <TCrudPage
    :state="crud"
    :all-columns="columns"
    :search-fields="searchFields"
    :title="title"
    :row-actions="rowActions"
    :translate="t"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="itemFormSchema"
        :sections="itemFormSections"
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
import { editAction, deleteAction, type RowAction } from '../../headless/row-actions'
import { createFinanceBridge, ItemType, type UpdateItemDto } from '../../services/bridges/finance-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer, { selectRenderer } from '../_shared/form-schema'
import { makePageTranslator } from '../_shared/translate'
import { createFinanceOptionSources } from './options'
import {
  buildItemSearchFields,
  buildItemColumns,
  itemFormSchema,
  itemFormSections,
  type ItemRow,
} from './item-config'

const bridge = createFinanceBridge({ client: useAdminClient() })
const t = makePageTranslator('finance.items')
const sources = createFinanceOptionSources(bridge)

const columns = buildItemColumns(t)

// 真实筛选（标准 1）：只声明后端 QueryDto 真的支持的字段。
const searchFields = buildItemSearchFields(t)

function toPayload(d: Record<string, unknown>): UpdateItemDto {
  return {
    name: String(d.name ?? '').trim(),
    code: (d.code as string | null) || null,
    type: (d.type as ItemType) ?? ItemType.Service,
    description: (d.description as string | null) || null,
    salesPrice: d.salesPrice == null ? null : Number(d.salesPrice),
    purchasePrice: d.purchasePrice == null ? null : Number(d.purchasePrice),
    incomeAccountId: (d.incomeAccountId as string | null) || null,
    expenseAccountId: (d.expenseAccountId as string | null) || null,
    defaultTaxCodeId: (d.defaultTaxCodeId as string | null) || null,
    isActive: d.isActive !== false,
  }
}

const crud = useCrudPage<ItemRow>({
  pageId: 'finance.items',
  permission: 'finance.item',
  columns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => bridge.items.fetch(q),
  createData: (d) => bridge.items.create(toPayload(d)),
  updateData: (id, d) => bridge.items.update(String(id), toPayload(d)),
  deleteData: (ids) => bridge.items.delete(ids.map(String)),
})

const title = 'tnzi.admin.modules.finance.items.title'
const rowActions: RowAction<ItemRow>[] = [editAction(crud), deleteAction(crud)]

const fieldRenderers = {
  'finance-account': selectRenderer(() => sources.leafAccountOptions.value, { placeholder: t('form.accountPlaceholder') }),
  'finance-tax-code': selectRenderer(() => sources.taxCodeOptions.value, { placeholder: t('form.taxCodePlaceholder') }),
}

onMounted(() => {
  void sources.ensureLeafAccounts()
  void sources.ensureTaxCodes()
})
</script>
