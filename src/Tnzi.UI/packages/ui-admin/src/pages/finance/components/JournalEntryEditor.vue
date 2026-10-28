<template>
  <div class="je-editor">
    <div class="je-editor__header">
      <div class="je-editor__field">
        <span class="je-editor__label">{{ t('form.postingDate') }}</span>
        <NDatePicker v-model:value="postingDateTs" type="date" size="small" class="w-full" />
      </div>
      <div class="je-editor__field">
        <span class="je-editor__label">{{ t('form.currency') }}</span>
        <NInput v-model:value="currency" size="small" :placeholder="t('form.currencyPlaceholder')" />
      </div>
      <div class="je-editor__field">
        <span class="je-editor__label">{{ t('form.exchangeRate') }}</span>
        <NInputNumber v-model:value="exchangeRate" size="small" :min="0" :show-button="false" class="w-full" :placeholder="t('form.exchangeRatePlaceholder')" />
      </div>
      <div class="je-editor__field je-editor__field--wide">
        <span class="je-editor__label">{{ t('form.memo') }}</span>
        <NInput v-model:value="memo" size="small" :placeholder="t('form.memoPlaceholder')" />
      </div>
    </div>

    <div class="je-editor__lines">
      <div class="je-editor__line je-editor__line--head">
        <span class="je-editor__line-no">#</span>
        <span>{{ t('form.account') }}</span>
        <span>{{ t('form.debit') }}</span>
        <span>{{ t('form.credit') }}</span>
        <span>{{ t('form.lineMemo') }}</span>
        <span class="je-editor__line-remove" />
      </div>
      <!-- 每个字段裹在 __cell 里：桌面 display:contents 让包裹层透明、输入直接落进原网格；
           手机(<768px)__cell 变成带 data-label 的堆叠列，一字段一行、无横滚。 -->
      <div v-for="(line, index) in lines" :key="index" class="je-editor__line">
        <span class="je-editor__line-no">{{ index + 1 }}</span>
        <div class="je-editor__cell" :data-label="t('form.account')">
          <NSelect
            v-model:value="line.accountId"
            :options="accountOptions"
            size="small"
            filterable
            :placeholder="t('form.accountPlaceholder')"
          />
        </div>
        <!-- 一行只能填借或贷一侧：填一侧即清空另一侧 -->
        <div class="je-editor__cell" :data-label="t('form.debit')">
          <NInputNumber v-model:value="line.debit" size="small" :min="0" :show-button="false" :placeholder="'0.00'" @update:value="(v) => { if ((v ?? 0) > 0) line.credit = null }" />
        </div>
        <div class="je-editor__cell" :data-label="t('form.credit')">
          <NInputNumber v-model:value="line.credit" size="small" :min="0" :show-button="false" :placeholder="'0.00'" @update:value="(v) => { if ((v ?? 0) > 0) line.debit = null }" />
        </div>
        <div class="je-editor__cell" :data-label="t('form.lineMemo')">
          <NInput v-model:value="line.memo" size="small" :placeholder="t('form.lineMemoPlaceholder')" />
        </div>
        <NButton size="tiny" quaternary circle class="je-editor__line-remove" :disabled="lines.length <= 1" @click="removeLine(index)">
          <template #icon>
            <TSvgIcon icon="mdi:close" :size="14" />
          </template>
        </NButton>
      </div>
      <div class="je-editor__lines-footer">
        <NButton size="small" dashed @click="addLine">
          <template #icon>
            <TSvgIcon icon="mdi:plus" :size="16" />
          </template>
          {{ t('dialog.addLine') }}
        </NButton>
        <div class="je-editor__totals">
          <span>{{ t('dialog.totalDebit') }}: <strong>{{ fmtAmount(totalDebit) }}</strong></span>
          <span>{{ t('dialog.totalCredit') }}: <strong>{{ fmtAmount(totalCredit) }}</strong></span>
          <span :class="['je-editor__diff', balanced ? 'je-editor__diff--ok' : 'je-editor__diff--bad']">
            {{ t('dialog.difference') }}: {{ fmtAmount(totalDebit - totalCredit) }}
          </span>
        </div>
      </div>
    </div>

    <div class="je-editor__footer">
      <NButton size="small" @click="emit('cancel')">{{ t('dialog.cancel') }}</NButton>
      <NButton size="small" :loading="savingDraft" :disabled="saving" @click="save(false)">{{ t('dialog.saveDraft') }}</NButton>
      <NButton size="small" type="primary" :loading="savingPost" :disabled="!balanced || saving" @click="save(true)">
        {{ t('dialog.saveAndPost') }}
      </NButton>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { NButton, NDatePicker, NInput, NInputNumber, NSelect } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import type { CreateJournalEntryDto, FinanceBridge, JournalEntryDto } from '../../../services/bridges/finance-bridge'
import { makePageTranslator } from '../../_shared/translate'
import { useSafeMessage } from '../../_shared/safe-message'
import { fmtAmount, isoDateToLocalTs, tsToIsoDate } from '../money'

interface EditableLine {
  accountId: string | null
  debit: number | null
  credit: number | null
  memo: string
  /**
   * Party / dimension tags of a loaded line. This editor has no controls for
   * them (integrations and the API set them), but a draft update rebuilds every
   * line from the payload, so they are carried on the line object - they follow
   * the line when another one is added or removed, and a new line has none.
   */
  partyType?: string | null
  partyId?: string | null
  dimensions?: string | null
}

const props = defineProps<{
  /** Null → create a new draft; a draft entry → edit it. */
  entry: JournalEntryDto | null
  accountOptions: Array<{ label: string; value: string }>
  bridge: FinanceBridge
}>()

const emit = defineEmits<{
  (e: 'saved'): void
  (e: 'cancel'): void
  /** A new entry's first save created this draft; the editor now writes to it. */
  (e: 'drafted', entry: JournalEntryDto): void
}>()

const t = makePageTranslator('finance.journals')
const message = useSafeMessage()

const postingDateTs = ref<number>(Date.now())
const currency = ref('')
const exchangeRate = ref<number | null>(null)
const memo = ref('')
const lines = ref<EditableLine[]>([])
const savingDraft = ref(false)
const savingPost = ref(false)
/** Either save in flight: both buttons wait, or two clicks would create two drafts. */
const saving = computed(() => savingDraft.value || savingPost.value)
/**
 * The draft this editor writes to: the entry it was opened on, or the draft its
 * own first save created. "Save & post" is create-then-post, and when the post
 * fails the retry must update that draft, not create another one.
 */
const draftId = ref<string | null>(null)

function emptyLine(): EditableLine {
  return { accountId: null, debit: null, credit: null, memo: '' }
}

function resetFrom(entry: JournalEntryDto | null) {
  if (entry) {
    postingDateTs.value = entry.postingDate ? isoDateToLocalTs(entry.postingDate) : Date.now()
    currency.value = entry.currency ?? ''
    exchangeRate.value = entry.exchangeRate && entry.exchangeRate !== 1 ? entry.exchangeRate : null
    memo.value = entry.memo ?? ''
    lines.value = (entry.lines ?? []).map((l) => ({
      accountId: l.accountId,
      debit: l.txnDebit > 0 ? l.txnDebit : null,
      credit: l.txnCredit > 0 ? l.txnCredit : null,
      memo: l.memo ?? '',
      partyType: l.partyType ?? null,
      partyId: l.partyId ?? null,
      dimensions: l.dimensions ?? null,
    }))
    if (lines.value.length === 0) lines.value = [emptyLine(), emptyLine()]
  } else {
    postingDateTs.value = Date.now()
    currency.value = ''
    exchangeRate.value = null
    memo.value = ''
    lines.value = [emptyLine(), emptyLine()]
  }
}

// The host remounts the editor per open (keyed by open sequence), so
// `immediate` covers the initial bind; the watch covers async record swaps
// while mounted (e.g. a deep-link restore resolving after paint).
watch(
  () => props.entry,
  (entry) => {
    draftId.value = entry?.id ?? null
    resetFrom(entry)
  },
  { immediate: true },
)

const totalDebit = computed(() => lines.value.reduce((sum, l) => sum + (l.debit ?? 0), 0))
const totalCredit = computed(() => lines.value.reduce((sum, l) => sum + (l.credit ?? 0), 0))
const balanced = computed(() => totalDebit.value > 0 && Math.abs(totalDebit.value - totalCredit.value) < 0.000001)

function addLine() {
  lines.value.push(emptyLine())
}

function removeLine(index: number) {
  lines.value.splice(index, 1)
}

function buildPayload(): CreateJournalEntryDto | null {
  const usable = lines.value.filter((l) => l.accountId || (l.debit ?? 0) > 0 || (l.credit ?? 0) > 0)
  if (usable.length === 0) {
    message.warning(t('dialog.noLines'))
    return null
  }
  for (const line of usable) {
    if (!line.accountId) {
      message.warning(t('dialog.accountRequired'))
      return null
    }
    // 借贷互斥（UI 已即时清空另一侧，此处为提交前兜底校验）
    if ((line.debit ?? 0) > 0 && (line.credit ?? 0) > 0) {
      message.warning(t('dialog.debitOrCredit'))
      return null
    }
  }
  const cur = currency.value.trim().toUpperCase()
  return {
    postingDate: tsToIsoDate(postingDateTs.value),
    memo: memo.value.trim() || null,
    currency: cur || null,
    exchangeRate: exchangeRate.value ?? null,
    lines: usable.map((l) => ({
      accountId: l.accountId!,
      debit: l.debit ?? 0,
      credit: l.credit ?? 0,
      memo: l.memo.trim() || null,
      partyType: l.partyType ?? null,
      partyId: l.partyId ?? null,
      dimensions: l.dimensions ?? null,
    })),
  }
}

async function save(post: boolean) {
  if (saving.value) return
  const payload = buildPayload()
  if (!payload) return

  const loading = post ? savingPost : savingDraft
  loading.value = true
  try {
    const existingId = draftId.value
    const saved = existingId
      ? await props.bridge.journals.updateDraft(existingId, payload)
      : await props.bridge.journals.createDraft(payload)
    if (!existingId) {
      draftId.value = saved.id
      emit('drafted', saved)
    }

    if (post) {
      await props.bridge.journals.post(saved.id)
      message.success(t('dialog.postedSuccess'))
    } else {
      message.success(t('dialog.savedSuccess'))
    }

    emit('saved')
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.je-editor {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.je-editor__header {
  display: grid;
  grid-template-columns: 160px 110px 130px 1fr;
  gap: 12px;
}

.je-editor__field {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.je-editor__label {
  font-size: 12px;
  color: var(--tnzi-text-secondary, rgba(0, 0, 0, 0.55));
}

.je-editor__lines {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.je-editor__line {
  display: grid;
  grid-template-columns: 24px minmax(200px, 1.4fr) 120px 120px 1fr 28px;
  gap: 8px;
  align-items: center;
}

.je-editor__line--head {
  font-size: 12px;
  color: var(--tnzi-text-secondary, rgba(0, 0, 0, 0.55));
}

/* Desktop: transparent wrapper - the input becomes the grid cell directly, so
   the original column sizing is untouched and the label stays hidden. */
.je-editor__cell {
  display: contents;
}
.je-editor__cell[data-label]::before {
  content: none;
}

.je-editor__line-no {
  text-align: center;
  font-variant-numeric: tabular-nums;
}

.je-editor__lines-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
}

.je-editor__totals {
  display: flex;
  gap: 16px;
  font-variant-numeric: tabular-nums;
  font-size: 13px;
}

.je-editor__diff--ok {
  color: var(--tnzi-success, #18a058);
}

.je-editor__diff--bad {
  color: var(--tnzi-error, #d03050);
}

.je-editor__footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}

/* Phone (<md): the 6-column grid overflows a fullscreen modal - stack each line
   into a single-column labeled card so debit/credit/account/memo each get their
   own row and the panel never scrolls horizontally (content-page iron-law). */
@media (max-width: 767px) {
  .je-editor__header {
    grid-template-columns: 1fr 1fr;
  }

  .je-editor__field--wide {
    grid-column: 1 / -1;
  }

  .je-editor__line--head {
    display: none;
  }

  .je-editor__line {
    grid-template-columns: 1fr;
    gap: 10px;
    padding: 12px;
    border: 1px solid var(--tnzi-border, rgba(0, 0, 0, 0.09));
    border-radius: var(--tnzi-admin-radius-md, 8px);
  }

  .je-editor__line-no {
    text-align: left;
    font-size: 12px;
    font-weight: 600;
    color: var(--tnzi-text-secondary, rgba(0, 0, 0, 0.55));
  }
  .je-editor__line-no::before {
    content: '#';
  }

  .je-editor__cell {
    display: flex;
    flex-direction: column;
    gap: 4px;
    min-width: 0;
  }
  .je-editor__cell[data-label]::before {
    content: attr(data-label);
    font-size: 12px;
    color: var(--tnzi-text-secondary, rgba(0, 0, 0, 0.55));
  }

  /* Delete row moves to the card end as a full-width ≥44px touch target. */
  .je-editor__line-remove.n-button {
    width: 100%;
    height: 44px;
    border-radius: var(--tnzi-admin-radius-md, 8px);
  }
}
</style>
