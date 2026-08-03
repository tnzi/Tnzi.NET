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
        :schema="transferFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :field-renderers="fieldRenderers"
        :translate="t"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { watch } from 'vue'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { deleteAction, editAction, viewAction, type RowAction } from '../../headless/row-actions'
import { createFinanceBridge, FinanceDocumentStatus } from '../../services/bridges/finance-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer, { selectRenderer } from '../_shared/form-schema'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safe-message'
import { createFinanceOptionSources } from './options'
import { tsToIsoDate } from './money'
import { buildTransferSearchFields, buildTransferColumns, transferFormSchema, type TransferRow } from './transfer-config'

const bridge = createFinanceBridge({ client: useAdminClient() })
const t = makePageTranslator('finance.transfers')
const message = useSafeMessage()
const { can } = usePermissionGuard()
const sources = createFinanceOptionSources(bridge)

const columns = buildTransferColumns(t)

// 真实筛选（标准 1）：只声明后端 QueryDto 真的支持的字段。
const searchFields = buildTransferSearchFields(t)

function normalizeCurrency(value: unknown): string | null {
  return typeof value === 'string' && value.trim() ? value.trim().toUpperCase() : null
}

function toPayload(d: Record<string, unknown>) {
  const currency = normalizeCurrency(d.currency)
  const targetCurrency = normalizeCurrency(d.targetCurrency)
  // Cross-currency only when a target currency is set and differs from the source currency.
  const isCross = !!targetCurrency && targetCurrency !== currency
  return {
    fromAccountId: String(d.fromAccountId ?? ''),
    toAccountId: String(d.toAccountId ?? ''),
    transferDate: typeof d.transferDate === 'number' ? tsToIsoDate(d.transferDate) : String(d.transferDate ?? ''),
    amount: Number(d.amount ?? 0),
    currency,
    targetCurrency: isCross ? targetCurrency : null,
    targetAmount: isCross ? Number(d.targetAmount ?? 0) : null,
    targetExchangeRate: isCross && d.targetExchangeRate != null && d.targetExchangeRate !== '' ? Number(d.targetExchangeRate) : null,
    reference: (d.reference as string | null) || null,
    memo: (d.memo as string | null) || null,
  }
}

const crud = useCrudPage<TransferRow>({
  pageId: 'finance.transfers',
  permission: 'finance.document',
  columns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => bridge.transfers.fetch(q),
  loadDetailById: (id) => bridge.transfers.getById(String(id)),
  createData: (d) => bridge.transfers.createDraft(toPayload(d)),
  updateData: (id, d) => bridge.transfers.updateDraft(String(id), toPayload(d)),
  deleteData: async (ids) => {
    for (const id of ids) await bridge.transfers.deleteDraft(String(id))
  },
})

const title = 'tnzi.admin.modules.finance.transfers.title'

const fieldRenderers = {
  'finance-account': selectRenderer(() => sources.leafAccountOptions.value, { placeholder: t('form.accountPlaceholder'), clearable: false }),
}

watch(
  () => crud.formModal.visible.value,
  (open) => {
    if (open) void sources.ensureLeafAccounts()
  },
  { immediate: true },
)

const isDraft = (row: TransferRow) => row.status === FinanceDocumentStatus.Draft

async function run(action: () => Promise<unknown>, successKey: string) {
  try {
    await action()
    message.success(t(successKey))
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  }
}

// edit/delete 走内置工厂（权限门 = crud.canUpdate/canDelete，与其余 finance 页同源）；
// post/void 为自定义生命周期操作，保留 can() 门控
const rowActions: RowAction<TransferRow>[] = [
  editAction(crud, { show: isDraft }),
  // Posted / voided transfers are immutable - offer a read-only detail view
  // (the form schema renders readonly in view mode) since Edit is draft-only.
  viewAction(crud, { show: (row) => !isDraft(row) }),
  { key: 'post', label: 'actions.post', type: 'primary', show: (row) => can('finance.document.update') && isDraft(row), confirm: 'confirmPost', onClick: (row) => void run(() => bridge.transfers.post(String(row.id ?? '')), 'postSuccess') },
  { key: 'void', label: 'actions.void', type: 'warning', show: (row) => can('finance.document.update') && row.status === FinanceDocumentStatus.Posted, confirm: 'confirmVoid', onClick: (row) => void run(() => bridge.transfers.voidDoc(String(row.id ?? '')), 'voidSuccess') },
  deleteAction(crud, { show: isDraft, confirm: 'confirmDelete' }),
]
</script>
