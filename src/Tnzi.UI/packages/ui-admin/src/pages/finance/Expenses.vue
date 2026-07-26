<template>
  <TItemPage
    :state="crud"
    :title="title"
    :search-fields="searchFields"
    :row-actions="rowActions"
    :translate="t"
    :detail-width="720"
:detail-title="detailTitle"
    show-batch
  >
    <!-- One row per document: party leads, status + number are chips beside it,
         dates underneath, money (and what is still open) right-aligned. -->
    <template #item="{ item, selected, selectable, toggleSelect }">
      <TDocumentCard
        :row="item"
        party-key="vendorName"
        icon="mdi:cash-minus"
        :t="t"
        :selectable="selectable"
        :checked="selected"
        @update:checked="toggleSelect"
        @open="crud.openView(item)"
      >
        <template #actions>
          <TRowActions :row="item" :actions="rowActions" :translate="t" />
        </template>
      </TDocumentCard>
    </template>    <!-- 批量过账（标准 1）：只处理选中的草稿，串行执行，部分失败逐条报出。 -->
    <template #batchActions="{ selectedIds }">
      <NPopconfirm @positive-click="() => batch.postMany(selectedIds)">
        <template #trigger>
          <NButton
            v-if="selectedIds.length > 0 && can('finance.document.update')"
            size="small"
            type="primary"
            ghost
            :loading="batch.running.value"
            :disabled="batch.countFor(selectedIds) === 0"
          >
            {{ td('batch.postAction', { n: batch.countFor(selectedIds), total: selectedIds.length }) }}
          </NButton>
        </template>
        {{ td('batch.confirmPost', { n: batch.countFor(selectedIds) }) }}
      </NPopconfirm>
    </template>

    <template #primary>
      <NButton v-if="can('finance.document.create')" type="primary" tertiary size="small" @click="openCreate">
        <template #icon>
          <TSvgIcon icon="mdi:plus" :size="16" />
        </template>
        {{ t('actions.create') }}
      </NButton>
    </template>

    <!-- Read-only detail (deep-linkable ?detail=view:<id>). `onView` lazy-loads lines. -->
    <template #detail>
      <template v-if="viewed">
        <NDescriptions :column="2" size="small" bordered class="fin-doc-detail__meta">
          <NDescriptionsItem :label="t('columns.status')">{{ statusLabel(viewed.status) }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.docDate')">{{ fmtDate(viewed.docDate) }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.party')">{{ viewed.vendorName ?? EMPTY_DASH }}</NDescriptionsItem>
          <NDescriptionsItem :label="td('editor.paidFrom')">{{ viewed.paidFromAccountName ?? EMPTY_DASH }}</NDescriptionsItem>
          <NDescriptionsItem :label="td('editor.paymentMethod')">{{ methodLabel(viewed.paymentMethod) }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.currency')">{{ viewed.currency }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.total')">{{ fmtAmount(viewed.total) }}</NDescriptionsItem>
          <NDescriptionsItem :label="td('editor.memo')" :span="2">{{ viewed.memo ?? EMPTY_DASH }}</NDescriptionsItem>
        </NDescriptions>
        <TResponsiveTable
          :columns="lineColumns"
          :data="viewed.lines"
          :bordered="false"
          size="small"
          mobile="scroll"
          :pagination="false"
        />

        <DocumentCollaborationPanel v-if="viewed.id" :doc-type="FinanceDocToken.Expense" :doc-id="String(viewed.id)" />
      </template>
    </template>
  </TItemPage>

  <!-- Line editor (create / edit draft) - useDetail + TDetailHost (?entry=new / edit:<id>). -->
  <TDetailHost :state="entryDetail" :title="editorTitle" :width="920" :footer="false" :translate="t">
    <DocumentEditor
      :key="editorSeq"
      kind="expense"
      :entry="editingEntry"
      :party-label="t('editor.party')"
      :party-options="sources.vendorOptions.value"
      :party-optional="true"
      :show-due-date="false"
      :account-options="sources.leafAccountOptions.value"
      :item-options="sources.itemOptions.value"
      :tax-code-options="sources.taxCodeOptions.value"
      :on-save="saveEditor"
      @cancel="entryDetail.close()"
    />
  </TDetailHost>
</template>

<script setup lang="ts">
import { EMPTY_DASH } from '../../utils/placeholders'
import { computed, h, ref, watch } from 'vue'
import { NButton, NPopconfirm, NDescriptions, NDescriptionsItem, type DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'

import TItemPage from '../../components/crud/TItemPage.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import TDocumentCard from '../../components/finance/TDocumentCard.vue'
import TDetailHost from '../../components/detail/TDetailHost.vue'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { useDocumentBatch } from './useDocumentBatch'
import { useDetail } from '../../headless/useDetail'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import type { RowAction } from '../../headless/rowActions'
import TPartySelect from '../../components/finance/TPartySelect.vue'
import { createFinanceBridge, FinanceDocumentStatus, PAYMENT_METHODS, type ExpenseDto, type CreateExpenseDto } from '../../services/bridges/finance-bridge'
import { useAdminClient } from '../../plugin/client'
import { makePageTranslator } from '../_shared/translate'
import DocumentCollaborationPanel from './components/DocumentCollaborationPanel.vue'
import { FinanceDocToken } from './source-type'
import { useSafeMessage } from '../_shared/safeMessage'
import { buildDocumentColumns, buildDocumentSearchFields, DOC_STATUS_META, type FinanceDocRow } from './document-config'
import { createFinanceOptionSources } from './options'
import { amountCell, fmtAmount, fmtDate } from './money'
import DocumentEditor, { type DocumentEditorPayload, type EditableDocument } from './components/DocumentEditor.vue'

const bridge = createFinanceBridge({ client: useAdminClient() })
const t = makePageTranslator('finance.expenses')
// Shared document namespace for the read-only line-table headers + memo/paid-from labels.
const td = makePageTranslator('finance.docs')
const message = useSafeMessage()
const { can } = usePermissionGuard()
const sources = createFinanceOptionSources(bridge)

const columns = buildDocumentColumns(t, 'vendorName', { showApplied: false, showDueDate: false })

// 真实筛选（标准 1）：状态 / 单据日期区间 / 往来方。往来方走 render 逃生口挂
// TPartySelect —— 它是异步远程搜索，form-schema 的静态 select 承载不了。
const searchFields = buildDocumentSearchFields(td, {
  renderParty: (model) =>
    h(TPartySelect, {
      modelValue: (model.partyId as string) ?? null,
      bridge,
      kind: 'vendor',
      size: 'small',
      'onUpdate:modelValue': (v: string | null) => (model.partyId = v ?? undefined),
    }),
})

const crud = useCrudPage<FinanceDocRow>({
  pageId: 'finance.expenses',
  permission: 'finance.document',
  columns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => bridge.expenses.fetch(q),
  // Deep-link cold restore (?detail=view:<id>) hydrates even off-page rows.
  loadDetailById: (id) => bridge.expenses.getById(id),
  onView: (row) => void loadViewed(row),
})

const title = 'tnzi.admin.modules.finance.expenses.title'
const detailTitle = (d: FinanceDocRow) => d.number ?? t('detail.draftTitle')

function statusLabel(status?: FinanceDocumentStatus): string {
  const meta = DOC_STATUS_META[status ?? '']
  return meta ? t(meta.label) : String(status ?? '')
}

function methodLabel(method?: string | null): string {
  if (!method) return EMPTY_DASH
  const known = PAYMENT_METHODS.find((m) => m === method)
  return known ? td(`method.${known.charAt(0).toLowerCase()}${known.slice(1)}`) : method
}

// ── Read-only detail ────────────────────────────────────────────
const viewed = ref<ExpenseDto | null>(null)

async function loadViewed(row: FinanceDocRow) {
  viewed.value = null
  try {
    viewed.value = await bridge.expenses.getById(String(row.id ?? ''))
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  }
}

const lineColumns: DataTableColumns<ExpenseDto['lines'][number]> = [
  { key: 'lineNumber', title: '#', width: 48 },
  { key: 'description', title: td('editor.description'), minWidth: 180, render: (row) => row.description ?? EMPTY_DASH },
  { key: 'amount', title: td('editor.amount'), width: 130, render: (row) => amountCell(fmtAmount(row.amount)) },
]

// ── Line-editor overlay ─────────────────────────────────────────
const entryDetail = useDetail<ExpenseDto>({
  mode: 'modal',
  url: 'entry',
  loadData: (id) => bridge.expenses.getById(String(id)),
})

const editingEntry = computed<EditableDocument | null>(() => {
  const d = entryDetail.data.value
  if (!d?.id) return null
  return {
    id: d.id,
    partyId: d.vendorId,
    paidFromAccountId: d.paidFromAccountId,
    paymentMethod: d.paymentMethod,
    docDate: d.docDate,
    dueDate: null,
    currency: d.currency,
    memo: d.memo,
    lines: d.lines,
  }
})
const editorTitle = computed(() => (editingEntry.value ? t('editor.editTitle') : t('editor.createTitle')))

const editorSeq = ref(0)
watch(
  () => entryDetail.visible.value,
  (open) => {
    if (open) {
      editorSeq.value++
      void sources.ensureLeafAccounts()
      void sources.ensureItems()
      void sources.ensureTaxCodes()
      void sources.ensureVendors()
    }
  },
  { immediate: true },
)

function openCreate() {
  void entryDetail.open('create')
}

function openEdit(row: FinanceDocRow) {
  void entryDetail.open('edit', String(row.id ?? ''))
}

async function saveEditor(payload: DocumentEditorPayload, post: boolean) {
  const data: CreateExpenseDto = {
    vendorId: payload.partyId,
    paidFromAccountId: payload.paidFromAccountId!,
    paymentMethod: payload.paymentMethod,
    docDate: payload.docDate,
    currency: payload.currency,
    memo: payload.memo,
    lines: payload.lines.map((l) => ({
      description: l.description,
      accountId: l.accountId!,
      amount: l.amount,
      taxCodeId: l.taxCodeId,
    })),
  }

  const wasCreate = !editingEntry.value?.id
  const saved = editingEntry.value?.id
    ? await bridge.expenses.updateDraft(editingEntry.value.id, data)
    : await bridge.expenses.createDraft(data)
  // 幂等：createDraft 成功后 hydrate 编辑器，post 失败重试走 update 而非再建孤儿草稿。
  if (wasCreate) await entryDetail.open('edit', saved)

  if (post) {
    await bridge.expenses.post(saved.id)
    message.success(t('postSuccess'))
  } else {
    message.success(t('savedSuccess'))
  }

  entryDetail.close()
  await crud.refresh()
}

// ── Lifecycle actions ───────────────────────────────────────────
// 批量过账：只处理草稿、串行、部分失败逐条报出（见 useDocumentBatch）。
const batch = useDocumentBatch({
  items: crud.items,
  post: (id) => bridge.expenses.post(id),
  translate: td,
  message,
  refresh: () => crud.refresh(),
  clearSelection: () => crud.batchActions.clear(),
})

async function run(action: () => Promise<unknown>, successKey: string) {
  try {
    await action()
    message.success(t(successKey))
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  }
}

const isDraft = (row: FinanceDocRow) => row.status === FinanceDocumentStatus.Draft
const isVoidable = (row: FinanceDocRow) => row.status === FinanceDocumentStatus.Posted

const rowActions: RowAction<FinanceDocRow>[] = [
  // No View action: the row card itself opens the read-only detail.
  { key: 'edit', type: 'primary', show: (row) => can('finance.document.update') && isDraft(row), onClick: openEdit },
  { key: 'post', label: 'actions.post', type: 'primary', show: (row) => can('finance.document.update') && isDraft(row), confirm: 'confirmPost', onClick: (row) => void run(() => bridge.expenses.post(String(row.id ?? '')), 'postSuccess') },
  { key: 'void', label: 'actions.void', type: 'warning', show: (row) => can('finance.document.update') && isVoidable(row), confirm: 'confirmVoid', onClick: (row) => void run(() => bridge.expenses.voidDoc(String(row.id ?? '')), 'voidSuccess') },
  { key: 'delete', label: 'actions.delete', type: 'error', show: (row) => can('finance.document.delete') && isDraft(row), confirm: 'confirmDelete', onClick: (row) => void run(() => bridge.expenses.deleteDraft(String(row.id ?? '')), 'deleteSuccess') },
]
</script>

<style scoped>
.fin-doc-detail__meta {
  margin-bottom: 12px;
}
</style>
