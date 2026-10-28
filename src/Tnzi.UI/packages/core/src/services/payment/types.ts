/**
 * Payment Module Types - Payment processing and management
 * Aligned with Tnzi.NET backend Payment module
 */

import type { PagedQueryDto } from '../../types/pagination';
import {
  PaymentStatus,
  PaymentMethod,
  BusinessType,
  RefundStatus,
  SubscriptionStatus,
  BillingCycleType,
  InvoiceStatus,
  InvoiceType,
  PromotionType,
  DiscountType,
  ProductType,
  ApplyScope,
  SubscriptionChangeType,
  SubscriptionChangeStatus,
  UserCouponStatus,
  TrendGranularity,
  BillingStanding,
} from './metadata';

export {
  BillingStanding,
  PaymentStatus,
  PaymentMethod,
  BusinessType,
  RefundStatus,
  SubscriptionStatus,
  BillingCycleType,
  InvoiceStatus,
  InvoiceType,
  PromotionType,
  DiscountType,
  ProductType,
  ApplyScope,
  SubscriptionChangeType,
  SubscriptionChangeStatus,
  UserCouponStatus,
  TrendGranularity,
};

// ============================================
// Payment Types
// ============================================

/**
 * Payment record DTO - mirrors Tnzi.Payment.Dtos.PaymentDto exactly.
 *
 * NOTE: the backend DTO keys the record on `tradeNo` (there is no `paymentNo`),
 * carries no `userId` / `finalAmount` / `refundAmount` fields, and exposes the
 * money as `originalAmount` / `paidAmount` / `discountAmount`.
 */
export interface PaymentDto {
  id: string;
  tradeNo: string;
  externalTradeNo?: string | null;
  businessOrderNo: string;
  businessType: BusinessType;
  originalAmount: number;
  paidAmount: number;
  discountAmount: number;
  /** Tax amount; already included in `payableAmount` when tax is priced in. */
  taxAmount: number;
  /** Amount actually charged by the channel; the basis for callback amount checks. */
  payableAmount: number;
  currency: string;
  /** Paying user; set even for background charges where there is no request user. */
  userId?: string | null;
  customerName?: string | null;
  customerEmail?: string | null;
  status: PaymentStatus;
  channelCode: string;
  paymentMethod: PaymentMethod;
  description?: string | null;
  expireTime?: Date | string | null;
  paidTime?: Date | string | null;
  /** Billing account the payment was made for (`User` / `Installation` / `Tenant` / custom); null on older records. */
  billingAccountType?: string | null;
  billingAccountId?: string | null;
  /** Card the payment was made with, when known (printed on receipts). */
  paymentMethodBrand?: string | null;
  paymentMethodLast4?: string | null;
  /**
   * Chargeback state: `None` | `WarningNeedsResponse` | `WarningUnderReview` | `WarningClosed` |
   * `NeedsResponse` | `UnderReview` | `Won` | `Lost` | `Unknown` (Tnzi.Payment.Metadata.PaymentDisputeStatus).
   */
  disputeStatus?: string;
  disputeId?: string | null;
  disputeReason?: string | null;
  disputeAmount?: number | null;
  creationTime: Date | string;
}

/**
 * Create payment request
 */
export interface CreatePaymentDto {
  businessOrderNo: string;
  /**
   * `Subscription` is refused here (400 `PAYMENT_BUSINESS_TYPE_SYSTEM_ONLY`): subscription
   * payments are issued by the server when a subscription is created or upgraded, and their
   * metadata drives the subscription state machine. Pay the order returned by those calls instead.
   */
  businessType: BusinessType;
  amount: number;
  currency?: string;
  channelCode?: string;
  paymentMethod?: PaymentMethod;
  description?: string;
  expireMinutes?: number;
  couponCode?: string;
  /**
   * Target plan/product id for the coupon's apply scope.
   * A promotion limited to one plan is rejected without it, so quoting and
   * redeeming must pass the same value.
   */
  couponScopeId?: string;
  returnUrl?: string;
  extraData?: string;
}

/**
 * Close payment request
 */
export interface ClosePaymentDto {
  reason?: string;
}

/**
 * Payment order creation result
 */
export interface PaymentOrderResultDto {
  tradeNo: string;
  payParams?: string | null;
  payUrl?: string | null;
  expireTime?: Date | string | null;
  /** Payable amount: discount deducted and tax applied. */
  amount: number;
  originalAmount: number;
  discountAmount: number;
  taxAmount: number;
  /** Set when a coupon code was accepted and redeemed for this order. */
  appliedCouponCode?: string | null;
  currency: string;
}

/**
 * Manual confirmation of an offline payment (bank transfer, wire, cheque).
 * Only valid for the `Offline` channel - online channels must go through the
 * channel callback so nobody can mark an order paid without real settlement.
 */
export interface ConfirmOfflinePaymentDto {
  /** Defaults to the order's payable amount when omitted. */
  paidAmount?: number;
  /** Bank reference / cheque number - the audit trail for a manual entry. */
  reference: string;
  paidTime?: Date | string;
  remark?: string;
}

/**
 * Payment list query - mirrors Tnzi.Payment.Dtos.PaymentQueryDto.
 *
 * The list is always newest first (creation time descending); the endpoint
 * takes no sort parameters. `startTime` / `endTime` bound the creation time,
 * and there is no amount-range or free-text filter.
 */
export interface PaymentQueryDto extends PagedQueryDto {
  tradeNo?: string;
  externalTradeNo?: string;
  status?: PaymentStatus;
  businessType?: BusinessType;
  channelCode?: string;
  userId?: string;
  startTime?: Date | string;
  endTime?: Date | string;
  businessOrderNo?: string;
  /** Filter by billing account type (admin list); pair with `billingAccountId`. */
  billingAccountType?: string;
  billingAccountId?: string;
}

// ============================================
// Refund Types
// ============================================

/**
 * Refund DTO - mirrors Tnzi.Payment.Dtos.RefundDto exactly.
 *
 * NOTE: the backend keys the refund on `tradeNo` (the related payment's trade
 * number, mapped from `Refund.Payment.TradeNo`). There is no `paymentId` /
 * `paymentNo` / `amount` / `channelRefundId` / `refundedTime` / `operator*`
 * field - the amount is `refundAmount`.
 */
export interface RefundDto {
  id: string;
  refundNo: string;
  tradeNo: string;
  refundAmount: number;
  currency: string;
  reason: string;
  status: RefundStatus;
  approverId?: string | null;
  approveTime?: Date | string | null;
  approveRemark?: string | null;
  completedTime?: Date | string | null;
  /** Billing account of the refunded payment; null on older records. */
  billingAccountType?: string | null;
  billingAccountId?: string | null;
  /** `Framework` (raised here) or `Channel` (made in the payment channel's dashboard, recorded from its webhook). */
  origin?: 'Framework' | 'Channel';
  creationTime: Date | string;
}

/**
 * Create refund request
 */
export interface CreateRefundDto {
  tradeNo: string;
  refundAmount: number;
  /** Stored on the refund; at most 500 characters. */
  reason: string;
}

/**
 * Refund query request
 */
export interface RefundQueryDto extends PagedQueryDto {
  tradeNo?: string;
  refundNo?: string;
  status?: RefundStatus;
  startTime?: Date | string;
  endTime?: Date | string;
  /** Filter by billing account type (admin list); pair with `billingAccountId`. */
  billingAccountType?: string;
  billingAccountId?: string;
}

/**
 * Approve refund request (admin)
 */
export interface ApproveRefundDto {
  approved: boolean;
  remark?: string;
}

/**
 * Cancel refund request
 */
export interface CancelRefundDto {
  reason?: string;
}

// ============================================
// Subscription Types
// ============================================

/**
 * Subscription DTO
 */
export interface SubscriptionDto {
  id: string;
  subscriptionNo: string;
  userId: string;
  planId: string;
  planName?: string | null;
  /** Product this subscription belongs to; null for single-product apps. */
  productCode?: string | null;
  status: SubscriptionStatus;
  cycleType: BillingCycleType;
  cycleValue: number;
  startTime: Date | string;
  endTime?: Date | string | null;
  nextBillingTime?: Date | string | null;
  trialStartTime?: Date | string | null;
  trialEndTime?: Date | string | null;
  originalPrice: number;
  paidAmount: number;
  discountAmount: number;
  currency: string;
  autoRenew: boolean;
  storedPaymentMethodId?: string | null;
  paymentMethodBrand?: string | null;
  paymentMethodLast4?: string | null;
  /** False means auto-renewal will fail: warn before the charge date, not after. */
  hasPaymentMethod: boolean;
  /** When the pause started - the remaining period is credited back on resume. */
  pausedAt?: Date | string | null;
  pausedUntil?: Date | string | null;
  pastDueSince?: Date | string | null;
  /** When service was suspended (`status === Suspended`). */
  suspendedAt?: Date | string | null;
  /** Billing account the subscription belongs to (`User` / `Installation` / `Tenant` / custom). */
  billingAccountType: string;
  billingAccountId: string;
  renewalRetryCount: number;
  creationTime: Date | string;
}

/**
 * Result of paying an overdue / suspended subscription on the spot.
 * `payParams` is the channel client secret: hand it to Stripe.js `confirmPayment` (handles 3-D Secure).
 */
export interface SubscriptionPayNowResultDto {
  subscriptionId: string;
  tradeNo?: string | null;
  payParams?: string | null;
  payUrl?: string | null;
  amount: number;
  taxAmount: number;
  currency: string;
  expireTime?: Date | string | null;
  channelCode: string;
  /** Nothing to charge (free plan / fully discounted trial): the subscription was restored without a payment. */
  settledWithoutPayment: boolean;
}

/**
 * The billing account the current user acts for.
 */
export interface BillingAccountDto {
  accountType: string;
  accountId: string;
  /** Whether the current user may pay, change subscriptions and cards for this account. */
  canManage: boolean;
  displayName?: string | null;
  billingEmails: string[];
  address?: string | null;
  taxId?: string | null;
}

/**
 * The current user's billing standing (`GET subscriptions/standing`).
 *
 * Any signed-in user may read it and the access gate never blocks it, so the
 * shell can always learn why it is being refused. The subscription fields name
 * the subscription that needs action and are all null when the caller may not
 * see billing details (`canView === false`) or nothing needs action.
 */
export interface BillingStandingDto {
  standing: BillingStanding;
  /** May see billing details (in User scope: always true for the own account). */
  canView: boolean;
  /** May pay and change cards: decided by the server, never re-derived on the client. */
  canManage: boolean;
  /** Whether the server-side access gate is switched on. */
  gateEnabled: boolean;
  /**
   * The gate does not apply to this user (platform super admin, a bypass
   * permission, gate off, or no billing account behind the user). A suspended
   * standing is then informational: the server will not refuse their requests.
   */
  isExempt: boolean;
  subscriptionId?: string | null;
  subscriptionNo?: string | null;
  planName?: string | null;
  status?: SubscriptionStatus | null;
  pastDueSince?: Date | string | null;
  /** When a past-due subscription will be suspended or expired. */
  graceDeadline?: Date | string | null;
  suspendedAt?: Date | string | null;
  amountDue?: number | null;
  currency?: string | null;
}

/**
 * Update the current billing account's billing profile.
 */
export interface UpdateBillingAccountDto {
  displayName?: string | null;
  billingEmails?: string[] | null;
  address?: string | null;
  taxId?: string | null;
}

/**
 * Browser-safe channel configuration (Stripe publishable key) for mounting the channel's payment UI.
 */
export interface PaymentClientConfigDto {
  channelCode: string;
  publishableKey?: string | null;
}

/**
 * Create subscription request
 */
export interface CreateSubscriptionDto {
  planId: string;
  channelCode?: string;
  couponCode?: string;
  enableTrial?: boolean;
  /** Channel-side token from the setup session; binds a reusable payment method. */
  paymentMethodToken?: string;
  /** Existing stored payment method; falls back to the channel default when omitted. */
  paymentMethodId?: string;
}

/**
 * Create subscription result: the subscription plus the first payment's
 * credentials, so the checkout can be opened without re-querying by order no.
 */
export interface SubscriptionCreateResultDto {
  subscription: SubscriptionDto;
  /** Absent for trial sign-ups and zero-cost subscriptions. */
  payment?: PaymentOrderResultDto | null;
  requiresPayment: boolean;
}

/**
 * Subscription plan change record
 */
export interface SubscriptionChangeDto {
  id: string;
  subscriptionId: string;
  fromPlanId: string;
  fromPlanName?: string | null;
  toPlanId: string;
  toPlanName?: string | null;
  changeType: SubscriptionChangeType;
  /** Positive = amount to top up; negative = credit back to the customer. */
  proratedAmount: number;
  effectiveDate: Date | string;
  status: SubscriptionChangeStatus;
  /** Present when a top-up is due and no card is on file - open the checkout with it. */
  payment?: PaymentOrderResultDto | null;
  /** Trade number of the top-up order; persisted on the change, so it is also present when the change is read back later. */
  paymentTradeNo?: string | null;
  creationTime: Date | string;
}

/**
 * Pause subscription request.
 *
 * Resuming credits the paused duration back onto the next billing date, so a
 * pause never shortens or extends what the customer already paid for.
 */
export interface PauseSubscriptionDto {
  /** Omit to pause until manually resumed (capped by Payment:Subscription:MaxPauseDays). */
  resumeAt?: Date | string;
  reason?: string;
}

/**
 * Subscription query request
 */
export interface SubscriptionQueryDto extends PagedQueryDto {
  userId?: string;
  status?: SubscriptionStatus;
  planId?: string;
  autoRenew?: boolean;
  /** Filter by billing account type (admin list); pair with `billingAccountId`. */
  billingAccountType?: string;
  billingAccountId?: string;
}

/**
 * Subscription plan DTO
 */
export interface SubscriptionPlanDto {
  id: string;
  planCode: string;
  /** Plans sharing a product code are upgrades/downgrades of each other. */
  productCode?: string | null;
  planName: string;
  description?: string | null;
  price: number;
  currency: string;
  cycleType: BillingCycleType;
  cycleValue: number;
  trialDays: number;
  allowTrial: boolean;
  trialDiscount?: number | null;
  /**
   * Storage the plan includes, in bytes (1 GB = 2^30 bytes, see
   * `STORAGE_BYTES_PER_GB`). Null: the plan does not meter storage - no
   * sampling, no limit, no overage.
   */
  includedStorageBytes?: number | null;
  /** Price per GB-month above the included storage, in the plan's currency. Null or 0: metered, not charged. */
  storageOveragePricePerGbMonth?: number | null;
  sortOrder: number;
  isActive: boolean;
  /**
   * What one period's price comes to under the deployment's tax rules. Filled
   * by the customer plan list only (`GET subscriptions/plans`); null on the
   * admin list, and when the tax calculator could not answer.
   */
  tax?: SubscriptionPlanTaxDto | null;
}

/**
 * What the admin plan create / update endpoints accept. `id` is absent on
 * create: the server's `Guid` cannot bind an empty string, and one field that
 * fails to bind makes the whole body fail ("The dto field is required.").
 * A blank `currency` takes the deployment's `Payment:DefaultCurrency`.
 */
export type SubscriptionPlanWriteDto = Omit<SubscriptionPlanDto, 'id' | 'tax'> & { id?: string };

/** Values a new plan form starts with (`GET admin/subscriptions/plans/defaults`). */
export interface SubscriptionPlanDefaultsDto {
  /** The deployment's default currency (`Payment:DefaultCurrency`). */
  currency: string;
}

/** One period of a plan, taxed the way its first payment will be. */
export interface SubscriptionPlanTaxDto {
  /** The price already includes the tax (the payable amount equals the price). */
  taxIncluded: boolean;
  /** Tax for one period; 0 when no tax applies. */
  taxAmount: number;
  /** What one period charges: price plus tax, or the price when tax is included. */
  payableAmount: number;
  lines: TaxLineDto[];
}

/** One tax line, as printed on invoices and receipts (e.g. `HST`, 13, registration number). */
export interface TaxLineDto {
  name: string;
  /** Percent (13 = 13%). */
  rate: number;
  amount: number;
  registrationNumber?: string | null;
}

/**
 * Cancel subscription request
 */
export interface CancelSubscriptionDto {
  reason?: string;
  immediate?: boolean;
}

/**
 * Change subscription plan request (prorated).
 *
 * Replaces the former `ChangeSubscriptionDto`, whose backing service method
 * switched plans without charging the difference at all.
 */
export interface ChangeSubscriptionPlanDto {
  newPlanId: string;
  /** Upgrades take effect immediately; downgrades always wait for period end. */
  effectiveImmediately?: boolean;
}

/**
 * Attach a payment method to a subscription.
 * Supply either a stored method id or a fresh channel token; omitting both
 * uses the user's default method for the subscription's channel.
 */
export interface AttachPaymentMethodDto {
  paymentMethodId?: string;
  paymentMethodToken?: string;
}

// ============================================
// Stored Payment Method (card-on-file) Types
// ============================================

/**
 * Setup session request - step 1 of binding a reusable payment method.
 */
export interface CreateSetupSessionDto {
  channelCode?: string;
  /**
   * Where the channel sends the payer back after they approve (redirect
   * channels such as PayPal). Falls back to the channel's configured URL.
   */
  returnUrl?: string;
  /** Where the payer lands if they abandon the approval. */
  cancelUrl?: string;
}

/**
 * Setup session result. Exactly one of the two fields drives the flow:
 *
 * - `clientSecret` (Stripe): hand it to the channel SDK to collect the payment
 *   method inline, including 3DS.
 * - `approvalUrl` (PayPal): send the payer to that URL. They approve at PayPal
 *   and come back to your `returnUrl`.
 *
 * Either way, `bind` is then called with `setupId` as the token.
 */
export interface SetupSessionDto {
  channelCode: string;
  setupId: string;
  clientSecret?: string | null;
  approvalUrl?: string | null;
}

/**
 * Register a collected payment method - step 3 of binding.
 */
export interface BindPaymentMethodDto {
  paymentMethodToken: string;
  channelCode?: string;
  setAsDefault?: boolean;
}

/**
 * A saved payment method. The channel holds the card; this is only a reference
 * plus display data used for off-session renewal charges.
 */
export interface StoredPaymentMethodDto {
  id: string;
  channelCode: string;
  methodType: PaymentMethod;
  brand?: string | null;
  last4?: string | null;
  /**
   * Masked wallet account (e.g. a PayPal payer email). Wallets have no card
   * digits - with two PayPal accounts bound this is the only thing telling
   * them apart.
   */
  accountLabel?: string | null;
  expiryMonth?: number | null;
  expiryYear?: number | null;
  isDefault: boolean;
  isExpired: boolean;
  lastUsedTime?: Date | string | null;
  creationTime: Date | string;
}

/**
 * Update auto-renew setting
 */
export interface UpdateAutoRenewDto {
  autoRenew: boolean;
}

// ============================================
// Invoice Types
// ============================================

/**
 * Invoice DTO
 */
export interface InvoiceDto {
  id: string;
  invoiceNo: string;
  /** Billing owner; drives "my invoices" (auto-created invoices have no creator). */
  userId?: string | null;
  paymentId?: string | null;
  type: InvoiceType;
  status: InvoiceStatus;
  amount: number;
  currency: string;
  taxAmount: number;
  discountAmount: number;
  dueAmount: number;
  paidAmount: number;
  customerName: string;
  customerEmail: string;
  customerCompany?: string | null;
  invoiceDate: Date | string;
  dueDate?: Date | string | null;
  paidDate?: Date | string | null;
  pdfFileUrl?: string | null;
  /** How many times the invoice has been delivered (capped by the server). */
  sendCount?: number;
  lastSentTime?: Date | string | null;
  /** Card the invoice / receipt was paid with, when known. */
  paymentMethodBrand?: string | null;
  paymentMethodLast4?: string | null;
  /** Credit notes: the invoice they credit, and the refund they were issued for. */
  originalInvoiceId?: string | null;
  refundId?: string | null;
  notes?: string | null;
  lineItems: InvoiceLineItemDto[];
  creationTime: Date | string;
}

/**
 * Invoice line item DTO
 */
export interface InvoiceLineItemDto {
  description: string;
  quantity: number;
  unitPrice: number;
  amount: number;
  discountAmount: number;
  taxRate: number;
  taxAmount: number;
  productCode?: string | null;
  /** Service period the line covers (subscription payments). */
  periodStart?: Date | string | null;
  periodEnd?: Date | string | null;
  /** What kind of line this is, when it is a special one (`STORAGE_OVERAGE_LINE_KIND`); empty for ordinary lines. */
  kind?: string | null;
}

/**
 * Invoice query request
 */
export interface InvoiceQueryDto extends PagedQueryDto {
  invoiceNo?: string;
  type?: InvoiceType;
  status?: InvoiceStatus;
  customerEmail?: string;
  startTime?: Date | string;
  endTime?: Date | string;
}

/**
 * Create invoice request (admin manual)
 */
export interface CreateInvoiceDto {
  type?: InvoiceType;
  /** Billing owner - required for manual invoices to appear under "my invoices". */
  userId?: string;
  /** Required for manual invoices; filled from the payment snapshot otherwise. */
  customerName?: string;
  customerEmail?: string;
  customerCompany?: string;
  customerTaxId?: string;
  customerAddress?: string;
  billingAddress?: string;
  /** Required for manual invoices; defaults to the global currency. */
  currency?: string;
  invoiceDate?: Date | string;
  dueDate?: Date | string;
  templateName?: string;
  notes?: string;
  internalNotes?: string;
  /** Required for manual invoices; derived from the payment otherwise. */
  lineItems?: InvoiceLineItemDto[];
}

/**
 * Send invoice request
 */
export interface SendInvoiceDto {
  recipientEmail?: string;
}

/**
 * Mark invoice as paid request
 */
export interface MarkInvoicePaidDto {
  paidAmount: number;
  remark?: string;
}

/**
 * Cancel invoice request
 */
export interface CancelInvoiceDto {
  reason?: string;
}

// ============================================
// Promotion / Coupon Types
// ============================================

/**
 * Promotion DTO
 */
export interface PromotionDto {
  id: string;
  promotionCode: string;
  name: string;
  description?: string | null;
  type: PromotionType;
  discountValue: number;
  discountType: DiscountType;
  maxDiscountAmount?: number | null;
  minimumOrderAmount?: number | null;
  productType: ProductType;
  applyScope: ApplyScope;
  startTime: Date | string;
  endTime?: Date | string | null;
  totalUsageLimit?: number | null;
  usedCount: number;
  perUserUsageLimit?: number | null;
  stackable: boolean;
  priority: number;
  isActive: boolean;
  firstSubscriptionOnly: boolean;
  /** Currency the fixed-amount discount is denominated in. */
  currency: string;
  /** Public promotions work by entering the code; private ones must be redeemed first. */
  isPublic: boolean;
  /** Target plan/product ids when applyScope is Plan or Product. Empty on list responses. */
  scopeIds: string[];
  isValid: boolean;
}

/**
 * Create promotion request (admin)
 */
export interface CreatePromotionDto {
  promotionCode: string;
  name: string;
  description?: string;
  type: PromotionType;
  discountValue: number;
  discountType: DiscountType;
  maxDiscountAmount?: number;
  minimumOrderAmount?: number;
  productType?: ProductType;
  applyScope?: ApplyScope;
  scopeIds?: string[];
  startTime?: Date | string;
  endTime?: Date | string;
  totalUsageLimit?: number;
  perUserUsageLimit?: number;
  stackable?: boolean;
  priority?: number;
  firstSubscriptionOnly?: boolean;
  currency?: string;
  isPublic?: boolean;
}

/**
 * Update promotion request (admin)
 */
export interface UpdatePromotionDto {
  name?: string;
  description?: string;
  discountValue?: number;
  maxDiscountAmount?: number;
  minimumOrderAmount?: number;
  endTime?: Date | string;
  totalUsageLimit?: number;
  perUserUsageLimit?: number;
  stackable?: boolean;
  priority?: number;
  isActive?: boolean;
  isPublic?: boolean;
}

/**
 * Grant a coupon directly to a user (admin compensation / targeted campaign)
 */
export interface GrantCouponDto {
  userId: string;
}

/**
 * Promotion query request (admin)
 */
export interface PromotionQueryDto extends PagedQueryDto {
  promotionCode?: string;
  type?: PromotionType;
  productType?: ProductType;
  activeOnly?: boolean;
}

/**
 * Validate coupon request
 */
export interface ValidateCouponDto {
  couponCode: string;
  orderAmount: number;
  /** Target plan/product id - checked against the promotion's apply scope. */
  productId?: string;
  productType?: ProductType;
  /** Fixed-amount discounts are currency-bound; omit only in single-currency apps. */
  currency?: string;
}

/**
 * What a payer may see about the promotion behind a coupon.
 *
 * Deliberately not the full `PromotionDto`: `totalUsageLimit` / `usedCount` /
 * `isPublic` / `stripeCouponId` are operator and channel facts. Served from an
 * endpoint any signed-in user can call, used count over total is the live
 * progress of the campaign and `isPublic` says which codes are worth guessing.
 */
export interface CouponSummaryDto {
  name: string;
  description?: string | null;
  discountType: DiscountType;
  discountValue: number;
  currency?: string | null;
  minimumOrderAmount?: number | null;
  maxDiscountAmount?: number | null;
  endTime?: string | null;
}

/**
 * Coupon validation result as answered to the payer (`POST promotions/validate-coupon`).
 */
export interface CouponValidationResponseDto {
  isValid: boolean;
  couponCode?: string | null;
  /** Present when the code resolved to a promotion, valid or not. */
  coupon?: CouponSummaryDto | null;
  discountAmount: number;
  errorMessage?: string | null;
}

/**
 * @deprecated The validate-coupon endpoint no longer returns the full promotion;
 * read `coupon` on `CouponValidationResponseDto` instead.
 */
export type CouponValidationResultDto = CouponValidationResponseDto;

/**
 * Calculate discount request
 */
export interface CalculateDiscountDto {
  couponCode: string;
  orderAmount: number;
  productId?: string;
  productType?: ProductType;
  /** Fixed-amount discounts are currency-bound; omit only in single-currency apps. */
  currency?: string;
}

/**
 * Discount calculation result
 */
export interface DiscountCalculationResultDto {
  couponCode: string;
  originalAmount: number;
  discountAmount: number;
  finalAmount: number;
  discountType: DiscountType;
}

/**
 * User available coupon DTO
 */
export interface UserCouponDto {
  /** Promotion id. */
  id: string;
  /** Set when the coupon was redeemed into the user's wallet; null for public promotions. */
  userCouponId?: string | null;
  /** True when the user actually holds this coupon (vs. a public promotion anyone can enter). */
  isHeld: boolean;
  couponCode: string;
  name: string;
  description?: string | null;
  discountValue: number;
  discountType: DiscountType;
  maxDiscountAmount?: number | null;
  remainingUsageCount: number;
  expireTime?: Date | string | null;
  stackable: boolean;
}

/**
 * Coupon usage record DTO
 */
export interface CouponUsageDto {
  id: string;
  couponCode: string;
  discountAmount: number;
  usedTime: Date | string;
  orderId?: string | null;
  businessOrderNo?: string | null;
  paymentId?: string | null;
}

/**
 * Redeem code request
 */
export interface RedeemCodeDto {
  code: string;
}

/**
 * Create redemption code request (admin)
 */
export interface CreateRedemptionCodeDto {
  promotionId: string;
  quantity: number;
  /**
   * How many times one user may redeem this code. Omitted = 1; 0 = explicitly
   * unlimited (a single user can then take the whole batch). Unique codes are always 1.
   */
  perUserLimit?: number;
}

// ============================================
// Payment Channel Types
// ============================================

/**
 * Payment params DTO (for provider SDK/redirect integration)
 */
export interface PaymentParamsDto {
  tradeNo: string;
  clientSecret?: string | null;
  orderId?: string | null;
  availableMethods: string[];
}

// ============================================
// Statistics Types
// ============================================

/**
 * Payment statistics overview DTO
 */
export interface PaymentStatisticsDto {
  startTime: Date | string;
  endTime: Date | string;
  totalRevenue: number;
  /**
   * Currency of the amounts: the one currency collected in the window, the
   * deployment default when nothing was collected, `null` when currencies are
   * mixed (the totals are then a plain sum and must not carry a symbol).
   */
  currency?: string | null;
  totalTransactions: number;
  successfulTransactions: number;
  failedTransactions: number;
  totalRefunds: number;
  refundCount: number;
  /** Refunds per transaction as a percentage on a 0-100 scale (6 = 6%), not a fraction. */
  refundRate: number;
  /**
   * Active subscription count, or `null` when this deployment does not do
   * subscriptions at all (the optional `Tnzi.Payment.Subscriptions` module is
   * not loaded).
   *
   * Deliberately nullable rather than defaulting to 0: the overview endpoint's
   * payment and refund half is owned by the payment module and keeps working
   * without the recurring-billing package, so this field has to carry some
   * value - and 0 would be indistinguishable from "every subscription
   * disappeared overnight", which is the one alarm a subscription business most
   * needs to see. Render it as not-applicable, not as a number.
   */
  activeSubscriptions: number | null;
  channelDistribution: ChannelStatisticsDto[];
}

/**
 * Channel statistics DTO
 */
export interface ChannelStatisticsDto {
  channelCode: string;
  revenue: number;
  transactionCount: number;
  percentage: number;
}

/**
 * Statistics query DTO
 */
export interface StatisticsQueryDto {
  startTime?: Date | string;
  endTime?: Date | string;
}

/**
 * Revenue trend query DTO
 */
export interface RevenueTrendQueryDto {
  startTime?: Date | string;
  endTime?: Date | string;
  granularity?: TrendGranularity;
}

/**
 * Revenue trend data point DTO
 */
export interface RevenueTrendPointDto {
  date: Date | string;
  revenue: number;
  transactionCount: number;
  refundAmount: number;
  netRevenue: number;
}

/**
 * Subscription metrics DTO
 */
export interface SubscriptionMetricsDto {
  monthlyRecurringRevenue: number;
  activeSubscriptions: number;
  trialSubscriptions: number;
  newSubscriptionsThisMonth: number;
  cancelledThisMonth: number;
  /** This month's cancellations over last month's active subscriptions, as a percentage on a 0-100 scale. */
  churnRate: number;
  averageRevenuePerUser: number;
  planDistribution: PlanDistributionDto[];
}

/**
 * Plan distribution DTO
 */
export interface PlanDistributionDto {
  planName: string;
  subscriptionCount: number;
  revenue: number;
}

/**
 * Promotion analytics DTO
 */
export interface PromotionAnalyticsDto {
  promotionId: string;
  name: string;
  promotionCode: string;
  discountType: string;
  discountValue: number;
  usageCount: number;
  uniqueUsers: number;
  totalDiscountAmount: number;
  averageDiscountPerUse: number;
  redemptionRate: number;
  isActive: boolean;
}

/**
 * Refund analytics DTO
 */
export interface RefundAnalyticsDto {
  totalRefundCount: number;
  totalRefundAmount: number;
  averageProcessingTimeHours: number;
  reasonBreakdown: RefundReasonBreakdownDto[];
  channelBreakdown: RefundChannelBreakdownDto[];
  statusBreakdown: RefundStatusBreakdownDto[];
}

/**
 * Refund reason breakdown DTO
 */
export interface RefundReasonBreakdownDto {
  reason: string;
  count: number;
  amount: number;
  percentage: number;
}

/**
 * Refund channel breakdown DTO
 */
export interface RefundChannelBreakdownDto {
  channelCode: string;
  count: number;
  amount: number;
  percentage: number;
}

/**
 * Refund status breakdown DTO
 */
export interface RefundStatusBreakdownDto {
  status: string;
  count: number;
  percentage: number;
}

/**
 * Reconciliation query DTO
 */
export interface ReconciliationQueryDto {
  startTime?: Date | string;
  endTime?: Date | string;
  channelCode?: string;
  status?: PaymentStatus;
}

/**
 * Reconciliation export result DTO
 */
export interface ReconciliationExportResultDto {
  /**
   * Null on the summary endpoint, which reports counts and totals without the file.
   * The CSV download carries the same counts in the `X-Reconciliation-Total-Records`,
   * `X-Reconciliation-Matched-Records` and `X-Reconciliation-Truncated` response headers.
   */
  csvContent?: string | null;
  fileName: string;
  /** Rows that matched the query window, before the export cap. */
  matchedRecords: number;
  /** Rows actually written; equals `matchedRecords` unless `truncated`. */
  totalRecords: number;
  /** True when the export stopped at the server-side cap; narrow the window and export again. */
  truncated: boolean;
  totalRevenue: number;
  totalRefunds: number;
  netRevenue: number;
}

/**
 * The billing account's storage against its plan's allowance
 * (`GET subscriptions/storage-usage`). Bytes are 1 GB = 2^30 bytes.
 */
export interface StorageUsageDto {
  /**
   * There is a storage reading to show: the subscription in force meters storage, or, with none in force,
   * the product has a plan that does. False: every other field is empty.
   */
  metered: boolean;
  /** The reading is for a subscription in force. False: not subscribed yet (or the first payment is unpaid); read against the plan on offer, nothing accrues. */
  subscribed: boolean;
  /** The server can measure storage (the storage module is loaded). False: metered but unmeasured, nothing is charged. */
  usageAvailable: boolean;
  subscriptionId?: string | null;
  planName?: string | null;
  /** The plan's allowance. Empty before subscribing when the product sells several plans (each card states its own). */
  includedBytes?: number | null;
  /** Current usage: today's daily sample, else a reading taken on demand (cached a few minutes); empty when storage cannot be measured. */
  usedBytes?: number | null;
  /** When the reading was taken (a daily sample reads as that day, 00:00 UTC). */
  sampledOn?: Date | string | null;
  periodStart?: Date | string | null;
  /** Next billing date: the overage accrued in this period is charged with that renewal. */
  periodEnd?: Date | string | null;
  /** Days sampled so far this period. */
  sampleDays: number;
  /** Average of this period's samples so far. */
  averageBytes?: number | null;
  /** GB-months above the allowance at this period's average so far. */
  overageQuantity: number;
  /** Price per GB-month; empty when overage is not charged. */
  overageUnitPrice?: number | null;
  /** What the renewal would add for storage if this period ended now (before tax). Always 0 in a trial. */
  accruedOverageAmount: number;
  currency?: string | null;
  inTrial: boolean;
}
