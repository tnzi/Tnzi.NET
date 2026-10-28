<template>
  <div class="t-stripe-form">
    <NAlert v-if="errorText" type="error" :show-icon="true" class="t-stripe-form__error" data-testid="stripe-error">
      {{ errorText }}
    </NAlert>
    <div v-if="loading && !loadFailed" class="t-stripe-form__loading">{{ t('stripe.loading') }}</div>
    <div v-show="!loadFailed" ref="host" class="t-stripe-form__element" data-testid="stripe-element" />
  </div>
</template>

<script setup lang="ts">
/**
 * Stripe Payment Element for one client secret: a SetupIntent (save a card,
 * `mode="setup"`) or a PaymentIntent (pay now, `mode="payment"`). Stripe.js is
 * an optional peer loaded on mount; without it the form says what to install
 * instead of failing. The parent drives submission through the exposed
 * `confirm()`, which runs 3-D Secure in Stripe's own modal (`redirect:
 * 'if_required'`), so the page never navigates away for a card.
 *
 * Stripe Link is off unless asked for (`link`, else `defineAdminApp({ billing:
 * { stripeLink } })`): the platform already keeps the card for renewals, and
 * Link's sign-up box would ask the payer for an email and phone number on
 * Stripe's behalf. Restricting the intent to cards server-side does not hide
 * it, since Link is a card wallet; only the Element's `wallets` option does.
 */
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { NAlert } from 'naive-ui'
import {
  isStripeJsMissingError,
  loadStripeClient,
  setupIntentPaymentMethodId,
  type StripeElementLike,
  type StripeElementsLike,
  type StripeErrorLike,
  type StripeLike,
} from '@tnzi/core/services/payment'
import { makePageTranslator } from '../_shared/translate'
import { useAdminBillingConfig } from '../../plugin/billing-config'
// Mountable on its own (a consumer page), so it brings the billing strings itself.
import './billing-locales'

export interface StripeConfirmResult {
  /** Setup: the Stripe PaymentMethod id (`pm_...`) to bind. */
  paymentMethodId?: string
  /** Payment: the PaymentIntent status (`succeeded`, `processing`, ...). */
  status?: string
  /** The confirmed intent's id. */
  intentId?: string
}

const props = withDefaults(
  defineProps<{
    publishableKey: string
    clientSecret: string
    mode: 'setup' | 'payment'
    /** Where Stripe returns the payer for a method that must redirect; defaults to this page. */
    returnUrl?: string
    /**
     * Offer Stripe Link in this form. `null` (the default) follows the
     * billing config's `stripeLink`, which defaults to off. (Not a plain
     * optional boolean: Vue reads an absent boolean prop as `false`, which
     * would hide the config.)
     */
    link?: boolean | null
  }>(),
  { returnUrl: undefined, link: null },
)

const emit = defineEmits<{
  ready: []
  error: [message: string]
}>()

const t = makePageTranslator('payment.billing')
const configuredLink = useAdminBillingConfig()?.stripeLink ?? false

const host = ref<HTMLElement | null>(null)
const loading = ref(true)
const loadFailed = ref(false)
const errorText = ref<string | null>(null)

let stripe: StripeLike | null = null
let elements: StripeElementsLike | null = null
let element: StripeElementLike | null = null
// Bumped on every (re)mount so a load that finishes after the secret changed,
// or after unmount, does not mount into the wrong form.
let generation = 0

function fail(message: string): void {
  errorText.value = message
  emit('error', message)
}

/**
 * Card and validation errors carry a message written for the payer (Stripe
 * localises it); anything else is an integration problem whose text would
 * mean nothing to them.
 */
function stripeMessage(error: StripeErrorLike | undefined): string {
  if (error && (error.type === 'card_error' || error.type === 'validation_error') && error.message) {
    return error.message
  }
  return t('stripe.confirmFailed')
}

function teardown(): void {
  element?.destroy()
  element = null
  elements = null
}

async function mount(): Promise<void> {
  const mine = ++generation
  teardown()
  loading.value = true
  loadFailed.value = false
  errorText.value = null
  if (!props.publishableKey || !props.clientSecret) {
    loading.value = false
    loadFailed.value = true
    fail(t('stripe.notConfigured'))
    return
  }
  try {
    const loaded = await loadStripeClient(props.publishableKey)
    if (mine !== generation || !host.value) return
    stripe = loaded
    elements = loaded.elements({ clientSecret: props.clientSecret })
    element = elements.create('payment', {
      wallets: { link: (props.link ?? configuredLink) ? 'auto' : 'never' },
    })
    element.on('ready', () => {
      if (mine !== generation) return
      loading.value = false
      emit('ready')
    })
    element.mount(host.value)
  } catch (err) {
    if (mine !== generation) return
    loading.value = false
    loadFailed.value = true
    fail(
      isStripeJsMissingError(err)
        ? t('stripe.missing')
        : t('stripe.loadFailed', { error: err instanceof Error ? err.message : String(err) }),
    )
  }
}

/**
 * Confirm the intent with what the payer entered. Resolves with the outcome;
 * rejects with the message shown in the form when Stripe refuses.
 */
async function confirm(): Promise<StripeConfirmResult> {
  if (!stripe || !elements) {
    const message = errorText.value ?? t('stripe.notReady')
    fail(message)
    throw new Error(message)
  }
  errorText.value = null
  const options = {
    elements,
    redirect: 'if_required' as const,
    confirmParams: { return_url: props.returnUrl ?? window.location.href },
  }
  if (props.mode === 'setup') {
    const result = await stripe.confirmSetup(options)
    if (result.error) {
      const message = stripeMessage(result.error)
      fail(message)
      throw new Error(message)
    }
    const paymentMethodId = setupIntentPaymentMethodId(result.setupIntent)
    if (!paymentMethodId) {
      const message = t('stripe.confirmFailed')
      fail(message)
      throw new Error(message)
    }
    return { paymentMethodId, status: result.setupIntent?.status, intentId: result.setupIntent?.id }
  }
  const result = await stripe.confirmPayment(options)
  if (result.error) {
    const message = stripeMessage(result.error)
    fail(message)
    throw new Error(message)
  }
  return { status: result.paymentIntent?.status, intentId: result.paymentIntent?.id }
}

defineExpose({ confirm })

onMounted(() => {
  void mount()
})
watch(
  () => [props.publishableKey, props.clientSecret] as const,
  () => {
    void mount()
  },
)
onBeforeUnmount(() => {
  generation += 1
  teardown()
})
</script>

<style scoped>
.t-stripe-form {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.t-stripe-form__loading {
  font-size: 13px;
  color: var(--tnzi-base-text-muted);
}
.t-stripe-form__element {
  min-height: 40px;
}
</style>
