<template>
  <div class="t-recrow" :class="{ 't-recrow--busy': busy }">
    <!-- ── Left: what the bank says. Read-only fact. ─────────────── -->
    <div class="t-recrow__bank">
      <div class="t-recrow__date">{{ formatAccountingDate(txn.txnDate) }}</div>
      <div class="t-recrow__payee" :title="payee">{{ payee }}</div>
      <div v-if="txn.reference" class="t-recrow__ref">{{ txn.reference }}</div>
      <TMoney class="t-recrow__amount" :value="txn.amount" :currency="txn.currency" strong :label="t('row.bankAmount')" />
    </div>

    <!-- ── Middle: the one action that matters. ──────────────────── -->
    <div class="t-recrow__go">
      <NButton
        type="primary"
        size="medium"
        class="t-recrow__ok"
        :loading="busy"
        :disabled="!canConfirm"
        @click="confirm"
      >
        {{ t('row.ok') }}
      </NButton>
      <NButton quaternary size="tiny" :disabled="busy" @click="emit('exclude', txn)">
        {{ t('row.exclude') }}
      </NButton>
    </div>

    <!-- ── Right: what we propose to do about it. ────────────────── -->
    <div class="t-recrow__action">
      <NTabs v-model:value="tab" type="segment" size="small" class="t-recrow__tabs">
        <NTab name="match" :disabled="!hasSuggestion && candidateCount === 0">{{ t('row.tabMatch') }}</NTab>
        <NTab name="create">{{ t('row.tabCreate') }}</NTab>
      </NTabs>

      <!-- Match: the engine's proposal, or a manual pick. -->
      <div v-if="tab === 'match'" class="t-recrow__pane">
        <template v-if="chosen">
          <div class="t-recrow__match">
            <div class="t-recrow__match-main">
              <span class="t-recrow__entry">{{ chosen.entryNumber || t('row.unnumbered') }}</span>
              <span class="t-recrow__match-date">{{ formatAccountingDate(chosen.postingDate) }}</span>
            </div>
            <div v-if="chosen.memo" class="t-recrow__memo" :title="chosen.memo">{{ chosen.memo }}</div>
          </div>
          <div class="t-recrow__match-side">
            <TMoney :value="chosen.amount" :currency="txn.currency" />
            <NTag v-if="confidenceLabel" size="tiny" :type="confidenceType" :bordered="false">
              {{ confidenceLabel }}
            </NTag>
          </div>
        </template>
        <p v-else class="t-recrow__hint">{{ t('row.noSuggestion') }}</p>

        <NButton text size="tiny" type="primary" :loading="loadingCandidates" @click="pickAnother">
          {{ t('row.findMatch') }}
        </NButton>
      </div>

      <!-- Create: a new document standing in for this bank line. -->
      <div v-else class="t-recrow__pane t-recrow__pane--create">
        <div class="t-recrow__controls">
          <NSelect
            v-model:value="docType"
            :options="docTypeOptions"
            size="small"
            class="t-recrow__field"
            :aria-label="t('row.docType')"
          />
          <NSelect
            v-if="docType === BankFeedDocType.PaymentEntry"
            v-model:value="partyId"
            :options="partyOptions"
            :placeholder="isInbound ? t('row.customer') : t('row.vendor')"
            size="small"
            filterable
            clearable
            class="t-recrow__field"
          />
          <NSelect
            v-else
            v-model:value="counterAccountId"
            :options="docType === BankFeedDocType.Transfer ? fundsAccountOptions : expenseAccountOptions"
            :placeholder="docType === BankFeedDocType.Transfer ? t('row.otherAccount') : t('row.category')"
            size="small"
            filterable
            clearable
            class="t-recrow__field"
          />
        </div>

        <!-- Naming the rule is the point: a prefilled form nobody can explain is
             worse than an empty one, because it gets accepted without looking. -->
        <div v-if="txn.suggestedRuleName" class="t-recrow__rule">
          <TSvgIcon icon="mdi:filter-check-outline" :size="13" />
          <span class="t-recrow__rule-text">{{ t('row.byRule').replace('{rule}', txn.suggestedRuleName) }}</span>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
/**
 * One bank line in the reconcile workspace: bank fact on the left, a single
 * confirm button in the middle, the proposed treatment on the right.
 *
 * The middle button is the whole point of the layout - the operator's hand
 * stays in one place while they run down fifty lines. Anything that would make
 * them hunt for a differently-placed control on the next row (a per-row menu,
 * a modal) breaks the rhythm that makes reconciling tolerable.
 */
import { computed, ref, watch } from 'vue'
import { NButton, NSelect, NTab, NTabs, NTag } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TMoney from './TMoney.vue'
import { formatAccountingDate } from '../../utils/finance-format'
import {
  BankFeedDocType,
  type BankMatchCandidateDto,
  type BankTransactionDto,
} from '../../services/bridges/finance-bridge'
import type { SelectOption } from 'naive-ui'

const props = defineProps<{
  txn: BankTransactionDto
  /** Engine suggestion + manually loaded alternatives for this row. */
  candidates: BankMatchCandidateDto[]
  loadingCandidates?: boolean
  busy?: boolean
  expenseAccountOptions: SelectOption[]
  fundsAccountOptions: SelectOption[]
  customerOptions: SelectOption[]
  vendorOptions: SelectOption[]
  t: (key: string) => string
}>()

const emit = defineEmits<{
  /** Confirm the highlighted match. */
  match: [txn: BankTransactionDto, journalLineId: string]
  /** Create + post + match a new document for this line. */
  create: [txn: BankTransactionDto, payload: { docType: BankFeedDocType; counterAccountId?: string; partyId?: string }]
  exclude: [txn: BankTransactionDto]
  /** Ask the parent to load the alternative candidates. */
  'load-candidates': [txn: BankTransactionDto]
}>()

const tab = ref<'match' | 'create'>('match')
const manualLineId = ref<string | null>(null)
const docType = ref<BankFeedDocType>(BankFeedDocType.Expense)
const counterAccountId = ref<string | null>(null)
const partyId = ref<string | null>(null)

const isInbound = computed(() => props.txn.amount > 0)
const payee = computed(() => props.txn.payee || props.txn.description || props.t('row.noDescription'))

// Money in is a receipt from a customer; money out is a spend. Default the
// create form to the shape that fits the sign, so the common case is one click.
watch(
  () => props.txn.id,
  () => {
    tab.value = 'match'
    manualLineId.value = null
    // A rule already said what this line is - start from its answer rather than
    // making the operator retype what the system just worked out.
    docType.value = (props.txn.suggestedDocType as typeof docType.value | undefined)
      ?? (isInbound.value ? BankFeedDocType.PaymentEntry : BankFeedDocType.Expense)
    counterAccountId.value = props.txn.suggestedCounterAccountId ?? null
    partyId.value = props.txn.suggestedPartyId ?? null
  },
  { immediate: true },
)

// Without a suggestion there is nothing to confirm on the Match tab - land the
// operator on Create instead of on a dead pane.
watch(
  () => props.txn.suggestedJournalLineId,
  (id) => {
    if (!id && props.candidates.length === 0) tab.value = 'create'
  },
  { immediate: true },
)

const candidateCount = computed(() => props.candidates.length)
const hasSuggestion = computed(() => Boolean(props.txn.suggestedJournalLineId))

/** The line the OK button will confirm: a manual pick wins over the engine's. */
const chosen = computed<BankMatchCandidateDto | null>(() => {
  const id = manualLineId.value ?? props.txn.suggestedJournalLineId
  if (!id) return props.candidates[0] ?? null
  return props.candidates.find((c) => c.journalLineId === id) ?? null
})

/**
 * Confidence is shown as a word, not a number: `0.8` means nothing to an
 * operator, "likely" tells them how hard to look before clicking OK.
 */
const confidenceLabel = computed(() => {
  if (manualLineId.value) return props.t('row.manual')
  const c = props.txn.matchConfidence
  if (c === null || c === undefined) return ''
  return c >= 1 ? props.t('row.exact') : props.t('row.likely')
})

const confidenceType = computed(() => {
  if (manualLineId.value) return 'info' as const
  return (props.txn.matchConfidence ?? 0) >= 1 ? ('success' as const) : ('warning' as const)
})

const docTypeOptions = computed<SelectOption[]>(() => [
  { label: props.t('row.docExpense'), value: BankFeedDocType.Expense },
  { label: props.t('row.docPayment'), value: BankFeedDocType.PaymentEntry },
  { label: props.t('row.docTransfer'), value: BankFeedDocType.Transfer },
])

const partyOptions = computed(() => (isInbound.value ? props.customerOptions : props.vendorOptions))

const canConfirm = computed(() => {
  if (props.busy) return false
  if (tab.value === 'match') return Boolean(chosen.value)
  if (docType.value === BankFeedDocType.PaymentEntry) return Boolean(partyId.value)
  return Boolean(counterAccountId.value)
})

function pickAnother() {
  emit('load-candidates', props.txn)
  // Cycle through the loaded alternatives in place rather than opening a modal
  // - a picker dialog is exactly the rhythm break this layout exists to avoid.
  if (props.candidates.length > 1) {
    const current = chosen.value?.journalLineId
    const i = props.candidates.findIndex((c) => c.journalLineId === current)
    const next = props.candidates[(i + 1) % props.candidates.length]
    if (next) manualLineId.value = next.journalLineId
  }
}

function confirm() {
  if (!canConfirm.value) return
  if (tab.value === 'match') {
    if (chosen.value) emit('match', props.txn, chosen.value.journalLineId)
    return
  }
  emit('create', props.txn, {
    docType: docType.value,
    counterAccountId: counterAccountId.value ?? undefined,
    partyId: partyId.value ?? undefined,
  })
}
</script>

<style scoped>
.t-recrow {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 108px minmax(0, 1.15fr);
  gap: 12px;
  align-items: center;
  padding: 10px 14px;
  border: 1px solid var(--tnzi-border);
  border-radius: var(--tnzi-admin-radius-md, 6px);
  background: var(--tnzi-container-bg);
}

.t-recrow--busy {
  opacity: 0.6;
}

.t-recrow__bank {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  grid-template-rows: auto auto;
  column-gap: 12px;
  align-items: center;
}

.t-recrow__date {
  grid-column: 1;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
  font-variant-numeric: tabular-nums;
}

.t-recrow__payee {
  grid-column: 1;
  grid-row: 2;
  font-weight: 600;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.t-recrow__ref {
  grid-column: 1;
  grid-row: 3;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}

.t-recrow__amount {
  grid-column: 2;
  grid-row: 1 / span 2;
  font-size: 16px;
}

.t-recrow__go {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 2px;
}

.t-recrow__ok {
  width: 100%;
}

.t-recrow__action {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 6px;
  min-width: 0;
  width: 100%;
}

/* The toggle is two five-letter words. Stretched across the pane it reads as
   two big empty buttons competing with the controls underneath, so it is
   sized to its content and left-aligned. */
.t-recrow__tabs {
  --n-tab-padding: 2px 10px;
  width: 190px;
  flex: 0 0 auto;
}

.t-recrow__pane {
  display: flex;
  align-items: center;
  gap: 10px;
  min-height: 30px;
  min-width: 0;
  width: 100%;
}

/* The create pane stacks: controls on one line, the rule note under it.
   Putting the note in the same flex row starved the selects to zero width. */
.t-recrow__pane--create {
  flex-direction: column;
  align-items: stretch;
  gap: 3px;
}

.t-recrow__controls {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}

.t-recrow__field {
  flex: 1 1 0;
  min-width: 0;
}

.t-recrow__match {
  flex: 1 1 auto;
  min-width: 0;
}

.t-recrow__match-main {
  display: flex;
  gap: 8px;
  align-items: baseline;
  min-width: 0;
}

.t-recrow__entry {
  font-weight: 600;
  font-family: var(--tnzi-font-mono);
  font-size: 12px;
}

/* Supporting detail about the matched entry: muted, never competing with the
   entry number itself. */
.t-recrow__match-date,
.t-recrow__memo {
  font-size: 11px;
  color: var(--tnzi-base-text-muted);
}

.t-recrow__memo {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

/* Why this row is pre-filled. Its own line, and it must never claim width from
   the controls it is explaining. */
.t-recrow__rule {
  display: flex;
  align-items: center;
  gap: 4px;
  font-size: 11px;
  line-height: 1.3;
  color: var(--tnzi-primary);
  min-width: 0;
}

.t-recrow__rule-text {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.t-recrow__hint {
  flex: 1 1 auto;
  margin: 0;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}

.t-recrow__match-side {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-shrink: 0;
}

@media (max-width: 900px) {
  .t-recrow {
    grid-template-columns: 1fr;
    gap: 10px;
  }

  /* Keep the confirm button reachable without scrolling past the proposal:
     bank fact -> proposal -> confirm, in reading order. */
  .t-recrow__go {
    order: 3;
    flex-direction: row-reverse;
    justify-content: space-between;
  }

  .t-recrow__ok {
    width: auto;
    flex: 1 1 auto;
  }

  .t-recrow__action {
    order: 2;
  }
}
</style>
