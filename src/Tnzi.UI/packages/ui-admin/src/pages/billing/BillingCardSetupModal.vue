<template>
  <TModalShell :show="show" :title="title || t('card.addTitle')" :width="520" @update:show="onShow">
    <div class="flex flex-col gap-12px">
      <NAlert v-if="error" type="error" :show-icon="true" data-testid="card-setup-error">{{ error }}</NAlert>
      <div v-if="preparing" class="text-13px text-muted">{{ t('stripe.loading') }}</div>
      <template v-else-if="session">
        <TStripePaymentForm
          v-if="session.clientSecret && publishableKey"
          ref="form"
          mode="setup"
          :publishable-key="publishableKey"
          :client-secret="session.clientSecret"
          @ready="ready = true"
        />
        <NAlert v-else-if="session.approvalUrl" type="info" :show-icon="true">
          {{ t('card.redirect') }}
          <NButton text type="primary" class="ml-4px" @click="goApprove">{{ t('actions.continue') }}</NButton>
        </NAlert>
      </template>
    </div>
    <template #footer>
      <NButton @click="close">{{ t('actions.cancel') }}</NButton>
      <NButton
        type="primary"
        :loading="saving"
        :disabled="!ready || saving"
        data-testid="card-setup-save"
        @click="save"
      >
        {{ t('actions.saveCard') }}
      </NButton>
    </template>
  </TModalShell>
</template>

<script setup lang="ts">
/**
 * Save a card: open a setup session, collect the card in Stripe's Payment
 * Element (3-D Secure in Stripe's modal), then bind the resulting PaymentMethod
 * as the default card.
 *
 * Given `subscriptionId`, the saved card is then put on that subscription (the
 * card its renewals are charged to; a failure to do so is reported, the card
 * stays saved). Public as `TBillingCardSetupDialog` from `@tnzi/ui-admin/billing`.
 *
 * Events: `saved` with the stored method (always, once the card is saved),
 * `attached` with the updated subscription (only when it was put on one),
 * `cancel` when the dialog is closed without saving, `update:show`.
 */
import { ref, watch } from 'vue'
import { NAlert, NButton } from 'naive-ui'
import { TModalShell } from '@tnzi/ui'
import type { SetupSessionDto, StoredPaymentMethodDto, SubscriptionDto } from '@tnzi/core/services/payment'
import { useSafeMessage } from '../_shared/safe-message'
import TStripePaymentForm, { type StripeConfirmResult } from './TStripePaymentForm.vue'
import { billingT as t, useBillingBridge } from './billing-context'

const props = withDefaults(
  defineProps<{
    show: boolean
    title?: string
    /** Put the saved card on this subscription (replace the card its renewals are charged to). */
    subscriptionId?: string | null
  }>(),
  { title: undefined, subscriptionId: null },
)
const emit = defineEmits<{
  'update:show': [value: boolean]
  saved: [method: StoredPaymentMethodDto]
  attached: [subscription: SubscriptionDto]
  cancel: []
}>()

const bridge = useBillingBridge()
const message = useSafeMessage()

const session = ref<SetupSessionDto | null>(null)
const publishableKey = ref<string | null>(null)
const preparing = ref(false)
const ready = ref(false)
const saving = ref(false)
const error = ref<string | null>(null)
const form = ref<{ confirm: () => Promise<StripeConfirmResult> } | null>(null)

function messageOf(err: unknown): string {
  return err instanceof Error ? err.message : String(err)
}

async function prepare(): Promise<void> {
  session.value = null
  publishableKey.value = null
  ready.value = false
  error.value = null
  preparing.value = true
  try {
    const opened = await bridge.createSetupSession()
    if (opened.clientSecret) {
      const config = await bridge.getClientConfig(opened.channelCode)
      if (!config.publishableKey) throw new Error(t('stripe.notConfigured'))
      publishableKey.value = config.publishableKey
    }
    session.value = opened
  } catch (err) {
    error.value = messageOf(err)
  } finally {
    preparing.value = false
  }
}

watch(
  () => props.show,
  (open) => {
    if (open) void prepare()
  },
  { immediate: true },
)

function onShow(value: boolean): void {
  if (!value && saving.value) return
  emit('update:show', value)
  if (!value) emit('cancel')
}

function close(): void {
  onShow(false)
}

function goApprove(): void {
  if (session.value?.approvalUrl) window.location.assign(session.value.approvalUrl)
}

/** The card is saved either way; only putting it on the subscription can still fail, and says so. */
async function attach(subscriptionId: string, method: StoredPaymentMethodDto): Promise<SubscriptionDto | null> {
  try {
    const subscription = await bridge.attachPaymentMethod(subscriptionId, method.id)
    message.success(t('card.attached'))
    return subscription
  } catch (err) {
    message.error(t('card.attachFailed', { error: messageOf(err) }))
    return null
  }
}

async function save(): Promise<void> {
  if (!form.value || !session.value || saving.value) return
  saving.value = true
  error.value = null
  try {
    // A refusal is shown inside the form; nothing more to say here.
    const confirmed = await form.value.confirm().catch(() => null)
    if (!confirmed?.paymentMethodId) return
    const method = await bridge.bindPaymentMethod(confirmed.paymentMethodId, session.value.channelCode, true)
    const attached = props.subscriptionId ? await attach(props.subscriptionId, method) : null
    if (!props.subscriptionId) message.success(t('card.saved'))
    emit('saved', method)
    if (attached) emit('attached', attached)
    emit('update:show', false)
  } catch (err) {
    error.value = messageOf(err)
  } finally {
    saving.value = false
  }
}
</script>
