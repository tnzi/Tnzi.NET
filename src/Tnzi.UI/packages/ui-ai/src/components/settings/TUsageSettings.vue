<script setup lang="ts">
/**
 * @experimental
 * TUsageSettings - the signed-in user's token quota and how much of it is left.
 *
 * Backed by `GET /quotas/me` (`Tnzi.AI`, user-facing), so it ships wired: the
 * consumer supplies a client and nothing else.
 *
 * ★ Renders "no limit in force" rather than an error when the deployment has
 * quotas switched off. Those are different facts, and a red banner on a page a
 * user merely opened would report a problem that does not exist.
 */
import { onMounted, computed } from 'vue'
import { NProgress } from 'naive-ui'
import { QuotaWarningLevel } from '@tnzi/core/services/ai'
import TSettingGroup from '../layout/TSettingGroup.vue'
import TSettingRow from '../layout/TSettingRow.vue'
import {
  usageBarPercent,
  formatTokens,
  isUnlimited,
  type UseAiUsageReturn,
} from '../../headless/useAiUsage'
import { useAiI18n, formatAiMessage } from '../../i18n'

const props = defineProps<{
  controller: UseAiUsageReturn
}>()

const t = useAiI18n()

onMounted(() => {
  void props.controller.load()
})

/* `warningLevel` is the backend's own judgement (it owns the thresholds), so
   the colour follows it rather than a second set of numbers here that could
   disagree with the ones enforcing the quota. */
const meterStatus = computed<'success' | 'warning' | 'error'>(() => {
  const level = props.controller.quota.value?.warningLevel
  if (level === QuotaWarningLevel.Critical) return 'error'
  if (level === QuotaWarningLevel.Warning) return 'warning'
  return 'success'
})

const q = computed(() => props.controller.quota.value)
const dailyUnlimited = computed(() => isUnlimited(q.value?.dailyTokenLimit))
const monthlyUnlimited = computed(() => isUnlimited(q.value?.monthlyTokenLimit))

/* An unlimited quota is described as a bare count; a limited one as a share of
   its budget. */
function usageLine(used: number | undefined, limit: number | undefined, unlimited: boolean): string {
  return unlimited
    ? formatAiMessage(t.value.usageSettings.used, { used: formatTokens(used) })
    : formatAiMessage(t.value.usageSettings.usedOfLimit, { used: formatTokens(used), limit: formatTokens(limit) })
}

function tokenCount(count: number | undefined): string {
  return formatAiMessage(t.value.usageSettings.tokens, { count: formatTokens(count) })
}
</script>

<template>
  <TSettingGroup :title="t.usageSettings.title" :separator="false">
    <template v-if="controller.enabled.value && q">
      <!-- An unlimited quota gets a count, not a meter: a bar measures how
           much of a budget is gone, and there is no budget to be part-way
           through. -->
      <TSettingRow
        :label="t.usageSettings.today"
        :description="usageLine(q.currentDailyUsage, q.dailyTokenLimit, dailyUnlimited)"
        :stacked="!dailyUnlimited"
      >
        <NProgress
          v-if="!dailyUnlimited"
          type="line"
          :percentage="usageBarPercent(q.dailyUsagePercentage)"
          :status="meterStatus"
          :height="8"
        />
        <span v-else class="t-settings-field__readonly">{{ t.usageSettings.noDailyLimit }}</span>
      </TSettingRow>

      <TSettingRow
        :label="t.usageSettings.thisMonth"
        :description="usageLine(q.currentMonthlyUsage, q.monthlyTokenLimit, monthlyUnlimited)"
        :stacked="!monthlyUnlimited"
      >
        <NProgress
          v-if="!monthlyUnlimited"
          type="line"
          :percentage="usageBarPercent(q.monthlyUsagePercentage)"
          :status="meterStatus"
          :height="8"
        />
        <span v-else class="t-settings-field__readonly">{{ t.usageSettings.noMonthlyLimit }}</span>
      </TSettingRow>

      <TSettingRow v-if="!dailyUnlimited" :label="t.usageSettings.remainingToday">
        <span class="t-settings-field__readonly">
          {{ tokenCount(q.remainingDailyQuota) }}
        </span>
      </TSettingRow>

      <TSettingRow v-if="!monthlyUnlimited" :label="t.usageSettings.remainingThisMonth">
        <span class="t-settings-field__readonly">
          {{ tokenCount(q.remainingMonthlyQuota) }}
        </span>
      </TSettingRow>
    </template>

    <p v-else-if="controller.loading.value" class="t-settings-field__hint">{{ t.usageSettings.loading }}</p>

    <!-- Not an error: this deployment does not meter usage. -->
    <p v-else class="t-settings-field__hint">
      {{ t.usageSettings.noLimit }}
    </p>
  </TSettingGroup>
</template>
