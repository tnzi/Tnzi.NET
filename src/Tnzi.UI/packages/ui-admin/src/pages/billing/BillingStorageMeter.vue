<template>
  <span v-if="!usage.usageAvailable" class="text-muted">{{ t('subscription.storageUnavailable') }}</span>
  <span v-else-if="usage.usedBytes == null" class="text-muted">{{ t('subscription.storagePending') }}</span>
  <div v-else class="t-billing-storage" :class="{ 't-billing-storage--over': over }">
    <NProgress
      v-if="hasAllowance"
      type="line"
      :percentage="percent"
      :status="over ? 'warning' : 'default'"
      :show-indicator="false"
      :height="6"
    />
    <div class="flex flex-wrap justify-between gap-x-12px">
      <span data-testid="billing-storage-used">{{ usedText }}</span>
      <span class="text-12px text-muted">{{ t('subscription.storageSampled', { date: formatDateOnly(usage.sampledOn, { dateStyle: 'medium' }) }) }}</span>
    </div>
    <span v-if="over && !usage.subscribed" class="text-12px t-billing-storage__over" data-testid="billing-storage-over">
      {{ overText }}
    </span>
  </div>
</template>

<script setup lang="ts">
/**
 * One storage reading: used against the plan's allowance, when it was taken,
 * and - before subscribing - a line saying the account is already above the
 * allowance. Nothing accrues without a subscription, so that line names the
 * rate, never an amount. With no allowance to read against (a catalogue of
 * several plans) it shows usage alone.
 */
import { computed } from 'vue'
import { NProgress } from 'naive-ui'
import { formatCurrency, formatDateOnly } from '@tnzi/core'
import { formatStorageGb, type StorageUsageDto } from '@tnzi/core/services/payment'
import { billingT as t } from './billing-context'

const props = defineProps<{ usage: StorageUsageDto }>()

const hasAllowance = computed(() => props.usage.includedBytes != null)
const over = computed(() => hasAllowance.value && (props.usage.usedBytes ?? 0) > (props.usage.includedBytes ?? 0))
const percent = computed(() => {
  const included = props.usage.includedBytes ?? 0
  const used = props.usage.usedBytes ?? 0
  if (included <= 0) return used > 0 ? 100 : 0
  return Math.min(100, Math.round((used / included) * 100))
})
const usedText = computed(() =>
  hasAllowance.value
    ? t('subscription.storageUsed', { used: formatStorageGb(props.usage.usedBytes), included: formatStorageGb(props.usage.includedBytes) })
    : t('subscription.storageUsedOnly', { used: formatStorageGb(props.usage.usedBytes) }),
)
const overText = computed(() => {
  const price = props.usage.overageUnitPrice
  return price
    ? t('subscription.storageOverBeforeSubscribingPriced', { price: formatCurrency(price, props.usage.currency ?? undefined) })
    : t('subscription.storageOverBeforeSubscribing')
})
</script>

<style scoped>
.t-billing-storage {
  display: flex;
  flex-direction: column;
  gap: 4px;
  max-width: 360px;
}
.t-billing-storage__over {
  color: var(--tnzi-warning, #f0a020);
}
</style>
