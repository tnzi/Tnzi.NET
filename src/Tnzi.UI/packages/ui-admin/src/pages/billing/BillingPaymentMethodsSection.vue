<template>
  <div class="t-billing-methods-section">
    <BillingPaymentMethods ref="block" :can-manage="canManage" />
    <p v-if="!canManage" class="mt-12px text-13px text-muted" data-testid="billing-read-only">{{ t('readOnly') }}</p>
  </div>
</template>

<script setup lang="ts">
/**
 * Billing: the account's saved cards on their own (`TBillingPaymentMethodsSection`).
 *
 * @deprecated The cards are part of the subscription section (`TBillingSubscriptionSection`, the
 * `billing-subscription` Settings Center section), which also marks the card renewals are charged to.
 * This standalone form stays for a consumer page that already hosts it and renders the same card block.
 */
import { computed, onMounted, ref } from 'vue'
import type { BillingAccountDto } from '@tnzi/core/services/payment'
import BillingPaymentMethods from './BillingPaymentMethods.vue'
import { billingT as t, useBillingBridge, useBillingStanding } from './billing-context'

const block = ref<InstanceType<typeof BillingPaymentMethods> | null>(null)
const bridge = useBillingBridge()
const store = useBillingStanding(bridge)

const account = ref<BillingAccountDto | null>(null)
const canManage = computed(() => account.value?.canManage ?? store.standing?.canManage ?? false)

onMounted(async () => {
  try {
    account.value = await bridge.getAccount()
  } catch {
    account.value = null
  }
})

defineExpose({ reload: (): Promise<void> | undefined => block.value?.reload() })
</script>
