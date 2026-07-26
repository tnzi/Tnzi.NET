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
  currency: string;
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
  amount: number;
  currency: string;
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
  paymentMethodId?: string;
  extraData?: string;
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
 * Change subscription plan request
 */
export interface ChangeSubscriptionDto {
  newPlanId: string;
  effectiveTime?: string;
}

/**
 * Update payment method for subscription
 */
export interface UpdatePaymentMethodDto {
  paymentMethodId: string;
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
  customerName: string;
  customerEmail: string;
  customerCompany?: string;
  customerTaxId?: string;
  customerAddress?: string;
  billingAddress?: string;
  invoiceDate?: Date | string;
  dueDate?: Date | string;
  templateName?: string;
  notes?: string;
  internalNotes?: string;
  lineItems: InvoiceLineItemDto[];
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
  productId?: string;
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
  id: string;
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
