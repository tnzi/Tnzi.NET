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
} from './metadata';

export {
  PaymentStatus,
  PaymentMethod,
  BusinessType,
  RefundStatus,
  CouponType,
};

// ============================================
// Payment Types
// ============================================

/**
 * Payment record DTO
 */
export interface PaymentDto extends AuditedEntity<string> {
  tradeNo?: string;
  paymentNo: string;
  externalTradeNo?: string | null;
  businessOrderNo: string;
  businessType: BusinessType;
  amount: number;
  originalAmount?: number;
  paidAmount?: number;
  currency: string;
  status: PaymentStatus;
  channelCode?: string | null;
  channelName?: string | null;
  channelTransactionId?: string | null;
  paymentMethod?: PaymentMethod | null;
  description?: string | null;
  expireTime?: Date | string | null;
  paidTime?: Date | string | null;
  closedTime?: Date | string | null;
  userId: string;
  userName?: string | null;
  couponId?: string | null;
  couponCode?: string | null;
  discountAmount: number;
  finalAmount: number;
  refundAmount: number;
  extraData?: string | null;
  qrCodeUrl?: string | null;
  h5Url?: string | null;
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
  notifyUrl?: string;
  extraData?: string;
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
 * Refund DTO
 */
export interface RefundDto extends AuditedEntity<string> {
  refundNo: string;
  tradeNo?: string;
  paymentId: string;
  paymentNo: string;
  amount: number;
  refundAmount?: number;
  reason: string;
  status: RefundStatus;
  channelRefundId?: string | null;
  approverId?: string | null;
  approveTime?: Date | string | null;
  approveRemark?: string | null;
  completedTime?: Date | string | null;
  currency?: string;
  refundedTime?: Date | string | null;
  operatorId?: string | null;
  operatorName?: string | null;
}

/**
 * Create refund request
 */
export interface CreateRefundDto {
  tradeNo?: string;
  refundAmount?: number;
  remark?: string;
  paymentId?: string;
  amount?: number;
  reason: string;
  notifyUrl?: string;
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
// Coupon Types
// ============================================

/**
 * Coupon DTO
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
 * User coupon DTO
 */
export interface UserCouponDto {
  id: string;
  couponId: string;
  couponCode: string;
  name?: string;
  couponName: string;
  description?: string | null;
  type: CouponType;
  value: number;
  discountValue?: number;
  discountType?: string;
  minAmount: number;
  maxDiscount?: number | null;
  maxDiscountAmount?: number | null;
  remainingUsageCount?: number;
  stackable?: boolean;
  endDate: Date | string;
  expireTime?: Date | string | null;
  isUsed: boolean;
  usedAt?: Date | string | null;
  orderId?: string | null;
}

/**
 * Apply coupon request
 */
export interface ApplyCouponDto {
  code: string;
  amount: number;
  businessType: BusinessType;
}

/**
 * Coupon validation result
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
// Statistics Types
// ============================================

/**
 * Payment statistics
 */
export interface PaymentStatisticsDto {
  totalPayments: number;
  totalAmount: number;
  totalRefunded: number;
  successRate: number;
  byStatus: Record<PaymentStatus, number>;
  byChannel: Record<string, ChannelStatistics>;
  byBusinessType: Record<BusinessType, number>;
  dailyStats: DailyPaymentStats[];
}

/**
 * Channel statistics
 */
export interface ChannelStatistics {
  count: number;
  amount: number;
  fee: number;
}

/**
 * Daily payment statistics
 */
export interface DailyPaymentStats {
  date: string;
  count: number;
  amount: number;
  refunded: number;
}
