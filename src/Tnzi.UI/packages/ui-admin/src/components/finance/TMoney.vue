<template>
  <component
    :is="drilldown ? 'button' : 'span'"
    :class="classes"
    :type="drilldown ? 'button' : undefined"
    :aria-label="ariaLabel"
    :title="drilldown ? drilldownHint : undefined"
    @click="onClick"
  >
    <span aria-hidden="true">{{ text }}</span>
  </component>
</template>

<script setup lang="ts">
/**
 * `TMoney` - the money display primitive every finance surface renders through.
 *
 * It bakes in the North-American accounting conventions so no page has to
 * remember them (see `utils/finance-format.ts` for the reasoning):
 *
 * - negatives as `(1,234.56)`, never `-1,234.56`
 * - `font-variant-numeric: tabular-nums` so columns of figures line up
 * - the visible glyph is `aria-hidden`; the accessible name carries a real
 *   `-` sign, because parentheses and red text are both purely visual
 *   (AODA / WCAG 2.0 AA)
 * - no `$` is invented when the document carries no currency code
 *
 * `drilldown` turns the amount into a real `<button>` - that is the Stripe
 * "every number can be verified" affordance, and it must be keyboard-reachable
 * rather than a `<span>` with a click handler.
 */
import { computed } from 'vue'
import { formatMoney, srMoney, type FormatMoneyOptions } from '../../utils/finance-format'

const props = withDefaults(
  defineProps<{
    value?: number | null
    /** ISO currency code. Omit for bare numbers (report bodies). */
    currency?: string | null
    decimals?: number
    /** Render `0` as an em-dash (report totals). */
    zeroDash?: boolean
    /** Always show `+` on positives (variance columns). */
    signed?: boolean
    /**
     * Colour treatment. `auto` tints negatives; `sign` tints both directions
     * (variance columns); `none` stays neutral (most ledger columns - a wall
     * of red is noise, and negative liabilities/income are normal).
     */
    tone?: 'none' | 'auto' | 'sign'
    strong?: boolean
    /** Bigger figure for KPI tiles. */
    size?: 'sm' | 'md' | 'lg'
    /** Make the amount an activatable button that emits `drilldown`. */
    drilldown?: boolean
    drilldownHint?: string
    /** Prefix the accessible name, e.g. "Total assets". */
    label?: string
  }>(),
  { tone: 'none', size: 'md' },
)

const emit = defineEmits<{ drilldown: [] }>()

const options = computed<FormatMoneyOptions>(() => ({
  currency: props.currency,
  decimals: props.decimals,
  zeroDash: props.zeroDash,
  signed: props.signed,
}))

const text = computed(() => formatMoney(props.value, options.value))

const ariaLabel = computed(() => {
  const amount = srMoney(props.value, options.value)
  return props.label ? `${props.label}: ${amount}` : amount
})

const isNegative = computed(() => typeof props.value === 'number' && props.value < 0)
const isPositive = computed(() => typeof props.value === 'number' && props.value > 0)

const classes = computed(() => [
  't-money',
  `t-money--${props.size}`,
  props.strong ? 't-money--strong' : '',
  props.drilldown ? 't-money--drilldown' : '',
  props.tone !== 'none' && isNegative.value ? 't-money--negative' : '',
  props.tone === 'sign' && isPositive.value ? 't-money--positive' : '',
])

function onClick() {
  if (props.drilldown) emit('drilldown')
}
</script>

<style scoped>
.t-money {
  font-variant-numeric: tabular-nums;
  /* `lining-nums` matters on fonts that default to old-style figures, where
     3/4/7/9 drop below the baseline and ruin a column of amounts. */
  font-feature-settings: 'tnum' 1, 'lnum' 1;
  white-space: nowrap;
  color: inherit;
}

.t-money--sm {
  font-size: 12px;
}

.t-money--lg {
  font-size: 22px;
  font-weight: 600;
  letter-spacing: -0.01em;
}

.t-money--strong {
  font-weight: 600;
}

.t-money--negative {
  color: var(--tnzi-error);
}

.t-money--positive {
  color: var(--tnzi-success);
}

.t-money--drilldown {
  padding: 0;
  border: 0;
  background: none;
  font: inherit;
  cursor: pointer;
  border-bottom: 1px dashed transparent;
  transition: border-color 0.15s var(--tnzi-admin-motion-ease-in-out, ease);
}

.t-money--drilldown:hover,
.t-money--drilldown:focus-visible {
  color: var(--tnzi-primary);
  border-bottom-color: currentColor;
}

.t-money--drilldown:focus-visible {
  outline: 2px solid var(--tnzi-primary);
  outline-offset: 2px;
  border-radius: 2px;
}
</style>
