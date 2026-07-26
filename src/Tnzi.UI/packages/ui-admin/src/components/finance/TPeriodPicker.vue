<template>
  <div class="t-period">
    <NSelect
      v-model:value="presetValue"
      :options="presetOptions"
      size="small"
      class="t-period__preset"
      :aria-label="label('presetLabel')"
    />

    <NDatePicker
      v-if="mode === 'range'"
      :value="rangeTs"
      type="daterange"
      size="small"
      class="t-period__range"
      :actions="null"
      @update:value="onRangeChange"
    />
    <NDatePicker
      v-else
      :value="asOfTs"
      type="date"
      size="small"
      class="t-period__asof"
      :actions="null"
      @update:value="onAsOfChange"
    />

    <NSelect
      v-if="showComparison"
      v-model:value="comparisonValue"
      :options="comparisonOptions"
      size="small"
      class="t-period__compare"
      :aria-label="label('comparisonLabel')"
    />

    <span v-if="comparisonHint" class="t-period__hint">{{ comparisonHint }}</span>
  </div>
</template>

<script setup lang="ts">
/**
 * `TPeriodPicker` - the single reporting-period control.
 *
 * Every finance page mounts this instead of its own `NDatePicker`, so the
 * selection follows the user across P&L → balance sheet → general ledger
 * (Stripe's "same time control on every page" rule). State lives in
 * `useFinancePeriod`, not in this component, which is why two mounted copies
 * stay in sync.
 *
 * `mode="as-of"` collapses to a single date for point-in-time reports
 * (balance sheet, aging) while still writing into the same shared range - so
 * switching back to a period report keeps a sensible window instead of an
 * empty picker.
 */
import { computed } from 'vue'
import { NDatePicker, NSelect } from 'naive-ui'
import {
  useFinancePeriod,
  type FinanceComparison,
  type FinancePeriodPreset,
} from '../../headless/useFinancePeriod'
import { formatAccountingDateRange, isoDateToLocalTs, tsToIsoDate } from '../../utils/finance-format'

const props = withDefaults(
  defineProps<{
    /** `range` = from/to (P&L, trial balance); `as-of` = single date (BS, aging). */
    mode?: 'range' | 'as-of'
    /** Show the period-over-period selector. Off for point-in-time reports. */
    showComparison?: boolean
    /** i18n lookup; keys are relative to `finance.period.*`. */
    translate?: (key: string) => string
  }>(),
  { mode: 'range', showComparison: false },
)

const emit = defineEmits<{ change: [] }>()

const { period, preset, comparison, comparisonPeriod, setPreset, setRange } = useFinancePeriod()

const FALLBACK: Record<string, string> = {
  presetLabel: 'Period',
  comparisonLabel: 'Compare',
  'preset.this-month': 'This month',
  'preset.last-month': 'Last month',
  'preset.this-quarter': 'This quarter',
  'preset.last-quarter': 'Last quarter',
  'preset.year-to-date': 'Year to date',
  'preset.last-year': 'Last year',
  'preset.custom': 'Custom',
  'comparison.none': 'No comparison',
  'comparison.previous-period': 'vs previous period',
  'comparison.previous-year': 'vs previous year',
}

function label(key: string): string {
  const translated = props.translate?.(`period.${key}`)
  // `makePageTranslator` echoes the key back when a message is missing - fall
  // back to English rather than rendering `period.preset.this-month`.
  if (translated && !translated.includes(`period.${key}`)) return translated
  return FALLBACK[key] ?? key
}

const PRESETS: FinancePeriodPreset[] = [
  'this-month',
  'last-month',
  'this-quarter',
  'last-quarter',
  'year-to-date',
  'last-year',
  'custom',
]

const COMPARISONS: FinanceComparison[] = ['none', 'previous-period', 'previous-year']

const presetOptions = computed(() => PRESETS.map((p) => ({ label: label(`preset.${p}`), value: p })))
const comparisonOptions = computed(() => COMPARISONS.map((c) => ({ label: label(`comparison.${c}`), value: c })))

const presetValue = computed({
  get: () => preset.value,
  set: (v: FinancePeriodPreset) => {
    setPreset(v)
    emit('change')
  },
})

const comparisonValue = computed({
  get: () => comparison.value,
  set: (v: FinanceComparison) => {
    comparison.value = v
    emit('change')
  },
})

const rangeTs = computed<[number, number]>(() => [
  isoDateToLocalTs(period.value.from),
  isoDateToLocalTs(period.value.to),
])

const asOfTs = computed(() => isoDateToLocalTs(period.value.to))

function onRangeChange(value: [number, number] | null) {
  if (!value) return
  setRange({ from: tsToIsoDate(value[0]), to: tsToIsoDate(value[1]) })
  emit('change')
}

function onAsOfChange(value: number | null) {
  if (value === null) return
  const to = tsToIsoDate(value)
  // Keep `from` behind `to` so flipping back to a period report is still a
  // valid range instead of an inverted one the backend would reject.
  const from = period.value.from <= to ? period.value.from : to
  setRange({ from, to })
  emit('change')
}

const comparisonHint = computed(() => {
  if (!props.showComparison || !comparisonPeriod.value) return ''
  return formatAccountingDateRange(comparisonPeriod.value.from, comparisonPeriod.value.to)
})
</script>

<style scoped>
.t-period {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.t-period__preset {
  width: 140px;
}

.t-period__compare {
  width: 170px;
}

.t-period__range {
  width: 250px;
}

.t-period__asof {
  width: 150px;
}

.t-period__hint {
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
  font-variant-numeric: tabular-nums;
}

@media (max-width: 767px) {
  .t-period__preset,
  .t-period__compare,
  .t-period__range,
  .t-period__asof {
    width: 100%;
  }
}
</style>
