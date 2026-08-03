<template>
  <TCrudPage
    :state="crud"
    :all-columns="columns"
    :title="tp('title')"
    :title-help="tp('help')"
    :row-actions="rowActions"
    :translate="tp"
    :field-renderers="fieldRenderers"
    :form-schema="recurringFormSchema"
    row-key-field="id"
  >
    <template #toolbarRight>
      <NButton v-if="canRun" size="small" tertiary :loading="sweeping" @click="runDue">
        <template #icon><TSvgIcon icon="mdi:play-circle-outline" :size="16" /></template>
        {{ tp('actions.runDue') }}
      </NButton>
      <NButton size="small" tertiary @click="openHistory()">
        <template #icon><TSvgIcon icon="mdi:history" :size="16" /></template>
        {{ tp('actions.history') }}
      </NButton>
    </template>

    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="recurringFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :field-renderers="fieldRenderers"
        :translate="tp"
        :columns="2"
      />
      <!-- Anchor 31 x quarterly x February is not something anyone works out in
           their head, and getting it wrong bills the customer on the wrong day.
           So the dates are shown before the template is saved, not after. -->
      <div class="fin-rec__preview flex flex-col gap-8px mt-12px p-[10px_12px]">
        <div class="fin-rec__previewhead flex items-center justify-between text-12px uppercase text-muted">
          <span>{{ tp('preview.title') }}</span>
          <NButton size="tiny" tertiary :loading="previewing" @click="previewSchedule(formData)">
            {{ tp('preview.refresh') }}
          </NButton>
        </div>
        <div v-if="previewDates.length" class="flex flex-wrap gap-6px">
          <NTag v-for="d in previewDates" :key="d" size="small" :bordered="false">{{ fmtDate(d) }}</NTag>
        </div>
        <span v-else class="text-12px text-muted">{{ tp('preview.hint') }}</span>
      </div>
    </template>
  </TCrudPage>

  <!-- Generation history: the only place that answers "did it actually run". -->
  <TDetailHost :state="history" mode="drawer" :title="tp('history.title')" :width="720" :footer="false">
    <template #default>
      <div class="fin-rec__runs">
        <NSpin :show="runsLoading">
          <TEmpty v-if="!runsLoading && runs.length === 0" :text="tp('history.empty')" />
          <TResponsiveTable
            v-else
            :columns="runColumns"
            :data="runs"
            :row-key="(row: RecurringRunDto) => row.id"
            :pagination="false"
            :bordered="false"
            size="small"
            mobile="cards"
          />
        </NSpin>
      </div>
    </template>
  </TDetailHost>
</template>

<script setup lang="ts">
/**
 * Recurring document templates.
 *
 * A template is not a document: it has no number, it never touches the ledger,
 * and its amount is only an estimate at today's prices. What it does is create
 * a real invoice / bill / expense on a calendar - by default a **draft**,
 * because letting a calendar post straight into the ledger is the kind of
 * mistake nobody finds until month end.
 */
import { EMPTY_DASH } from '../../utils/placeholders'
import { computed, h, ref } from 'vue'
import { NButton, NSpin, NTag, type DataTableColumns } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TDetailHost from '../../components/detail/TDetailHost.vue'
import TResponsiveTable from '../../components/data/TResponsiveTable.vue'
import { TEmpty } from '@tnzi/ui'
import TFormSchemaRenderer, { selectRenderer, type FieldRenderer } from '../_shared/form-schema'
import { useCrudPage } from '../../headless/useCrudPage'
import { useDetail } from '../../headless/useDetail'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { editAction, deleteAction, type RowAction } from '../../headless/row-actions'
import { makePageTranslator } from '../_shared/translate'
import { useAdminClient } from '../../plugin/client'
import { useSafeMessage } from '../_shared/safe-message'
import { fmtDate } from './money'

import { createFinanceOptionSources } from './options'
import RecurringLinesEditor from './components/RecurringLinesEditor'
import { buildRecurringColumns, recurringFormSchema, type RecurringRow } from './recurring-config'
import {
  createFinanceBridge,
  type RecurringRunDto,
} from '../../services/bridges/finance-bridge'

const bridge = createFinanceBridge({ client: useAdminClient() })
const tp = makePageTranslator('finance.recurring')
const message = useSafeMessage()
const { can } = usePermissionGuard()

const sources = createFinanceOptionSources(bridge)
const columns = computed(() => buildRecurringColumns(tp))
const canRun = computed(() => can('finance.recurring.execute'))

// selectRenderer 没有"打开时再取"的钩子，页面挂载即预热这几张选项表。
void sources.ensureCustomers()
void sources.ensureVendors()
void sources.ensureFundsAccounts()
void sources.ensureLeafAccounts()
void sources.ensureTaxCodes()

const crud = useCrudPage<RecurringRow>({
  pageId: 'finance.recurring',
  columns: columns.value,
  rowKey: (r) => String(r.id ?? ''),
  permission: 'finance.recurring',
  fetchData: (q) => bridge.recurring.fetch(q),
  createData: (d) => bridge.recurring.create(toCreate(d)),
  updateData: (id, d) => bridge.recurring.update(String(id), toUpdate(d)),
  deleteData: (ids) => bridge.recurring.delete(ids.map(String)),
})

// ── Schedule preview ────────────────────────────────────────

const previewDates = ref<string[]>([])
const previewing = ref(false)

async function previewSchedule(formData: unknown) {
  const model = (formData ?? {}) as Record<string, unknown>
  if (!model.startDate || !model.frequency) {
    previewDates.value = []
    return
  }
  previewing.value = true
  try {
    const result = await bridge.recurring.previewSchedule(toCreate(model), 6)
    previewDates.value = result.dates ?? []
  } catch {
    // A failed preview must not block saving - it is an aid, not a gate.
    previewDates.value = []
  } finally {
    previewing.value = false
  }
}

// ── Generation history ──────────────────────────────────────

const history = useDetail({ mode: 'drawer', url: 'runs' })
const runs = ref<RecurringRunDto[]>([])
const runsLoading = ref(false)

const RUN_STATUS_TYPE: Record<string, 'success' | 'warning' | 'error'> = {
  Generated: 'success',
  Skipped: 'warning',
  Failed: 'error',
}

const runColumns = computed<DataTableColumns<RecurringRunDto>>(() => [
  { title: tp('history.period'), key: 'periodDate', width: 120, render: (r) => fmtDate(r.periodDate) },
  { title: tp('history.template'), key: 'recurringDocumentName', minWidth: 160 },
  {
    title: tp('history.status'),
    key: 'status',
    width: 110,
    render: (r) =>
      h(
        NTag,
        { size: 'small', bordered: false, type: RUN_STATUS_TYPE[r.status] ?? 'default' },
        { default: () => tp(`runStatus.${r.status.charAt(0).toLowerCase()}${r.status.slice(1)}`) },
      ),
  },
  {
    title: tp('history.document'),
    key: 'docNumber',
    minWidth: 140,
    // A draft has no number yet; say "draft" rather than showing a blank cell.
    render: (r) => r.docNumber || (r.docId ? tp('history.draft') : EMPTY_DASH),
  },
  {
    title: tp('history.detail'),
    key: 'failReason',
    minWidth: 200,
    render: (r) => r.failReason ?? EMPTY_DASH,
  },
])

async function openHistory(templateId?: string) {
  runs.value = []
  runsLoading.value = true
  await history.open('view', templateId ?? 'all')
  try {
    const page = await bridge.recurring.runs({
      pageIndex: 1,
      pageSize: 50,
      recurringDocumentId: templateId,
    } as never)
    runs.value = page.items ?? []
  } finally {
    runsLoading.value = false
  }
}

// ── Lifecycle actions ───────────────────────────────────────

const sweeping = ref(false)

async function runDue() {
  sweeping.value = true
  try {
    const result = await bridge.recurring.runDue()
    message.success(
      tp('actions.sweepDone')
        .replace('{generated}', String(result.generated))
        .replace('{skipped}', String(result.skipped))
        .replace('{failed}', String(result.failed)),
    )
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : tp('actions.sweepFailed'))
  } finally {
    sweeping.value = false
  }
}

async function transition(row: RecurringRow, action: 'pause' | 'resume' | 'end') {
  if (!row.id) return
  try {
    await bridge.recurring[action](row.id)
    await crud.refresh()
  } catch (error) {
    message.error(error instanceof Error ? error.message : tp('actions.failed'))
  }
}

const rowActions = computed((): RowAction<RecurringRow>[] => [
  editAction(crud),
  {
    key: 'run',
    label: 'finance.recurring.actions.runNow',
    show: (row) => canRun.value && row.status === 'Active',
    onClick: async (row) => {
      if (!row.id) return
      try {
        const result = await bridge.recurring.run(row.id)
        message.success(
          result.generated > 0
            ? tp('actions.ranOne').replace('{n}', String(result.generated))
            : tp('actions.nothingDue'),
        )
        await crud.refresh()
      } catch (error) {
        message.error(error instanceof Error ? error.message : tp('actions.failed'))
      }
    },
  },
  {
    key: 'pause',
    label: 'finance.recurring.actions.pause',
    show: (row) => crud.canUpdate && row.status === 'Active',
    onClick: (row) => transition(row, 'pause'),
  },
  {
    key: 'resume',
    label: 'finance.recurring.actions.resume',
    show: (row) => crud.canUpdate && row.status === 'Paused',
    onClick: (row) => transition(row, 'resume'),
  },
  {
    key: 'history',
    label: 'finance.recurring.actions.history',
    onClick: (row) => openHistory(row.id),
  },
  {
    key: 'end',
    label: 'finance.recurring.actions.end',
    type: 'warning',
    confirm: 'finance.recurring.actions.endConfirm',
    show: (row) => crud.canUpdate && row.status !== 'Ended',
    onClick: (row) => transition(row, 'end'),
  },
  // Deleting is only legal before anything has been generated; the backend
  // returns 409 afterwards so the origin of those documents stays traceable.
  deleteAction(crud),
])

// ── Field renderers ─────────────────────────────────────────

const fieldRenderers: Record<string, FieldRenderer> = {
  'recurring-kind': selectRenderer(() => [
    { label: tp('kind.invoice'), value: 'Invoice' },
    { label: tp('kind.bill'), value: 'Bill' },
    { label: tp('kind.expense'), value: 'Expense' },
  ]),
  'recurring-frequency': selectRenderer(() => [
    { label: tp('frequency.daily'), value: 'Daily' },
    { label: tp('frequency.weekly'), value: 'Weekly' },
    { label: tp('frequency.monthly'), value: 'Monthly' },
    { label: tp('frequency.quarterly'), value: 'Quarterly' },
    { label: tp('frequency.yearly'), value: 'Yearly' },
  ]),
  'recurring-autopost': selectRenderer(() => [
    { label: tp('autoPost.inherit'), value: '' },
    { label: tp('autoPost.yes'), value: 'true' },
    { label: tp('autoPost.no'), value: 'false' },
  ]),
  // Customers bill; vendors get billed. The party list follows the kind so the
  // form cannot offer a customer for a vendor bill.
  'recurring-party': (ctx) => {
    // FieldRenderContext 不带 model，联动字段读表单数据（同 Payments 的方向联动先例）。
    const kind = String((crud.formModal.formData.value as Record<string, unknown> | null)?.kind ?? 'Invoice')
    const renderer =
      kind === 'Invoice'
        ? selectRenderer(() => sources.customerOptions.value)
        : selectRenderer(() => sources.vendorOptions.value)
    return renderer(ctx)
  },
  'finance-account': selectRenderer(() => sources.fundsAccountOptions.value),
  'recurring-lines': (ctx) =>
    h(RecurringLinesEditor, {
      ctx,
      model: (crud.formModal.formData.value ?? {}) as Record<string, unknown>,
      accountOptions: sources.leafAccountOptions.value,
      taxOptions: sources.taxCodeOptions.value,
      translate: tp,
    }),
}

// ── Payload mapping ─────────────────────────────────────────

/** The form keeps three-state auto-post as a string so the select can hold `null`. */
function normalizeAutoPost(value: unknown): boolean | null {
  if (value === true || value === 'true') return true
  if (value === false || value === 'false') return false
  return null
}

function toCreate(model: Record<string, unknown> | unknown) {
  const m = (model ?? {}) as Record<string, unknown>
  return {
    name: String(m.name ?? ''),
    kind: (m.kind ?? 'Invoice') as 'Invoice' | 'Bill' | 'Expense',
    partyId: String(m.partyId ?? ''),
    paidFromAccountId: (m.paidFromAccountId as string | null) ?? null,
    currency: (m.currency as string | null) ?? null,
    memo: (m.memo as string | null) ?? null,
    frequency: (m.frequency ?? 'Monthly') as 'Daily' | 'Weekly' | 'Monthly' | 'Quarterly' | 'Yearly',
    interval: Number(m.interval ?? 1) || 1,
    anchorDay: m.anchorDay == null || m.anchorDay === '' ? null : Number(m.anchorDay),
    startDate: String(m.startDate ?? ''),
    endDate: (m.endDate as string | null) || null,
    maxOccurrences: m.maxOccurrences == null || m.maxOccurrences === '' ? null : Number(m.maxOccurrences),
    dueDays: m.dueDays == null || m.dueDays === '' ? null : Number(m.dueDays),
    autoPost: normalizeAutoPost(m.autoPost),
    lines: ((m.lines ?? []) as Array<Record<string, unknown>>).map((l) => ({
      itemId: (l.itemId as string | null) ?? null,
      description: (l.description as string | null) ?? null,
      accountId: (l.accountId as string | null) ?? null,
      quantity: Number(l.quantity ?? 1) || 1,
      unitPrice: Number(l.unitPrice ?? 0) || 0,
      taxCodeId: (l.taxCodeId as string | null) ?? null,
    })),
  }
}

function toUpdate(model: Record<string, unknown> | unknown) {
  const m = (model ?? {}) as Record<string, unknown>
  const { kind: _kind, ...rest } = toCreate(m)
  return { ...rest, concurrencyStamp: String(m.concurrencyStamp ?? '') }
}
</script>

<style scoped>
/* 语义化 BEM：虚线框把"还没保存的推演"与已保存内容区分开。 */
.fin-rec__preview {
  border: 1px dashed var(--tnzi-border, #efeff5);
  border-radius: 6px;
}

/* unocss 无 tracking-* 原子类（实测不生成规则），按 house 做法留 scoped。 */
.fin-rec__previewhead { letter-spacing: 0.04em; }

.fin-rec__runs { flex: 1 1 auto; min-height: 0; }

/* 行编辑器是 h() 渲染的子树，unocss 够不到——布局已在 h() 里用原子类，
   这里只留列宽比例与手机端塌单列。 */
:deep(.fin-rec-lines__row) {
  display: flex;
  align-items: center;
  gap: 6px;
}

:deep(.fin-rec-lines__desc) { flex: 2 1 160px; }
:deep(.fin-rec-lines__acct) { flex: 2 1 180px; }
:deep(.fin-rec-lines__qty) { flex: 0 0 90px; }
:deep(.fin-rec-lines__price) { flex: 0 0 120px; }
:deep(.fin-rec-lines__tax) { flex: 1 1 120px; }

:deep(.fin-rec-lines__total) { font-variant-numeric: tabular-nums lining-nums; }

/* 手机(<md)：固定列宽的行会横向溢出——塌成「标签: 值」单列，
   规范 §5.5 的行编辑器铁律（参考 SettlementApplyPanel / JournalEntryEditor）。 */
@media (max-width: 767px) {
  :deep(.fin-rec-lines__row) {
    flex-direction: column;
    align-items: stretch;
    gap: 4px;
    padding: 8px 0;
    border-bottom: 1px solid var(--tnzi-border, #efeff5);
  }

  :deep(.fin-rec-lines__cell) { flex: 1 1 auto; }

  :deep(.fin-rec-lines__cell[data-label])::before {
    content: attr(data-label);
    display: block;
    margin-bottom: 2px;
    font-size: 12px;
    color: var(--tnzi-text-3, #999);
  }
}
</style>
