<template>
  <div class="t-rule-builder">
    <div class="t-rule-builder__head">
      <span class="t-rule-builder__lead">{{ label('when') }}</span>
      <NRadioGroup :value="matchMode" size="small" :disabled="readonly" @update:value="(v: string) => emit('update:matchMode', v)">
        <NRadioButton :value="ALL">{{ label('all') }}</NRadioButton>
        <NRadioButton :value="ANY">{{ label('any') }}</NRadioButton>
      </NRadioGroup>
      <span class="t-rule-builder__lead">{{ matchMode === ANY ? label('anySuffix') : label('allSuffix') }}</span>
    </div>

    <ul class="t-rule-builder__rows">
      <li v-for="(row, index) in modelValue" :key="index" class="t-rule-builder__row">
        <NSelect
          :value="row.field"
          :options="fieldOptions"
          :disabled="readonly"
          size="small"
          class="t-rule-builder__field"
          @update:value="(v: RuleField) => patch(index, { field: v, operator: defaultOperator(v) })"
        />
        <NSelect
          :value="row.operator"
          :options="operatorOptionsFor(row.field)"
          :disabled="readonly"
          size="small"
          class="t-rule-builder__op"
          @update:value="(v: RuleOperator) => patch(index, { operator: v })"
        />
        <NInput
          :value="row.value"
          :disabled="readonly"
          size="small"
          class="t-rule-builder__value"
          :placeholder="row.field === AMOUNT ? label('amountPlaceholder') : label('textPlaceholder')"
          @update:value="(v: string) => patch(index, { value: v })"
        />
        <NButton v-if="!readonly" quaternary circle size="small" :aria-label="label('remove')" @click="remove(index)">
          <TSvgIcon icon="mdi:close" :size="16" />
        </NButton>
      </li>
    </ul>

    <!-- A rule with no conditions is legitimate (account + direction only), but
         it is also the one most likely to be an accident, so it says so. -->
    <NAlert v-if="modelValue.length === 0" type="warning" :bordered="false" class="t-rule-builder__empty">
      {{ label('noConditions') }}
    </NAlert>

    <NButton v-if="!readonly" size="small" dashed block @click="add">
      <template #icon><TSvgIcon icon="mdi:plus" :size="16" /></template>
      {{ label('addCondition') }}
    </NButton>
  </div>
</template>

<script setup lang="ts">
/**
 * `TRuleBuilder` - condition editor for a bank rule.
 *
 * Two things it enforces that a pair of free-form dropdowns would not:
 *
 * - **The operator list follows the field.** "Greater than" on a description
 *   and "contains" on an amount both parse fine and then silently never match,
 *   which is far worse to diagnose than being unable to pick them. Changing the
 *   field re-picks a valid operator rather than leaving an impossible pair.
 * - **An empty condition list is called out.** It is a valid rule (scoped by
 *   account and direction alone), but it is also what an unfinished rule looks
 *   like, and it would quietly claim every line in range.
 *
 * Public on purpose: consumer apps building their own rule screens get the same
 * guard rails instead of re-deriving them.
 */
import { computed } from 'vue'
import { NAlert, NButton, NInput, NRadioButton, NRadioGroup, NSelect } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'

/** Wire values are the backend's PascalCase enum names. */
export type RuleField = 'Description' | 'Payee' | 'Reference' | 'Amount'
export type RuleOperator = 'Contains' | 'NotContains' | 'Equals' | 'StartsWith' | 'EndsWith' | 'GreaterThan' | 'LessThan'

export interface RuleConditionRow {
  field: RuleField
  operator: RuleOperator
  value: string
}

const props = withDefaults(
  defineProps<{
    modelValue: RuleConditionRow[]
    matchMode: string
    readonly?: boolean
    /** i18n lookup relative to `rules.*`. */
    translate?: (key: string) => string
  }>(),
  { readonly: false },
)

const emit = defineEmits<{
  'update:modelValue': [RuleConditionRow[]]
  'update:matchMode': [string]
}>()

const ALL = 'All'
const ANY = 'Any'
const AMOUNT: RuleField = 'Amount'

const FALLBACK: Record<string, string> = {
  when: 'When',
  all: 'all',
  any: 'any',
  allSuffix: 'of these are true',
  anySuffix: 'of these is true',
  addCondition: 'Add condition',
  remove: 'Remove condition',
  textPlaceholder: 'Text to look for',
  amountPlaceholder: 'Amount, e.g. 50',
  noConditions: 'This rule has no conditions - it will claim every line that matches its account and direction.',
  'field.Description': 'Description',
  'field.Payee': 'Payee',
  'field.Reference': 'Reference',
  'field.Amount': 'Amount',
  'op.Contains': 'contains',
  'op.NotContains': 'does not contain',
  'op.Equals': 'is',
  'op.StartsWith': 'starts with',
  'op.EndsWith': 'ends with',
  'op.GreaterThan': 'is greater than',
  'op.LessThan': 'is less than',
}

function label(key: string): string {
  const translated = props.translate?.(`rules.${key}`)
  if (translated && !translated.includes(`rules.${key}`)) return translated
  return FALLBACK[key] ?? key
}

const FIELDS: RuleField[] = ['Description', 'Payee', 'Reference', 'Amount']
const TEXT_OPERATORS: RuleOperator[] = ['Contains', 'NotContains', 'Equals', 'StartsWith', 'EndsWith']
const AMOUNT_OPERATORS: RuleOperator[] = ['Equals', 'GreaterThan', 'LessThan']

const fieldOptions = computed(() => FIELDS.map((f) => ({ label: label(`field.${f}`), value: f })))

/** The operator list follows the field - a mismatched pair never matches. */
function operatorOptionsFor(field: RuleField) {
  const ops = field === AMOUNT ? AMOUNT_OPERATORS : TEXT_OPERATORS
  return ops.map((o) => ({ label: label(`op.${o}`), value: o }))
}

function defaultOperator(field: RuleField): RuleOperator {
  return field === AMOUNT ? 'Equals' : 'Contains'
}

function patch(index: number, changes: Partial<RuleConditionRow>) {
  const next = props.modelValue.map((row, i) => (i === index ? { ...row, ...changes } : row))
  emit('update:modelValue', next)
}

function add() {
  emit('update:modelValue', [...props.modelValue, { field: 'Description', operator: 'Contains', value: '' }])
}

function remove(index: number) {
  emit('update:modelValue', props.modelValue.filter((_, i) => i !== index))
}
</script>

<style scoped>
.t-rule-builder {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.t-rule-builder__head {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  font-size: 13px;
}

.t-rule-builder__lead {
  color: var(--tnzi-base-text-muted);
}

.t-rule-builder__rows {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin: 0;
  padding: 0;
  list-style: none;
}

.t-rule-builder__row {
  display: flex;
  align-items: center;
  gap: 8px;
}

.t-rule-builder__field {
  width: 140px;
  flex-shrink: 0;
}

.t-rule-builder__op {
  width: 170px;
  flex-shrink: 0;
}

.t-rule-builder__value {
  flex: 1 1 auto;
  min-width: 0;
}

.t-rule-builder__empty {
  font-size: 12px;
}

@media (max-width: 767px) {
  /* Three controls per row do not fit a phone; stack them and let the value
     field own the full width. */
  .t-rule-builder__row {
    flex-wrap: wrap;
  }

  .t-rule-builder__field,
  .t-rule-builder__op {
    width: calc(50% - 20px);
  }

  .t-rule-builder__value {
    flex-basis: 100%;
  }
}
</style>
