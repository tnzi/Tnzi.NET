/**
 * How a subscription plan's price reads on the customer's billing page: the
 * amount per billing cycle, and whether tax is added to it.
 */
import { formatCurrency } from '@tnzi/core'
import { BillingCycleType, formatStorageGb, type SubscriptionPlanDto } from '@tnzi/core/services/payment'
import { billingT as t } from './billing-context'

const CYCLE_KEY: Record<string, string> = {
  [BillingCycleType.Day]: 'day',
  [BillingCycleType.Week]: 'week',
  [BillingCycleType.Month]: 'month',
  [BillingCycleType.Year]: 'year',
}

/**
 * An amount with the plan's cycle: `$100.00 per month`, `$250.00 every 3 months`, `$900.00 one-time`.
 * The price line and the tax line both say it this way, so one plan never has two names for its cycle.
 */
function amountPerCycle(plan: Pick<SubscriptionPlanDto, 'cycleType' | 'cycleValue'>, price: string): string {
  const key = CYCLE_KEY[String(plan.cycleType)]
  if (!key) return t('plans.oneTime', { price })
  const count = plan.cycleValue > 1 ? plan.cycleValue : 1
  return count > 1 ? t(`plans.everyN.${key}`, { price, n: count }) : t(`plans.per.${key}`, { price })
}

/** `$100.00 per month`, `$250.00 every 3 months`, `$900.00 one-time`. */
export function planPriceText(plan: SubscriptionPlanDto): string {
  return amountPerCycle(plan, formatCurrency(plan.price, plan.currency))
}

/**
 * `Plus HST 13%: $113.00 per month` (the same cycle wording as the price line), `Includes HST 13%`,
 * `No tax added`; empty when the server could not say (the plan is then listed without a tax line).
 */
export function planTaxText(plan: SubscriptionPlanDto): string {
  const tax = plan.tax
  if (!tax) return ''
  if (tax.taxAmount <= 0) return t('plans.noTax')
  const label = tax.lines.length
    ? tax.lines.map((line) => `${line.name} ${line.rate}%`).join(' + ')
    : t('plans.tax')
  return tax.taxIncluded
    ? t('plans.taxIncluded', { tax: label })
    : t('plans.taxAdded', { tax: label, total: amountPerCycle(plan, formatCurrency(tax.payableAmount, plan.currency)) })
}

/**
 * The one plan to offer when the product sells exactly one: the section shows
 * it directly (its terms and a Subscribe button), with no list to choose from
 * and no "choose another plan". Several plans keep the catalogue. Automatic:
 * a deployment that sells a single plan needs no setting for it.
 */
export function singlePlanOffer(plans: readonly SubscriptionPlanDto[]): SubscriptionPlanDto | null {
  return plans.length === 1 ? (plans[0] ?? null) : null
}

/** `Includes 250 GB of storage, then CA$0.20 per GB-month`; empty when the plan does not meter storage. */
export function planStorageText(plan: Pick<SubscriptionPlanDto, 'includedStorageBytes' | 'storageOveragePricePerGbMonth' | 'currency'>): string {
  if (plan.includedStorageBytes === null || plan.includedStorageBytes === undefined) return ''
  const size = formatStorageGb(plan.includedStorageBytes, 3)
  const price = Number(plan.storageOveragePricePerGbMonth ?? 0)
  return price > 0
    ? t('plans.storageWithOverage', { size, price: formatCurrency(price, plan.currency) })
    : t('plans.storage', { size })
}
