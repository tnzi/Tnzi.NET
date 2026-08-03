<template>
  <TItemCard
    :title="partyName"
    :icon="icon"
    :icon-tone="statusTone"
    :tags="tags"
    :muted="isVoid"
    :selectable="selectable"
    :checked="checked"
    :selected="checked"
    clickable
    @update:checked="(v: boolean) => emit('update:checked', v)"
    @click="emit('open')"
  >
    <template #meta>
      <div class="fdc-meta">
        <span class="fdc-meta__item">
          <TSvgIcon icon="mdi:calendar-outline" :size="13" />{{ fmtDate(row.docDate) }}
        </span>
        <!-- Due date only when the document has one AND is still owed: on a
             settled or voided document it is noise, and on an overdue one it is
             the most important thing on the row, so it gets a tone. -->
        <span v-if="row.dueDate && isOpen" class="fdc-meta__item" :class="{ 'fdc-meta__item--late': isOverdue }">
          <TSvgIcon icon="mdi:calendar-alert" :size="13" />{{ t('columns.dueDate') }} {{ fmtDate(row.dueDate) }}
        </span>
        <span v-if="row.memo" class="fdc-meta__memo" :title="row.memo">{{ row.memo }}</span>
      </div>
    </template>

    <template #trailing>
      <div class="fdc-amount">
        <TMoney class="fdc-amount__total" :value="total" :currency="row.currency" />
        <!-- An open document's outstanding balance is the figure the reader is
             actually chasing; the gross total alone does not answer "how much is
             still owed". -->
        <span v-if="isOpen && outstanding !== total" class="fdc-amount__label">
          {{ t('columns.outstanding') }} <TMoney :value="outstanding" :currency="row.currency" />
        </span>
      </div>
    </template>

    <template v-if="$slots.actions" #actions>
      <slot name="actions" />
    </template>
  </TItemCard>
</template>

<script setup lang="ts">
/**
 * TDocumentCard - one finance document as a row, shared by every document list
 * (invoices, bills, expenses, credit memos).
 *
 * A document list is read one row at a time: who, when, what state, how much,
 * and how much is still open. The table gave those five facts the same weight as
 * currency, applied total and creation time across eight columns, so the party
 * name truncated while `appliedTotal` got a full column of its own. Here the
 * party leads, the status is a chip beside it, the dates sit underneath, and the
 * money is right-aligned with the outstanding balance under the gross figure.
 *
 * The page still owns the row operations (post / void / delete / downstream
 * actions) through the `#actions` slot - the card only fixes the layout, not the
 * lifecycle.
 */
import { computed } from 'vue'
import { TSvgIcon } from '@tnzi/ui'
import TItemCard, { type ItemCardTag, type ItemCardTone } from '../data/TItemCard.vue'
import TMoney from './TMoney.vue'
import { EMPTY_DASH } from '../../utils/placeholders'
import { isoDateToLocalTs } from '../../utils/finance-format'
import { DOC_STATUS_META, type FinanceDocRow } from './document-row'
import { FinanceDocumentStatus } from '../../services/bridges/finance-bridge'
import { formatAccountingDate as fmtDate } from '../../utils/finance-format'

interface Props {
  row: FinanceDocRow
  /** Which denormalised party name this document kind carries. */
  partyKey?: 'customerName' | 'vendorName' | 'partyName'
  /** Iconify glyph for the leading tile (document kind). */
  icon?: string
  /** Page-scoped translator (status labels + column captions live per page). */
  t: (key: string, named?: Record<string, unknown>) => string
  selectable?: boolean
  checked?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  partyKey: 'partyName',
  icon: 'mdi:file-document-outline',
  selectable: false,
  checked: false,
})

const emit = defineEmits<{ open: []; 'update:checked': [value: boolean] }>()

defineSlots<{ actions?: () => unknown }>()

const partyName = computed(() => {
  const row = props.row
  return (
    row[props.partyKey] ??
    row.partyName ??
    row.customerName ??
    row.vendorName ??
    row.number ??
    EMPTY_DASH
  )
})

// `?? null`, not `?? 0`: a document with no figure must render the money dash,
// not a confident 0.00. `formatMoney` already handles null; collapsing it here
// would make "not loaded" and "zero" look identical on a ledger.
const total = computed(() => props.row.total ?? props.row.amount ?? null)
const outstanding = computed(() =>
  total.value == null ? null : total.value - (props.row.appliedTotal ?? 0),
)

const isVoid = computed(() => props.row.status === FinanceDocumentStatus.Voided)
const isOpen = computed(
  () =>
    (props.row.status === FinanceDocumentStatus.Posted ||
      props.row.status === FinanceDocumentStatus.PartiallyPaid) &&
    (outstanding.value ?? 0) > 0,
)
const isOverdue = computed(() => {
  if (!isOpen.value || !props.row.dueDate) return false
  // `dueDate` is a backend date-only value, so `Date.parse` reads it as UTC
  // midnight. West of UTC that instant has already passed by the time the local
  // day starts, which flagged every document as overdue a full day early (and
  // east of UTC, from mid-morning). Compare calendar days in local time instead:
  // a document is overdue only once its due date is behind today.
  const due = isoDateToLocalTs(String(props.row.dueDate))
  if (!Number.isFinite(due)) return false
  const now = new Date()
  const todayStart = new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime()
  return due < todayStart
})

const statusMeta = computed(() => DOC_STATUS_META[props.row.status ?? ''])

const statusTone = computed<ItemCardTone>(() => {
  const type = statusMeta.value?.type
  if (type === 'success' || type === 'warning' || type === 'error' || type === 'info') return type
  return 'default'
})

const tags = computed<ItemCardTag[]>(() => {
  const out: ItemCardTag[] = []
  const meta = statusMeta.value
  if (meta) out.push({ label: props.t(meta.label), type: meta.type })
  // The document number identifies the paper; it belongs beside the status, not
  // in a leading column that pushes the party name off the row.
  if (props.row.number) out.push({ label: props.row.number, type: 'default' })
  if (isOverdue.value) out.push({ label: props.t('columns.overdue'), type: 'error' })
  return out
})
</script>

<style scoped>
.fdc-meta {
  display: flex;
  flex-wrap: nowrap;
  min-width: 0;
  gap: 4px 16px;
  font-size: 12.5px;
  color: var(--tnzi-base-text-muted);
}
.fdc-meta__item {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  flex-shrink: 0;
}
.fdc-meta__item--late {
  color: var(--tnzi-error);
  font-weight: 500;
}
/* `display: block` so the ellipsis works: inside a flex row the text becomes an
   anonymous flex item and overflow clips mid-word instead. */
.fdc-meta__memo {
  display: block;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.fdc-amount {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 2px;
  text-align: right;
}
.fdc-amount__total {
  font-size: 15px;
  font-weight: 700;
}
.fdc-amount__label {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: 11.5px;
  color: var(--tnzi-base-text-muted);
}
@media (max-width: 660px) {
  .fdc-amount {
    align-items: flex-start;
    text-align: left;
  }
}
</style>
