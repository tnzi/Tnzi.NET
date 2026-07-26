<script setup lang="ts">
/**
 * `TRelativeTime` - render a timestamp as a human relative phrase
 * ("3 minutes ago", "in 2 days"). Falls back to absolute date for
 * values older than 30 days.
 *
 * Renders an `<NTooltip>` with the absolute timestamp on hover.
 */
import { computed } from 'vue'
import { NTooltip } from 'naive-ui'
import { EMPTY_DASH } from '../../utils/placeholders'

interface Props {
  /** ISO 8601 string, epoch millis, or Date. `null`/`undefined`/`''` renders {@link EMPTY_DASH}. */
  value: string | number | Date | null | undefined
  /** Override locale (defaults to navigator.language). */
  locale?: string
  /** Format used inside the tooltip (defaults to `YYYY-MM-DD HH:mm:ss`). */
  absoluteFormat?: 'iso' | 'datetime' | 'date'
}

const props = withDefaults(defineProps<Props>(), {
  locale: undefined,
  absoluteFormat: 'datetime',
})

function parseValue(v: Props['value']): Date | null {
  if (v === null || v === undefined || v === '') return null
  if (v instanceof Date) return isNaN(v.getTime()) ? null : v
  const d = new Date(v)
  return isNaN(d.getTime()) ? null : d
}

const RTF_UNITS: Array<{ unit: Intl.RelativeTimeFormatUnit; ms: number }> = [
  { unit: 'year',   ms: 365 * 24 * 60 * 60 * 1000 },
  { unit: 'month',  ms: 30 * 24 * 60 * 60 * 1000 },
  { unit: 'day',    ms: 24 * 60 * 60 * 1000 },
  { unit: 'hour',   ms: 60 * 60 * 1000 },
  { unit: 'minute', ms: 60 * 1000 },
  { unit: 'second', ms: 1000 },
]

const parsed = computed(() => parseValue(props.value))

const relative = computed<string>(() => {
  if (!parsed.value) return '-'
  const now = Date.now()
  const diff = parsed.value.getTime() - now
  const absDiff = Math.abs(diff)

  // Older than 30 days → show absolute date, not relative
  if (absDiff > 30 * 24 * 60 * 60 * 1000) {
    return absoluteValue.value
  }

  const rtf = new Intl.RelativeTimeFormat(props.locale ?? navigator.language, {
    numeric: 'auto',
  })
  for (const { unit, ms } of RTF_UNITS) {
    if (absDiff >= ms || unit === 'second') {
      const value = Math.round(diff / ms)
      return rtf.format(value, unit)
    }
  }
  return rtf.format(0, 'second')
})

const absoluteValue = computed<string>(() => {
  if (!parsed.value) return '-'
  if (props.absoluteFormat === 'iso') {
    return parsed.value.toISOString()
  }
  if (props.absoluteFormat === 'date') {
    return parsed.value.toLocaleDateString(props.locale)
  }
  return parsed.value.toLocaleString(props.locale, {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false,
  })
})
</script>

<template>
  <NTooltip v-if="parsed" placement="top" :delay="200">
    <template #trigger>
      <span class="t-relative-time">{{ relative }}</span>
    </template>
    {{ absoluteValue }}
  </NTooltip>
  <span v-else class="t-relative-time t-relative-time--empty">{{ EMPTY_DASH }}</span>
</template>

<style scoped>
.t-relative-time {
  display: inline-block;
  color: var(--tnzi-base-text);
  cursor: default;
}
.t-relative-time--empty {
  color: var(--tnzi-base-text-muted);
}
</style>
