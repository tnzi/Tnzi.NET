<template>
  <div class="flex flex-col gap-12px" data-testid="billing-plan-list">
    <NAlert v-if="loadError" type="error" :show-icon="true" data-testid="billing-plans-error">
      {{ t('plans.loadFailed', { error: loadError }) }}
    </NAlert>
    <div v-else-if="loading" class="text-13px text-muted">{{ t('loading') }}</div>
    <p v-else-if="!plans.length" class="m-0 text-13px text-muted" data-testid="billing-plans-empty">{{ t('plans.empty') }}</p>
    <div
      v-else-if="offer"
      class="t-billing-plan t-billing-plan--offer"
      :data-testid="`billing-plan-${offer.planCode}`"
      data-single-plan="true"
    >
      <div class="flex items-center gap-6px">
        <span class="font-600 text-15px">{{ offer.planName }}</span>
        <THint :content="t('plans.subscribeHint')" :size="14" />
      </div>
      <div v-if="offer.description" class="text-13px text-muted">{{ offer.description }}</div>
      <div class="text-16px font-600 t-billing-plan__price" data-testid="billing-plan-price">{{ planPriceText(offer) }}</div>
      <div v-if="planTaxText(offer)" class="text-13px text-muted" data-testid="billing-plan-tax">{{ planTaxText(offer) }}</div>
      <div v-if="planStorageText(offer)" class="text-13px text-muted" data-testid="billing-plan-storage">{{ planStorageText(offer) }}</div>
      <NButton
        type="primary"
        size="small"
        class="mt-8px self-start"
        :data-testid="`billing-plan-subscribe-${offer.planCode}`"
        @click="emit('select', offer)"
      >
        {{ t('actions.subscribe') }}
      </NButton>
    </div>
    <template v-else>
      <p class="m-0 text-14px">{{ t('plans.intro') }}</p>
      <div class="t-billing-plans">
        <div v-for="plan in plans" :key="plan.id" class="t-billing-plan" :data-testid="`billing-plan-${plan.planCode}`">
          <div class="font-600 text-15px">{{ plan.planName }}</div>
          <div v-if="plan.description" class="text-13px text-muted">{{ plan.description }}</div>
          <div class="text-16px font-600 t-billing-plan__price" data-testid="billing-plan-price">{{ planPriceText(plan) }}</div>
          <div v-if="planTaxText(plan)" class="text-13px text-muted" data-testid="billing-plan-tax">{{ planTaxText(plan) }}</div>
          <div v-if="planStorageText(plan)" class="text-13px text-muted" data-testid="billing-plan-storage">{{ planStorageText(plan) }}</div>
          <NButton
            type="primary"
            size="small"
            class="mt-8px self-start"
            :data-testid="`billing-plan-subscribe-${plan.planCode}`"
            @click="emit('select', plan)"
          >
            {{ t('actions.subscribe') }}
          </NButton>
        </div>
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
/**
 * The plans an account without a subscription can start: the active plans of
 * the app's product (`defineAdminApp({ billing: { productCode } })`; every
 * active plan when it is unset), each with its price per cycle, whether tax is
 * added, and the storage it includes. Choosing one is the parent's business
 * (`select`).
 *
 * A product that sells exactly one plan gets that plan directly - its terms and
 * a Subscribe button - instead of a one-item catalogue to pick from
 * (`singlePlanOffer`).
 */
import { computed, onMounted, ref } from 'vue'
import { NAlert, NButton } from 'naive-ui'
import { THint } from '@tnzi/ui'
import type { SubscriptionPlanDto } from '@tnzi/core/services/payment'
import { useAdminBillingConfig } from '../../plugin/billing-config'
import { billingT as t, useBillingBridge } from './billing-context'
import { planPriceText, planStorageText, planTaxText, singlePlanOffer } from './billing-plans'

const emit = defineEmits<{ select: [plan: SubscriptionPlanDto]; loaded: [plans: SubscriptionPlanDto[]] }>()

const bridge = useBillingBridge()
const productCode = useAdminBillingConfig()?.productCode

const plans = ref<SubscriptionPlanDto[]>([])
const loading = ref(false)
const loadError = ref<string | null>(null)
const offer = computed(() => singlePlanOffer(plans.value))

onMounted(async () => {
  loading.value = true
  try {
    plans.value = await bridge.listPlans(productCode)
    emit('loaded', plans.value)
  } catch (err) {
    loadError.value = err instanceof Error ? err.message : String(err)
  } finally {
    loading.value = false
  }
})
</script>

<style scoped>
.t-billing-plans {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
  gap: 12px;
}
.t-billing-plan {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 16px;
  border: 1px solid var(--tnzi-border);
  border-radius: 8px;
}
.t-billing-plan--offer {
  max-width: 420px;
}
.t-billing-plan__price {
  font-variant-numeric: tabular-nums;
}
</style>
