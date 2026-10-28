/**
 * Shared pieces of the billing UI: the bridge for the current admin client and
 * the label / tone of each standing and subscription status.
 */
import type { StatusType } from '@tnzi/ui'
import { BillingStanding, InvoiceType, SubscriptionStatus, type InvoiceDto, type SubscriptionDto } from '@tnzi/core/services/payment'
import { createBillingBridge, type BillingBridge } from '../../services/bridges/billing-bridge'
import { useAdminClient } from '../../plugin/client'
import { useConfiguredStandingStore } from './use-billing-standing'
import { makePageTranslator } from '../_shared/translate'
// The billing UI's strings travel with this chunk, not with the locale pack.
import './billing-locales'

export const billingT = makePageTranslator('payment.billing')

export function useBillingBridge(): BillingBridge {
  return createBillingBridge({ client: useAdminClient() })
}

/**
 * The standing store, ready to refresh from wherever a billing section is
 * hosted: the shell's billing host configures it for the app's product; a
 * section mounted without that host configures it here.
 */
export function useBillingStanding(bridge: BillingBridge) {
  return useConfiguredStandingStore(bridge)
}

export const STANDING_TONE: Record<string, StatusType> = {
  [BillingStanding.None]: 'default',
  [BillingStanding.Good]: 'success',
  [BillingStanding.Warning]: 'warning',
  [BillingStanding.Suspended]: 'error',
}

const SUBSCRIPTION_TONE: Record<string, StatusType> = {
  Pending: 'warning',
  Trial: 'info',
  Active: 'success',
  PendingRenewal: 'warning',
  Paused: 'warning',
  Cancelled: 'default',
  Expired: 'default',
  PastDue: 'error',
  Suspended: 'error',
}

export function subscriptionTone(status: string | null | undefined): StatusType {
  return SUBSCRIPTION_TONE[String(status ?? '')] ?? 'default'
}

/** Status label, from the admin subscriptions page's dictionary (one wording for both). */
export function subscriptionStatusLabel(status: string | null | undefined): string {
  const value = String(status ?? '')
  if (!value) return ''
  const leaf = value.charAt(0).toLowerCase() + value.slice(1)
  return billingT(`admin.modules.payment.subscriptions.status.${leaf}`)
}

export function standingLabel(standing: string | null | undefined): string {
  const value = String(standing ?? BillingStanding.None)
  return billingT(`standing.${value.toLowerCase()}`)
}

const NEEDS_PAYMENT = new Set<string>([SubscriptionStatus.PastDue, SubscriptionStatus.Suspended])
const LIVE = new Set<string>([
  SubscriptionStatus.Active,
  SubscriptionStatus.Trial,
  SubscriptionStatus.PendingRenewal,
  SubscriptionStatus.Paused,
])

/**
 * The figure a billing document is about: on a receipt what was paid, on a
 * credit note what was credited (negative, it is money back), otherwise the
 * total due. Never the pre-tax `amount`: a CA$100 plan with 13% HST is a
 * CA$113 receipt.
 */
export function documentAmount(row: Pick<InvoiceDto, 'type' | 'amount' | 'dueAmount' | 'paidAmount'>): number {
  const type = String(row.type ?? '')
  if (type === InvoiceType.CreditNote) return -(row.dueAmount || row.paidAmount || row.amount)
  if (type === InvoiceType.Receipt) return row.paidAmount || row.dueAmount || row.amount
  return row.dueAmount || row.amount
}

export function needsPayment(subscription: SubscriptionDto | null | undefined): boolean {
  return !!subscription && NEEDS_PAYMENT.has(String(subscription.status))
}

/** Ended for good: the server lets the account subscribe to the product again. */
const ENDED = new Set<string>([SubscriptionStatus.Cancelled, SubscriptionStatus.Expired])

/** Signed up, first payment not completed yet. */
export function isPending(subscription: SubscriptionDto | null | undefined): boolean {
  return !!subscription && String(subscription.status) === SubscriptionStatus.Pending
}

/**
 * The account's subscription that is still in force (anything but Cancelled /
 * Expired, the same rule the server uses to refuse a second one), or null
 * when the account has none and may start one.
 */
export function currentSubscription(items: SubscriptionDto[], preferredId?: string | null): SubscriptionDto | null {
  return pickSubscription(items.filter((s) => !ENDED.has(String(s.status))), preferredId)
}

/**
 * The subscription the page is about: the one the standing names, else the
 * first that needs payment, else the first live one, else the newest.
 */
export function pickSubscription(items: SubscriptionDto[], preferredId?: string | null): SubscriptionDto | null {
  if (!items.length) return null
  return (
    (preferredId ? items.find((s) => s.id === preferredId) : undefined) ??
    items.find((s) => NEEDS_PAYMENT.has(String(s.status))) ??
    items.find((s) => LIVE.has(String(s.status))) ??
    items[0] ??
    null
  )
}
