<template>
  <TStatusBadge :value="value" :type="mapping.type" :label="mapping.label" />
</template>

<script setup lang="ts">
/**
 * `TDocStatusBadge` - one vocabulary for finance document state.
 *
 * Every finance list had its own `h(NTag)` with its own colour choice, so
 * "Posted" was green on one page and blue on the next, and `Voided` vs
 * `Reversed` - which mean different things to an accountant - looked
 * identical. The mapping lives here once.
 *
 * The tone rules encode the audit stance the module takes: a **Draft** is
 * neutral (nothing has happened yet), **Posted** is success (it is in the
 * ledger), and **Voided / Reversed** are warnings rather than errors - an
 * error tone would read as "something went wrong", when in fact voiding is the
 * correct, expected way to undo work in a ledger that never deletes.
 */
import { computed } from 'vue'
import TStatusBadge from '../display/TStatusBadge.vue'

type Tone = 'default' | 'success' | 'warning' | 'error' | 'info'

const props = defineProps<{
  /** Wire value, PascalCase (the backend serializes enums by name). */
  value?: string | number | null
  /** i18n lookup relative to `finance.docs.status.*`. */
  translate?: (key: string) => string
}>()

const TONES: Record<string, Tone> = {
  Draft: 'default',
  Posted: 'success',
  Voided: 'warning',
  Reversed: 'warning',
  Reconciled: 'success',
  Completed: 'success',
  Paid: 'success',
  PartiallyPaid: 'info',
  Overdue: 'error',
  Open: 'info',
  Pending: 'warning',
  Matched: 'success',
  Excluded: 'default',
  Issued: 'success',
  Spoiled: 'warning',
  Generated: 'success',
}

/** Humanized fallback for a code with no message: `PartiallyPaid` → `Partially paid`. */
function humanize(code: string): string {
  const spaced = code.replace(/([a-z0-9])([A-Z])/g, '$1 $2')
  return spaced.charAt(0).toUpperCase() + spaced.slice(1).toLowerCase()
}

const mapping = computed(() => {
  const code = props.value === null || props.value === undefined ? '' : String(props.value)
  const translated = props.translate?.(`docs.status.${code}`)
  // `makePageTranslator` echoes an unknown key back - treat that as "no
  // message" and humanize rather than rendering `docs.status.PartiallyPaid`.
  const label = translated && !translated.includes(`docs.status.${code}`) ? translated : humanize(code)
  return { type: TONES[code] ?? 'default', label }
})
</script>
