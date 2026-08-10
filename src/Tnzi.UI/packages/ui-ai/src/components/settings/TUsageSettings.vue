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

const props = defineProps<{
  controller: UseAiUsageReturn
}>()

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
</script>

<template>
  <TSettingGroup title="Token usage" :separator="false">
    <template v-if="controller.enabled.value && q">
      <!-- An unlimited quota gets a count, not a meter: a bar measures how
           much of a budget is gone, and there is no budget to be part-way
           through. -->
      <TSettingRow
        label="Today"
        :description="dailyUnlimited
          ? `${formatTokens(q.currentDailyUsage)} tokens used`
          : `${formatTokens(q.currentDailyUsage)} of ${formatTokens(q.dailyTokenLimit)} tokens used`"
        :stacked="!dailyUnlimited"
      >
        <NProgress
          v-if="!dailyUnlimited"
          type="line"
          :percentage="usageBarPercent(q.dailyUsagePercentage)"
          :status="meterStatus"
          :height="8"
        />
        <span v-else class="t-settings-field__readonly">No daily limit</span>
      </TSettingRow>

      <TSettingRow
        label="This month"
        :description="monthlyUnlimited
          ? `${formatTokens(q.currentMonthlyUsage)} tokens used`
          : `${formatTokens(q.currentMonthlyUsage)} of ${formatTokens(q.monthlyTokenLimit)} tokens used`"
        :stacked="!monthlyUnlimited"
      >
        <NProgress
          v-if="!monthlyUnlimited"
          type="line"
          :percentage="usageBarPercent(q.monthlyUsagePercentage)"
          :status="meterStatus"
          :height="8"
        />
        <span v-else class="t-settings-field__readonly">No monthly limit</span>
      </TSettingRow>

      <TSettingRow v-if="!dailyUnlimited" label="Remaining today">
        <span class="t-settings-field__readonly">
          {{ formatTokens(q.remainingDailyQuota) }} tokens
        </span>
      </TSettingRow>

      <TSettingRow v-if="!monthlyUnlimited" label="Remaining this month">
        <span class="t-settings-field__readonly">
          {{ formatTokens(q.remainingMonthlyQuota) }} tokens
        </span>
      </TSettingRow>
    </template>

    <p v-else-if="controller.loading.value" class="t-settings-field__hint">Loading…</p>

    <!-- Not an error: this deployment does not meter usage. -->
    <p v-else class="t-settings-field__hint">
      No usage limit is in force on this deployment.
    </p>
  </TSettingGroup>
</template>
