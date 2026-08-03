<template>
  <div class="t-recon">
    <!-- ── Progress header: the "N left" counter that makes this bearable ── -->
    <div class="t-recon__head">
      <div class="t-recon__counter">
        <span class="t-recon__count">{{ remaining }}</span>
        <span class="t-recon__count-label">{{ t('workspace.leftToReconcile') }}</span>
      </div>
      <NProgress
        v-if="totalKnown > 0"
        class="t-recon__bar"
        type="line"
        :percentage="progress"
        :height="6"
        :show-indicator="false"
        :status="remaining === 0 ? 'success' : 'default'"
      />
      <div class="t-recon__head-actions">
        <slot name="actions" />
      </div>
    </div>

    <!-- ── Three-state machine, QuickBooks vocabulary ────────────────── -->
    <NRadioGroup v-model:value="status" size="small" class="t-recon__status">
      <NRadioButton :value="BankTransactionStatus.Pending">
        {{ t('workspace.forReview') }}<span v-if="counts.pending !== null"> ({{ counts.pending }})</span>
      </NRadioButton>
      <NRadioButton :value="BankTransactionStatus.Matched">{{ t('workspace.categorized') }}</NRadioButton>
      <NRadioButton :value="BankTransactionStatus.Excluded">{{ t('workspace.excluded') }}</NRadioButton>
    </NRadioGroup>

    <!-- The confirm path writes a cleared line into an open reconciliation, so
         without one every OK would 400. Say so up front instead of letting the
         operator discover it on their first click. -->
    <NAlert v-if="needsDraft" type="warning" class="t-recon__alert" :bordered="false">
      <div class="t-recon__alert-body">
        <span>{{ t('workspace.needsDraft') }}</span>
        <NButton size="tiny" @click="emit('create-reconciliation')">{{ t('workspace.createDraft') }}</NButton>
      </div>
    </NAlert>

    <NAlert v-if="error" type="error" class="t-recon__alert" :bordered="false" closable @close="error = null">
      {{ error }}
    </NAlert>

    <!-- ── The stack ─────────────────────────────────────────────────── -->
    <NSpin :show="loading" class="t-recon__spin">
      <div v-if="status === BankTransactionStatus.Pending" class="t-recon__list">
        <TReconcileRow
          v-for="txn in rows"
          :key="txn.id"
          :txn="txn"
          :candidates="candidatesFor(txn.id)"
          :loading-candidates="candidateLoadingId === txn.id"
          :busy="busyId === txn.id"
          :expense-account-options="expenseAccountOptions"
          :funds-account-options="fundsAccountOptions"
          :customer-options="customerOptions"
          :vendor-options="vendorOptions"
          :t="t"
          @match="onMatch"
          @create="onCreate"
          @exclude="onExclude"
          @load-candidates="onLoadCandidates"
        />
        <TEmpty v-if="!loading && rows.length === 0" :text="t('workspace.allDone')" />
      </div>

      <!-- Settled lines are a ledger, not a workflow - a plain table is right. -->
      <TResponsiveTable
        v-else
        :columns="settledColumns"
        :data="rows"
        :row-key="(r: BankTransactionDto) => r.id"
        :row-actions="settledActions"
        :translate="t"
        size="small"
        mobile="scroll"
        :pagination="false"
        :bordered="false"
      />
    </NSpin>

    <div v-if="totalCount > pageSize" class="t-recon__pager">
      <NPagination
        :page="pageIndex"
        :page-size="pageSize"
        :item-count="totalCount"
        size="small"
        @update:page="goToPage"
      />
    </div>
  </div>
</template>

<script setup lang="ts">
/**
 * `TReconcileWorkspace` - bank reconciliation as a *flow*, not a table.
 *
 * Reconciling is where most of a bookkeeper's time goes, and the interaction
 * that makes it survivable is the one Xero settled on: one bank line per row,
 * the system's proposal beside it, a single confirm button always in the same
 * place, and a visible countdown so the work has an end. A filterable grid of
 * transactions technically exposes the same operations and is materially worse
 * to use fifty times in a row.
 *
 * The three states are QuickBooks' vocabulary on purpose - **For review /
 * Categorized / Excluded**. The third one is the load-bearing one: without a
 * way to throw out duplicates, personal spend and internal transfers, the
 * review queue never empties and people abandon the feature.
 */
import { EMPTY_DASH } from '../../utils/placeholders'
import { computed, h, ref, watch } from 'vue'
import {
  NAlert,
  NButton,
  NPagination,
  NProgress,
  NRadioButton,
  NRadioGroup,
  NSpin,
  type DataTableColumns,
  type SelectOption,
} from 'naive-ui'
import TReconcileRow from './TReconcileRow.vue'
import TMoney from './TMoney.vue'
import { TEmpty } from '@tnzi/ui'
import TResponsiveTable from '../data/TResponsiveTable.vue'
import { formatAccountingDate } from '../../utils/finance-format'
import type { RowAction } from '../../headless/row-actions'
import {
  BankTransactionStatus,
  type BankFeedDocType,
  type BankMatchCandidateDto,
  type BankTransactionDto,
  type FinanceBridge,
} from '../../services/bridges/finance-bridge'

const props = withDefaults(
  defineProps<{
    bridge: FinanceBridge
    /** Funds account whose feed we are reconciling. */
    accountId: string | null
    /** Whether the account has an open (Draft) reconciliation. */
    hasDraftReconciliation: boolean
    expenseAccountOptions: SelectOption[]
    fundsAccountOptions: SelectOption[]
    customerOptions: SelectOption[]
    vendorOptions: SelectOption[]
    /**
     * Gate write actions on `finance.bankFeed.update` / `finance.document.create`.
     *
     * Defaulted explicitly: Vue casts an ABSENT Boolean prop to `false`, so
     * leaving these undeclared would silently disable every action for callers
     * that never pass them (the framework's own permission gates are the
     * exception, not the rule).
     */
    canMatch?: boolean
    canCreateDocument?: boolean
    t: (key: string) => string
  }>(),
  { canMatch: true, canCreateDocument: true },
)

const emit = defineEmits<{
  'create-reconciliation': []
  /** A line settled - the host refreshes reconciliation totals / KPI tiles. */
  settled: [txn: BankTransactionDto]
}>()

const PAGE_SIZE = 20

const status = ref<BankTransactionStatus>(BankTransactionStatus.Pending)
const rows = ref<BankTransactionDto[]>([])
const totalCount = ref(0)
const pageIndex = ref(1)
const loading = ref(false)
const busyId = ref<string | null>(null)
const error = ref<string | null>(null)
const candidateLoadingId = ref<string | null>(null)
const candidateCache = ref<Record<string, BankMatchCandidateDto[]>>({})
/** Pending count, kept across tab switches so the counter never blanks. */
const pendingTotal = ref<number | null>(null)
/** Pending count when the operator arrived - the denominator of the progress bar. */
const sessionStart = ref(0)

const pageSize = PAGE_SIZE
const counts = computed(() => ({ pending: pendingTotal.value }))
const remaining = computed(() => pendingTotal.value ?? 0)
const totalKnown = computed(() => sessionStart.value)
const progress = computed(() => {
  if (sessionStart.value <= 0) return 100
  const done = Math.max(0, sessionStart.value - remaining.value)
  return Math.round((done / sessionStart.value) * 100)
})

const needsDraft = computed(
  () => Boolean(props.accountId) && !props.hasDraftReconciliation && status.value === BankTransactionStatus.Pending,
)

function candidatesFor(id: string): BankMatchCandidateDto[] {
  return candidateCache.value[id] ?? []
}

async function load() {
  if (!props.accountId) {
    rows.value = []
    totalCount.value = 0
    pendingTotal.value = null
    return
  }
  loading.value = true
  error.value = null
  try {
    const page = await props.bridge.bankFeed.transactions({
      pageIndex: pageIndex.value,
      pageSize: PAGE_SIZE,
      filters: { accountId: props.accountId, status: status.value },
    })
    rows.value = page.items
    totalCount.value = page.totalCount
    if (status.value === BankTransactionStatus.Pending) {
      pendingTotal.value = page.totalCount
      // Anchor the progress denominator on the first load for this account, so
      // the bar measures *this session's* work instead of resetting to 0% on
      // every refresh.
      if (sessionStart.value === 0) sessionStart.value = page.totalCount
    }
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
    rows.value = []
  } finally {
    loading.value = false
  }
}

watch(
  () => props.accountId,
  () => {
    pageIndex.value = 1
    sessionStart.value = 0
    pendingTotal.value = null
    candidateCache.value = {}
    void load()
  },
  { immediate: true },
)

watch(status, () => {
  pageIndex.value = 1
  void load()
})

function goToPage(next: number) {
  pageIndex.value = next
  void load()
}

/** Drop the settled row in place - a full reload would scroll the operator away. */
function settle(txn: BankTransactionDto) {
  rows.value = rows.value.filter((r) => r.id !== txn.id)
  totalCount.value = Math.max(0, totalCount.value - 1)
  if (pendingTotal.value !== null) pendingTotal.value = Math.max(0, pendingTotal.value - 1)
  emit('settled', txn)
  // Refill an emptied page so the operator is never left staring at a blank
  // list while later pages still hold work.
  if (rows.value.length === 0 && totalCount.value > 0) void load()
}

async function run(txn: BankTransactionDto, action: () => Promise<unknown>) {
  busyId.value = txn.id
  error.value = null
  try {
    await action()
    settle(txn)
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    busyId.value = null
  }
}

function onMatch(txn: BankTransactionDto, journalLineId: string) {
  if (!props.canMatch) return
  void run(txn, () => props.bridge.bankFeed.confirm(txn.id, { journalLineId }))
}

function onCreate(
  txn: BankTransactionDto,
  payload: { docType: BankFeedDocType; counterAccountId?: string; partyId?: string },
) {
  if (!props.canCreateDocument) return
  // `postAndMatch` is what makes Create a one-click action: draft → post →
  // confirm in a single call. Without it the operator would have to leave the
  // workspace, post the document, come back and re-run the matcher.
  void run(txn, () =>
    props.bridge.bankFeed.createDocument(txn.id, {
      docType: payload.docType,
      counterAccountId: payload.counterAccountId ?? null,
      partyId: payload.partyId ?? null,
      postAndMatch: true,
    }),
  )
}

function onExclude(txn: BankTransactionDto) {
  void run(txn, () => props.bridge.bankFeed.exclude(txn.id))
}

async function onLoadCandidates(txn: BankTransactionDto) {
  if (candidateCache.value[txn.id]) return
  candidateLoadingId.value = txn.id
  try {
    const list = await props.bridge.bankFeed.candidates(txn.id)
    candidateCache.value = { ...candidateCache.value, [txn.id]: list }
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    candidateLoadingId.value = null
  }
}

/**
 * Seed each pending row's candidate list from the engine's own suggestion so
 * the Match pane has something to render before the operator asks for
 * alternatives (the list endpoint returns the suggested id, not the line).
 */
watch(rows, async (list) => {
  if (status.value !== BankTransactionStatus.Pending) return
  for (const txn of list) {
    if (txn.suggestedJournalLineId && !candidateCache.value[txn.id]) {
      await onLoadCandidates(txn)
    }
  }
})

const settledColumns = computed<DataTableColumns<BankTransactionDto>>(() => [
  {
    key: 'txnDate',
    title: props.t('columns.date'),
    width: 120,
    render: (r) => formatAccountingDate(r.txnDate),
  },
  {
    key: 'payee',
    title: props.t('columns.payee'),
    minWidth: 200,
    render: (r) => r.payee || r.description || EMPTY_DASH,
  },
  {
    key: 'amount',
    title: props.t('columns.amount'),
    width: 140,
    align: 'right',
    render: (r) => h(TMoney, { value: r.amount, currency: r.currency }),
  },
])

const settledActions = computed<RowAction<BankTransactionDto>[]>(() => {
  if (status.value === BankTransactionStatus.Excluded) {
    return [
      {
        key: 'restore',
        label: 'row.restore',
        onClick: (r) => void run(r, () => props.bridge.bankFeed.restore(r.id)),
      },
    ]
  }
  return [
    {
      key: 'unmatch',
      label: 'row.unmatch',
      type: 'error',
      confirm: true,
      onClick: (r) => void run(r, () => props.bridge.bankFeed.unmatch(r.id)),
    },
  ]
})

defineExpose({ reload: load })
</script>

<style scoped>
.t-recon {
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-height: 0;
  /* Fill the page body so the list below can own the overflow. Without this the
     workspace grows past a fixed-height, overflow:hidden parent and the last
     rows become unreachable - no scrollbar anywhere. */
  flex: 1 1 auto;
  height: 100%;
}

.t-recon__head {
  display: flex;
  align-items: center;
  gap: 16px;
  flex-wrap: wrap;
}

.t-recon__counter {
  display: flex;
  align-items: baseline;
  gap: 8px;
}

.t-recon__count {
  font-size: 26px;
  font-weight: 700;
  line-height: 1;
  font-variant-numeric: tabular-nums;
  color: var(--tnzi-primary);
}

.t-recon__count-label {
  font-size: 13px;
  color: var(--tnzi-base-text-muted);
}

.t-recon__bar {
  flex: 1 1 160px;
  max-width: 320px;
}

.t-recon__head-actions {
  margin-left: auto;
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}

.t-recon__alert {
  margin: 0;
}

.t-recon__alert-body {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}

.t-recon__list {
  display: flex;
  flex-direction: column;
  gap: 8px;
  /* The scrolling region: the "N left" counter and the three-state tabs stay
     put while the operator runs down the stack. */
  flex: 1 1 auto;
  min-height: 0;
  overflow-y: auto;
  /* Room for the last row's focus ring / hover shadow against the edge. */
  padding: 2px;
  margin: -2px;
}

/* NSpin wraps the list in two plain block elements, which would break the flex
   chain and leave the list at its natural height (i.e. no scroll at all). */
.t-recon__spin,
.t-recon__spin :deep(.n-spin-content) {
  flex: 1 1 auto;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

.t-recon__pager {
  display: flex;
  justify-content: flex-end;
}

@media (max-width: 767px) {
  .t-recon__head-actions {
    margin-left: 0;
    width: 100%;
  }

  .t-recon__bar {
    max-width: none;
  }
}
</style>
