<template>
  <BillingWarningBanner
    v-if="store.view === 'banner' && store.standing"
    :standing="store.standing"
    :settings-section="config?.settingsSection ?? BILLING_SUBSCRIPTION_SECTION_KEY"
  />
  <BillingSuspendedView v-else-if="store.view === 'suspended' && store.standing" :standing="store.standing" />
</template>

<script setup lang="ts">
/**
 * The billing half of the admin shell, mounted only when
 * `defineAdminApp({ billing: { enabled: true } })`.
 *
 * - Reads the standing whenever a user is signed in and identified (after the
 *   permission load, and again on a session restore), and forgets it when the
 *   user changes or signs out.
 * - Re-reads it whenever any request comes back refused by the billing gate
 *   (`SUBSCRIPTION_SUSPENDED`), so a suspension that starts mid-session takes
 *   over the shell at the first refusal.
 * - Tells the shell what to render through the provided billing state: the
 *   shell hides its content on `suspended` and this host renders the
 *   suspended screen in its place; on `banner` the content stays and the
 *   banner sits above it.
 */
import { onBeforeUnmount, watch } from 'vue'
import { useAdminAuthStore } from '../../stores/useAdminAuthStore'
import { useAdminClient } from '../../plugin/client'
import { useAdminBillingConfig } from '../../plugin/billing-config'
import { BILLING_SUBSCRIPTION_SECTION_KEY } from './billing-keys'
import { isSubscriptionSuspendedError } from '../../services/bridges/billing-bridge'
import { useBillingStandingStore } from './billing-standing'
import { useBillingBridge } from './billing-context'
import BillingWarningBanner from './BillingWarningBanner.vue'
import BillingSuspendedView from './BillingSuspendedView.vue'

const config = useAdminBillingConfig()
const authStore = useAdminAuthStore()
const client = useAdminClient()
const store = useBillingStandingStore()
store.configure(useBillingBridge(), config?.productCode)

watch(
  () => (authStore.isLogin ? (authStore.userInfo?.id ?? null) : null),
  (userId, previous) => {
    if (userId !== previous) store.reset()
    if (userId) void store.refresh()
  },
  { immediate: true },
)

const unsubscribe = client.addResultListener?.((result) => {
  if (isSubscriptionSuspendedError(result)) void store.refresh()
})

watch(
  () => store.view,
  (view) => {
    if (config) config.state.view = view
  },
  { immediate: true },
)

onBeforeUnmount(() => {
  unsubscribe?.()
  if (config) config.state.view = 'normal'
})
</script>
