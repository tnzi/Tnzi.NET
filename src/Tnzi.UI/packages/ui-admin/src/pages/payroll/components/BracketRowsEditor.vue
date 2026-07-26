<template>
  <div class="pr-brackets">
    <div class="pr-brackets__hint">{{ t('rows.continuityHint') }}</div>
    <div class="pr-brackets__row pr-brackets__row--head">
      <span>{{ t('rows.sequence') }}</span>
      <span>{{ t('rows.lowerBound') }}</span>
      <span>{{ t('rows.upperBound') }}</span>
      <span>{{ t('rows.rate') }}</span>
      <span>{{ t('rows.quickDeduction') }}</span>
      <span />
    </div>
    <div v-if="rows.length === 0" class="pr-brackets__empty">{{ t('rows.empty') }}</div>
    <!-- 每个输入裹在 __cell 里：桌面 display:contents 保持原网格；手机(<768px)__cell 变
         带 data-label 的堆叠列，序号/下界/上界/税率/速算扣除各占一行、可辨认、无横滚。 -->
    <div v-for="(row, i) in rows" :key="i" class="pr-brackets__row">
      <div class="pr-brackets__cell" :data-label="t('rows.sequence')">
        <NInputNumber :value="row.sequence" :min="1" :show-button="false" :disabled="readonly" size="small" @update:value="(v) => patch(i, { sequence: Number(v ?? 0) })" />
      </div>
      <div class="pr-brackets__cell" :data-label="t('rows.lowerBound')">
        <NInputNumber :value="row.lowerBound" :show-button="false" :disabled="readonly" size="small" @update:value="(v) => patch(i, { lowerBound: Number(v ?? 0) })" />
      </div>
      <div class="pr-brackets__cell" :data-label="t('rows.upperBound')">
        <NInputNumber :value="row.upperBound ?? null" :show-button="false" :disabled="readonly" size="small" :placeholder="t('rows.unbounded')" @update:value="(v) => patch(i, { upperBound: v == null ? null : Number(v) })" />
      </div>
      <div class="pr-brackets__cell" :data-label="t('rows.rate')">
        <NInputNumber :value="ratePercent(row.rate)" :show-button="false" :disabled="readonly" size="small" :min="0" :max="100" :step="0.01" @update:value="(v) => patch(i, { rate: fromPercent(v) })">
          <template #suffix>%</template>
        </NInputNumber>
      </div>
      <div class="pr-brackets__cell" :data-label="t('rows.quickDeduction')">
        <NInputNumber :value="row.quickDeduction ?? null" :show-button="false" :disabled="readonly" size="small" @update:value="(v) => patch(i, { quickDeduction: v == null ? null : Number(v) })" />
      </div>
      <NButton v-if="!readonly" quaternary circle size="small" type="error" class="pr-brackets__remove" @click="remove(i)">
        <template #icon><TSvgIcon icon="mdi:close" :size="14" /></template>
      </NButton>
    </div>
    <NButton v-if="!readonly" size="small" dashed block @click="add">
      <template #icon><TSvgIcon icon="mdi:plus" :size="14" /></template>
      {{ t('rows.add') }}
    </NButton>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { NButton, NInputNumber } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import type { BracketRowInputDto } from '../../../services/bridges/payroll-bridge'

const props = withDefaults(
  defineProps<{
    value?: BracketRowInputDto[]
    readonly?: boolean
    translate?: (key: string) => string
  }>(),
  { value: () => [], readonly: false, translate: undefined },
)

const emit = defineEmits<{ 'update:value': [BracketRowInputDto[]] }>()

const t = (key: string) => (props.translate ? props.translate(key) : key)

const rows = computed<BracketRowInputDto[]>(() => props.value ?? [])

// Bracket tables are authored in percentages; the DTO stores a 0-1 fraction. Present/enter as a
// percentage (0-100 with a % suffix) so staff can't accidentally type 20 (silently clamped to 1 = 100%).
function ratePercent(rate: number): number {
  return Math.round((rate ?? 0) * 10000) / 100
}
function fromPercent(v: number | null): number {
  return Math.round(Number(v ?? 0) * 100) / 10000
}

function emitRows(next: BracketRowInputDto[]) {
  emit('update:value', next)
}

function patch(index: number, patchData: Partial<BracketRowInputDto>) {
  emitRows(rows.value.map((r, i) => (i === index ? { ...r, ...patchData } : r)))
}

function add() {
  const last = rows.value[rows.value.length - 1]
  const nextSeq = rows.value.reduce((max, r) => Math.max(max, r.sequence), 0) + 1
  const nextLower = last?.upperBound ?? 0
  emitRows([...rows.value, { sequence: nextSeq, lowerBound: nextLower, upperBound: null, rate: 0, quickDeduction: null }])
}

function remove(index: number) {
  emitRows(rows.value.filter((_, i) => i !== index))
}
</script>

<style scoped>
.pr-brackets {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.pr-brackets__hint {
  font-size: 12px;
  color: var(--tnzi-text-secondary, rgba(0, 0, 0, 0.55));
}

.pr-brackets__empty {
  font-size: 13px;
  color: var(--tnzi-text-secondary, rgba(0, 0, 0, 0.55));
  padding: 8px 0;
}

.pr-brackets__row {
  display: grid;
  grid-template-columns: 64px 1fr 1fr 90px 1fr 32px;
  gap: 8px;
  align-items: center;
}

.pr-brackets__row--head {
  font-size: 12px;
  color: var(--tnzi-text-secondary, rgba(0, 0, 0, 0.55));
}

/* Desktop: transparent wrapper - the input becomes the grid cell directly, so
   the original column sizing is untouched and the label stays hidden. */
.pr-brackets__cell {
  display: contents;
}
.pr-brackets__cell[data-label]::before {
  content: none;
}

/* Phone (<md): the multi-column grid dropped its labels (header hidden) - stack
   each row into a single-column labeled card so every bound/rate is legible and
   the panel never scrolls horizontally (content-page iron-law). */
@media (max-width: 767px) {
  .pr-brackets__row {
    grid-template-columns: 1fr;
    gap: 10px;
    padding: 12px;
    border: 1px solid var(--tnzi-border, rgba(0, 0, 0, 0.09));
    border-radius: var(--tnzi-admin-radius-md, 8px);
  }

  .pr-brackets__row--head {
    display: none;
  }

  .pr-brackets__cell {
    display: flex;
    flex-direction: column;
    gap: 4px;
    min-width: 0;
  }
  .pr-brackets__cell[data-label]::before {
    content: attr(data-label);
    font-size: 12px;
    color: var(--tnzi-text-secondary, rgba(0, 0, 0, 0.55));
  }

  /* Delete row moves to the card end as a full-width ≥44px touch target. */
  .pr-brackets__remove.n-button {
    width: 100%;
    height: 44px;
    border-radius: var(--tnzi-admin-radius-md, 8px);
  }
}
</style>
