<script setup lang="ts">
/**
 * TContextMeter - how full the conversation's context window was as its last turn ended, as a
 * small ring and the share used ("12%"; the tokens when the window is not known). Hovering or
 * focusing it gives the tokens used against the window in a tooltip, which is also its accessible
 * name. It turns the warning colour past 80% and the danger colour past 95%, where the next turn
 * may get its history compacted. Sized for the composer's toolbar (`#thread-composer-right`).
 *
 * The numbers are an external CLI run's `contextTokens` / `contextWindow` (`contextUsageOf(run)`).
 */
import { computed } from 'vue'
import { Icon } from '@iconify/vue'
import { formatAiMessage, useAiI18n } from '../../i18n/index'
import { vTooltip } from '../../headless/tooltip'
import { compactTokens, contextLevel, contextShare, sharePercent } from '../../headless/context-usage'

const props = defineProps<{
  /** Tokens in the context window. */
  tokens: number
  /** The window's size; without it the meter shows the tokens alone. */
  window?: number | null
}>()

const t = useAiI18n()

const RADIUS = 6
const CIRCUMFERENCE = 2 * Math.PI * RADIUS

const share = computed(() => contextShare({ tokens: props.tokens, window: props.window }))
const level = computed(() => contextLevel(share.value))
const used = computed(() => compactTokens(props.tokens))
const text = computed(() => (share.value === null ? used.value : sharePercent(share.value)))
const detail = computed(() =>
  share.value === null || !props.window
    ? formatAiMessage(t.value.context.usedOnly, { used: used.value })
    : formatAiMessage(t.value.context.used, { used: used.value, window: compactTokens(props.window) }),
)
const offset = computed(() => CIRCUMFERENCE * (1 - (share.value ?? 0)))
</script>

<template>
  <span
    v-tooltip="{ text: detail, side: 'top' }"
    class="t-context-meter"
    :class="`is-${level}`"
    tabindex="0"
    role="img"
    :aria-label="detail"
    data-context-meter
    :data-level="level"
  >
    <svg v-if="share !== null" class="t-context-meter__ring" viewBox="0 0 16 16" aria-hidden="true">
      <circle cx="8" cy="8" :r="RADIUS" class="t-context-meter__track" />
      <circle cx="8" cy="8" :r="RADIUS" class="t-context-meter__fill" :stroke-dasharray="CIRCUMFERENCE" :stroke-dashoffset="offset" />
    </svg>
    <Icon v-else icon="lucide:gauge" class="t-context-meter__icon" aria-hidden="true" />
    <span class="t-context-meter__text">{{ text }}</span>
  </span>
</template>

<style scoped>
.t-context-meter {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  height: 32px;
  padding: 0 8px;
  border-radius: 999px;
  color: var(--tnzi-ai-text-tertiary);
  font-size: 12px;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
  cursor: default;
}
.t-context-meter:hover,
.t-context-meter:focus-visible {
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text-secondary);
}
.t-context-meter:focus-visible {
  outline: var(--tnzi-ai-focus-outline);
  outline-offset: 2px;
}
.t-context-meter__ring {
  width: 16px;
  height: 16px;
  transform: rotate(-90deg);
}
.t-context-meter__ring circle {
  fill: none;
  stroke-width: 2.5;
}
.t-context-meter__track {
  stroke: var(--tnzi-ai-border-strong);
}
.t-context-meter__fill {
  stroke: var(--tnzi-ai-text-secondary);
  stroke-linecap: round;
}
.t-context-meter__icon {
  font-size: 14px;
}
.t-context-meter.is-warn {
  color: var(--tnzi-ai-warning);
}
.t-context-meter.is-warn .t-context-meter__fill {
  stroke: var(--tnzi-ai-warning);
}
.t-context-meter.is-full {
  color: var(--tnzi-ai-danger);
}
.t-context-meter.is-full .t-context-meter__fill {
  stroke: var(--tnzi-ai-danger);
}
</style>
