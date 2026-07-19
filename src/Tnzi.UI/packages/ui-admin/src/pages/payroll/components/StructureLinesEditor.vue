<template>
  <div class="pr-lines">
    <div class="pr-lines__hint">{{ t('lines.sequenceHint') }}</div>
    <div v-if="rows.length === 0" class="pr-lines__empty">{{ t('lines.empty') }}</div>
    <!-- 每个输入裹在 __cell 里：桌面 display:contents 保持原网格；手机(<768px)__cell 变
         带 data-label 的堆叠列，componentId/公式/金额/条件各占一行、可辨认、无横滚
         (本编辑器桌面无表头，故 data-label 复用各字段的 placeholder i18n 键)。 -->
    <div v-for="(row, i) in rows" :key="i" class="pr-lines__row">
      <div class="pr-lines__cell" :data-label="t('lines.sequence')">
        <NInputNumber
          class="pr-lines__seq"
          :value="row.sequence"
          :min="1"
          :show-button="false"
          :disabled="readonly"
          size="small"
          :placeholder="t('lines.sequence')"
          @update:value="(v) => patch(i, { sequence: Number(v ?? 0) })"
        />
      </div>
      <div class="pr-lines__cell" :data-label="t('lines.component')">
        <NSelect
          class="pr-lines__component"
          :value="row.componentId"
          :options="componentOptions"
          :disabled="readonly"
          filterable
          size="small"
          :placeholder="t('lines.component')"
          @update:value="(v) => patch(i, { componentId: String(v ?? '') })"
        />
      </div>
      <div class="pr-lines__cell" :data-label="t('lines.formulaOverride')">
        <NInput
          class="pr-lines__formula"
          :value="row.formulaOverride ?? ''"
          :disabled="readonly"
          size="small"
          :placeholder="t('lines.formulaOverride')"
          @update:value="(v) => patch(i, { formulaOverride: v || null })"
        />
      </div>
      <div class="pr-lines__cell" :data-label="t('lines.amountOverride')">
        <NInputNumber
          class="pr-lines__amount"
          :value="row.amountOverride ?? null"
          :show-button="false"
          :disabled="readonly"
          size="small"
          :placeholder="t('lines.amountOverride')"
          @update:value="(v) => patch(i, { amountOverride: v == null ? null : Number(v) })"
        />
      </div>
      <div class="pr-lines__cell" :data-label="t('lines.conditionOverride')">
        <NInput
          class="pr-lines__condition"
          :value="row.conditionOverride ?? ''"
          :disabled="readonly"
          size="small"
          :placeholder="t('lines.conditionOverride')"
          @update:value="(v) => patch(i, { conditionOverride: v || null })"
        />
      </div>
      <NButton v-if="!readonly" quaternary circle size="small" type="error" class="pr-lines__remove" @click="remove(i)">
        <template #icon><TSvgIcon icon="mdi:close" :size="14" /></template>
      </NButton>
    </div>
    <NButton v-if="!readonly" size="small" dashed block @click="add">
      <template #icon><TSvgIcon icon="mdi:plus" :size="14" /></template>
      {{ t('lines.add') }}
    </NButton>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { NButton, NInput, NInputNumber, NSelect } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import type { SalaryComponentDto, SalaryStructureLineInputDto } from '../../../services/bridges/payroll-bridge'

const props = withDefaults(
  defineProps<{
    value?: SalaryStructureLineInputDto[]
    components?: SalaryComponentDto[]
    readonly?: boolean
    translate?: (key: string) => string
  }>(),
  { value: () => [], components: () => [], readonly: false, translate: undefined },
)

const emit = defineEmits<{ 'update:value': [SalaryStructureLineInputDto[]] }>()

const t = (key: string) => (props.translate ? props.translate(key) : key)

const rows = computed<SalaryStructureLineInputDto[]>(() => props.value ?? [])

const componentOptions = computed(() =>
  (props.components ?? []).map((c) => ({ label: `${c.code} · ${c.name}`, value: c.id })),
)

function emitRows(next: SalaryStructureLineInputDto[]) {
  emit('update:value', next)
}

function patch(index: number, patchData: Partial<SalaryStructureLineInputDto>) {
  emitRows(rows.value.map((r, i) => (i === index ? { ...r, ...patchData } : r)))
}

function add() {
  const nextSeq = rows.value.reduce((max, r) => Math.max(max, r.sequence), 0) + 1
  emitRows([
    ...rows.value,
    { componentId: '', sequence: nextSeq, formulaOverride: null, amountOverride: null, conditionOverride: null },
  ])
}

function remove(index: number) {
  emitRows(rows.value.filter((_, i) => i !== index))
}
</script>

<style scoped>
.pr-lines {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.pr-lines__hint {
  font-size: 12px;
  color: var(--tnzi-text-secondary, rgba(0, 0, 0, 0.55));
}

.pr-lines__empty {
  font-size: 13px;
  color: var(--tnzi-text-secondary, rgba(0, 0, 0, 0.55));
  padding: 8px 0;
}

.pr-lines__row {
  display: grid;
  grid-template-columns: 72px minmax(140px, 1.4fr) minmax(120px, 1.6fr) 120px minmax(120px, 1.4fr) 32px;
  gap: 8px;
  align-items: center;
}

/* Desktop: transparent wrapper — the input becomes the grid cell directly, so
   the original column sizing is untouched and the label stays hidden. */
.pr-lines__cell {
  display: contents;
}
.pr-lines__cell[data-label]::before {
  content: none;
}

/* Phone (<md): the multi-column grid had no header at all - stack each row into a
   single-column labeled card so componentId/formula/amount/condition are legible
   and the panel never scrolls horizontally (content-page iron-law). */
@media (max-width: 767px) {
  .pr-lines__row {
    grid-template-columns: 1fr;
    gap: 10px;
    padding: 12px;
    border: 1px solid var(--tnzi-border, rgba(0, 0, 0, 0.09));
    border-radius: var(--tnzi-admin-radius-md, 8px);
  }

  .pr-lines__cell {
    display: flex;
    flex-direction: column;
    gap: 4px;
    min-width: 0;
  }
  .pr-lines__cell[data-label]::before {
    content: attr(data-label);
    font-size: 12px;
    color: var(--tnzi-text-secondary, rgba(0, 0, 0, 0.55));
  }

  /* Delete row moves to the card end as a full-width ≥44px touch target. */
  .pr-lines__remove.n-button {
    width: 100%;
    height: 44px;
    border-radius: var(--tnzi-admin-radius-md, 8px);
  }
}
</style>
