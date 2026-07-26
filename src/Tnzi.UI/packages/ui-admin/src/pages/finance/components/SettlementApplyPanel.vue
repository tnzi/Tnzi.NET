<template>
  <div class="fin-apply">
    <div v-if="openDocs.length === 0" class="fin-apply__empty">{{ td('apply.noOpenDocs') }}</div>
    <template v-else>
      <div class="fin-apply__row fin-apply__row--head">
        <span>{{ td('apply.document') }}</span>
        <span>{{ td('apply.dueDate') }}</span>
        <span>{{ td('apply.currency') }}</span>
        <span>{{ td('apply.outstanding') }}</span>
        <span>{{ td('apply.amount') }}</span>
      </div>
      <div v-for="doc in openDocs" :key="doc.docId" class="fin-apply__row">
        <span class="fin-apply__cell" :data-label="td('apply.document')">{{ doc.number ?? doc.docId }}</span>
        <span class="fin-apply__cell" :data-label="td('apply.dueDate')">{{ fmtDate(doc.dueDate, { fallback: EMPTY_DASH }) }}</span>
        <span class="fin-apply__cell" :class="{ 'fin-apply__mismatch': !sameCurrency(doc) }" :data-label="td('apply.currency')">{{ doc.currency }}</span>
        <span class="fin-apply__cell fin-apply__num" :data-label="td('apply.outstanding')">{{ fmtAmount(doc.outstanding) }}</span>
        <!-- Different-currency documents can't be settled against this source. -->
        <NInputNumber
          v-model:value="allocations[doc.docId]"
          size="small"
          :min="0"
          :max="doc.outstanding"
          :disabled="!sameCurrency(doc)"
          :show-button="false"
          :placeholder="sameCurrency(doc) ? '0.00' : td('apply.currencyMismatch')"
        />
      </div>
      <div class="fin-apply__footer">
        <span class="fin-apply__num">
          {{ td('apply.remaining') }}: <strong>{{ fmtAmount(applyRemaining) }}</strong>
        </span>
        <div class="fin-apply__actions">
          <NButton size="small" @click="emit('cancel')">{{ td('editor.cancel') }}</NButton>
          <NButton size="small" type="primary" :loading="applying" :disabled="allocatedTotal <= 0 || !source || allocatedTotal > source.remaining" @click="submitApply">
            {{ td('apply.submit') }}
          </NButton>
        </div>
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
import { EMPTY_DASH } from '../../../utils/placeholders'
import { computed, reactive, ref, watch } from 'vue'
import { NButton, NInputNumber } from 'naive-ui'

import { makePageTranslator } from '../../_shared/translate'
import { useSafeMessage } from '../../_shared/safeMessage'
import type { FinanceBridge, OpenDocumentDto, SettlementDocType, FinancePartyType } from '../../../services/bridges/finance-bridge'
import { fmtAmount, fmtDate } from '../money'

/** The source document (payment or credit memo) being allocated across open targets. */
export interface SettlementApplySource {
  id: string
  sourceType: SettlementDocType
  partyType: FinancePartyType
  partyId: string
  currency: string
  /** Remaining (unapplied) amount available to allocate. */
  remaining: number
}

const props = defineProps<{
  bridge: FinanceBridge
  source: SettlementApplySource | null
}>()

const emit = defineEmits<{ (e: 'applied'): void; (e: 'cancel'): void }>()

// Panel strings live in the shared finance.docs namespace so both Payments and Credit Memos reuse them.
const td = makePageTranslator('finance.docs')
const message = useSafeMessage()

const openDocs = ref<OpenDocumentDto[]>([])
const allocations = reactive<Record<string, number | null>>({})
const applying = ref(false)

// The backend only settles same-currency documents against the source.
function sameCurrency(doc: OpenDocumentDto): boolean {
  return doc.currency === props.source?.currency
}

watch(
  () => props.source?.id,
  async (id) => {
    openDocs.value = []
    Object.keys(allocations).forEach((k) => delete allocations[k])
    if (!id || !props.source) return
    try {
      openDocs.value = await props.bridge.settlements.openDocuments(props.source.partyType, props.source.partyId)
    } catch (error) {
      message.error(error instanceof Error ? error.message : String(error))
    }
  },
  { immediate: true },
)

const allocatedTotal = computed(() =>
  Object.values(allocations).reduce<number>((sum, v) => sum + (v ?? 0), 0))
const applyRemaining = computed(() => (props.source?.remaining ?? 0) - allocatedTotal.value)

async function submitApply() {
  if (!props.source) return
  const targets = openDocs.value
    .filter((d) => (allocations[d.docId] ?? 0) > 0 && sameCurrency(d))
    .map((d) => ({ targetType: d.docType, targetId: d.docId, amount: allocations[d.docId]! }))
  if (targets.length === 0) return

  applying.value = true
  try {
    await props.bridge.settlements.apply({
      sourceType: props.source.sourceType,
      sourceId: props.source.id,
      targets,
    })
    message.success(td('apply.success'))
    emit('applied')
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    applying.value = false
  }
}
</script>

<style scoped>
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

.fin-apply__cell[data-label]::before {
  content: none;
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

/* Phone (<md): the fixed 5-column grid overflows a fullscreen drawer - stack each row to
   label: value single-column so the panel never scrolls horizontally (content-page iron-law). */
@media (max-width: 767px) {
  .fin-apply__row--head {
    display: none;
  }
  .fin-apply__row {
    grid-template-columns: 1fr;
    gap: 4px;
    padding: 8px 0;
    border-bottom: 1px solid var(--tnzi-border, rgba(0, 0, 0, 0.08));
  }
  .fin-apply__cell[data-label]::before {
    content: attr(data-label) ': ';
    color: var(--tnzi-text-secondary, rgba(0, 0, 0, 0.55));
    font-size: 12px;
  }
}
</style>
