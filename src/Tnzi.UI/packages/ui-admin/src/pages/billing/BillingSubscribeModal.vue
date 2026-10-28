<template>
  <TModalShell :show="show" :title="t('subscribe.title')" :width="520" @update:show="onShow">
    <div v-if="plan" class="flex flex-col gap-12px">
      <NAlert v-if="error" type="error" :show-icon="true" data-testid="subscribe-error">{{ error }}</NAlert>
      <div class="flex items-baseline justify-between gap-8px">
        <span class="font-600">{{ plan.planName }}</span>
        <span class="text-muted text-13px">{{ planPriceText(plan) }}</span>
      </div>
      <div v-if="planTaxText(plan)" class="text-13px text-muted">{{ planTaxText(plan) }}</div>

      <template v-if="!checkout">
        <p class="m-0 text-13px text-muted">{{ t('subscribe.review') }}</p>
      </template>
      <template v-else>
        <div class="flex items-baseline justify-between gap-8px">
          <span class="text-muted">{{ t('subscribe.firstPayment') }}</span>
          <span class="text-18px font-600" data-testid="subscribe-amount">{{ amountText }}</span>
        </div>
        <TStripePaymentForm
          ref="form"
          mode="payment"
          :publishable-key="checkout.publishableKey"
          :client-secret="checkout.clientSecret"
          @ready="ready = true"
        />
        <p class="m-0 text-12px text-muted">{{ t('subscribe.cardSaved') }}</p>
      </template>
    </div>
    <template #footer>
      <NButton :disabled="busy" @click="close">{{ t('actions.cancel') }}</NButton>
      <NButton
        v-if="!checkout"
        type="primary"
        :loading="busy"
        :disabled="busy || !plan"
        data-testid="subscribe-continue"
        @click="start"
      >
        {{ t('subscribe.continue') }}
      </NButton>
      <NButton v-else type="primary" :loading="busy" :disabled="!ready || busy" data-testid="subscribe-pay" @click="pay">
        {{ t('actions.confirmPay', { amount: amountText }) }}
      </NButton>
    </template>
  </TModalShell>
</template>

<script setup lang="ts">
/**
 * Start a subscription: review the plan, then `Continue` creates it (Pending,
 * with its first payment) and mounts the Payment Element on that payment.
 * Confirming charges the first period (3-D Secure in Stripe's own modal) and
 * saves the card for renewals; `payments/{tradeNo}/sync` then asks the channel
 * for the result so the subscription turns Active without waiting for the
 * webhook.
 *
 * Nothing is created until `Continue`: opening and closing the dialog leaves
 * no trace. After that, a declined card keeps the same payment open for
 * another try, and closing the dialog leaves a Pending subscription whose first
 * payment the section offers to finish (it never starts a second one).
 */
import { computed, ref, watch } from 'vue'
import { NAlert, NButton } from 'naive-ui'
import { TModalShell } from '@tnzi/ui'
import { formatCurrency } from '@tnzi/core'
import type { SubscriptionPlanDto } from '@tnzi/core/services/payment'
import TStripePaymentForm, { type StripeConfirmResult } from './TStripePaymentForm.vue'
import { billingT as t, useBillingBridge } from './billing-context'
import { planPriceText, planTaxText } from './billing-plans'
import { useSafeMessage } from '../_shared/safe-message'

/** `paid` / `processing`: a first payment went through; `started`: nothing was due (trial or free plan). */
export type SubscribeOutcome = 'paid' | 'processing' | 'started'

interface Checkout {
  subscriptionId: string
  tradeNo: string
  clientSecret: string
  publishableKey: string
  amount: number
  currency: string
}

const props = defineProps<{ show: boolean; plan: SubscriptionPlanDto | null }>()
const emit = defineEmits<{
  'update:show': [value: boolean]
  subscribed: [outcome: SubscribeOutcome]
  /** Closed without subscribing (a Pending sign-up may still have been left behind: read the subscriptions again). */
  cancel: []
}>()

const bridge = useBillingBridge()
const message = useSafeMessage()

const checkout = ref<Checkout | null>(null)
const busy = ref(false)
const ready = ref(false)
const error = ref<string | null>(null)
const form = ref<{ confirm: () => Promise<StripeConfirmResult> } | null>(null)

const amountText = computed(() => (checkout.value ? formatCurrency(checkout.value.amount, checkout.value.currency) : ''))

watch(
  () => props.show,
  (open) => {
    if (!open) return
    checkout.value = null
    ready.value = false
    error.value = null
  },
)

function messageOf(err: unknown): string {
  return err instanceof Error ? err.message : String(err)
}

function finish(outcome: SubscribeOutcome): void {
  message.success(t(`subscribe.${outcome}`))
  emit('subscribed', outcome)
  emit('update:show', false)
}

async function start(): Promise<void> {
  const plan = props.plan
  if (!plan || busy.value) return
  busy.value = true
  error.value = null
  let createdId: string | null = null
  try {
    const created = await bridge.createSubscription(plan.id)
    createdId = created.subscription.id
    const payment = created.payment
    if (!payment) {
      finish('started')
      return
    }
    if (!payment.payParams) throw new Error(t('payNow.noClientSecret'))
    const config = await bridge.getClientConfig()
    if (!config.publishableKey) throw new Error(t('stripe.notConfigured'))
    checkout.value = {
      subscriptionId: createdId,
      tradeNo: payment.tradeNo,
      clientSecret: payment.payParams,
      publishableKey: config.publishableKey,
      amount: payment.amount,
      currency: payment.currency,
    }
  } catch (err) {
    error.value = messageOf(err)
    // A subscription this page cannot take payment for would only sit Pending
    // in the way of the next try. Best effort: the next sign-up clears it too.
    if (createdId) await bridge.cancelSubscription(createdId, 'Card payment not available').catch(() => undefined)
  } finally {
    busy.value = false
  }
}

async function pay(): Promise<void> {
  if (!form.value || !checkout.value || busy.value) return
  busy.value = true
  error.value = null
  try {
    // A declined card is shown inside the form; the payment stays open for another card.
    const confirmed = await form.value.confirm().catch(() => null)
    if (!confirmed) return
    // Best effort: the webhook settles it anyway. A failed sync must not read
    // as a failed payment the card was already charged for.
    await bridge.syncPayment(checkout.value.tradeNo).catch(() => undefined)
    finish(confirmed.status === 'processing' ? 'processing' : 'paid')
  } catch (err) {
    error.value = messageOf(err)
  } finally {
    busy.value = false
  }
}

function onShow(value: boolean): void {
  if (!value && busy.value) return
  emit('update:show', value)
  if (!value) emit('cancel')
}

function close(): void {
  onShow(false)
}
</script>
