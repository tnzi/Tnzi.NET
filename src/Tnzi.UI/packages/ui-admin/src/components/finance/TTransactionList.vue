<template>
  <div class="t-txn">
    <div v-if="showScope || $slots.toolbar" class="t-txn__toolbar">
      <NRadioGroup
        v-if="showScope" :value="scope" size="small" @update:value="(v: 'all' | 'open') => emit('update:scope', v)">
        <NRadioButton value="all">{{ label('all') }}</NRadioButton>
        <NRadioButton value="open">
          {{ label('open') }}<span v-if="openCount !== undefined"> ({{ openCount }})</span>
        </NRadioButton>
      </NRadioGroup>
      <slot name="toolbar" />
    </div>

    <NSpin :show="loading">
      <!-- One empty state, not two: the table renders its own "No Data" when
           handed an empty array, which would sit directly above ours. -->
      <TResponsiveTable
        v-if="entries.length > 0"
        :columns="columns"
        :data="entries"
        :row-key="(r: PartyLedgerEntryDto) => `${r.docType}:${r.docId}`"
        size="small"
        mobile="scroll"
        :pagination="false"
        :bordered="false"
      />
      <TEmpty v-else-if="!loading" :text="label('empty')" />
    </NSpin>

    <div v-if="total > pageSize" class="t-txn__pager">
      <NPagination
        :page="pageIndex"
        :page-size="pageSize"
        :item-count="total"
        size="small"
        @update:page="(n: number) => emit('update:pageIndex', n)"
      />
    </div>
  </div>
</template>

<script setup lang="ts">
/**
 * `TTransactionList` - a party's activity as one dated ledger, across document
 * types.
 *
 * Two rules from the research, both load-bearing:
 *
 * - **Every row drills through to its source document.** A transaction line
 *   nobody can open is a claim, not a record; the whole point of showing it is
 *   that the reader can verify it.
 * - **The amount carries its own sign** (invoice positive, payment negative),
 *   so direction is readable without decoding the document type. The backend
 *   signs it; this component must not re-derive the sign.
 */
import { EMPTY_DASH } from '../../utils/placeholders'
import { computed, h } from 'vue'
import { NPagination, NRadioButton, NRadioGroup, NSpin, type DataTableColumns } from 'naive-ui'
import TMoney from './TMoney.vue'
import TDocStatusBadge from './TDocStatusBadge.vue'
import TEmpty from '../data/TEmpty.vue'
import TResponsiveTable from '../data/TResponsiveTable.vue'
import { formatAccountingDate } from '../../utils/finance-format'
import type { PartyLedgerEntryDto } from '../../services/bridges/finance-bridge'

const props = withDefaults(
  defineProps<{
    entries: PartyLedgerEntryDto[]
    loading?: boolean
    total?: number
    pageIndex?: number
    pageSize?: number
    scope?: 'all' | 'open'
    openCount?: number
    /** Show the All / Open filter. Off for a preview list (an overview's
     *  "recent activity" is not a place to filter - the full ledger is). */
    showScope?: boolean
    /** i18n lookup for this component's own strings (`transactions.*`). */
    translate?: (key: string) => string
    /**
     * Human name for a source token. Supplied by the host because the token set
     * is open - a consuming app that posts through `ILedgerPostingService`
     * writes its own tokens, and a component-local table could only ever cover
     * the framework's. Unknown tokens must render as themselves, never blank.
     */
    docTypeLabel?: (docType: string) => string
    /** Forwarded to `TDocStatusBadge` (resolves `docs.status.*`). */
    statusTranslate?: (key: string) => string
  }>(),
  { total: 0, pageIndex: 1, pageSize: 20, scope: 'all', showScope: true },
)

const emit = defineEmits<{
  'update:scope': ['all' | 'open']
  'update:pageIndex': [number]
  /** Drill through to the source document. */
  open: [entry: PartyLedgerEntryDto]
}>()

const FALLBACK: Record<string, string> = {
  all: 'All',
  open: 'Open',
  empty: 'No transactions in this period.',
  date: 'Date',
  document: 'Document',
  due: 'Due',
  amount: 'Amount',
  outstanding: 'Outstanding',
  status: 'Status',
  overdueSuffix: 'd overdue',
}

function label(key: string): string {
  const translated = props.translate?.(`transactions.${key}`)
  if (translated && !translated.includes(`transactions.${key}`)) return translated
  return FALLBACK[key] ?? key
}

/** Human name for a source token; unknown tokens fall back to the raw token. */
function docTypeLabel(docType: string): string {
  return props.docTypeLabel?.(docType) ?? docType
}

const columns = computed<DataTableColumns<PartyLedgerEntryDto>>(() => [
  {
    key: 'docDate',
    title: label('date'),
    width: 110,
    render: (r) => formatAccountingDate(r.docDate),
  },
  {
    key: 'number',
    title: label('document'),
    minWidth: 200,
    render: (r) =>
      h('button', {
        type: 'button',
        class: 't-txn__link',
        onClick: () => emit('open', r),
        title: label('document'),
      }, [
        h('span', { class: 't-txn__number' }, r.number ?? EMPTY_DASH),
        h('span', { class: 't-txn__doctype' }, docTypeLabel(r.docType)),
      ]),
  },
  {
    key: 'dueDate',
    title: label('due'),
    width: 140,
    render: (r) =>
      r.dueDate
        ? h('span', { class: r.overdueDays > 0 ? 't-txn__overdue' : undefined }, [
            formatAccountingDate(r.dueDate),
            r.overdueDays > 0 ? h('span', { class: 't-txn__overdue-days' }, ` ${r.overdueDays}${label('overdueSuffix')}`) : null,
          ])
        : EMPTY_DASH,
  },
  {
    key: 'amount',
    title: label('amount'),
    width: 130,
    align: 'right',
    render: (r) => h(TMoney, { value: r.amount, currency: r.currency }),
  },
  {
    key: 'outstanding',
    title: label('outstanding'),
    width: 130,
    align: 'right',
    // 0 → em-dash：付清的单据不该显示一个看起来像金额的 0.00
    render: (r) => h(TMoney, { value: r.outstanding, currency: r.currency, zeroDash: true, strong: r.outstanding > 0 }),
  },
  {
    key: 'status',
    title: label('status'),
    width: 110,
    render: (r) => h(TDocStatusBadge, { value: r.status as unknown as string, translate: props.statusTranslate }),
  },
])
</script>

<style scoped>
.t-txn {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.t-txn__toolbar {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
}

.t-txn__pager {
  display: flex;
  justify-content: flex-end;
}

:deep(.t-txn__link) {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 1px;
  padding: 0;
  border: 0;
  background: none;
  font: inherit;
  cursor: pointer;
  text-align: left;
  color: inherit;
}

:deep(.t-txn__number) {
  font-weight: 600;
  font-family: var(--tnzi-font-mono);
  font-size: 12px;
  border-bottom: 1px dashed transparent;
}

:deep(.t-txn__link:hover .t-txn__number),
:deep(.t-txn__link:focus-visible .t-txn__number) {
  color: var(--tnzi-primary);
  border-bottom-color: currentColor;
}

:deep(.t-txn__link:focus-visible) {
  outline: 2px solid var(--tnzi-primary);
  outline-offset: 2px;
  border-radius: 2px;
}

:deep(.t-txn__doctype) {
  font-size: 11px;
  color: var(--tnzi-base-text-muted);
}

:deep(.t-txn__overdue) {
  color: var(--tnzi-error);
}

:deep(.t-txn__overdue-days) {
  font-size: 11px;
}
</style>
