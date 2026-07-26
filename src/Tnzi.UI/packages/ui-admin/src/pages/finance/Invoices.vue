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
    <!-- One row per invoice: customer leads, status + number are chips beside
         it, dates underneath, money (and what is still owed) right-aligned. -->
    <template #item="{ item, selected, selectable, toggleSelect }">
      <TDocumentCard
        :row="item"
        party-key="customerName"
        icon="mdi:receipt-text-outline"
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
    </template>

    <!-- 概览区（标准 2）：这一页的钱现在是什么状态，四个数字都可下钻到账龄报表。 -->
    <template #kpis>
      <DocumentKpiStrip :bridge="bridge" kind="ar" :translate="td" />
    </template>

    <!-- 批量过账（标准 1）：只处理选中的草稿，串行执行，部分失败逐条报出。 -->
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
          <NDescriptionsItem :label="t('columns.currency')">{{ viewed.currency }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.subTotal')">{{ fmtAmount(viewed.subTotal) }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.taxTotal')">{{ fmtAmount(viewed.taxTotal) }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.total')">{{ fmtAmount(viewed.total) }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.applied')">{{ fmtAmount(viewed.appliedTotal) }}</NDescriptionsItem>
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

        <DocumentCollaborationPanel v-if="viewed.id" :doc-type="FinanceDocToken.Invoice" :doc-id="String(viewed.id)" />
      </template>
    </template>
  </TItemPage>

  <!-- Line editor (create / edit draft) - useDetail + TDetailHost (?entry=new / edit:<id>). -->
  <TDetailHost :state="entryDetail" :title="editorTitle" :width="920" :footer="false" :translate="t">
    <DocumentEditor
      :key="editorSeq"
      kind="sales"
      :entry="editingEntry"
      :party-label="t('editor.party')"
      :party-options="sources.customerOptions.value"
      :party-optional="false"
      :show-due-date="true"
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
import { useRoute, useRouter } from 'vue-router'
import { NButton, NPopconfirm, NDescriptions, NDescriptionsItem, type DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'

import TItemPage from '../../components/crud/TItemPage.vue'
import TRowActions from '../../components/crud/TRowActions.vue'
import TDocumentCard from '../../components/finance/TDocumentCard.vue'
import DocumentKpiStrip from './components/DocumentKpiStrip.vue'
import TDetailHost from '../../components/detail/TDetailHost.vue'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { useDocumentBatch } from './useDocumentBatch'
import { useDetail } from '../../headless/useDetail'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import type { RowAction } from '../../headless/rowActions'
import TPartySelect from '../../components/finance/TPartySelect.vue'
import { createFinanceBridge, FinanceDocumentStatus, type InvoiceDto, type CreateInvoiceDto } from '../../services/bridges/finance-bridge'
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
const route = useRoute()
const router = useRouter()
const t = makePageTranslator('finance.invoices')
// Shared document namespace for the read-only line-table headers + memo label
// (qty/price/description/amount/memo live under finance.docs.editor, not each
// document page's own namespace).
const td = makePageTranslator('finance.docs')
const message = useSafeMessage()
const { can } = usePermissionGuard()
const sources = createFinanceOptionSources(bridge)

const columns = buildDocumentColumns(t, 'customerName', { showApplied: true, showDueDate: true })

// 真实筛选（标准 1）：状态 / 单据日期区间 / 往来方。往来方走 render 逃生口挂
// TPartySelect —— 它是异步远程搜索，form-schema 的静态 select 承载不了。
const searchFields = buildDocumentSearchFields(td, {
  renderParty: (model) =>
    h(TPartySelect, {
      modelValue: (model.partyId as string) ?? null,
      bridge,
      kind: 'customer',
      size: 'small',
      'onUpdate:modelValue': (v: string | null) => (model.partyId = v ?? undefined),
    }),
})

const crud = useCrudPage<FinanceDocRow>({
  pageId: 'finance.invoices',
  permission: 'finance.document',
  columns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => bridge.invoices.fetch(q),
  // Deep-link cold restore (?detail=view:<id>) hydrates even off-page rows.
  loadDetailById: (id) => bridge.invoices.getById(id),
  onView: (row) => void loadViewed(row),
})

const title = 'tnzi.admin.modules.finance.invoices.title'
const detailTitle = (d: FinanceDocRow) => d.number ?? t('detail.draftTitle')

function statusLabel(status?: FinanceDocumentStatus): string {
  const meta = DOC_STATUS_META[status ?? '']
  return meta ? t(meta.label) : String(status ?? '')
}

// ── Read-only detail ────────────────────────────────────────────
const viewed = ref<InvoiceDto | null>(null)

async function loadViewed(row: FinanceDocRow) {
  viewed.value = null
  try {
    viewed.value = await bridge.invoices.getById(String(row.id ?? ''))
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  }
}

const lineColumns: DataTableColumns<InvoiceDto['lines'][number]> = [
  { key: 'lineNumber', title: '#', width: 48 },
  { key: 'description', title: td('editor.description'), minWidth: 180, render: (row) => row.description ?? EMPTY_DASH },
  { key: 'quantity', title: td('editor.qty'), width: 90, render: (row) => amountCell(fmtAmount(row.quantity)) },
  { key: 'unitPrice', title: td('editor.price'), width: 110, render: (row) => amountCell(fmtAmount(row.unitPrice)) },
  { key: 'amount', title: td('editor.amount'), width: 130, render: (row) => amountCell(fmtAmount(row.amount)) },
]

// ── Line-editor overlay ─────────────────────────────────────────
const entryDetail = useDetail<InvoiceDto>({
  mode: 'modal',
  url: 'entry',
  loadData: (id) => bridge.invoices.getById(String(id)),
})

// `?party=<id>` hands the editor its party - a customer/vendor page's "new
// document" action already knows who, and making the operator pick again is
// how a context action degrades into a plain menu link.
const seedPartyId = computed(() => (typeof route.query.party === 'string' ? route.query.party : null))
const isEditing = computed(() => Boolean(entryDetail.data.value?.id))

const editingEntry = computed<EditableDocument | null>(() => {
  const d = entryDetail.data.value
  if (!d?.id) return seedPartyId.value ? { partyId: seedPartyId.value } : null
  return {
    id: d.id,
    partyId: d.customerId,
    paidFromAccountId: null,
    docDate: d.docDate,
    dueDate: d.dueDate,
    currency: d.currency,
    memo: d.memo,
    lines: d.lines,
  }
})
const editorTitle = computed(() => (isEditing.value ? t('editor.editTitle') : t('editor.createTitle')))

const editorSeq = ref(0)
watch(
  () => entryDetail.visible.value,
  (open) => {
    if (open) {
      editorSeq.value++
      void sources.ensureLeafAccounts()
      void sources.ensureItems()
      void sources.ensureTaxCodes()
      void sources.ensureCustomers()
    }
  },
  { immediate: true },
)

async function openCreate() {
  // Drop a consumed hand-off so the NEXT blank create is actually blank - a
  // `?party=` left in the URL would silently pre-fill every later document.
  // Awaited: `open()` writes its own `?entry=` off the current query, and would
  // otherwise carry the party straight back in.
  if (seedPartyId.value) {
    const { party: _party, ...rest } = route.query
    await router.replace({ query: rest })
  }
  await entryDetail.open('create')
}

function openEdit(row: FinanceDocRow) {
  void entryDetail.open('edit', String(row.id ?? ''))
}

async function saveEditor(payload: DocumentEditorPayload, post: boolean) {
  const data: CreateInvoiceDto = {
    customerId: payload.partyId!,
    docDate: payload.docDate,
    dueDate: payload.dueDate,
    currency: payload.currency,
    memo: payload.memo,
    lines: payload.lines.map((l) => ({
      itemId: l.itemId,
      description: l.description,
      accountId: l.accountId,
      quantity: l.quantity,
      unitPrice: l.unitPrice,
      taxCodeId: l.taxCodeId,
    })),
  }

  const wasCreate = !isEditing.value
  const saved = editingEntry.value?.id
    ? await bridge.invoices.updateDraft(editingEntry.value.id, data)
    : await bridge.invoices.createDraft(data)
  // 幂等：createDraft 成功后 hydrate 编辑器，post 失败重试走 update 而非再建孤儿草稿。
  if (wasCreate) await entryDetail.open('edit', saved)

  if (post) {
    await bridge.invoices.post(saved.id)
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
  post: (id) => bridge.invoices.post(id),
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
const isVoidable = (row: FinanceDocRow) => row.status === FinanceDocumentStatus.Posted || row.status === FinanceDocumentStatus.PartiallyPaid

/** 未清 = 已过账且还有余额没收回来。 */
const isOpen = (row: FinanceDocRow) =>
  (row.status === FinanceDocumentStatus.Posted || row.status === FinanceDocumentStatus.PartiallyPaid)
  && (row.total ?? 0) - (row.appliedTotal ?? 0) > 0

/**
 * 行内下游动作（标准 1「行内能直接发起下游动作，不用先进详情」）。
 *
 * 把客户交接给付款页并预置方向 —— 操作员看到一张未清发票时想做的下一件事就是
 * 收这笔钱，而不是"记住客户名 → 去付款页 → 新建 → 再选一遍客户"。
 */
function receivePayment(row: FinanceDocRow) {
  void router.push({
    name: 'finance.payments',
    query: { party: row.customerId, direction: 'Inbound' },
  })
}

const rowActions: RowAction<FinanceDocRow>[] = [
  // No View action: the row card itself opens the read-only detail.
  { key: 'edit', type: 'primary', show: (row) => can('finance.document.update') && isDraft(row), onClick: openEdit },
  { key: 'post', label: 'actions.post', type: 'primary', show: (row) => can('finance.document.update') && isDraft(row), confirm: 'confirmPost', onClick: (row) => void run(() => bridge.invoices.post(String(row.id ?? '')), 'postSuccess') },
  // 下游动作：未清发票 → 直接收款（把客户与方向一起交接过去）。
  { key: 'receive', label: 'actions.receivePayment', show: (row) => can('finance.document.create') && isOpen(row) && !!row.customerId, onClick: receivePayment },
  // Void only when nothing is applied - the backend rejects voiding a document
  // with settled payments (unapply them first).
  { key: 'void', label: 'actions.void', type: 'warning', show: (row) => can('finance.document.update') && isVoidable(row) && (row.appliedTotal ?? 0) === 0, confirm: 'confirmVoid', onClick: (row) => void run(() => bridge.invoices.voidDoc(String(row.id ?? '')), 'voidSuccess') },
  { key: 'delete', label: 'actions.delete', type: 'error', show: (row) => can('finance.document.delete') && isDraft(row), confirm: 'confirmDelete', onClick: (row) => void run(() => bridge.invoices.deleteDraft(String(row.id ?? '')), 'deleteSuccess') },
]
</script>

<style scoped>
.fin-doc-detail__meta {
  margin-bottom: 12px;
}
</style>
