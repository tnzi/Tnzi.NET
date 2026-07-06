<template>
  <TCrudPage
    :state="crud"
    :all-columns="columns"
    :title="title"
    :row-actions="rowActions"
    :translate="t"
    :detail-width="640"
    :detail-title="detailTitle"
  >
    <template #primary>
      <NButton type="primary" tertiary size="small" @click="crud.openCreate">
        <template #icon>
          <TSvgIcon icon="mdi:plus" :size="16" />
        </template>
        {{ t('actions.create') }}
      </NButton>
    </template>

    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="paymentFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :field-renderers="fieldRenderers"
        :translate="t"
      />
    </template>

    <!-- Read-only detail: meta + settlement applications with unapply. -->
    <template #detail>
      <template v-if="viewed">
        <NDescriptions :column="2" size="small" bordered class="fin-pay-detail__meta">
          <NDescriptionsItem :label="t('columns.status')">{{ statusLabel(viewed.status) }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.direction')">{{ directionLabel(viewed.direction) }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.docDate')">{{ formatDateOnly(viewed.docDate, { utc: true }) }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.amount')">{{ fmtAmount(viewed.amount) }} {{ viewed.currency }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.applied')">{{ fmtAmount(viewed.appliedTotal) }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.reference')">{{ viewed.reference ?? '—' }}</NDescriptionsItem>
        </NDescriptions>
        <h4 class="fin-pay-detail__subtitle">{{ t('applications.title') }}</h4>
        <TResponsiveTable :columns="applicationColumns" :data="applications" :row-actions="applicationActions" :translate="t" :bordered="false" size="small" mobile="scroll" :pagination="false" />
      </template>
    </template>
  </TCrudPage>

  <!-- Apply panel — allocate a posted payment across open documents. -->
  <TDetailHost :state="applyDetail" :title="t('apply.title')" :width="680" :footer="false" :translate="t">
    <div class="fin-apply">
      <div v-if="openDocs.length === 0" class="fin-apply__empty">{{ t('apply.noOpenDocs') }}</div>
      <template v-else>
        <div class="fin-apply__row fin-apply__row--head">
          <span>{{ t('apply.document') }}</span>
          <span>{{ t('apply.dueDate') }}</span>
          <span>{{ t('apply.currency') }}</span>
          <span>{{ t('apply.outstanding') }}</span>
          <span>{{ t('apply.amount') }}</span>
        </div>
        <div v-for="doc in openDocs" :key="doc.docId" class="fin-apply__row">
          <span>{{ doc.number ?? doc.docId }}</span>
          <span>{{ formatDateOnly(doc.dueDate, { utc: true, fallback: '—' }) }}</span>
          <span :class="{ 'fin-apply__mismatch': !sameCurrency(doc) }">{{ doc.currency }}</span>
          <span class="fin-apply__num">{{ fmtAmount(doc.outstanding) }}</span>
          <!-- Different-currency documents can't be settled against this payment. -->
          <NInputNumber
            v-model:value="allocations[doc.docId]"
            size="small"
            :min="0"
            :max="doc.outstanding"
            :disabled="!sameCurrency(doc)"
            :show-button="false"
            :placeholder="sameCurrency(doc) ? '0.00' : t('apply.currencyMismatch')"
          />
        </div>
        <div class="fin-apply__footer">
          <span class="fin-apply__num">
            {{ t('apply.remaining') }}: <strong>{{ fmtAmount(applyRemaining) }}</strong>
          </span>
          <div class="fin-apply__actions">
            <NButton size="small" @click="applyDetail.close()">{{ t('editor.cancel') }}</NButton>
            <NButton size="small" type="primary" :loading="applying" :disabled="allocatedTotal <= 0 || allocatedTotal > applySource!.remaining" @click="submitApply">
              {{ t('apply.submit') }}
            </NButton>
          </div>
        </div>
      </template>
    </div>
  </TDetailHost>
</template>

<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { NButton, NDescriptions, NDescriptionsItem, NInputNumber, type DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { formatDateOnly } from '@tnzi/core'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TDetailHost from '../../components/detail/TDetailHost.vue'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { useDetail } from '../../headless/useDetail'
import { viewAction, type RowAction } from '../../headless/rowActions'
import {
  createFinanceBridge,
  FinanceDocumentStatus,
  FinancePartyType,
  PaymentDirection,
  SettlementDocType,
  type OpenDocumentDto,
  type PaymentApplicationDto,
  type PaymentEntryDto,
} from '../../services/bridges/finance-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer, { selectRenderer, type FormSchemaItem } from '../_shared/form-schema'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safeMessage'
import { buildDocumentColumns, DOC_STATUS_META, type FinanceDocRow } from './document-config'
import { createFinanceOptionSources } from './options'
import { amountCell, fmtAmount, tsToIsoDate } from './money'

const bridge = createFinanceBridge({ client: useAdminClient() })
const t = makePageTranslator('finance.payments')
const message = useSafeMessage()
const sources = createFinanceOptionSources(bridge)

const columns = buildDocumentColumns(t, 'partyName', { amountKey: 'amount', showApplied: true })

function toPayload(d: Record<string, unknown>) {
  const direction = (d.direction as PaymentDirection) ?? PaymentDirection.Inbound
  return {
    direction,
    partyType: direction === PaymentDirection.Inbound ? FinancePartyType.Customer : FinancePartyType.Vendor,
    partyId: String(d.partyId ?? ''),
    docDate: typeof d.docDate === 'number' ? tsToIsoDate(d.docDate) : String(d.docDate ?? ''),
    amount: Number(d.amount ?? 0),
    currency: typeof d.currency === 'string' && d.currency.trim() ? d.currency.trim().toUpperCase() : null,
    depositToAccountId: (d.depositToAccountId as string | null) || null,
    reference: (d.reference as string | null) || null,
    memo: (d.memo as string | null) || null,
  }
}

const crud = useCrudPage<FinanceDocRow>({
  pageId: 'finance.payments',
  columns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => bridge.payments.fetch(q),
  // Deep-link cold restore (?detail=view:<id> / edit:<id>) hydrates off-page rows.
  loadDetailById: (id) => bridge.payments.getById(id),
  createData: (d) => bridge.payments.createDraft(toPayload(d)),
  updateData: (id, d) => bridge.payments.updateDraft(String(id), toPayload(d)),
  deleteData: async (ids) => {
    for (const id of ids) await bridge.payments.deleteDraft(String(id))
  },
  onView: (row) => void loadViewed(row),
})

const title = 'tnzi.admin.modules.finance.payments.title'
const detailTitle = (d: FinanceDocRow) => d.number ?? t('detail.draftTitle')

function statusLabel(status?: FinanceDocumentStatus): string {
  const meta = DOC_STATUS_META[status ?? '']
  return meta ? t(meta.label) : String(status ?? '')
}

function directionLabel(direction?: PaymentDirection): string {
  return direction === PaymentDirection.Outbound ? t('direction.outbound') : t('direction.inbound')
}

// ── Form schema (draft create/edit through the standard modal) ──
const paymentFormSchema: FormSchemaItem[] = [
  {
    key: 'direction',
    labelKey: 'form.direction',
    label: 'Direction',
    type: 'select',
    required: true,
    options: [
      { label: 'Received', value: PaymentDirection.Inbound, labelKey: 'direction.inbound' },
      { label: 'Paid', value: PaymentDirection.Outbound, labelKey: 'direction.outbound' },
    ],
  },
  { key: 'partyId', labelKey: 'form.party', label: 'Party', type: 'finance-party', required: true },
  { key: 'docDate', labelKey: 'form.docDate', label: 'Date', type: 'date', required: true },
  { key: 'amount', labelKey: 'form.amount', label: 'Amount', type: 'number', required: true },
  { key: 'currency', labelKey: 'form.currency', label: 'Currency', type: 'text' },
  { key: 'depositToAccountId', labelKey: 'form.depositTo', label: 'Deposit / Pay From', type: 'finance-account' },
  { key: 'reference', labelKey: 'form.reference', label: 'Reference', type: 'text' },
  { key: 'memo', labelKey: 'form.memo', label: 'Memo', type: 'textarea' },
]

const fieldRenderers = {
  'finance-party': selectRenderer(() => {
    const model = crud.formModal.formData.value as Record<string, unknown> | null
    const direction = (model?.direction as PaymentDirection) ?? PaymentDirection.Inbound
    return direction === PaymentDirection.Outbound ? sources.vendorOptions.value : sources.customerOptions.value
  }, { placeholder: t('form.partyPlaceholder'), clearable: false }),
  'finance-account': selectRenderer(() => sources.leafAccountOptions.value, { placeholder: t('form.depositToPlaceholder') }),
}

watch(
  () => crud.formModal.visible.value,
  (open) => {
    if (open) {
      void sources.ensureCustomers()
      void sources.ensureVendors()
      void sources.ensureLeafAccounts()
    }
  },
  { immediate: true },
)

// ── Read-only detail + applications ─────────────────────────────
const viewed = ref<PaymentEntryDto | null>(null)
const applications = ref<PaymentApplicationDto[]>([])

async function loadViewed(row: FinanceDocRow) {
  viewed.value = null
  applications.value = []
  try {
    viewed.value = await bridge.payments.getById(String(row.id ?? ''))
    applications.value = await bridge.settlements.applications(SettlementDocType.PaymentEntry, String(row.id ?? ''))
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  }
}

async function unapply(applicationId: string) {
  try {
    await bridge.settlements.unapply(applicationId)
    message.success(t('applications.unapplied'))
    if (viewed.value) await loadViewed({ id: viewed.value.id })
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  }
}

const applicationColumns: DataTableColumns<PaymentApplicationDto> = [
  { key: 'targetNumber', title: t('applications.target'), minWidth: 140, render: (r) => r.targetNumber ?? r.targetId },
  { key: 'appliedAmount', title: t('applications.amount'), width: 120, render: (r) => amountCell(fmtAmount(r.appliedAmount)) },
]

// Declarative unapply (confirm) — TResponsiveTable synthesises the action
// column (TRowActions) instead of a hand-written NPopconfirm cell.
const applicationActions: RowAction<PaymentApplicationDto>[] = [
  { key: 'unapply', label: 'applications.unapply', type: 'warning', confirm: 'applications.confirmUnapply', onClick: (r) => void unapply(r.id) },
]

// ── Apply panel ─────────────────────────────────────────────────
interface ApplySource {
  id: string
  partyType: FinancePartyType
  partyId: string
  currency: string
  remaining: number
}

const applyDetail = useDetail<PaymentEntryDto>({
  mode: 'drawer',
  url: 'apply',
  loadData: (id) => bridge.payments.getById(String(id)),
})

const applySource = computed<ApplySource | null>(() => {
  const d = applyDetail.data.value
  if (!d?.id) return null
  return { id: d.id, partyType: d.partyType, partyId: d.partyId, currency: d.currency, remaining: d.amount - d.appliedTotal }
})

// The backend only settles same-currency documents against the payment.
function sameCurrency(doc: OpenDocumentDto): boolean {
  return doc.currency === applySource.value?.currency
}

const openDocs = ref<OpenDocumentDto[]>([])
const allocations = reactive<Record<string, number | null>>({})
const applying = ref(false)

watch(
  () => applySource.value?.id,
  async (id) => {
    openDocs.value = []
    Object.keys(allocations).forEach((k) => delete allocations[k])
    if (!id || !applySource.value) return
    try {
      openDocs.value = await bridge.settlements.openDocuments(applySource.value.partyType, applySource.value.partyId)
    } catch (error) {
      message.error(error instanceof Error ? error.message : String(error))
    }
  },
  { immediate: true },
)

const allocatedTotal = computed(() =>
  Object.values(allocations).reduce<number>((sum, v) => sum + (v ?? 0), 0))
const applyRemaining = computed(() => (applySource.value?.remaining ?? 0) - allocatedTotal.value)

async function submitApply() {
  if (!applySource.value) return
  const targets = openDocs.value
    .filter((d) => (allocations[d.docId] ?? 0) > 0 && sameCurrency(d))
    .map((d) => ({ targetType: d.docType, targetId: d.docId, amount: allocations[d.docId]! }))
  if (targets.length === 0) return

  applying.value = true
  try {
    await bridge.settlements.apply({
      sourceType: SettlementDocType.PaymentEntry,
      sourceId: applySource.value.id,
      targets,
    })
    message.success(t('apply.success'))
    applyDetail.close()
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    applying.value = false
  }
}

// ── Row actions ─────────────────────────────────────────────────
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
const isPosted = (row: FinanceDocRow) => row.status === FinanceDocumentStatus.Posted
const canApply = (row: FinanceDocRow) =>
  isPosted(row) && (row.amount ?? 0) - (row.appliedTotal ?? 0) > 0

const rowActions: RowAction<FinanceDocRow>[] = [
  viewAction(crud),
  { key: 'edit', type: 'primary', show: isDraft, onClick: (row) => crud.openEdit(row) },
  { key: 'post', label: 'actions.post', type: 'primary', show: isDraft, confirm: 'confirmPost', onClick: (row) => void run(() => bridge.payments.post(String(row.id ?? '')), 'postSuccess') },
  { key: 'apply', label: 'actions.apply', type: 'info', show: canApply, onClick: (row) => void applyDetail.open('edit', String(row.id ?? '')) },
  { key: 'void', label: 'actions.void', type: 'warning', show: (row) => isPosted(row) && (row.appliedTotal ?? 0) === 0, confirm: 'confirmVoid', onClick: (row) => void run(() => bridge.payments.voidDoc(String(row.id ?? '')), 'voidSuccess') },
  { key: 'delete', label: 'actions.delete', type: 'error', show: isDraft, confirm: 'confirmDelete', onClick: (row) => void run(() => bridge.payments.deleteDraft(String(row.id ?? '')), 'deleteSuccess') },
]
</script>

<style scoped>
.fin-pay-detail__meta {
  margin-bottom: 12px;
}

.fin-pay-detail__subtitle {
  margin: 0 0 8px;
  font-size: 13px;
  font-weight: 600;
}

.fin-apply {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.fin-apply__empty {
  color: var(--tnzi-text-secondary, rgba(0, 0, 0, 0.55));
  font-size: 13px;
  padding: 24px 0;
  text-align: center;
}

.fin-apply__row {
  display: grid;
  grid-template-columns: minmax(120px, 1fr) 100px 72px 110px 130px;
  gap: 8px;
  align-items: center;
}

.fin-apply__row--head {
  font-size: 12px;
  color: var(--tnzi-text-secondary, rgba(0, 0, 0, 0.55));
}

.fin-apply__num {
  font-variant-numeric: tabular-nums;
}

.fin-apply__mismatch {
  color: var(--tnzi-error, #d03050);
}

.fin-apply__footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-top: 8px;
  flex-wrap: wrap;
}

.fin-apply__actions {
  display: flex;
  gap: 8px;
}
</style>
