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
        icon="mdi:file-document-outline"
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
    </template>    <!-- 概览区（标准 2）：这一页的钱现在是什么状态，四个数字都可下钻到账龄报表。 -->
    <template #kpis>
      <DocumentKpiStrip :bridge="bridge" kind="ap" :translate="td" />
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
      <NButton v-if="can('finance.document.create')" tertiary size="small" @click="payDetail.open('create')">
        <template #icon>
          <TSvgIcon icon="mdi:cash-multiple" :size="16" />
        </template>
        {{ t('pay.title') }}
      </NButton>
    </template>

    <!-- Read-only detail (deep-linkable ?detail=view:<id>). `onView` lazy-loads lines. -->
    <template #detail>
      <template v-if="viewed">
        <NDescriptions :column="2" size="small" bordered class="fin-doc-detail__meta">
          <NDescriptionsItem :label="t('columns.status')">{{ statusLabel(viewed.status) }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.docDate')">{{ fmtDate(viewed.docDate) }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.currency')">{{ viewed.currency }}</NDescriptionsItem>
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

        <DocumentCollaborationPanel v-if="viewed.id" :doc-type="FinanceDocToken.Bill" :doc-id="String(viewed.id)" />
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
      :party-options="sources.vendorOptions.value"
      :party-optional="false"
      :show-due-date="true"
      :account-options="sources.leafAccountOptions.value"
      :item-options="sources.itemOptions.value"
      :tax-code-options="sources.taxCodeOptions.value"
      :on-save="saveEditor"
      @cancel="entryDetail.close()"
    />
  </TDetailHost>

  <!-- Pay Bills - batch settlement over open bills; backend posts one payment
       per (vendor, currency) group and applies it atomically (?pay=new). -->
  <TDetailHost :state="payDetail" :title="t('pay.title')" :width="860" :footer="false" :translate="t">
    <div class="fin-pay-run">
      <div class="fin-pay-run__header">
        <div class="fin-pay-run__field">
          <span class="fin-pay-run__label">{{ t('pay.fundsAccount') }}</span>
          <NSelect v-model:value="payForm.fundsAccountId" :options="sources.leafAccountOptions.value" size="small" filterable :placeholder="t('pay.fundsAccountPlaceholder')" />
        </div>
        <div class="fin-pay-run__field">
          <span class="fin-pay-run__label">{{ td('editor.paymentMethod') }}</span>
          <NSelect v-model:value="payForm.paymentMethod" :options="methodOptions" size="small" filterable clearable tag :placeholder="td('editor.paymentMethodPlaceholder')" />
        </div>
        <div class="fin-pay-run__field">
          <span class="fin-pay-run__label">{{ td('editor.docDate') }}</span>
          <NDatePicker v-model:value="payForm.docDateTs" type="date" size="small" style="width: 100%" />
        </div>
        <div class="fin-pay-run__field">
          <span class="fin-pay-run__label">{{ t('pay.reference') }}</span>
          <NInput v-model:value="payForm.reference" size="small" :placeholder="t('pay.referencePlaceholder')" />
        </div>
      </div>

      <div v-if="openBills.length === 0" class="fin-pay-run__empty">{{ t('pay.noOpenBills') }}</div>
      <template v-else>
        <div class="fin-pay-run__row fin-pay-run__row--head">
          <span>{{ t('pay.bill') }}</span>
          <span>{{ t('pay.vendor') }}</span>
          <span>{{ t('pay.dueDate') }}</span>
          <span>{{ t('pay.outstanding') }}</span>
          <span class="fin-pay-run__amount-head">
            {{ t('pay.amount') }}
            <NButton text size="tiny" type="primary" @click="fillAll">{{ t('pay.fillAll') }}</NButton>
          </span>
        </div>
        <div v-for="bill in openBills" :key="bill.id" class="fin-pay-run__row">
          <span class="fin-pay-run__cell" :data-label="t('pay.bill')">{{ bill.number ?? bill.id }}</span>
          <span class="fin-pay-run__cell" :data-label="t('pay.vendor')">{{ bill.vendorName ?? EMPTY_DASH }}</span>
          <span class="fin-pay-run__cell" :data-label="t('pay.dueDate')">{{ fmtDate(bill.dueDate, { fallback: EMPTY_DASH }) }}</span>
          <span class="fin-pay-run__cell fin-pay-run__num" :data-label="t('pay.outstanding')">{{ fmtAmount(bill.outstanding) }} {{ bill.currency }}</span>
          <NInputNumber v-model:value="payAllocations[bill.id]" size="small" :min="0" :max="bill.outstanding" :show-button="false" placeholder="0.00" />
        </div>
        <div class="fin-pay-run__footer">
          <span class="fin-pay-run__num">{{ t('pay.total') }}: <strong>{{ fmtAmount(payTotal) }}</strong></span>
          <div class="fin-pay-run__actions">
            <NButton size="small" @click="payDetail.close()">{{ td('editor.cancel') }}</NButton>
            <NButton size="small" type="primary" :loading="paying" :disabled="payTotal <= 0 || !payForm.fundsAccountId" @click="submitPay">
              {{ t('pay.submit') }}
            </NButton>
          </div>
        </div>
      </template>
    </div>
  </TDetailHost>
</template>

<script setup lang="ts">
import { EMPTY_DASH } from '../../utils/placeholders'
import { computed, h, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NButton, NPopconfirm, NDatePicker, NDescriptions, NDescriptionsItem, NInput, NInputNumber, NSelect, type DataTableColumns } from 'naive-ui'
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
import { createFinanceBridge, FinanceDocumentStatus, PAYMENT_METHODS, SettlementDocType, type BillDto, type CreateBillDto } from '../../services/bridges/finance-bridge'
import { useAdminClient } from '../../plugin/client'
import { makePageTranslator } from '../_shared/translate'
import DocumentCollaborationPanel from './components/DocumentCollaborationPanel.vue'
import { FinanceDocToken } from './source-type'
import { useSafeMessage } from '../_shared/safeMessage'
import { buildDocumentColumns, buildDocumentSearchFields, DOC_STATUS_META, type FinanceDocRow } from './document-config'
import { createFinanceOptionSources } from './options'
import { amountCell, fmtAmount, tsToIsoDate, fmtDate } from './money'
import DocumentEditor, { type DocumentEditorPayload, type EditableDocument } from './components/DocumentEditor.vue'

const bridge = createFinanceBridge({ client: useAdminClient() })
const route = useRoute()
const router = useRouter()
const t = makePageTranslator('finance.bills')
// Shared document namespace for the read-only line-table headers + memo label.
const td = makePageTranslator('finance.docs')
const message = useSafeMessage()
const { can } = usePermissionGuard()
const sources = createFinanceOptionSources(bridge)

const columns = buildDocumentColumns(t, 'vendorName', { showApplied: true, showDueDate: true })

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
  pageId: 'finance.bills',
  permission: 'finance.document',
  columns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => bridge.bills.fetch(q),
  // Deep-link cold restore (?detail=view:<id>) hydrates even off-page rows.
  loadDetailById: (id) => bridge.bills.getById(id),
  onView: (row) => void loadViewed(row),
})

const title = 'tnzi.admin.modules.finance.bills.title'
const detailTitle = (d: FinanceDocRow) => d.number ?? t('detail.draftTitle')

function statusLabel(status?: FinanceDocumentStatus): string {
  const meta = DOC_STATUS_META[status ?? '']
  return meta ? t(meta.label) : String(status ?? '')
}

// ── Read-only detail ────────────────────────────────────────────
const viewed = ref<BillDto | null>(null)

async function loadViewed(row: FinanceDocRow) {
  viewed.value = null
  try {
    viewed.value = await bridge.bills.getById(String(row.id ?? ''))
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  }
}

const lineColumns: DataTableColumns<BillDto['lines'][number]> = [
  { key: 'lineNumber', title: '#', width: 48 },
  { key: 'description', title: td('editor.description'), minWidth: 180, render: (row) => row.description ?? EMPTY_DASH },
  { key: 'quantity', title: td('editor.qty'), width: 90, render: (row) => amountCell(fmtAmount(row.quantity)) },
  { key: 'unitPrice', title: td('editor.price'), width: 110, render: (row) => amountCell(fmtAmount(row.unitPrice)) },
  { key: 'amount', title: td('editor.amount'), width: 130, render: (row) => amountCell(fmtAmount(row.amount)) },
]

// ── Line-editor overlay ─────────────────────────────────────────
const entryDetail = useDetail<BillDto>({
  mode: 'modal',
  url: 'entry',
  loadData: (id) => bridge.bills.getById(String(id)),
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
    partyId: d.vendorId,
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
      void sources.ensureVendors()
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
  const data: CreateBillDto = {
    vendorId: payload.partyId!,
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
    ? await bridge.bills.updateDraft(editingEntry.value.id, data)
    : await bridge.bills.createDraft(data)
  // 幂等：createDraft 成功后 hydrate 编辑器，post 失败重试走 update 而非再建孤儿草稿。
  if (wasCreate) await entryDetail.open('edit', saved)

  if (post) {
    await bridge.bills.post(saved.id)
    message.success(t('postSuccess'))
  } else {
    message.success(t('savedSuccess'))
  }

  entryDetail.close()
  await crud.refresh()
}

// ── Pay Bills (batch settlement) ────────────────────────────────
interface OpenBillRow {
  id: string
  number: string | null
  vendorName: string | null
  dueDate: string | null
  currency: string
  outstanding: number
}

const payDetail = useDetail<Record<string, never>>({ mode: 'modal', url: 'pay' })

/**
 * 行内「付款」发起时记住是哪一张账单——模态加载完未清列表后把它预先分配满额。
 *
 * 不预置的话，从某一行点「付款」跟从工具栏点「Pay Bills」没有区别：操作员还得
 * 在一屏未清账单里把刚才那张再找一遍，行内动作就白做了。
 */
const payFocusBillId = ref<string | null>(null)
const openBills = ref<OpenBillRow[]>([])
const payAllocations = reactive<Record<string, number | null>>({})
const paying = ref(false)
const payForm = reactive({
  fundsAccountId: null as string | null,
  paymentMethod: null as string | null,
  docDateTs: Date.now(),
  reference: '',
})

const methodOptions = computed(() =>
  PAYMENT_METHODS.map((m) => ({ label: td(`method.${m.charAt(0).toLowerCase()}${m.slice(1)}`), value: m })))

const payTotal = computed(() =>
  Object.values(payAllocations).reduce<number>((sum, v) => sum + (v ?? 0), 0))

watch(
  () => payDetail.visible.value,
  async (open) => {
    if (!open) return
    void sources.ensureLeafAccounts()
    payForm.docDateTs = Date.now()
    Object.keys(payAllocations).forEach((k) => delete payAllocations[k])
    openBills.value = []
    try {
      // Open bills = Posted or PartiallyPaid with outstanding > 0
      // (two status-filtered pages; admin-scale cap of 100 each).
      const [posted, partial] = await Promise.all([
        bridge.bills.fetch({ pageIndex: 1, pageSize: 100, filters: { status: FinanceDocumentStatus.Posted } }),
        bridge.bills.fetch({ pageIndex: 1, pageSize: 100, filters: { status: FinanceDocumentStatus.PartiallyPaid } }),
      ])
      openBills.value = [...posted.items, ...partial.items]
        .map((b) => ({
          id: String(b.id),
          number: b.number ?? null,
          vendorName: b.vendorName ?? null,
          dueDate: b.dueDate ?? null,
          currency: b.currency,
          outstanding: (b.total ?? 0) - (b.appliedTotal ?? 0),
        }))
        .filter((b) => b.outstanding > 0)
        .sort((a, b) => ((a.dueDate ?? '9999') < (b.dueDate ?? '9999') ? -1 : 1))

      // 从某一行发起的付款：把那张账单预先分配满额，其余留空。
      const focus = payFocusBillId.value
      if (focus) {
        const target = openBills.value.find((b) => b.id === focus)
        if (target) payAllocations[focus] = target.outstanding
        payFocusBillId.value = null
      }
    } catch (error) {
      message.error(error instanceof Error ? error.message : String(error))
    }
  },
  { immediate: true },
)

function fillAll() {
  openBills.value.forEach((b) => {
    payAllocations[b.id] = b.outstanding
  })
}

async function submitPay() {
  const targets = openBills.value
    .filter((b) => (payAllocations[b.id] ?? 0) > 0)
    .map((b) => ({ docType: SettlementDocType.Bill, docId: b.id, amount: payAllocations[b.id]! }))
  if (targets.length === 0) return

  paying.value = true
  try {
    const result = await bridge.settlements.pay({
      docDate: tsToIsoDate(payForm.docDateTs),
      fundsAccountId: payForm.fundsAccountId,
      paymentMethod: payForm.paymentMethod,
      reference: payForm.reference.trim() || null,
      memo: null,
      targets,
    })
    message.success(t('pay.success', { count: result.payments.length }))
    payDetail.close()
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    paying.value = false
  }
}

// ── Lifecycle actions ───────────────────────────────────────────
// 批量过账：只处理草稿、串行、部分失败逐条报出（见 useDocumentBatch）。
const batch = useDocumentBatch({
  items: crud.items,
  post: (id) => bridge.bills.post(id),
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

/** 未清 = 已过账且还有余额没付出去。 */
const isOpen = (row: FinanceDocRow) =>
  (row.status === FinanceDocumentStatus.Posted || row.status === FinanceDocumentStatus.PartiallyPaid)
  && (row.total ?? 0) - (row.appliedTotal ?? 0) > 0

/** 行内下游动作（标准 1）：这张账单直接去付，模态里它已被选中并填好金额。 */
function payBill(row: FinanceDocRow) {
  payFocusBillId.value = String(row.id ?? '')
  void payDetail.open('create')
}

const rowActions: RowAction<FinanceDocRow>[] = [
  // No View action: the row card itself opens the read-only detail.
  { key: 'edit', type: 'primary', show: (row) => can('finance.document.update') && isDraft(row), onClick: openEdit },
  { key: 'post', label: 'actions.post', type: 'primary', show: (row) => can('finance.document.update') && isDraft(row), confirm: 'confirmPost', onClick: (row) => void run(() => bridge.bills.post(String(row.id ?? '')), 'postSuccess') },
  // 下游动作：未清账单 → 直接付款（模态里这张已预选并填满）。
  { key: 'pay', label: 'actions.payBill', show: (row) => can('finance.document.create') && isOpen(row), onClick: payBill },
  { key: 'void', label: 'actions.void', type: 'warning', show: (row) => can('finance.document.update') && isVoidable(row) && (row.appliedTotal ?? 0) === 0, confirm: 'confirmVoid', onClick: (row) => void run(() => bridge.bills.voidDoc(String(row.id ?? '')), 'voidSuccess') },
  { key: 'delete', label: 'actions.delete', type: 'error', show: (row) => can('finance.document.delete') && isDraft(row), confirm: 'confirmDelete', onClick: (row) => void run(() => bridge.bills.deleteDraft(String(row.id ?? '')), 'deleteSuccess') },
]
</script>

<style scoped>
.fin-doc-detail__meta {
  margin-bottom: 12px;
}

.fin-pay-run {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.fin-pay-run__header {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 12px;
}

.fin-pay-run__field {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.fin-pay-run__label {
  font-size: 12px;
  color: var(--tnzi-text-secondary, rgba(0, 0, 0, 0.55));
}

.fin-pay-run__empty {
  color: var(--tnzi-text-secondary, rgba(0, 0, 0, 0.55));
  font-size: 13px;
  padding: 24px 0;
  text-align: center;
}

.fin-pay-run__row {
  display: grid;
  grid-template-columns: minmax(110px, 1fr) minmax(120px, 1fr) 100px 130px 130px;
  gap: 8px;
  align-items: center;
}

.fin-pay-run__row--head {
  font-size: 12px;
  color: var(--tnzi-text-secondary, rgba(0, 0, 0, 0.55));
}

.fin-pay-run__amount-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 4px;
}

.fin-pay-run__num {
  font-variant-numeric: tabular-nums;
}

.fin-pay-run__footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-top: 8px;
  flex-wrap: wrap;
}

.fin-pay-run__actions {
  display: flex;
  gap: 8px;
}

.fin-pay-run__cell[data-label]::before {
  content: none;
}

/* Phone (<md): the fixed 5-column allocation grid overflows the fullscreen modal - stack each
   row to label: value single-column so the panel never scrolls horizontally (content-page iron-law). */
@media (max-width: 767px) {
  .fin-pay-run__header {
    grid-template-columns: 1fr;
  }
  .fin-pay-run__row--head {
    display: none;
  }
  .fin-pay-run__row {
    grid-template-columns: 1fr;
    gap: 4px;
    padding: 8px 0;
    border-bottom: 1px solid var(--tnzi-border, rgba(0, 0, 0, 0.08));
  }
  .fin-pay-run__cell[data-label]::before {
    content: attr(data-label) ': ';
    color: var(--tnzi-text-secondary, rgba(0, 0, 0, 0.55));
    font-size: 12px;
  }
}
</style>
