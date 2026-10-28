/**
 * Settings Center key of the subscription section registered by
 * `billingSettingsSections()`; also where the banner's "Billing details" link
 * points when `defineAdminApp({ billing: { settingsSection } })` is not set.
 *
 * Lives with the billing chunk, not the shell: only billing code reads it.
 */
export const BILLING_SUBSCRIPTION_SECTION_KEY = 'billing-subscription'
