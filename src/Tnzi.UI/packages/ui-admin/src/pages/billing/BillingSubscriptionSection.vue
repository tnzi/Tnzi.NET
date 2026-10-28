<template>
  <TDetailSection :title="t('sections.subscription')" icon="mdi:card-account-details-outline">
    <template #titleExtra>
      <TStatusBadge
        v-if="standing"
        :value="standing.standing"
        :type="STANDING_TONE[standing.standing] ?? 'default'"
        :label="standingLabel(standing.standing)"
        data-testid="billing-standing"
      />
    </template>
    <template v-if="canManage && subscription" #actions>
      <template v-if="pending">
        <NButton type="primary" size="small" data-testid="billing-finish-payment" @click="payOpen = true">
          {{ t('actions.finishPayment') }}
        </NButton>
        <NButton v-if="planCount !== 1" size="small" :loading="replacing" data-testid="billing-choose-another-plan" @click="chooseAnotherPlan">
          {{ t('actions.chooseAnotherPlan') }}
        </NButton>
      </template>
      <NButton
        v-else-if="needsPayment(subscription)"
        type="primary"
        size="small"
        data-testid="billing-pay-now"
        @click="payOpen = true"
      >
        {{ t('actions.payNow') }}
      </NButton>
    </template>

    <NAlert v-if="loadError" type="error" :show-icon="true" data-testid="billing-subscription-error">
      {{ t('subscription.loadFailed', { error: loadError }) }}
    </NAlert>
    <div v-else-if="loading && !subscription" class="text-13px text-muted">{{ t('loading') }}</div>
    <template v-else-if="!subscription">
      <BillingPlanList v-if="canManage" @select="startSubscribe" @loaded="(plans) => (planCount = plans.length)" />
      <p v-else class="m-0 text-14px text-muted" data-testid="billing-no-subscription">{{ t('subscription.noneReadOnly') }}</p>
      <dl v-if="offerUsage" class="t-billing-facts mt-12px" data-testid="billing-storage-offer">
        <dt class="flex items-center gap-4px">
          {{ t('subscription.storage') }}
          <THint :content="t('subscription.storageOfferHint')" :size="13" />
        </dt>
        <dd data-testid="billing-storage">
          <BillingStorageMeter :usage="offerUsage" />
        </dd>
      </dl>
    </template>
    <template v-else>
      <NAlert v-if="pending" type="info" :show-icon="true" class="mb-12px" data-testid="billing-pending-notice">
        {{ canManage ? t('subscription.pendingNotice') : t('subscription.pendingNoticeReadOnly') }}
      </NAlert>
      <dl v-if="pending && offerUsage" class="t-billing-facts mb-12px" data-testid="billing-storage-offer">
        <dt class="flex items-center gap-4px">
          {{ t('subscription.storage') }}
          <THint :content="t('subscription.storageOfferHint')" :size="13" />
        </dt>
        <dd data-testid="billing-storage">
          <BillingStorageMeter :usage="offerUsage" />
        </dd>
      </dl>
      <NAlert v-else-if="needsPayment(subscription)" :type="isSuspended ? 'error' : 'warning'" :show-icon="true" class="mb-12px">
        {{ attentionText }}
      </NAlert>
      <dl class="t-billing-facts" data-testid="billing-subscription-facts">
        <dt>{{ t('subscription.plan') }}</dt>
        <dd>{{ subscription.planName || EMPTY_DASH }}</dd>
        <dt>{{ t('subscription.status') }}</dt>
        <dd>
          <TStatusBadge
            :value="String(subscription.status)"
            :type="subscriptionTone(subscription.status)"
            :label="subscriptionStatusLabel(subscription.status)"
          />
        </dd>
        <dt>{{ t('subscription.price') }}</dt>
        <dd>{{ formatCurrency(subscription.originalPrice, subscription.currency) }}</dd>
        <dt>{{ t('subscription.nextCharge') }}</dt>
        <dd>{{ formatDateOnly(subscription.nextBillingTime, { fallback: EMPTY_DASH }) }}</dd>
        <template v-if="usage?.metered && usage.subscribed">
          <dt class="flex items-center gap-4px">
            {{ t('subscription.storage') }}
            <THint :content="usage.inTrial ? t('subscription.storageTrialHint') : t('subscription.storageHint')" :size="13" />
          </dt>
          <dd data-testid="billing-storage">
            <BillingStorageMeter :usage="usage" />
          </dd>
          <template v-if="usage.accruedOverageAmount > 0">
            <dt>{{ t('subscription.storageOverage') }}</dt>
            <dd data-testid="billing-storage-overage">
              {{ t('subscription.storageOverageValue', { amount: formatCurrency(usage.accruedOverageAmount, usage.currency ?? subscription.currency), quantity: usage.overageQuantity }) }}
            </dd>
          </template>
        </template>
      </dl>
    </template>

    <!-- The card the subscription is charged to and the account's other cards: one concern for the customer. -->
    <BillingPaymentMethods
      ref="cards"
      class="t-billing-cards-block"
      :can-manage="canManage"
      :subscription="subscription ?? null"
      @changed="onCardsChanged"
    />
    <p v-if="!canManage && !loadError" class="mt-12px text-13px text-muted" data-testid="billing-read-only">{{ t('readOnly') }}</p>

    <BillingPayNowModal
      v-model:show="payOpen"
      :subscription-id="subscription?.id ?? null"
      :purpose="pending ? 'firstPayment' : 'recovery'"
      @paid="onPaid"
      @update:show="onPayDialogShow"
    />
    <BillingSubscribeModal v-model:show="subscribeOpen" :plan="chosenPlan" @subscribed="onPaid" @update:show="onSubscribeDialogShow" />
  </TDetailSection>
</template>

<script setup lang="ts">
/**
 * Billing: the account's subscription and the cards it is paid with - plan,
 * price, next charge and standing, then the saved cards with the one renewals
 * are charged to marked on it. Billing managers (the server's `canManage`) pay
 * an overdue amount here, replace the renewal card, add cards, choose the
 * default and remove cards; everyone else with `payment.account.view` reads it.
 *
 * With no subscription in force a manager picks a plan and pays the first
 * period here (the card is saved for renewals); a sign-up whose first payment
 * was not completed is finished, never doubled, or replaced by another plan.
 *
 * Hostable on its own (`TBillingSubscriptionSection`) and as a Settings Center
 * section (`billingSettingsSections()`, both from `@tnzi/ui-admin/billing`); the suspended screen embeds it too.
 */
import { computed, onMounted, ref } from 'vue'
import { NAlert, NButton } from 'naive-ui'
import { THint } from '@tnzi/ui'
import { formatCurrency, formatDateOnly } from '@tnzi/core'
import {
  type BillingAccountDto,
  type StorageUsageDto,
  type SubscriptionDto,
  type SubscriptionPlanDto,
} from '@tnzi/core/services/payment'
import { useAdminBillingConfig } from '../../plugin/billing-config'
import TDetailSection from '../../components/detail/TDetailSection.vue'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import { EMPTY_DASH } from '../../utils/placeholders'
import { useSafeMessage } from '../_shared/safe-message'
import BillingPayNowModal from './BillingPayNowModal.vue'
import BillingPaymentMethods from './BillingPaymentMethods.vue'
import BillingPlanList from './BillingPlanList.vue'
import BillingSubscribeModal from './BillingSubscribeModal.vue'
import BillingStorageMeter from './BillingStorageMeter.vue'
import {
  STANDING_TONE,
  billingT as t,
  currentSubscription,
  isPending,
  needsPayment,
  standingLabel,
  subscriptionStatusLabel,
  subscriptionTone,
  useBillingBridge,
  useBillingStanding,
} from './billing-context'

const bridge = useBillingBridge()
const store = useBillingStanding(bridge)
const message = useSafeMessage()

const account = ref<BillingAccountDto | null>(null)
const subscriptions = ref<SubscriptionDto[]>([])
const loading = ref(false)
const loadError = ref<string | null>(null)
const payOpen = ref(false)
const cards = ref<InstanceType<typeof BillingPaymentMethods> | null>(null)
const subscribeOpen = ref(false)
const chosenPlan = ref<SubscriptionPlanDto | null>(null)
const replacing = ref(false)
const usage = ref<StorageUsageDto | null>(null)
// How many plans the product sells; null until known. With exactly one there is nothing to "choose another" from.
const planCount = ref<number | null>(null)
const productCode = useAdminBillingConfig()?.productCode

const standing = computed(() => store.standing)
// The server decides; the billing account answers it, the standing repeats it.
const canManage = computed(() => account.value?.canManage ?? standing.value?.canManage ?? false)
// Only a subscription still in force counts: an ended one leaves the account free to subscribe again.
const subscription = computed(() => currentSubscription(subscriptions.value, standing.value?.subscriptionId))
const pending = computed(() => isPending(subscription.value))
const isSuspended = computed(() => String(subscription.value?.status) === 'Suspended')

const attentionText = computed(() => {
  const s = standing.value
  if (isSuspended.value) return t('subscription.suspendedNotice')
  if (s?.graceDeadline) {
    return t('subscription.pastDueNotice', { date: formatDateOnly(s.graceDeadline, { dateStyle: 'medium' }) })
  }
  return t('subscription.pastDueNoticeNoDate')
})

// Before subscribing (none in force, or a Pending sign-up): the account's storage against the plan on offer.
const offerUsage = computed(() => (usage.value?.metered && !usage.value.subscribed ? usage.value : null))

async function load(): Promise<void> {
  loading.value = true
  loadError.value = null
  // The account read is optional (it only sharpens canManage); the
  // subscription list is what this section states, so its failure is shown.
  const [accountResult, listResult] = await Promise.allSettled([
    bridge.getAccount(),
    bridge.listSubscriptions(),
    store.refresh(),
  ])
  account.value = accountResult.status === 'fulfilled' ? accountResult.value : null
  if (listResult.status === 'fulfilled') {
    subscriptions.value = listResult.value
  } else {
    subscriptions.value = []
    loadError.value = listResult.reason instanceof Error ? listResult.reason.message : String(listResult.reason)
  }
  await Promise.all([loadUsage(), loadPlanCount()])
  loading.value = false
}

/**
 * Storage against the plan's allowance - also before subscribing, since what the account already stores is part
 * of deciding to. Optional: a failure leaves the rest of the section as it was.
 */
async function loadUsage(): Promise<void> {
  try {
    usage.value = await bridge.getStorageUsage(productCode)
  } catch {
    usage.value = null
  }
}

/** A Pending sign-up offers "choose another plan" only when there is another plan to choose. */
async function loadPlanCount(): Promise<void> {
  if (!pending.value || !canManage.value || planCount.value !== null) return
  try {
    planCount.value = (await bridge.listPlans(productCode)).length
  } catch {
    planCount.value = null
  }
}

onMounted(() => {
  void load()
})

async function onPaid(): Promise<void> {
  // The standing refresh is what lifts a suspended shell back to normal; paying may have saved a card too.
  await Promise.all([load(), cards.value?.reload()])
}

/** Replacing, defaulting or removing a card can change the card on the subscription (and so its standing). */
function onCardsChanged(): Promise<void> {
  return load()
}

function startSubscribe(plan: SubscriptionPlanDto): void {
  chosenPlan.value = plan
  subscribeOpen.value = true
}

// Closing either payment dialog without paying may still have left a Pending
// sign-up behind; reading again shows it with its "finish payment" action.
function onSubscribeDialogShow(open: boolean): void {
  if (!open) void load()
}

function onPayDialogShow(open: boolean): void {
  if (!open && pending.value) void load()
}

/** Drop the unpaid sign-up (its first payment is closed with it) and show the plans again. */
async function chooseAnotherPlan(): Promise<void> {
  const target = subscription.value
  if (!target || replacing.value) return
  replacing.value = true
  try {
    await bridge.cancelSubscription(target.id, 'Replaced before the first payment')
  } catch (err) {
    message.error(t('subscription.replaceFailed', { error: err instanceof Error ? err.message : String(err) }))
  } finally {
    replacing.value = false
  }
  await load()
}

defineExpose({ reload: load })
</script>

<style scoped>
/* Blocks inside the section are separated by space and a rule, as the section's own spacing. */
.t-billing-cards-block {
  margin-top: 20px;
  padding-top: 16px;
  border-top: 1px solid var(--tnzi-border);
}
.t-billing-facts {
  display: grid;
  grid-template-columns: minmax(120px, max-content) 1fr;
  gap: 8px 24px;
  margin: 0;
  font-size: 14px;
}
.t-billing-facts dt {
  color: var(--tnzi-base-text-muted);
}
.t-billing-facts dd {
  margin: 0;
  font-variant-numeric: tabular-nums;
}
@media (max-width: 767px) {
  .t-billing-facts {
    grid-template-columns: 1fr;
    gap: 2px;
  }
  .t-billing-facts dd {
    margin-bottom: 8px;
  }
}
</style>
