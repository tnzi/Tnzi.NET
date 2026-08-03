/**
 * Payment Module Types - Payment processing and management
 * Aligned with Tnzi.NET backend Payment module
 */

import type { AuditedEntity } from '../../types/entities';
import type { PagedQueryDto, SortedPagedQueryDto } from '../../types/pagination';
import {
  PaymentStatus,
  PaymentMethod,
  BusinessType,
  RefundStatus,
  CouponType,
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
} from './metadata';

export {
  PaymentStatus,
  PaymentMethod,
  BusinessType,
  RefundStatus,
  CouponType,
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
  creationTime: Date | string;
}

/**
 * Payment detail with refunds
 */
export interface PaymentDetailDto extends PaymentDto {
  refunds: RefundDto[];
  logs: PaymentLogDto[];
}

/**
 * Create payment request
 */
export interface CreatePaymentDto {
  businessOrderNo: string;
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
 * Payment list query
 */
export interface PaymentQueryDto extends SortedPagedQueryDto {
  tradeNo?: string;
  externalTradeNo?: string;
  status?: PaymentStatus;
  businessType?: BusinessType;
  channelCode?: string;
  userId?: string;
  minAmount?: number;
  maxAmount?: number;
  startDate?: Date | string;
  endDate?: Date | string;
  startTime?: Date | string;
  endTime?: Date | string;
  paymentNo?: string;
  businessOrderNo?: string;
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
  creationTime: Date | string;
}

/**
 * Create refund request
 */
export interface CreateRefundDto {
  tradeNo: string;
  refundAmount: number;
  reason: string;
  remark?: string;
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
  renewalRetryCount: number;
  creationTime: Date | string;
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
  extraData?: string;
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
  sortOrder: number;
  isActive: boolean;
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
  paymentId?: string;
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
 * Coupon validation result
 */
export interface CouponValidationResultDto {
  isValid: boolean;
  couponCode?: string | null;
  promotion?: PromotionDto | null;
  discountAmount: number;
  errorMessage?: string | null;
}

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
}

/**
 * Apply coupon request (legacy compatibility)
 */
export interface ApplyCouponDto {
  code: string;
  amount: number;
  businessType: BusinessType;
}

/**
 * Coupon DTO (legacy compatibility for admin promotion listing)
 */
export interface CouponDto extends AuditedEntity<string> {
  promotionCode?: string;
  code: string;
  name: string;
  type: CouponType;
  value: number;
  minAmount: number;
  maxDiscount?: number | null;
  startDate: Date | string;
  endDate: Date | string;
  totalCount: number;
  usedCount: number;
  perUserLimit: number;
  isEnabled: boolean;
  isActive?: boolean;
  applicableBusinessTypes?: BusinessType[];
}

/**
 * Coupon validation DTO (legacy compatibility)
 */
export interface CouponValidationDto {
  isValid: boolean;
  couponCode?: string;
  couponId?: string;
  couponName?: string;
  discountAmount: number;
  message?: string;
  errorMessage?: string;
  promotion?: CouponDto | null;
}

// ============================================
// Payment Channel Types
// ============================================

/**
 * Payment channel DTO
 */
export interface PaymentChannelDto {
  code: string;
  name: string;
  type: string;
  icon?: string | null;
  isEnabled: boolean;
  supportedMethods: PaymentMethod[];
  supportedCurrencies: string[];
  minAmount: number;
  maxAmount: number;
  feeRate: number;
  feeFixed: number;
}

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
// Payment Log Types
// ============================================

/**
 * Payment log DTO
 */
export interface PaymentLogDto {
  id: string;
  paymentId: string;
  action: string;
  status: PaymentStatus;
  message?: string | null;
  requestData?: string | null;
  responseData?: string | null;
  createdAt: Date | string;
  createdBy?: string | null;
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
  totalTransactions: number;
  successfulTransactions: number;
  failedTransactions: number;
  totalRefunds: number;
  refundCount: number;
  refundRate: number;
  activeSubscriptions: number;
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
  csvContent: string;
  fileName: string;
  totalRecords: number;
  totalRevenue: number;
  totalRefunds: number;
  netRevenue: number;
}
