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
      <NButton v-if="can(`${permissionBase}.create`)" type="primary" tertiary size="small" @click="openCreate">
        <template #icon><TSvgIcon icon="mdi:plus" :size="16" /></template>
        {{ t('actions.create') }}
      </NButton>
    </template>

    <!-- Read-only detail (deep-linkable ?detail=view:<id>). -->
    <template #detail>
      <template v-if="viewed">
        <NDescriptions :column="2" size="small" bordered class="fin-offer-detail__meta">
          <NDescriptionsItem :label="t('columns.status')">
            <component :is="statusCell(viewed.status)" />
          </NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.docDate')">{{ fmtDate(viewed.docDate) }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.party')">{{ partyName(viewed) ?? EMPTY_DASH }}</NDescriptionsItem>
          <NDescriptionsItem :label="secondDateLabel">
            {{ secondDate(viewed) ? fmtDate(secondDate(viewed)) : EMPTY_DASH }}
          </NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.currency')">{{ viewed.currency }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.total')">{{ fmtMoney(viewed.total, viewed.currency) }}</NDescriptionsItem>
          <NDescriptionsItem v-if="kind === 'purchaseOrder' && viewed.shipTo" :label="t('form.shipTo')" :span="2">
            {{ viewed.shipTo }}
          </NDescriptionsItem>
          <NDescriptionsItem :label="td('editor.memo')" :span="2">{{ viewed.memo ?? EMPTY_DASH }}</NDescriptionsItem>
          <NDescriptionsItem v-if="viewed.internalNote" :label="t('form.internalNote')" :span="2">
            {{ viewed.internalNote }}
          </NDescriptionsItem>
        </NDescriptions>

        <!-- Where it went. A converted document without a way back to what it
             became is a dead end for whoever is trying to reconcile the two. -->
        <NAlert v-if="viewed.convertedToDocId" type="success" :bordered="false" class="fin-offer-detail__converted">
          {{ t('converted.banner') }}
          <NButton text type="primary" size="small" @click="openTarget(viewed)">{{ t('converted.open') }}</NButton>
        </NAlert>

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

  <!-- Line editor (create / edit) - useDetail + TDetailHost (?entry=new / edit:<id>). -->
  <TDetailHost :state="entryDetail" :title="editorTitle" :width="920" :footer="false" :translate="t">
    <DocumentEditor
      :key="editorSeq"
      :kind="kind === 'estimate' ? 'sales' : 'expense'"
      :entry="editingEntry"
      :party-label="t('form.party')"
      :party-options="partyOptions"
      :party-optional="false"
      :show-due-date="true"
      :due-date-label="secondDateLabel"
      :primary-label="t('actions.saveAndSend')"
      :account-options="sources.leafAccountOptions.value"
      :item-options="sources.itemOptions.value"
      :tax-code-options="sources.taxCodeOptions.value"
      :on-save="saveEditor"
      @cancel="entryDetail.close()"
    />
  </TDetailHost>

  <!-- Convert: the target document's date is a decision, so it gets asked. -->
  <TModalShell :show="convertShow" :title="t('convert.title')" :width="440" @update:show="(v: boolean) => (convertShow = v)">
    <div class="fin-offer-convert">
      <p class="fin-offer-convert__hint">{{ t('convert.hint') }}</p>
      <div class="fin-offer-convert__field">
        <span class="fin-offer-convert__label">{{ td('editor.docDate') }}</span>
        <NDatePicker v-model:value="convertDocDate" type="date" size="small" style="width: 100%" />
      </div>
      <div class="fin-offer-convert__field">
        <span class="fin-offer-convert__label">{{ td('editor.dueDate') }}</span>
        <NDatePicker v-model:value="convertDueDate" type="date" size="small" clearable style="width: 100%" />
      </div>
    </div>
    <template #footer>
      <NButton size="small" @click="convertShow = false">{{ td('editor.cancel') }}</NButton>
      <NButton size="small" type="primary" :loading="converting" @click="runConvert">{{ t('convert.confirm') }}</NButton>
    </template>
  </TModalShell>
</template>

<script setup lang="ts">
/**
 * Shared page for the two non-posting documents (estimates / purchase orders).
 *
 * They are the same document aimed in opposite directions, so they share one
 * page: everything that differs is derived from `kind`. The lifecycle actions
 * are business verbs - Send, Accept, Decline, Close, Convert - because these
 * documents never reach the ledger and borrowing Post/Void would suggest they do.
 */
import { EMPTY_DASH } from '../../../utils/placeholders'
import { computed, h, onMounted, ref, watch, type Component, type VNode } from 'vue'
import { useRouter } from 'vue-router'
import { NAlert, NButton, NDatePicker, NDescriptions, NDescriptionsItem, type DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TCrudPage from '../../../components/crud/TCrudPage.vue'
import TDetailHost from '../../../components/detail/TDetailHost.vue'
import TModalShell from '../../../components/overlay/TModalShell.vue'
import TResponsiveTable from '../../../components/data/TResponsiveTable.vue'
import DocumentEditor, { type DocumentEditorPayload, type EditableDocument } from './DocumentEditor.vue'
import { useCrudPage } from '../../../headless/useCrudPage'
import { useDetail } from '../../../headless/useDetail'
import { usePermissionGuard } from '../../../headless/usePermissionGuard'
import { editAction, deleteAction, type RowAction } from '../../../headless/rowActions'
import { useSafeMessage } from '../../_shared/safeMessage'
import { makePageTranslator } from '../../_shared/translate'
import { useAdminClient } from '../../../plugin/client'
import { createFinanceOptionSources } from '../options'
import { buildOfferColumns, offerStatusCell, type OfferRow } from '../offer-config'
import { fmtDate, fmtMoney, moneyCell, tsToIsoDate, isoDateToLocalTs } from '../money'
import {
  createFinanceBridge,
  FinanceOfferStatus,
  type ConvertOfferResultDto,
  type CreateEstimateDto,
  type CreatePurchaseOrderDto,
  type EstimateDto,
  type PurchaseOrderDto,
} from '../../../services/bridges/finance-bridge'

const props = defineProps<{ kind: 'estimate' | 'purchaseOrder' }>()

type OfferDto = EstimateDto & PurchaseOrderDto

const bridge = createFinanceBridge({ client: useAdminClient() })
const router = useRouter()
const message = useSafeMessage()
const { can } = usePermissionGuard()
const sources = createFinanceOptionSources(bridge)

const isEstimate = computed(() => props.kind === 'estimate')
const ns = computed(() => (isEstimate.value ? 'finance.estimates' : 'finance.purchaseOrders'))
const t = (key: string) => makePageTranslator(ns.value)(key)
const td = makePageTranslator('finance.docs')

const permissionBase = computed(() => (isEstimate.value ? 'finance.estimate' : 'finance.purchaseOrder'))
const api = computed(() => (isEstimate.value ? bridge.estimates : bridge.purchaseOrders))
const title = computed(() => (isEstimate.value ? 'tnzi.admin.modules.finance.estimates.title' : 'tnzi.admin.modules.finance.purchaseOrders.title'))
const secondDateLabel = computed(() => (isEstimate.value ? t('columns.expiryDate') : t('columns.expectedDate')))
const partyOptions = computed(() => (isEstimate.value ? sources.customerOptions.value : sources.vendorOptions.value))

const columns = computed(() => buildOfferColumns(t, props.kind, (row) => crud.openView(row)))
const viewed = ref<OfferDto | null>(null)

function partyName(row: OfferRow): string | null | undefined {
  return isEstimate.value ? row.customerName : row.vendorName
}

function secondDate(row: OfferRow): string | null | undefined {
  return isEstimate.value ? row.expiryDate : row.expectedDate
}

function statusCell(status?: FinanceOfferStatus): Component {
  return { render: () => offerStatusCell(status, t) }
}

const lineColumns = computed<DataTableColumns<Record<string, unknown>>>(() => [
  { key: 'lineNumber', title: '#', width: 50 },
  { key: 'description', title: td('editor.description'), minWidth: 180, render: (r) => (r.description as string) ?? EMPTY_DASH },
  { key: 'quantity', title: td('editor.qty'), width: 80, align: 'right' },
  { key: 'unitPrice', title: td('editor.price'), width: 110, align: 'right', render: (r) => moneyCell(r.unitPrice as number, viewed.value?.currency) },
  { key: 'amount', title: td('editor.amount'), width: 120, align: 'right', render: (r) => moneyCell(r.amount as number, viewed.value?.currency, true) },
])

const crud = useCrudPage<OfferRow>({
  pageId: computed(() => ns.value).value,
  permission: permissionBase.value,
  columns: columns.value,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => api.value.fetch(q),
  loadDetailById: (id) => api.value.getById(String(id)) as Promise<OfferRow | null>,
  deleteData: async (ids) => {
    for (const id of ids) await api.value.deleteDraft(String(id))
  },
  onView: (row) => void loadViewed(row),
})

const detailTitle = (d: OfferRow) => d.number ?? t('detail.draftTitle')

async function loadViewed(row: OfferRow) {
  viewed.value = null
  if (!row.id) return
  viewed.value = (await api.value.getById(String(row.id))) as OfferDto | null
}

// ── Line editor ─────────────────────────────────────────────────
const entryDetail = useDetail<OfferDto>({
  mode: 'modal',
  url: 'entry',
  loadData: (id) => api.value.getById(String(id)) as Promise<OfferDto | null>,
})

const isEditing = computed(() => Boolean(entryDetail.data.value?.id))
const editorTitle = computed(() => (isEditing.value ? t('editor.editTitle') : t('editor.createTitle')))

const editingEntry = computed<EditableDocument | null>(() => {
  const d = entryDetail.data.value
  if (!d?.id) return null
  return {
    id: d.id,
    partyId: isEstimate.value ? d.customerId : d.vendorId,
    paidFromAccountId: null,
    docDate: d.docDate,
    dueDate: isEstimate.value ? d.expiryDate : d.expectedDate,
    currency: d.currency,
    memo: d.memo,
    lines: d.lines,
  }
})

const editorSeq = ref(0)
watch(
  () => entryDetail.visible.value,
  (open) => {
    if (!open) return
    editorSeq.value++
    void sources.ensureLeafAccounts()
    void sources.ensureItems()
    void sources.ensureTaxCodes()
    if (isEstimate.value) void sources.ensureCustomers()
    else void sources.ensureVendors()
  },
  { immediate: true },
)

async function openCreate() {
  await entryDetail.open('create')
}

/**
 * `post` here means "send", not "post to the ledger" - the editor's primary
 * button is relabelled accordingly.
 */
async function saveEditor(payload: DocumentEditorPayload, send: boolean) {
  const lines = payload.lines.map((l) => ({
    itemId: l.itemId,
    description: l.description,
    accountId: l.accountId,
    quantity: l.quantity,
    unitPrice: l.unitPrice,
    taxCodeId: l.taxCodeId,
  }))

  const data = isEstimate.value
    ? ({
        customerId: payload.partyId!,
        docDate: payload.docDate,
        expiryDate: payload.dueDate,
        currency: payload.currency,
        memo: payload.memo,
        lines,
      } as CreateEstimateDto)
    : ({
        vendorId: payload.partyId!,
        docDate: payload.docDate,
        expectedDate: payload.dueDate,
        currency: payload.currency,
        memo: payload.memo,
        lines,
      } as CreatePurchaseOrderDto)

  const existing = entryDetail.data.value?.id
  // Idempotent: once created, a failed send retries as an update rather than
  // creating a second orphan draft.
  const saved = existing
    ? await api.value.update(existing, data as never)
    : await api.value.createDraft(data as never)
  if (!existing) await entryDetail.open('edit', saved.id)

  if (send) {
    await api.value.send(saved.id)
    message.success(t('sentSuccess'))
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
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  }
}

// ── Convert ─────────────────────────────────────────────────────
const convertShow = ref(false)
const convertTarget = ref<OfferRow | null>(null)
const convertDocDate = ref<number | null>(null)
const convertDueDate = ref<number | null>(null)
const converting = ref(false)

function openConvert(row: OfferRow) {
  convertTarget.value = row
  convertDocDate.value = Date.now()
  convertDueDate.value = null
  convertShow.value = true
}

async function runConvert() {
  if (!convertTarget.value?.id) return
  converting.value = true
  try {
    const result = await api.value.convert(String(convertTarget.value.id), {
      docDate: convertDocDate.value ? tsToIsoDate(convertDocDate.value) : null,
      dueDate: convertDueDate.value ? tsToIsoDate(convertDueDate.value) : null,
    })
    convertShow.value = false
    message.success(t('convert.success'))
    await crud.refresh()
    openConverted(result)
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e))
  } finally {
    converting.value = false
  }
}

/** Land the operator on the draft that was just created, not back on a list. */
function openConverted(result: ConvertOfferResultDto) {
  void router.push({
    name: isEstimate.value ? 'finance.invoices' : 'finance.bills',
    query: { detail: `view:${result.docId}` },
  })
}

function openTarget(row: OfferRow) {
  if (!row.convertedToDocId) return
  void router.push({
    name: isEstimate.value ? 'finance.invoices' : 'finance.bills',
    query: { detail: `view:${row.convertedToDocId}` },
  })
}

const rowActions = computed<RowAction<OfferRow>[]>(() => [
  {
    key: 'send',
    label: 'actions.send',
    show: (row) => row.status === FinanceOfferStatus.Draft || row.status === FinanceOfferStatus.Declined,
    disabled: () => !can(`${permissionBase.value}.update`),
    onClick: (row) => void run(() => api.value.send(String(row.id)), 'sentSuccess'),
  },
  {
    key: 'accept',
    label: 'actions.accept',
    show: (row) => row.status === FinanceOfferStatus.Sent,
    disabled: () => !can(`${permissionBase.value}.update`),
    onClick: (row) => void run(() => api.value.accept(String(row.id)), 'acceptedSuccess'),
  },
  {
    key: 'convert',
    label: 'actions.convert',
    type: 'primary',
    show: (row) => row.status === FinanceOfferStatus.Sent || row.status === FinanceOfferStatus.Accepted,
    disabled: () => !can('finance.document.create'),
    onClick: (row) => openConvert(row),
  },
  {
    key: 'decline',
    label: 'actions.decline',
    show: (row) => row.status === FinanceOfferStatus.Sent || row.status === FinanceOfferStatus.Accepted,
    disabled: () => !can(`${permissionBase.value}.update`),
    confirm: 'actions.declineConfirm',
    onClick: (row) => void run(() => api.value.decline(String(row.id)), 'declinedSuccess'),
  },
  {
    key: 'close',
    label: 'actions.close',
    show: (row) =>
      row.status === FinanceOfferStatus.Sent ||
      row.status === FinanceOfferStatus.Accepted ||
      row.status === FinanceOfferStatus.Declined,
    disabled: () => !can(`${permissionBase.value}.update`),
    confirm: 'actions.closeConfirm',
    onClick: (row) => void run(() => api.value.close(String(row.id)), 'closedSuccess'),
  },
  {
    ...editAction(crud),
    show: (row) => row.status !== FinanceOfferStatus.Converted && row.status !== FinanceOfferStatus.Closed,
    onClick: (row) => void entryDetail.open('edit', String(row.id ?? '')),
  },
  // Only a draft can be deleted: once sent, the other side holds a copy of that
  // number, and deleting it would leave a gap nobody can explain.
  { ...deleteAction(crud), show: (row) => row.status === FinanceOfferStatus.Draft },
])

onMounted(() => {
  if (isEstimate.value) void sources.ensureCustomers()
  else void sources.ensureVendors()
})
</script>

<style scoped>
.fin-offer-detail__meta {
  margin-bottom: 12px;
}

.fin-offer-detail__converted {
  margin-bottom: 12px;
}

.fin-offer-convert {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.fin-offer-convert__hint {
  margin: 0;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}

.fin-offer-convert__field {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.fin-offer-convert__label {
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}
</style>
