<template>
  <TCrudPage
    :state="crud"
    :all-columns="columns"
    :title="title"
    :search-fields="searchFields"
    :row-actions="rowActions"
    :translate="t"
    :detail-width="640"
    :detail-title="detailTitle"
  >
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
      <NButton v-if="crud.canCreate" type="primary" tertiary size="small" @click="crud.openCreate">
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
          <NDescriptionsItem :label="t('columns.docDate')">{{ fmtDate(viewed.docDate) }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.amount')">{{ fmtAmount(viewed.amount) }} {{ viewed.currency }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.applied')">{{ fmtAmount(viewed.appliedTotal) }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('form.paymentMethod')">{{ methodLabel(viewed.paymentMethod) }}</NDescriptionsItem>
          <NDescriptionsItem :label="t('columns.reference')">{{ viewed.reference ?? EMPTY_DASH }}</NDescriptionsItem>
        </NDescriptions>
        <h4 class="fin-pay-detail__subtitle">{{ t('applications.title') }}</h4>
        <TResponsiveTable :columns="applicationColumns" :data="applications" :row-actions="applicationActions" :translate="t" :bordered="false" size="small" mobile="scroll" :pagination="false" />
      </template>
    </template>
  </TCrudPage>

  <!-- Apply panel - allocate a posted payment across open documents (shared with Credit Memos). -->
  <TDetailHost :state="applyDetail" :title="t('apply.title')" :width="680" :footer="false" :translate="t">
    <SettlementApplyPanel :bridge="bridge" :source="applySource" @applied="onApplied" @cancel="applyDetail.close()" />
  </TDetailHost>
</template>

<script setup lang="ts">
import { EMPTY_DASH } from '../../utils/placeholders'
import { computed, h, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NButton, NPopconfirm, NDescriptions, NDescriptionsItem, type DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'

import TCrudPage from '../../components/crud/TCrudPage.vue'
import TDetailHost from '../../components/detail/TDetailHost.vue'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import TPartySelect from '../../components/finance/TPartySelect.vue'
import SettlementApplyPanel, { type SettlementApplySource } from './components/SettlementApplyPanel.vue'
import { useCrudPage } from '../../headless/useCrudPage'
import { useDocumentBatch } from './useDocumentBatch'
import { useDetail } from '../../headless/useDetail'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { viewAction, type RowAction } from '../../headless/row-actions'
import {
  createFinanceBridge,
  FinanceDocumentStatus,
  FinancePartyType,
  PAYMENT_METHODS,
  PaymentDirection,
  SettlementDocType,
  type PaymentApplicationDto,
  type PaymentEntryDto,
} from '../../services/bridges/finance-bridge'
import { useAdminClient } from '../../plugin/client'
import TFormSchemaRenderer, { selectRenderer, type FormSchemaItem } from '../_shared/form-schema'
import { makePageTranslator } from '../_shared/translate'
import { useSafeMessage } from '../_shared/safe-message'
import { buildDocumentColumns, buildDocumentSearchFields, DOC_STATUS_META, type FinanceDocRow } from './document-config'
import { createFinanceOptionSources } from './options'
import { amountCell, fmtAmount, tsToIsoDate, fmtDate } from './money'

const bridge = createFinanceBridge({ client: useAdminClient() })
const route = useRoute()
const router = useRouter()
const t = makePageTranslator('finance.payments')
// Shared document namespace (payment-method labels shared with the expense editor).
const td = makePageTranslator('finance.docs')
const message = useSafeMessage()
const { can } = usePermissionGuard()
const sources = createFinanceOptionSources(bridge)

const columns = buildDocumentColumns(t, 'partyName', { amountKey: 'amount', showApplied: true })

// 真实筛选（标准 1）：状态 / 单据日期区间 / 往来方。往来方走 render 逃生口挂
// TPartySelect —— 它是异步远程搜索，form-schema 的静态 select 承载不了。
const searchFields = buildDocumentSearchFields(td, {
  renderParty: (model) =>
    h(TPartySelect, {
      modelValue: (model.partyId as string) ?? null,
      bridge,
      kind: 'auto',
      size: 'small',
      'onUpdate:modelValue': (v: string | null) => (model.partyId = v ?? undefined),
    }),
})

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
    paymentMethod: (d.paymentMethod as string | null) || null,
    reference: (d.reference as string | null) || null,
    memo: (d.memo as string | null) || null,
  }
}

const crud = useCrudPage<FinanceDocRow>({
  pageId: 'finance.payments',
  permission: 'finance.document',
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

// Hand-off from a customer / vendor work surface:
// `?party=<id>&direction=Inbound|Outbound` opens a pre-filled draft. Without
// this the "Receive payment" action on a customer page would drop the operator
// on a blank form that already knew the answer to its first two questions.
onMounted(async () => {
  const party = route.query.party
  const direction = route.query.direction
  if (typeof party !== 'string' || !party) return
  const dir = direction === 'Outbound' ? PaymentDirection.Outbound : PaymentDirection.Inbound
  // Consume the hand-off BEFORE opening: `openCreate` writes its own `?detail=`
  // and would carry the stale party/direction back into the URL, re-opening
  // this form on every later visit to the page.
  const { party: _p, direction: _d, ...rest } = route.query
  await router.replace({ query: rest })
  crud.openCreate({ direction: dir, partyId: party } as Partial<FinanceDocRow>)
})

function statusLabel(status?: FinanceDocumentStatus): string {
  const meta = DOC_STATUS_META[status ?? '']
  return meta ? t(meta.label) : String(status ?? '')
}

function directionLabel(direction?: PaymentDirection): string {
  return direction === PaymentDirection.Outbound ? t('direction.outbound') : t('direction.inbound')
}

function methodLabel(method?: string | null): string {
  if (!method) return EMPTY_DASH
  const known = PAYMENT_METHODS.find((m) => m === method)
  return known ? td(`method.${known.charAt(0).toLowerCase()}${known.slice(1)}`) : method
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
  { key: 'paymentMethod', labelKey: 'form.paymentMethod', label: 'Payment Method', type: 'payment-method' },
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
  'payment-method': selectRenderer(() => methodOptions.value, { placeholder: t('form.paymentMethodPlaceholder'), clearable: true }),
}

const methodOptions = computed(() =>
  PAYMENT_METHODS.map((m) => ({ label: td(`method.${m.charAt(0).toLowerCase()}${m.slice(1)}`), value: m })))

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

// Party options swap between customers/vendors by direction; clear a now-wrong-type selection
// so toPayload never derives partyType=Vendor while partyId still points at a customer.
watch(
  () => (crud.formModal.formData.value as Record<string, unknown> | null)?.direction,
  (dir, prev) => {
    if (prev === undefined || dir === prev) return
    const model = crud.formModal.formData.value as Record<string, unknown> | null
    if (model) model.partyId = null
  },
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

// Declarative unapply (confirm) - TResponsiveTable synthesises the action
// column (TRowActions) instead of a hand-written NPopconfirm cell.
const applicationActions: RowAction<PaymentApplicationDto>[] = [
  { key: 'unapply', label: 'applications.unapply', type: 'warning', show: () => can('finance.document.update'), confirm: 'applications.confirmUnapply', onClick: (r) => void unapply(r.id) },
]

// ── Apply panel (shared SettlementApplyPanel) ───────────────────
const applyDetail = useDetail<PaymentEntryDto>({
  mode: 'drawer',
  url: 'apply',
  loadData: (id) => bridge.payments.getById(String(id)),
})

const applySource = computed<SettlementApplySource | null>(() => {
  const d = applyDetail.data.value
  if (!d?.id) return null
  return {
    id: d.id,
    sourceType: SettlementDocType.PaymentEntry,
    partyType: d.partyType,
    partyId: d.partyId,
    currency: d.currency,
    remaining: d.amount - d.appliedTotal,
  }
})

async function onApplied() {
  applyDetail.close()
  await crud.refresh()
}

// ── Row actions ─────────────────────────────────────────────────
// 批量过账：只处理草稿、串行、部分失败逐条报出（见 useDocumentBatch）。
const batch = useDocumentBatch({
  items: crud.items,
  post: (id) => bridge.payments.post(id),
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
const isPosted = (row: FinanceDocRow) => row.status === FinanceDocumentStatus.Posted
const canApply = (row: FinanceDocRow) =>
  isPosted(row) && (row.amount ?? 0) - (row.appliedTotal ?? 0) > 0

const rowActions: RowAction<FinanceDocRow>[] = [
  viewAction(crud),
  { key: 'edit', type: 'primary', show: (row) => crud.canUpdate && isDraft(row), onClick: (row) => crud.openEdit(row) },
  { key: 'post', label: 'actions.post', type: 'primary', show: (row) => can('finance.document.update') && isDraft(row), confirm: 'confirmPost', onClick: (row) => void run(() => bridge.payments.post(String(row.id ?? '')), 'postSuccess') },
  { key: 'apply', label: 'actions.apply', type: 'info', show: (row) => can('finance.document.update') && canApply(row), onClick: (row) => void applyDetail.open('edit', String(row.id ?? '')) },
  { key: 'void', label: 'actions.void', type: 'warning', show: (row) => can('finance.document.update') && isPosted(row) && (row.appliedTotal ?? 0) === 0, confirm: 'confirmVoid', onClick: (row) => void run(() => bridge.payments.voidDoc(String(row.id ?? '')), 'voidSuccess') },
  { key: 'delete', label: 'actions.delete', type: 'error', show: (row) => crud.canDelete && isDraft(row), confirm: 'confirmDelete', onClick: (row) => void run(() => bridge.payments.deleteDraft(String(row.id ?? '')), 'deleteSuccess') },
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
</style>
