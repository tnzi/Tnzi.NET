/**
 * Payment Module Zod Schemas
 */

import { z } from 'zod';

// ============================================
// Enum Schemas
// ============================================

export const paymentStatusSchema = z.number().int().min(0).max(8);
export const paymentMethodSchema = z.number().int().min(1).max(99);
export const businessTypeSchema = z.number().int().min(1).max(99);
export const refundStatusSchema = z.number().int().min(0).max(3);
export const couponTypeSchema = z.number().int().min(1).max(2);
export const currencySchema = z.enum(['CNY', 'USD', 'EUR', 'GBP', 'JPY', 'HKD', 'TWD']);

// ============================================
// Payment Schemas
// ============================================

export const paymentDtoSchema = z.object({
  id: z.string(),
  tradeNo: z.string().optional(),
  paymentNo: z.string().min(1),
  externalTradeNo: z.string().nullable().optional(),
  businessOrderNo: z.string().min(1),
  businessType: businessTypeSchema,
  amount: z.number().nonnegative(),
  originalAmount: z.number().nonnegative().optional(),
  paidAmount: z.number().nonnegative().optional(),
  currency: z.string(),
  status: paymentStatusSchema,
  channelCode: z.string().nullable().optional(),
  channelName: z.string().nullable().optional(),
  channelTransactionId: z.string().nullable().optional(),
  paymentMethod: paymentMethodSchema.nullable().optional(),
  description: z.string().nullable().optional(),
  expireTime: z.union([z.date(), z.string()]).nullable().optional(),
  paidTime: z.union([z.date(), z.string()]).nullable().optional(),
  closedTime: z.union([z.date(), z.string()]).nullable().optional(),
  userId: z.string(),
  userName: z.string().nullable().optional(),
  couponId: z.string().nullable().optional(),
  couponCode: z.string().nullable().optional(),
  discountAmount: z.number().nonnegative(),
  finalAmount: z.number().nonnegative(),
  refundAmount: z.number().nonnegative(),
  extraData: z.string().nullable().optional(),
  qrCodeUrl: z.string().url().nullable().optional(),
  h5Url: z.string().url().nullable().optional(),
  creationTime: z.union([z.date(), z.string()]),
  lastModificationTime: z.union([z.date(), z.string()]).nullable().optional(),
});

export const createPaymentSchema = z.object({
  businessOrderNo: z.string().min(1).max(100),
  businessType: businessTypeSchema,
  amount: z.number().positive(),
  currency: currencySchema.optional(),
  channelCode: z.string().optional(),
  paymentMethod: paymentMethodSchema.optional(),
  description: z.string().max(500).optional(),
  expireMinutes: z.number().int().min(1).max(1440).optional(),
  couponCode: z.string().max(50).optional(),
  returnUrl: z.string().url().optional(),
  notifyUrl: z.string().url().optional(),
  extraData: z.string().optional(),
});

export const paymentQuerySchema = z.object({
  pageIndex: z.number().int().min(1).default(1),
  pageSize: z.number().int().min(1).max(100).default(10),
  tradeNo: z.string().optional(),
  externalTradeNo: z.string().optional(),
  status: paymentStatusSchema.optional(),
  businessType: businessTypeSchema.optional(),
  channelCode: z.string().optional(),
  userId: z.string().optional(),
  minAmount: z.number().nonnegative().optional(),
  maxAmount: z.number().positive().optional(),
  startDate: z.union([z.date(), z.string()]).optional(),
  endDate: z.union([z.date(), z.string()]).optional(),
  startTime: z.union([z.date(), z.string()]).optional(),
  endTime: z.union([z.date(), z.string()]).optional(),
  paymentNo: z.string().optional(),
  businessOrderNo: z.string().optional(),
  sortBy: z.string().optional(),
  sortDescending: z.boolean().optional(),
});

export const paymentParamsSchema = z.object({
  tradeNo: z.string().min(1),
  clientSecret: z.string().nullable().optional(),
  orderId: z.string().nullable().optional(),
  availableMethods: z.array(z.string()),
});

// ============================================
// Refund Schemas
// ============================================

export const refundDtoSchema = z.object({
  id: z.string(),
  refundNo: z.string().min(1),
  tradeNo: z.string().optional(),
  paymentId: z.string(),
  paymentNo: z.string(),
  amount: z.number().positive(),
  refundAmount: z.number().positive().optional(),
  currency: z.string().optional(),
  reason: z.string().min(1),
  status: refundStatusSchema,
  channelRefundId: z.string().nullable().optional(),
  approverId: z.string().nullable().optional(),
  approveTime: z.union([z.date(), z.string()]).nullable().optional(),
  approveRemark: z.string().nullable().optional(),
  completedTime: z.union([z.date(), z.string()]).nullable().optional(),
  refundedTime: z.union([z.date(), z.string()]).nullable().optional(),
  operatorId: z.string().nullable().optional(),
  operatorName: z.string().nullable().optional(),
  creationTime: z.union([z.date(), z.string()]),
  lastModificationTime: z.union([z.date(), z.string()]).nullable().optional(),
});

export const createRefundSchema = z.object({
  tradeNo: z.string().min(1).optional(),
  refundAmount: z.number().positive().optional(),
  remark: z.string().max(500).optional(),
  paymentId: z.string().min(1).optional(),
  amount: z.number().positive().optional(),
  reason: z.string().min(1).max(500),
  notifyUrl: z.string().url().optional(),
}).refine(
  (data) => (data.tradeNo && data.refundAmount) || (data.paymentId && data.amount),
  {
    message: 'Either (tradeNo + refundAmount) or (paymentId + amount) is required',
  }
);

export const refundQuerySchema = z.object({
  pageIndex: z.number().int().min(1).default(1),
  pageSize: z.number().int().min(1).max(100).default(10),
  tradeNo: z.string().optional(),
  refundNo: z.string().optional(),
  status: refundStatusSchema.optional(),
  startTime: z.union([z.date(), z.string()]).optional(),
  endTime: z.union([z.date(), z.string()]).optional(),
});

// ============================================
// Coupon Schemas
// ============================================

export const couponDtoSchema = z.object({
  id: z.string(),
  code: z.string().min(1).max(50),
  name: z.string().min(1).max(100),
  type: couponTypeSchema,
  value: z.number().positive(),
  minAmount: z.number().nonnegative(),
  maxDiscount: z.number().positive().nullable().optional(),
  startDate: z.union([z.date(), z.string()]),
  endDate: z.union([z.date(), z.string()]),
  totalCount: z.number().int().nonnegative(),
  usedCount: z.number().int().nonnegative(),
  perUserLimit: z.number().int().nonnegative(),
  isEnabled: z.boolean(),
  applicableBusinessTypes: z.array(businessTypeSchema).optional(),
  creationTime: z.union([z.date(), z.string()]),
  lastModificationTime: z.union([z.date(), z.string()]).nullable().optional(),
});

export const userCouponDtoSchema = z.object({
  id: z.string(),
  couponId: z.string(),
  couponCode: z.string(),
  couponName: z.string(),
  type: couponTypeSchema,
  value: z.number().positive(),
  minAmount: z.number().nonnegative(),
  maxDiscount: z.number().positive().nullable().optional(),
  endDate: z.union([z.date(), z.string()]),
  isUsed: z.boolean(),
  usedAt: z.union([z.date(), z.string()]).nullable().optional(),
  orderId: z.string().nullable().optional(),
});

export const applyCouponSchema = z.object({
  code: z.string().min(1).max(50),
  amount: z.number().positive(),
  businessType: businessTypeSchema,
});

export const couponValidationSchema = z.object({
  isValid: z.boolean(),
  couponId: z.string().optional(),
  couponName: z.string().optional(),
  discountAmount: z.number().nonnegative(),
  message: z.string().optional(),
});

// ============================================
// Channel Schemas
// ============================================

export const paymentChannelDtoSchema = z.object({
  code: z.string(),
  name: z.string(),
  type: z.string(),
  icon: z.string().nullable().optional(),
  isEnabled: z.boolean(),
  supportedMethods: z.array(paymentMethodSchema),
  supportedCurrencies: z.array(z.string()),
  minAmount: z.number().nonnegative(),
  maxAmount: z.number().positive(),
  feeRate: z.number().nonnegative(),
  feeFixed: z.number().nonnegative(),
});
