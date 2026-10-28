/**
 * Billing in the admin shell - opt-in via `defineAdminApp({ billing })`.
 *
 * For a deployment that bills its customer (the organisation using the admin
 * app) on a subscription. When enabled the shell reads the user's billing
 * standing once the session is up, re-reads it whenever a request is refused
 * by the billing gate (HTTP 402 `SUBSCRIPTION_SUSPENDED`), and:
 *
 * - `Warning` + a user who may manage billing: a persistent banner with a
 *   "Pay now" action. Users who cannot manage billing see nothing.
 * - `Suspended` (and the gate applies to this user): the content area is
 *   replaced by a suspended screen. Billing managers pay there; everyone else
 *   is told to contact their administrators. Paying restores the normal shell
 *   without a reload.
 *
 * Absent (the default): nothing is mounted and no request is made.
 *
 * Everything billing-related loads lazily: this file and the shell hold only
 * the switch, so the shell chunk does not carry the billing UI. The section
 * helper and the hostable components live in the `@tnzi/ui-admin/billing`
 * subpath for the same reason.
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
import { inject, reactive, type App, type InjectionKey } from 'vue'

export interface AdminBillingConfig {
  /** Turn the shell's billing behaviour on. Default: off. */
  enabled?: boolean
  /** Product whose standing is read (`GET subscriptions/standing?productCode=`); omit for all products. */
  productCode?: string
  /**
   * Settings Center section key that holds the billing sections, for the
   * banner's "Billing details" link. Default: the subscription section
   * registered by `billingSettingsSections()` (`billing-subscription`).
   * The link is shown only when the user can open the Settings Center.
   */
  settingsSection?: string
  /**
   * Offer Stripe Link (save the card with Stripe for one-click checkout
   * elsewhere) in the card forms. Default: off - the platform already keeps
   * the card for renewals, and Link's sign-up box asks the payer for an email
   * and phone number on Stripe's behalf. A single form can still override it
   * with `TStripePaymentForm`'s `link` prop.
   */
  stripeLink?: boolean
}

/** What the shell renders for the current standing. Written by the lazy billing host. */
export interface AdminBillingShellState {
  view: 'normal' | 'banner' | 'suspended'
}

export interface ResolvedAdminBillingConfig {
  productCode?: string
  /** As configured; the billing chunk applies the default (`billing-subscription`). */
  settingsSection?: string
  stripeLink: boolean
  state: AdminBillingShellState
}

export const ADMIN_BILLING_CONFIG_KEY: InjectionKey<ResolvedAdminBillingConfig> = Symbol(
  'tnzi-admin-billing-config',
)

/** Provided only when `enabled`, so `useAdminBillingConfig()` is null otherwise. */
export function provideAdminBillingConfig(app: App, config: AdminBillingConfig | undefined): void {
  if (!config?.enabled) return
  app.provide(ADMIN_BILLING_CONFIG_KEY, {
    productCode: config.productCode,
    settingsSection: config.settingsSection,
    stripeLink: config.stripeLink === true,
    state: reactive<AdminBillingShellState>({ view: 'normal' }),
  })
}

export function useAdminBillingConfig(): ResolvedAdminBillingConfig | null {
  return inject(ADMIN_BILLING_CONFIG_KEY, null)
}
