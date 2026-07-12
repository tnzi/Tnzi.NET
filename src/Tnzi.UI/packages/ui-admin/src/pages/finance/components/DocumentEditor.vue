<template>
  <div class="fin-doc-editor">
    <div class="fin-doc-editor__header">
      <div v-if="showParty" class="fin-doc-editor__field">
        <span class="fin-doc-editor__label">{{ partyLabel }}</span>
        <NSelect v-model:value="partyId" :options="partyOptions" size="small" filterable :clearable="partyOptional" :placeholder="partyLabel" />
      </div>
      <div v-if="kind === 'expense'" class="fin-doc-editor__field">
        <span class="fin-doc-editor__label">{{ t('editor.paidFrom') }}</span>
        <NSelect v-model:value="paidFromAccountId" :options="accountOptions" size="small" filterable :placeholder="t('editor.paidFrom')" />
      </div>
      <div v-if="kind === 'expense'" class="fin-doc-editor__field">
        <span class="fin-doc-editor__label">{{ t('editor.paymentMethod') }}</span>
        <!-- Free-form on the backend: tag lets users type a custom instrument. -->
        <NSelect v-model:value="paymentMethod" :options="methodOptions" size="small" filterable clearable tag :placeholder="t('editor.paymentMethodPlaceholder')" />
      </div>
      <div class="fin-doc-editor__field">
        <span class="fin-doc-editor__label">{{ t('editor.docDate') }}</span>
        <NDatePicker v-model:value="docDateTs" type="date" size="small" style="width: 100%" />
      </div>
      <div v-if="showDueDate" class="fin-doc-editor__field">
        <span class="fin-doc-editor__label">{{ t('editor.dueDate') }}</span>
        <NDatePicker v-model:value="dueDateTs" type="date" size="small" clearable style="width: 100%" />
      </div>
      <div class="fin-doc-editor__field">
        <span class="fin-doc-editor__label">{{ t('editor.currency') }}</span>
        <NInput v-model:value="currency" size="small" :placeholder="t('editor.currencyPlaceholder')" />
      </div>
      <div class="fin-doc-editor__field fin-doc-editor__field--wide">
        <span class="fin-doc-editor__label">{{ t('editor.memo') }}</span>
        <NInput v-model:value="memo" size="small" :placeholder="t('editor.memoPlaceholder')" />
      </div>
    </div>

    <div class="fin-doc-editor__lines">
      <div class="fin-doc-editor__line fin-doc-editor__line--head" :class="`fin-doc-editor__line--${kind}`">
        <span class="fin-doc-editor__line-no">#</span>
        <template v-if="kind === 'sales'">
          <span>{{ t('editor.item') }}</span>
          <span>{{ t('editor.account') }}</span>
          <span>{{ t('editor.qty') }}</span>
          <span>{{ t('editor.price') }}</span>
        </template>
        <template v-else>
          <span>{{ t('editor.description') }}</span>
          <span>{{ t('editor.account') }}</span>
          <span>{{ t('editor.amount') }}</span>
        </template>
        <span>{{ t('editor.taxCode') }}</span>
        <span class="fin-doc-editor__line-remove" />
      </div>

      <div v-for="(line, index) in lines" :key="index" class="fin-doc-editor__line" :class="`fin-doc-editor__line--${kind}`">
        <span class="fin-doc-editor__line-no">{{ index + 1 }}</span>
        <template v-if="kind === 'sales'">
          <NSelect v-model:value="line.itemId" :options="itemOptions" size="small" filterable clearable :placeholder="t('editor.itemPlaceholder')" />
          <NSelect v-model:value="line.accountId" :options="accountOptions" size="small" filterable clearable :placeholder="t('editor.accountPlaceholder')" />
          <NInputNumber v-model:value="line.quantity" size="small" :min="0" :show-button="false" />
          <NInputNumber v-model:value="line.unitPrice" size="small" :min="0" :show-button="false" />
        </template>
        <template v-else>
          <NInput v-model:value="line.description" size="small" :placeholder="t('editor.descriptionPlaceholder')" />
          <NSelect v-model:value="line.accountId" :options="accountOptions" size="small" filterable :placeholder="t('editor.accountPlaceholder')" />
          <NInputNumber v-model:value="line.amount" size="small" :min="0" :show-button="false" />
        </template>
        <NSelect v-model:value="line.taxCodeId" :options="taxCodeOptions" size="small" filterable clearable :placeholder="t('editor.taxPlaceholder')" />
        <NButton size="tiny" quaternary circle class="fin-doc-editor__line-remove" :disabled="lines.length <= 1" @click="removeLine(index)">
          <template #icon>
            <TSvgIcon icon="mdi:close" :size="14" />
          </template>
        </NButton>
      </div>

      <div class="fin-doc-editor__lines-footer">
        <NButton size="small" dashed @click="addLine">
          <template #icon>
            <TSvgIcon icon="mdi:plus" :size="16" />
          </template>
          {{ t('editor.addLine') }}
        </NButton>
        <span class="fin-doc-editor__subtotal">{{ t('editor.subTotal') }}: <strong>{{ fmtAmount(subTotal) }}</strong></span>
      </div>
    </div>

    <div class="fin-doc-editor__footer">
      <NButton size="small" @click="emit('cancel')">{{ t('editor.cancel') }}</NButton>
      <NButton size="small" :loading="savingDraft" @click="save(false)">{{ t('editor.saveDraft') }}</NButton>
      <NButton size="small" type="primary" :loading="savingPost" @click="save(true)">{{ t('editor.saveAndPost') }}</NButton>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { NButton, NDatePicker, NInput, NInputNumber, NSelect } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { PAYMENT_METHODS } from '../../../services/bridges/finance-bridge'
import { makePageTranslator } from '../../_shared/translate'
import { useSafeMessage } from '../../_shared/safeMessage'
import type { SelectOption } from '../options'
import { fmtAmount, isoDateToLocalTs, tsToIsoDate } from '../money'

interface EditableLine {
  itemId: string | null
  description: string
  accountId: string | null
  quantity: number | null
  unitPrice: number | null
  amount: number | null
  taxCodeId: string | null
}

/** 页面适配后的通用单据形态（发票/账单/贷项 = sales；费用 = expense）。 */
export interface EditableDocument {
  id?: string
  partyId?: string | null
  paidFromAccountId?: string | null
  paymentMethod?: string | null
  docDate?: string
  dueDate?: string | null
  currency?: string
  memo?: string | null
  lines?: Array<{
    itemId?: string | null
    description?: string | null
    accountId?: string | null
    quantity?: number
    unitPrice?: number
    amount?: number
    taxCodeId?: string | null
  }>
}

/** 保存回调产出的载荷（页面转成各自的 Create*Dto）。 */
export interface DocumentEditorPayload {
  partyId: string | null
  paidFromAccountId: string | null
  paymentMethod: string | null
  docDate: string
  dueDate: string | null
  currency: string | null
  memo: string | null
  lines: Array<{
    itemId: string | null
    description: string | null
    accountId: string | null
    quantity: number
    unitPrice: number
    amount: number
    taxCodeId: string | null
  }>
}

const props = withDefaults(defineProps<{
  kind: 'sales' | 'expense'
  entry: EditableDocument | null
  partyLabel: string
  partyOptions: SelectOption[]
  partyOptional?: boolean
  showParty?: boolean
  showDueDate?: boolean
  accountOptions: SelectOption[]
  itemOptions?: SelectOption[]
  taxCodeOptions: SelectOption[]
  /** 页面提供的保存实现（草稿保存 + 可选立即过账），抛错即失败提示。 */
  onSave: (payload: DocumentEditorPayload, post: boolean) => Promise<void>
}>(), {
  partyOptional: false,
  showParty: true,
  showDueDate: false,
  itemOptions: () => [],
})

const emit = defineEmits<{ (e: 'cancel'): void }>()

const t = makePageTranslator('finance.docs')
const message = useSafeMessage()

const partyId = ref<string | null>(null)
const paidFromAccountId = ref<string | null>(null)
const paymentMethod = ref<string | null>(null)
const docDateTs = ref<number>(Date.now())
const dueDateTs = ref<number | null>(null)
const currency = ref('')
const memo = ref('')
const lines = ref<EditableLine[]>([])
const savingDraft = ref(false)
const savingPost = ref(false)

function emptyLine(): EditableLine {
  return { itemId: null, description: '', accountId: null, quantity: 1, unitPrice: null, amount: null, taxCodeId: null }
}

const methodOptions = computed<SelectOption[]>(() =>
  PAYMENT_METHODS.map((m) => ({ label: t(`method.${m.charAt(0).toLowerCase()}${m.slice(1)}`), value: m })))

function resetFrom(entry: EditableDocument | null) {
  partyId.value = entry?.partyId ?? null
  paidFromAccountId.value = entry?.paidFromAccountId ?? null
  paymentMethod.value = entry?.paymentMethod ?? null
  docDateTs.value = entry?.docDate ? isoDateToLocalTs(entry.docDate) : Date.now()
  dueDateTs.value = entry?.dueDate ? isoDateToLocalTs(entry.dueDate) : null
  currency.value = entry?.currency ?? ''
  memo.value = entry?.memo ?? ''
  lines.value = (entry?.lines ?? []).map((l) => ({
    itemId: l.itemId ?? null,
    description: l.description ?? '',
    accountId: l.accountId ?? null,
    quantity: l.quantity ?? 1,
    unitPrice: l.unitPrice ?? null,
    amount: l.amount ?? null,
    taxCodeId: l.taxCodeId ?? null,
  }))
  if (lines.value.length === 0) lines.value = [emptyLine()]
}

watch(() => props.entry, (entry) => resetFrom(entry), { immediate: true })

const subTotal = computed(() => lines.value.reduce((sum, l) => {
  const amount = props.kind === 'sales' ? (l.quantity ?? 0) * (l.unitPrice ?? 0) : (l.amount ?? 0)
  return sum + amount
}, 0))

function addLine() {
  lines.value.push(emptyLine())
}

function removeLine(index: number) {
  lines.value.splice(index, 1)
}

async function save(post: boolean) {
  if (props.showParty && !props.partyOptional && !partyId.value) {
    message.warning(t('editor.partyRequired'))
    return
  }
  if (props.kind === 'expense' && !paidFromAccountId.value) {
    message.warning(t('editor.paidFromRequired'))
    return
  }

  const usable = lines.value.filter((l) =>
    props.kind === 'sales' ? (l.unitPrice ?? 0) > 0 || l.itemId || l.accountId : (l.amount ?? 0) > 0)
  if (usable.length === 0) {
    message.warning(t('editor.noLines'))
    return
  }
  if (props.kind === 'expense' && usable.some((l) => !l.accountId)) {
    message.warning(t('editor.lineAccountRequired'))
    return
  }

  const payload: DocumentEditorPayload = {
    partyId: partyId.value,
    paidFromAccountId: paidFromAccountId.value,
    paymentMethod: paymentMethod.value,
    docDate: tsToIsoDate(docDateTs.value),
    dueDate: dueDateTs.value ? tsToIsoDate(dueDateTs.value) : null,
    currency: currency.value.trim() ? currency.value.trim().toUpperCase() : null,
    memo: memo.value.trim() || null,
    lines: usable.map((l) => ({
      itemId: l.itemId,
      description: l.description.trim() || null,
      accountId: l.accountId,
      quantity: l.quantity ?? 1,
      unitPrice: l.unitPrice ?? 0,
      amount: l.amount ?? 0,
      taxCodeId: l.taxCodeId,
    })),
  }

  const loading = post ? savingPost : savingDraft
  loading.value = true
  try {
    await props.onSave(payload, post)
  } catch (error) {
    message.error(error instanceof Error ? error.message : String(error))
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.fin-doc-editor {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.fin-doc-editor__header {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 12px;
}

.fin-doc-editor__field {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.fin-doc-editor__field--wide {
  grid-column: 1 / -1;
}

.fin-doc-editor__label {
  font-size: 12px;
  color: var(--tnzi-text-secondary, rgba(0, 0, 0, 0.55));
}

.fin-doc-editor__lines {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.fin-doc-editor__line {
  display: grid;
  gap: 8px;
  align-items: center;
}

.fin-doc-editor__line--sales {
  grid-template-columns: 24px minmax(140px, 1fr) minmax(140px, 1fr) 90px 110px minmax(120px, 0.9fr) 28px;
}

.fin-doc-editor__line--expense {
  grid-template-columns: 24px minmax(160px, 1.2fr) minmax(160px, 1.2fr) 120px minmax(120px, 0.9fr) 28px;
}

.fin-doc-editor__line--head {
  font-size: 12px;
  color: var(--tnzi-text-secondary, rgba(0, 0, 0, 0.55));
}

.fin-doc-editor__line-no {
  text-align: center;
  font-variant-numeric: tabular-nums;
}

.fin-doc-editor__lines-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
}

.fin-doc-editor__subtotal {
  font-variant-numeric: tabular-nums;
  font-size: 13px;
}

.fin-doc-editor__footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}

@media (max-width: 767px) {
  .fin-doc-editor__header {
    grid-template-columns: 1fr;
  }

  .fin-doc-editor__line--sales,
  .fin-doc-editor__line--expense {
    grid-template-columns: 20px minmax(120px, 1fr) minmax(120px, 1fr) 80px 90px minmax(100px, 0.9fr) 24px;
  }
}
</style>
