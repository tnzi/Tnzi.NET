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
    <template #primary>
      <NButton type="primary" tertiary size="small" @click="openCreate">
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
          <NDescriptionsItem :label="t('columns.docDate')">{{ formatDateOnly(viewed.docDate, { utc: true }) }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.party')">{{ viewed.vendorName ?? '—' }}</NDescriptionsItem>
          <NDescriptionsItem :label="td('editor.paidFrom')">{{ viewed.paidFromAccountName ?? '—' }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.currency')">{{ viewed.currency }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.total')">{{ fmtAmount(viewed.total) }}</NDescriptionsItem>
          <NDescriptionsItem :label="td('editor.memo')" :span="2">{{ viewed.memo ?? '—' }}</NDescriptionsItem>
        </NDescriptions>
        <TResponsiveTable
          :columns="lineColumns"
          :data="viewed.lines"
          :bordered="false"
          size="small"
          mobile="scroll"
          :pagination="false"
        />
      </template>
    </template>
  </TCrudPage>

  <!-- Line editor (create / edit draft) — useDetail + TDetailHost (?entry=new / edit:<id>). -->
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
import { computed, ref, watch } from 'vue'
import { NButton, NDescriptions, NDescriptionsItem, type DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { formatDateOnly } from '@tnzi/core'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TDetailHost from '../../components/detail/TDetailHost.vue'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { useDetail } from '../../headless/useDetail'
import { viewAction, type RowAction } from '../../headless/rowActions'
import { createFinanceBridge, FinanceDocumentStatus, type ExpenseDto, type CreateExpenseDto } from '../../services/bridges/finance-bridge'
import { useAdminClient } from '../../plugin/client'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safeMessage'
import { buildDocumentColumns, DOC_STATUS_META, type FinanceDocRow } from './document-config'
import { createFinanceOptionSources } from './options'
import { amountCell, fmtAmount } from './money'
import DocumentEditor, { type DocumentEditorPayload, type EditableDocument } from './components/DocumentEditor.vue'

const bridge = createFinanceBridge({ client: useAdminClient() })
const t = makePageTranslator('finance.expenses')
// Shared document namespace for the read-only line-table headers + memo/paid-from labels.
const td = makePageTranslator('finance.docs')
const message = useSafeMessage()
const sources = createFinanceOptionSources(bridge)

const columns = buildDocumentColumns(t, 'vendorName', { showApplied: false, showDueDate: false })

const crud = useCrudPage<FinanceDocRow>({
  pageId: 'finance.expenses',
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
  { key: 'description', title: td('editor.description'), minWidth: 180, render: (row) => row.description ?? '—' },
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

  const saved = editingEntry.value?.id
    ? await bridge.expenses.updateDraft(editingEntry.value.id, data)
    : await bridge.expenses.createDraft(data)

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
  viewAction(crud),
  { key: 'edit', type: 'primary', show: isDraft, onClick: openEdit },
  { key: 'post', label: 'actions.post', type: 'primary', show: isDraft, confirm: 'confirmPost', onClick: (row) => void run(() => bridge.expenses.post(String(row.id ?? '')), 'postSuccess') },
  { key: 'void', label: 'actions.void', type: 'warning', show: isVoidable, confirm: 'confirmVoid', onClick: (row) => void run(() => bridge.expenses.voidDoc(String(row.id ?? '')), 'voidSuccess') },
  { key: 'delete', label: 'actions.delete', type: 'error', show: isDraft, confirm: 'confirmDelete', onClick: (row) => void run(() => bridge.expenses.deleteDraft(String(row.id ?? '')), 'deleteSuccess') },
]
</script>

<style scoped>
.fin-doc-detail__meta {
  margin-bottom: 12px;
}
</style>
