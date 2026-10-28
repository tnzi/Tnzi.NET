<template>
  <TModalShell :show="show" :title="t(`${copy}.title`)" :width="520" @update:show="onShow">
    <div class="flex flex-col gap-12px">
      <NAlert v-if="error" type="error" :show-icon="true" data-testid="pay-now-error">{{ error }}</NAlert>
      <div v-if="preparing" class="text-13px text-muted">{{ t('stripe.loading') }}</div>
      <template v-else-if="checkout && publishableKey && checkout.payParams">
        <div class="flex items-baseline justify-between gap-8px">
          <span class="text-muted">{{ t('subscription.amountDue') }}</span>
          <span class="text-18px font-600" data-testid="pay-now-amount">{{ amountText }}</span>
        </div>
        <TStripePaymentForm
          ref="form"
          mode="payment"
          :publishable-key="publishableKey"
          :client-secret="checkout.payParams"
          @ready="ready = true"
        />
      </template>
    </div>
    <template #footer>
      <NButton @click="close">{{ t('actions.cancel') }}</NButton>
      <NButton type="primary" :loading="paying" :disabled="!ready || paying" data-testid="pay-now-confirm" @click="pay">
        {{ amountText ? t('actions.confirmPay', { amount: amountText }) : t('actions.payNow') }}
      </NButton>
    </template>
  </TModalShell>
</template>

<script setup lang="ts">
/**
 * Pay an overdue subscription on the spot, or finish the first payment of a
 * Pending one (`purpose="firstPayment"`): `pay-now` opens (or reuses) the
 * checkout payment, the Payment Element confirms it (3-D Secure in Stripe's
 * modal), then `payments/{tradeNo}/sync` asks the channel for the result so
 * the service recovers without waiting for the webhook. A subscription with
 * nothing to charge is restored by `pay-now` itself (`settledWithoutPayment`).
 */
import { computed, ref, watch } from 'vue'
import { NAlert, NButton } from 'naive-ui'
import { TModalShell } from '@tnzi/ui'
import { formatCurrency } from '@tnzi/core'
import type { SubscriptionPayNowResultDto } from '@tnzi/core/services/payment'
import TStripePaymentForm, { type StripeConfirmResult } from './TStripePaymentForm.vue'
import { billingT as t, useBillingBridge } from './billing-context'
import { useSafeMessage } from '../_shared/safe-message'

/** How the payment ended, for the caller's follow-up (refresh the standing). */
export type PayNowOutcome = 'paid' | 'processing' | 'settled'

const props = withDefaults(
  defineProps<{
    show: boolean
    subscriptionId: string | null
    /** `recovery`: an overdue amount; `firstPayment`: a sign-up whose first payment was not completed. */
    purpose?: 'recovery' | 'firstPayment'
  }>(),
  { purpose: 'recovery' },
)
const emit = defineEmits<{
  'update:show': [value: boolean]
  paid: [outcome: PayNowOutcome]
  /** Closed without paying. */
  cancel: []
}>()

const bridge = useBillingBridge()
const message = useSafeMessage()

const checkout = ref<SubscriptionPayNowResultDto | null>(null)
const publishableKey = ref<string | null>(null)
const preparing = ref(false)
const ready = ref(false)
const paying = ref(false)
const error = ref<string | null>(null)
const form = ref<{ confirm: () => Promise<StripeConfirmResult> } | null>(null)

// Title and outcome wording: "the service is restored" is not true of a sign-up.
const copy = computed(() => (props.purpose === 'firstPayment' ? 'firstPayment' : 'payNow'))

const amountText = computed(() =>
  checkout.value ? formatCurrency(checkout.value.amount, checkout.value.currency) : '',
)

function messageOf(err: unknown): string {
  return err instanceof Error ? err.message : String(err)
}

/** The server's answer when a Pending sign-up's first payment can no longer be paid. */
const CANNOT_PAY_NOW = 'SUBSCRIPTION_CANNOT_PAY_NOW'

function prepareError(err: unknown): string {
  const failure = err as { errorCode?: string | null; message?: string } | null
  const gone = failure?.errorCode === CANNOT_PAY_NOW || failure?.message === CANNOT_PAY_NOW
  // The sign-up cannot be paid any more; the section's "choose another plan" starts over.
  return gone && props.purpose === 'firstPayment' ? t('firstPayment.gone') : messageOf(err)
}

function finish(outcome: PayNowOutcome): void {
  message.success(t(`${copy.value}.${outcome}`))
  emit('paid', outcome)
  emit('update:show', false)
}

async function prepare(id: string): Promise<void> {
  checkout.value = null
  publishableKey.value = null
  ready.value = false
  error.value = null
  preparing.value = true
  try {
    const opened = await bridge.payNow(id)
    if (opened.settledWithoutPayment) {
      finish('settled')
      return
    }
    if (!opened.payParams) throw new Error(t('payNow.noClientSecret'))
    const config = await bridge.getClientConfig(opened.channelCode)
    if (!config.publishableKey) throw new Error(t('stripe.notConfigured'))
    publishableKey.value = config.publishableKey
    checkout.value = opened
  } catch (err) {
    error.value = prepareError(err)
  } finally {
    preparing.value = false
  }
}

watch(
  () => [props.show, props.subscriptionId] as const,
  ([open, id]) => {
    if (open && id) void prepare(id)
  },
  { immediate: true },
)

function onShow(value: boolean): void {
  if (!value && paying.value) return
  emit('update:show', value)
  if (!value) emit('cancel')
}

function close(): void {
  onShow(false)
}

async function pay(): Promise<void> {
  if (!form.value || !checkout.value || paying.value) return
  paying.value = true
  error.value = null
  try {
    const confirmed = await form.value.confirm().catch(() => null)
    if (!confirmed) return
    const tradeNo = checkout.value.tradeNo
    if (tradeNo) {
      // Best effort: the webhook settles it anyway. A failed sync must not
      // read as a failed payment the card was already charged for.
      await bridge.syncPayment(tradeNo).catch(() => undefined)
    }
    finish(confirmed.status === 'processing' ? 'processing' : 'paid')
  } catch (err) {
    error.value = messageOf(err)
  } finally {
    paying.value = false
  }
}
</script>
