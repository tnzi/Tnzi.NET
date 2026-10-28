/**
 * `@tnzi/ui-admin/billing`: the customer-facing billing UI for an app that
 * bills its customer on a subscription.
 *
 * A subpath rather than root exports so the admin shell, which every consumer
 * downloads, does not carry it. The sections and components below load on
 * first render; the shell's own switch is `defineAdminApp({ billing })`.
 *
 * Two ways to use it: the default pages (`billingSettingsSections()`), or an
 * app's own billing pages composed from the flows below - the same components
 * the default pages are built from, so both behave the same.
 *
 * ```ts
 * import { billingSettingsSections } from '@tnzi/ui-admin/billing'
 *
 * defineAdminApp({
 *   billing: { enabled: true, productCode: 'crm' },
 *   settings: { sections: billingSettingsSections() },
 * })
 * ```
 */
import { defineAsyncComponent } from 'vue'
export { useBillingStanding, type UseBillingStandingReturn } from '../pages/billing/use-billing-standing'
export type { BillingShellView } from '../pages/billing/billing-standing'
export type { SubscribeOutcome } from '../pages/billing/BillingSubscribeModal.vue'
export type { PayNowOutcome } from '../pages/billing/BillingPayNowModal.vue'
import type { AdminSettingsSection } from '../plugin/settings-config'
import { BILLING_SUBSCRIPTION_SECTION_KEY } from '../pages/billing/billing-keys'

export { BILLING_SUBSCRIPTION_SECTION_KEY }

export interface BillingSettingsSectionsOptions {
  /** Settings Center nav group. Default: the billing group label. */
  group?: string
  /** First section's order; billing history follows it. Default 10. */
  order?: number
  /** Permission that shows the sections. Default `payment.account.view`. */
  permission?: string
}

/**
 * The customer-facing billing sections for `defineAdminApp({ settings: { sections } })`:
 * the subscription with the cards it is paid with (`billing-subscription`), and
 * billing history. Each loads on first open.
 *
 * The cards are part of the subscription section, not a section of their own:
 * the subscription and the card it is charged to are one concern for the
 * customer. A link to the former `custom:billing-payment-methods` lands on the
 * Settings Center's default section.
 *
 * Visibility follows `permission`; pay / card actions inside follow the
 * server's `canManage`, never a code checked here.
 */
export function billingSettingsSections(options: BillingSettingsSectionsOptions = {}): AdminSettingsSection[] {
  const group = options.group ?? 'admin.modules.payment.billing.group'
  const order = options.order ?? 10
  const permission = options.permission ?? 'payment.account.view'
  return [
    {
      key: BILLING_SUBSCRIPTION_SECTION_KEY,
      label: 'admin.modules.payment.billing.sections.subscription',
      icon: 'mdi:card-account-details-outline',
      group,
      order,
      permission,
      component: () => import('../pages/billing/BillingSubscriptionSection.vue'),
    },
    {
      key: 'billing-history',
      label: 'admin.modules.payment.billing.sections.history',
      icon: 'mdi:receipt-text-outline',
      group,
      order: order + 1,
      permission,
      component: () => import('../pages/billing/BillingHistorySection.vue'),
    },
  ]
}

// Hostable outside the Settings Center (a consumer's own billing page), as
// async components so none of them lands in the shell chunk.
export const TBillingSubscriptionSection = defineAsyncComponent(() => import('../pages/billing/BillingSubscriptionSection.vue'))
/** @deprecated The cards are part of `TBillingSubscriptionSection`; this standalone form renders the same card block. */
export const TBillingPaymentMethodsSection = defineAsyncComponent(() => import('../pages/billing/BillingPaymentMethodsSection.vue'))
export const TBillingHistorySection = defineAsyncComponent(() => import('../pages/billing/BillingHistorySection.vue'))
export const TStripePaymentForm = defineAsyncComponent(() => import('../pages/billing/TStripePaymentForm.vue'))

// Flows for an app's own billing pages. Each is a dialog (`v-model:show`) or a display piece, loaded on first
// render; the default sections use the very same components.

/**
 * Start a subscription to `plan`: create it, charge the first period in Stripe's Payment Element (3-D Secure in
 * Stripe's modal), sync the payment. Props `show`, `plan`. Events `subscribed(outcome: SubscribeOutcome)`,
 * `cancel` (closed without subscribing; a Pending sign-up may have been left: read the subscriptions again),
 * `update:show`.
 */
export const TBillingSubscribeDialog = defineAsyncComponent(() => import('../pages/billing/BillingSubscribeModal.vue'))

/**
 * Pay what a subscription owes: `purpose: 'recovery'` (default) for PastDue / Suspended, `'firstPayment'` to
 * finish a Pending sign-up. 3-D Secure included. Props `show`, `subscriptionId`, `purpose`. Events
 * `paid(outcome: PayNowOutcome)`, `cancel`, `update:show`.
 */
export const TBillingPayNowDialog = defineAsyncComponent(() => import('../pages/billing/BillingPayNowModal.vue'))

/**
 * Save a card (SetupIntent, confirm, bind as the default card) and, given `subscriptionId`, put it on that
 * subscription (replace the renewal card). Props `show`, `title`, `subscriptionId`. Events
 * `saved(method: StoredPaymentMethodDto)`, `attached(subscription: SubscriptionDto)`, `cancel`, `update:show`.
 */
export const TBillingCardSetupDialog = defineAsyncComponent(() => import('../pages/billing/BillingCardSetupModal.vue'))

/**
 * Storage used against the plan's allowance, in the GB unit (2^30 bytes) the receipts use, with the
 * over-allowance state. Prop `usage: StorageUsageDto` (from `GET subscriptions/storage-usage`).
 */
export const TBillingStorageMeter = defineAsyncComponent(() => import('../pages/billing/BillingStorageMeter.vue'))

/**
 * The plans on sale for the configured product (`defineAdminApp({ billing: { productCode } })`), or the single
 * plan's offer when only one is sold. Events `select(plan: SubscriptionPlanDto)`, `loaded(plans)`.
 */
export const TBillingPlanList = defineAsyncComponent(() => import('../pages/billing/BillingPlanList.vue'))
