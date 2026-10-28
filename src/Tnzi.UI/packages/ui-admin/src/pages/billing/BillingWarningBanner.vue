<template>
  <NAlert
    class="t-billing-banner"
    :type="isSuspended ? 'error' : 'warning'"
    :show-icon="true"
    data-testid="billing-banner"
  >
    <div class="t-billing-banner__body">
      <span>{{ text }}</span>
      <div class="t-billing-banner__actions">
        <NButton
          v-if="standing.subscriptionId"
          size="small"
          type="primary"
          data-testid="billing-banner-pay"
          @click="payOpen = true"
        >
          {{ t('actions.payNow') }}
        </NButton>
        <NButton v-if="standing.subscriptionId" size="small" data-testid="billing-banner-card" @click="cardOpen = true">
          {{ t('actions.updateCard') }}
        </NButton>
        <NButton v-if="canOpenSettings" size="small" text type="primary" @click="openSettings">
          {{ t('actions.openBilling') }}
        </NButton>
      </div>
    </div>
    <BillingPayNowModal v-model:show="payOpen" :subscription-id="standing.subscriptionId ?? null" @paid="refresh" />
    <BillingCardSetupModal v-model:show="cardOpen" :title="t('card.title')" @saved="onCardSaved" />
  </NAlert>
</template>

<script setup lang="ts">
/**
 * The shell banner for a billing manager while a payment is overdue (or, for a
 * user the gate does not apply to, while the account is suspended). Pay now
 * and Update card work in place; "Billing details" opens the Settings Center
 * section when this user can reach the Settings Center.
 */
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { NAlert, NButton } from 'naive-ui'
import { formatDateOnly } from '@tnzi/core'
import { BillingStanding, type BillingStandingDto, type StoredPaymentMethodDto } from '@tnzi/core/services/payment'
import { usePermissionGuard } from '../../headless/usePermissionGuard'
import { useSafeMessage } from '../_shared/safe-message'
import BillingPayNowModal from './BillingPayNowModal.vue'
import BillingCardSetupModal from './BillingCardSetupModal.vue'
import { useBillingStandingStore } from './billing-standing'
import { billingT as t, useBillingBridge } from './billing-context'

const props = defineProps<{ standing: BillingStandingDto; settingsSection?: string }>()

const router = useRouter()
const { canReachRoute } = usePermissionGuard()
const store = useBillingStandingStore()
const bridge = useBillingBridge()
const message = useSafeMessage()

const payOpen = ref(false)
const cardOpen = ref(false)

const isSuspended = computed(() => props.standing.standing === BillingStanding.Suspended)

const text = computed(() => {
  if (isSuspended.value) return t('banner.suspendedExempt')
  const deadline = props.standing.graceDeadline
  return deadline
    ? t('banner.warningDeadline', { date: formatDateOnly(deadline, { dateStyle: 'medium' }) })
    : t('banner.warning')
})

const settingsRoute = computed(() => {
  if (!props.settingsSection || !router.hasRoute('settings')) return null
  return router.resolve({ name: 'settings', query: { section: `custom:${props.settingsSection}` } })
})
const canOpenSettings = computed(() => !!settingsRoute.value && canReachRoute(settingsRoute.value.meta))

function openSettings(): void {
  if (settingsRoute.value) void router.push(settingsRoute.value).catch(() => undefined)
}

function refresh(): void {
  void store.refresh()
}

async function onCardSaved(method: StoredPaymentMethodDto): Promise<void> {
  const id = props.standing.subscriptionId
  if (!id) return
  try {
    await bridge.attachPaymentMethod(id, method.id)
    message.success(t('card.attached'))
  } catch (err) {
    message.error(t('card.attachFailed', { error: err instanceof Error ? err.message : String(err) }))
  }
  refresh()
}
</script>

<style>
/* Not scoped: the banner sits in the content column above the page, and the
   page's own fill-height root (height: 100%) must give up the banner's share
   instead of running past the bottom edge. */
.t-admin-content__page > .t-billing-banner {
  flex: none;
  margin-bottom: var(--tnzi-admin-content-padding, 12px);
}
.t-admin-content__page:has(> .t-billing-banner) > :not(.t-billing-banner) {
  flex: 1 1 0;
  height: auto;
  min-height: 0;
}
.t-billing-banner__body {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 8px 16px;
}
.t-billing-banner__actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}
</style>
