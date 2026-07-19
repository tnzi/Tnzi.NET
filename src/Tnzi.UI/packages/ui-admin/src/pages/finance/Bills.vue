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
          <NDescriptionsItem :label="t('columns.docDate')">{{ formatDateOnly(viewed.docDate, { utc: true }) }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.currency')">{{ viewed.currency }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.total')">{{ fmtAmount(viewed.total) }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.applied')">{{ fmtAmount(viewed.appliedTotal) }}</NDescriptionsItem>
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
          <span class="fin-pay-run__cell" :data-label="t('pay.vendor')">{{ bill.vendorName ?? '—' }}</span>
          <span class="fin-pay-run__cell" :data-label="t('pay.dueDate')">{{ formatDateOnly(bill.dueDate, { utc: true, fallback: '—' }) }}</span>
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
import { computed, reactive, ref, watch } from 'vue'
import { NButton, NDatePicker, NDescriptions, NDescriptionsItem, NInput, NInputNumber, NSelect, type DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { formatDateOnly } from '@tnzi/core'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TDetailHost from '../../components/detail/TDetailHost.vue'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { useDetail } from '../../headless/useDetail'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { viewAction, type RowAction } from '../../headless/rowActions'
import { createFinanceBridge, FinanceDocumentStatus, PAYMENT_METHODS, SettlementDocType, type BillDto, type CreateBillDto } from '../../services/bridges/finance-bridge'
import { useAdminClient } from '../../plugin/client'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safeMessage'
import { buildDocumentColumns, DOC_STATUS_META, type FinanceDocRow } from './document-config'
import { createFinanceOptionSources } from './options'
import { amountCell, fmtAmount, tsToIsoDate } from './money'
import DocumentEditor, { type DocumentEditorPayload, type EditableDocument } from './components/DocumentEditor.vue'

const bridge = createFinanceBridge({ client: useAdminClient() })
const t = makePageTranslator('finance.bills')
// Shared document namespace for the read-only line-table headers + memo label.
const td = makePageTranslator('finance.docs')
const message = useSafeMessage()
const { can } = usePermissionGuard()
const sources = createFinanceOptionSources(bridge)

const columns = buildDocumentColumns(t, 'vendorName', { showApplied: true, showDueDate: true })

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
  { key: 'description', title: td('editor.description'), minWidth: 180, render: (row) => row.description ?? '—' },
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

const editingEntry = computed<EditableDocument | null>(() => {
  const d = entryDetail.data.value
  if (!d?.id) return null
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

  const wasCreate = !editingEntry.value?.id
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

const rowActions: RowAction<FinanceDocRow>[] = [
  viewAction(crud),
  { key: 'edit', type: 'primary', show: (row) => can('finance.document.update') && isDraft(row), onClick: openEdit },
  { key: 'post', label: 'actions.post', type: 'primary', show: (row) => can('finance.document.update') && isDraft(row), confirm: 'confirmPost', onClick: (row) => void run(() => bridge.bills.post(String(row.id ?? '')), 'postSuccess') },
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
