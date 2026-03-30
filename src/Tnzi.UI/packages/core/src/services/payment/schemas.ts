/**
 * Payment Module Zod Schemas
 */

import { z } from 'zod';

// ============================================
// Enum Schemas
// ============================================

export const paymentStatusSchema = z.number().int().min(0).max(6);
export const paymentMethodSchema = z.number().int().min(1).max(99);
export const businessTypeSchema = z.number().int().min(1).max(99);
export const refundStatusSchema = z.number().int().min(0).max(7);
export const couponTypeSchema = z.number().int().min(1).max(2);
export const subscriptionStatusSchema = z.number().int().min(0).max(6);
export const billingCycleTypeSchema = z.number().int().min(1).max(5);
export const invoiceStatusSchema = z.number().int().min(0).max(5);
export const invoiceTypeSchema = z.number().int().min(1).max(3);
export const promotionTypeSchema = z.number().int().min(1).max(5);
export const discountTypeSchema = z.number().int().min(1).max(2);
export const productTypeSchema = z.number().int().min(1).max(99);
export const applyScopeSchema = z.number().int().min(0).max(2);
export const trendGranularitySchema = z.number().int().min(1).max(3);
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
  tradeNo: z.string().min(1),
  refundAmount: z.number().positive(),
  reason: z.string().min(1).max(500),
  remark: z.string().max(500).optional(),
});

export const refundQuerySchema = z.object({
  pageIndex: z.number().int().min(1).default(1),
  pageSize: z.number().int().min(1).max(100).default(10),
  tradeNo: z.string().optional(),
  refundNo: z.string().optional(),
  status: refundStatusSchema.optional(),
  startTime: z.union([z.date(), z.string()]).optional(),
  endTime: z.union([z.date(), z.string()]).optional(),
});

export const approveRefundSchema = z.object({
  approved: z.boolean(),
  remark: z.string().max(500).optional(),
});

// ============================================
// Subscription Schemas
// ============================================

export const subscriptionDtoSchema = z.object({
  id: z.string(),
  subscriptionNo: z.string(),
  userId: z.string(),
  planId: z.string(),
  planName: z.string().nullable().optional(),
  status: subscriptionStatusSchema,
  cycleType: billingCycleTypeSchema,
  cycleValue: z.number().int().positive(),
  startTime: z.union([z.date(), z.string()]),
  endTime: z.union([z.date(), z.string()]).nullable().optional(),
  nextBillingTime: z.union([z.date(), z.string()]).nullable().optional(),
  trialStartTime: z.union([z.date(), z.string()]).nullable().optional(),
  trialEndTime: z.union([z.date(), z.string()]).nullable().optional(),
  originalPrice: z.number().nonnegative(),
  paidAmount: z.number().nonnegative(),
  discountAmount: z.number().nonnegative(),
  currency: z.string(),
  autoRenew: z.boolean(),
  creationTime: z.union([z.date(), z.string()]),
});

export const createSubscriptionSchema = z.object({
  planId: z.string().min(1),
  channelCode: z.string().optional(),
  couponCode: z.string().max(50).optional(),
  enableTrial: z.boolean().optional(),
  paymentMethodId: z.string().optional(),
  extraData: z.string().optional(),
});

export const subscriptionQuerySchema = z.object({
  pageIndex: z.number().int().min(1).default(1),
  pageSize: z.number().int().min(1).max(100).default(10),
  userId: z.string().optional(),
  status: subscriptionStatusSchema.optional(),
  planId: z.string().optional(),
  autoRenew: z.boolean().optional(),
});

export const subscriptionPlanDtoSchema = z.object({
  id: z.string(),
  planCode: z.string().min(1),
  planName: z.string().min(1),
  description: z.string().nullable().optional(),
  price: z.number().nonnegative(),
  currency: z.string(),
  cycleType: billingCycleTypeSchema,
  cycleValue: z.number().int().positive(),
  trialDays: z.number().int().nonnegative(),
  allowTrial: z.boolean(),
  trialDiscount: z.number().nullable().optional(),
  sortOrder: z.number().int(),
  isActive: z.boolean(),
});

export const cancelSubscriptionSchema = z.object({
  reason: z.string().max(500).optional(),
  immediate: z.boolean().optional(),
});

export const changeSubscriptionSchema = z.object({
  newPlanId: z.string().min(1),
  effectiveTime: z.string().optional(),
});

// ============================================
// Invoice Schemas
// ============================================

export const invoiceLineItemSchema = z.object({
  description: z.string().min(1),
  quantity: z.number().positive(),
  unitPrice: z.number().nonnegative(),
  amount: z.number().nonnegative(),
  discountAmount: z.number().nonnegative(),
  taxRate: z.number().nonnegative(),
  taxAmount: z.number().nonnegative(),
  productCode: z.string().nullable().optional(),
});

export const invoiceDtoSchema = z.object({
  id: z.string(),
  invoiceNo: z.string(),
  type: invoiceTypeSchema,
  status: invoiceStatusSchema,
  amount: z.number().nonnegative(),
  currency: z.string(),
  taxAmount: z.number().nonnegative(),
  discountAmount: z.number().nonnegative(),
  dueAmount: z.number().nonnegative(),
  paidAmount: z.number().nonnegative(),
  customerName: z.string(),
  customerEmail: z.string().email(),
  customerCompany: z.string().nullable().optional(),
  invoiceDate: z.union([z.date(), z.string()]),
  dueDate: z.union([z.date(), z.string()]).nullable().optional(),
  paidDate: z.union([z.date(), z.string()]).nullable().optional(),
  pdfFileUrl: z.string().nullable().optional(),
  notes: z.string().nullable().optional(),
  lineItems: z.array(invoiceLineItemSchema),
  creationTime: z.union([z.date(), z.string()]),
});

export const invoiceQuerySchema = z.object({
  pageIndex: z.number().int().min(1).default(1),
  pageSize: z.number().int().min(1).max(100).default(10),
  invoiceNo: z.string().optional(),
  type: invoiceTypeSchema.optional(),
  status: invoiceStatusSchema.optional(),
  customerEmail: z.string().optional(),
  startTime: z.union([z.date(), z.string()]).optional(),
  endTime: z.union([z.date(), z.string()]).optional(),
});

export const createInvoiceSchema = z.object({
  type: invoiceTypeSchema.optional(),
  paymentId: z.string().optional(),
  customerName: z.string().min(1),
  customerEmail: z.string().email(),
  customerCompany: z.string().optional(),
  customerTaxId: z.string().optional(),
  customerAddress: z.string().optional(),
  billingAddress: z.string().optional(),
  invoiceDate: z.union([z.date(), z.string()]).optional(),
  dueDate: z.union([z.date(), z.string()]).optional(),
  templateName: z.string().optional(),
  notes: z.string().optional(),
  internalNotes: z.string().optional(),
  lineItems: z.array(invoiceLineItemSchema).min(1),
});

export const markInvoicePaidSchema = z.object({
  paidAmount: z.number().positive(),
  remark: z.string().max(500).optional(),
});

// ============================================
// Promotion Schemas
// ============================================

export const promotionDtoSchema = z.object({
  id: z.string(),
  promotionCode: z.string(),
  name: z.string(),
  description: z.string().nullable().optional(),
  type: promotionTypeSchema,
  discountValue: z.number().nonnegative(),
  discountType: discountTypeSchema,
  maxDiscountAmount: z.number().nullable().optional(),
  minimumOrderAmount: z.number().nullable().optional(),
  productType: productTypeSchema,
  applyScope: applyScopeSchema,
  startTime: z.union([z.date(), z.string()]),
  endTime: z.union([z.date(), z.string()]).nullable().optional(),
  totalUsageLimit: z.number().int().nullable().optional(),
  usedCount: z.number().int().nonnegative(),
  perUserUsageLimit: z.number().int().nullable().optional(),
  stackable: z.boolean(),
  priority: z.number().int(),
  isActive: z.boolean(),
  firstSubscriptionOnly: z.boolean(),
  isValid: z.boolean(),
});

export const createPromotionSchema = z.object({
  promotionCode: z.string().min(1).regex(/^[A-Za-z0-9_-]+$/),
  name: z.string().min(1),
  description: z.string().optional(),
  type: promotionTypeSchema,
  discountValue: z.number().nonnegative(),
  discountType: discountTypeSchema,
  maxDiscountAmount: z.number().optional(),
  minimumOrderAmount: z.number().optional(),
  productType: productTypeSchema.optional(),
  applyScope: applyScopeSchema.optional(),
  scopeIds: z.array(z.string()).optional(),
  startTime: z.union([z.date(), z.string()]).optional(),
  endTime: z.union([z.date(), z.string()]).optional(),
  totalUsageLimit: z.number().int().optional(),
  perUserUsageLimit: z.number().int().optional(),
  stackable: z.boolean().optional(),
  priority: z.number().int().optional(),
  firstSubscriptionOnly: z.boolean().optional(),
});

export const updatePromotionSchema = z.object({
  name: z.string().min(1).optional(),
  description: z.string().optional(),
  discountValue: z.number().nonnegative().optional(),
  maxDiscountAmount: z.number().optional(),
  minimumOrderAmount: z.number().optional(),
  endTime: z.union([z.date(), z.string()]).optional(),
  totalUsageLimit: z.number().int().optional(),
  perUserUsageLimit: z.number().int().optional(),
  stackable: z.boolean().optional(),
  priority: z.number().int().optional(),
  isActive: z.boolean().optional(),
});

export const promotionQuerySchema = z.object({
  pageIndex: z.number().int().min(1).default(1),
  pageSize: z.number().int().min(1).max(100).default(10),
  promotionCode: z.string().optional(),
  type: promotionTypeSchema.optional(),
  productType: productTypeSchema.optional(),
  activeOnly: z.boolean().optional(),
});

// ============================================
// Coupon Schemas (legacy compatibility)
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
  couponCode: z.string(),
  name: z.string(),
  description: z.string().nullable().optional(),
  discountValue: z.number().positive(),
  discountType: discountTypeSchema,
  maxDiscountAmount: z.number().nullable().optional(),
  remainingUsageCount: z.number().int().nonnegative(),
  expireTime: z.union([z.date(), z.string()]).nullable().optional(),
  stackable: z.boolean(),
});

export const applyCouponSchema = z.object({
  code: z.string().min(1).max(50),
  amount: z.number().positive(),
  businessType: businessTypeSchema,
});

export const couponValidationSchema = z.object({
  isValid: z.boolean(),
  couponCode: z.string().nullable().optional(),
  discountAmount: z.number().nonnegative(),
  errorMessage: z.string().nullable().optional(),
});

// ============================================
// Statistics Schemas
// ============================================

export const statisticsQuerySchema = z.object({
  startTime: z.union([z.date(), z.string()]).optional(),
  endTime: z.union([z.date(), z.string()]).optional(),
});

export const revenueTrendQuerySchema = z.object({
  startTime: z.union([z.date(), z.string()]).optional(),
  endTime: z.union([z.date(), z.string()]).optional(),
  granularity: trendGranularitySchema.optional(),
});

export const reconciliationQuerySchema = z.object({
  startTime: z.union([z.date(), z.string()]).optional(),
  endTime: z.union([z.date(), z.string()]).optional(),
  channelCode: z.string().optional(),
  status: paymentStatusSchema.optional(),
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
