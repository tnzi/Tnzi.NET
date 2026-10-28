<script setup lang="ts">
/**
 * @experimental
 * TUsageSettings - how much of each usage window the signed-in user has used,
 * and when each resets.
 *
 * Backed by `GET /usage-limits/me` (`Tnzi.AI`, user-facing), so it ships wired:
 * the consumer supplies a client and nothing else. One row per window (the
 * session window, then the weekly one): a bar, the share used, and the reset;
 * then when the figures were read, with a refresh.
 *
 * Shares only, never amounts: what a turn costs is the operator's business,
 * and a percentage answers the user's actual question ("how much do I have
 * left, and when does it come back").
 *
 * ★ "No limits apply" is a state, not an error: a deployment (or an account)
 * without limits is not something broken.
 */
import { onMounted, computed } from 'vue'
import { NProgress } from 'naive-ui'
import { Icon } from '@iconify/vue'
import { UsageLimitWindow, type MyUsageWindowDto } from '@tnzi/core/services/ai'
import TSettingGroup from '../layout/TSettingGroup.vue'
import TSettingRow from '../layout/TSettingRow.vue'
import { usageBarPercent, type UseAiUsageReturn } from '../../headless/useAiUsage'
import { formatResetLabel, formatWindowDuration, wholePercent } from '../../headless/usage-limits'
import { useAiI18n, formatAiMessage } from '../../i18n'

const props = defineProps<{
  controller: UseAiUsageReturn
}>()

const t = useAiI18n()

onMounted(() => {
  void props.controller.load()
})

const texts = computed(() => t.value.usageSettings)
const limitTexts = computed(() => t.value.usageLimits)

function label(w: MyUsageWindowDto): string {
  return w.window === UsageLimitWindow.Weekly ? texts.value.weekly : texts.value.session
}

function hint(w: MyUsageWindowDto): string {
  const duration = formatWindowDuration(w.durationMinutes, limitTexts.value)
  return formatAiMessage(w.window === UsageLimitWindow.Weekly ? texts.value.weeklyHint : texts.value.sessionHint, { duration })
}

/* Under the bar: the reset when the window is running, otherwise when it starts. */
function resetLine(w: MyUsageWindowDto): string {
  return formatResetLabel(w.resetsAt, limitTexts.value) ?? texts.value.notStarted
}

function usedLine(w: MyUsageWindowDto): string {
  return formatAiMessage(texts.value.used, { percent: wholePercent(w.usedPercent) })
}

/* The colour follows the server's own judgement (it owns the threshold), not
   a second number here that could disagree with the one enforcing it. */
function meterStatus(w: MyUsageWindowDto): 'success' | 'warning' | 'error' {
  if (w.exceeded) return 'error'
  if (w.warningReached) return 'warning'
  return 'success'
}

const lastUpdated = computed(() => {
  const at = props.controller.checkedAt.value
  if (!at) return null
  let time: string
  try {
    time = at.toLocaleTimeString(limitTexts.value.dateLocale || undefined, { hour: 'numeric', minute: '2-digit' })
  } catch {
    time = at.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' })
  }
  return formatAiMessage(texts.value.lastUpdated, { time })
})

const hasReading = computed(() => props.controller.status.value !== null)
</script>

<template>
  <TSettingGroup :title="texts.title" :separator="false">
    <template v-if="hasReading && controller.enabled.value">
      <TSettingRow
        v-for="w in controller.windows.value"
        :key="w.window"
        :label="label(w)"
        :description="hint(w)"
        stacked
      >
        <div v-if="w.limited" class="t-usage-settings__meter">
          <NProgress
            type="line"
            :percentage="usageBarPercent(w.usedPercent)"
            :status="meterStatus(w)"
            :show-indicator="false"
            :height="8"
          />
          <div class="t-usage-settings__meter-line">
            <span>{{ resetLine(w) }}</span>
            <span class="t-usage-settings__percent">{{ usedLine(w) }}</span>
          </div>
        </div>
        <span v-else class="t-settings-field__readonly">{{ texts.noLimit }}</span>
      </TSettingRow>
    </template>

    <p v-else-if="controller.loading.value && !hasReading" class="t-settings-field__hint">{{ texts.loading }}</p>

    <p v-else-if="controller.failed.value && !hasReading" class="t-settings-field__error">{{ texts.loadFailed }}</p>

    <!-- Not an error: no limit applies to this account. -->
    <p v-else class="t-settings-field__hint">{{ texts.noLimits }}</p>

    <div v-if="controller.available.value" class="t-usage-settings__footer">
      <span v-if="lastUpdated">{{ lastUpdated }}</span>
      <span v-if="controller.failed.value && hasReading" class="t-settings-field__error">{{ texts.loadFailed }}</span>
      <button
        type="button"
        class="t-usage-settings__refresh"
        :disabled="controller.loading.value"
        :aria-label="texts.refresh"
        :title="texts.refresh"
        @click="controller.load()"
      >
        <Icon icon="lucide:refresh-cw" :class="{ 'is-spinning': controller.loading.value }" />
      </button>
    </div>
  </TSettingGroup>
</template>

<style scoped>
.t-usage-settings__meter {
  display: flex;
  flex-direction: column;
  gap: 6px;
  width: 100%;
}
.t-usage-settings__meter-line {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  font-size: 12px;
  color: var(--tnzi-ai-text-secondary);
}
.t-usage-settings__percent {
  flex-shrink: 0;
  font-variant-numeric: tabular-nums;
}
.t-usage-settings__footer {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 8px;
  padding-top: 12px;
  font-size: 12px;
  color: var(--tnzi-ai-text-tertiary);
}
.t-usage-settings__refresh {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 26px;
  height: 26px;
  padding: 0;
  border: none;
  border-radius: 6px;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  cursor: pointer;
}
.t-usage-settings__refresh:hover:not(:disabled) {
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text);
}
.t-usage-settings__refresh:disabled {
  cursor: default;
}
.is-spinning {
  animation: t-usage-settings-spin 0.9s linear infinite;
}
@keyframes t-usage-settings-spin {
  to {
    transform: rotate(360deg);
  }
}
</style>
