/**
 * Payment Module API - Payment, Refund, Subscription, Invoice, Promotion, and Statistics
 */

import type { HttpClient } from '../../http/http';
import type { PagedList } from '../../types/pagination';
import type {
  PaymentDto,
  PaymentOrderResultDto,
  CreatePaymentDto,
  PaymentQueryDto,
  PaymentParamsDto,
  RefundDto,
  CreateRefundDto,
  RefundQueryDto,
  ApproveRefundDto,
  SubscriptionDto,
  CreateSubscriptionDto,
  SubscriptionQueryDto,
  SubscriptionPlanDto,
  CancelSubscriptionDto,
  ChangeSubscriptionDto,
  InvoiceDto,
  InvoiceQueryDto,
  CreateInvoiceDto,
  SendInvoiceDto,
  MarkInvoicePaidDto,
  // CancelInvoiceDto is used by the cancel method inline
  PromotionDto,
  CreatePromotionDto,
  UpdatePromotionDto,
  PromotionQueryDto,
  CouponValidationResultDto,
  DiscountCalculationResultDto,
  UserCouponDto,
  CouponUsageDto,
  ApplyCouponDto,
  CouponValidationDto,
  PaymentStatisticsDto,
  StatisticsQueryDto,
  RevenueTrendPointDto,
  RevenueTrendQueryDto,
  SubscriptionMetricsDto,
  PromotionAnalyticsDto,
  RefundAnalyticsDto,
  ReconciliationQueryDto,
  ReconciliationExportResultDto,
} from './types';

const BASE = '/payments';
const REFUND_BASE = '/refunds';
const SUBSCRIPTION_BASE = '/subscriptions';
const INVOICE_BASE = '/invoices';
const PROMOTION_BASE = '/promotions';
const ADMIN_BASE = '/admin/payments';
const ADMIN_REFUND_BASE = '/admin/refunds';
const ADMIN_SUBSCRIPTION_BASE = '/admin/subscriptions';
const ADMIN_INVOICE_BASE = '/admin/invoices';
const ADMIN_PROMOTION_BASE = '/admin/promotions';
const ADMIN_STATISTICS_BASE = '/admin/payment-statistics';

// ============================================
// User APIs
// ============================================

/**
 * Payment API (User)
 */
export function usePaymentApi(client: HttpClient) {
  return {
    /** Get payment list */
    getList: (params?: PaymentQueryDto) =>
      client.get<PagedList<PaymentDto>>(BASE, { params }),

    /** Get payment by trade number */
    get: (tradeNo: string) =>
      client.get<PaymentDto>(`${BASE}/${tradeNo}`),

    /** Create payment order */
    create: (data: CreatePaymentDto) =>
      client.post<PaymentOrderResultDto>(BASE, data),

    /** Close payment order */
    close: (tradeNo: string, reason?: string) =>
      client.post<void>(`${BASE}/${tradeNo}/close`, { reason }),

    /** Get payment params for channel */
    getParams: (tradeNo: string) =>
      client.get<PaymentParamsDto>(`${BASE}/${tradeNo}/params`),

    /** Sync order status */
    sync: (tradeNo: string) =>
      client.post<void>(`${BASE}/${tradeNo}/sync`),
  };
}

/**
 * Refund API (User)
 */
export function useRefundApi(client: HttpClient) {
  return {
    /** Get refund list */
    getList: (params?: RefundQueryDto) =>
      client.get<PagedList<RefundDto>>(REFUND_BASE, { params }),

    /** Get refund by ID */
    get: (id: string) =>
      client.get<RefundDto>(`${REFUND_BASE}/${id}`),

    /** Get refunds by trade number */
    getByTradeNo: (tradeNo: string) =>
      client.get<RefundDto[]>(`${REFUND_BASE}/trade/${tradeNo}`),

    /** Create refund */
    create: (data: CreateRefundDto) =>
      client.post<RefundDto>(REFUND_BASE, data),
  };
}

/**
 * Subscription API (User)
 */
export function useSubscriptionApi(client: HttpClient) {
  return {
    /** Get subscription plans */
    getPlans: (activeOnly = true) =>
      client.get<SubscriptionPlanDto[]>(`${SUBSCRIPTION_BASE}/plans`, {
        params: { activeOnly },
      }),

    /** Create subscription */
    create: (data: CreateSubscriptionDto) =>
      client.post<SubscriptionDto>(SUBSCRIPTION_BASE, data),

    /** Get subscription by ID */
    get: (id: string) =>
      client.get<SubscriptionDto>(`${SUBSCRIPTION_BASE}/${id}`),

    /** Get my subscriptions */
    getMy: () =>
      client.get<PagedList<SubscriptionDto>>(`${SUBSCRIPTION_BASE}/my`),

    /** Get subscription list */
    getList: (params?: SubscriptionQueryDto) =>
      client.get<PagedList<SubscriptionDto>>(SUBSCRIPTION_BASE, { params }),

    /** Cancel subscription */
    cancel: (id: string, data: CancelSubscriptionDto) =>
      client.post<void>(`${SUBSCRIPTION_BASE}/${id}/cancel`, data),

    /** Resume subscription */
    resume: (id: string) =>
      client.post<void>(`${SUBSCRIPTION_BASE}/${id}/resume`),

    /** Change subscription plan */
    changePlan: (id: string, data: ChangeSubscriptionDto) =>
      client.post<SubscriptionDto>(`${SUBSCRIPTION_BASE}/${id}/change-plan`, data),

    /** Update payment method */
    updatePaymentMethod: (id: string, paymentMethodId: string) =>
      client.post<void>(`${SUBSCRIPTION_BASE}/${id}/payment-method`, { paymentMethodId }),

    /** Update auto-renew setting */
    updateAutoRenew: (id: string, autoRenew: boolean) =>
      client.post<void>(`${SUBSCRIPTION_BASE}/${id}/auto-renew`, { autoRenew }),
  };
}

/**
 * Invoice API (User)
 */
export function useInvoiceApi(client: HttpClient) {
  return {
    /** Get invoice list */
    getList: (params?: InvoiceQueryDto) =>
      client.get<PagedList<InvoiceDto>>(INVOICE_BASE, { params }),

    /** Get invoice by ID */
    get: (id: string) =>
      client.get<InvoiceDto>(`${INVOICE_BASE}/${id}`),

    /** Get my invoices */
    getMy: () =>
      client.get<PagedList<InvoiceDto>>(`${INVOICE_BASE}/my`),

    /** Download invoice PDF */
    downloadPdf: (id: string) =>
      client.get<string>(`${INVOICE_BASE}/${id}/pdf`),

    /** Send invoice to email */
    send: (id: string, data?: SendInvoiceDto) =>
      client.post<void>(`${INVOICE_BASE}/${id}/send`, data),
  };
}

/**
 * Promotion API (User)
 */
export function useCouponApi(client: HttpClient) {
  return {
    /** Get my available coupons */
    getMyCoupons: () =>
      client.get<UserCouponDto[]>(`${PROMOTION_BASE}/my-coupons`),

    /** Get my used coupons */
    getUsedCoupons: () =>
      client.get<CouponUsageDto[]>(`${PROMOTION_BASE}/used-coupons`),

    /** Apply coupon and calculate discount */
    apply: (data: ApplyCouponDto) =>
      client.post<CouponValidationDto>(`${PROMOTION_BASE}/calculate-discount`, {
        couponCode: data.code,
        orderAmount: data.amount,
      }),

    /** Validate coupon */
    validate: (data: ApplyCouponDto) =>
      client.post<CouponValidationDto>(`${PROMOTION_BASE}/validate-coupon`, {
        couponCode: data.code,
        orderAmount: data.amount,
      }),

    /** Validate coupon (typed) */
    validateCoupon: (data: { couponCode: string; orderAmount: number; productId?: string }) =>
      client.post<CouponValidationResultDto>(`${PROMOTION_BASE}/validate-coupon`, data),

    /** Calculate discount (typed) */
    calculateDiscount: (data: { couponCode: string; orderAmount: number }) =>
      client.post<DiscountCalculationResultDto>(`${PROMOTION_BASE}/calculate-discount`, data),

    /** Redeem coupon code */
    redeem: (code: string) =>
      client.post<UserCouponDto>(`${PROMOTION_BASE}/redeem`, { code }),

    /** Check if first subscription discount is available */
    canUseFirstSubscriptionDiscount: () =>
      client.get<boolean>(`${PROMOTION_BASE}/first-subscription-check`),
  };
}

// ============================================
// Admin APIs
// ============================================

/**
 * Admin Payment Management API
 */
export function useAdminPaymentApi(client: HttpClient) {
  return {
    /** Get payment list */
    getList: (params?: PaymentQueryDto) =>
      client.get<PagedList<PaymentDto>>(ADMIN_BASE, { params }),

    /** Get payment details */
    get: (tradeNo: string) =>
      client.get<PaymentDto>(`${ADMIN_BASE}/${tradeNo}`),

    /** Close payment order */
    close: (tradeNo: string, reason?: string) =>
      client.post<void>(`${ADMIN_BASE}/${tradeNo}/close`, { reason }),

    /** Sync order status */
    sync: (tradeNo: string) =>
      client.post<void>(`${ADMIN_BASE}/${tradeNo}/sync`),
  };
}

/**
 * Admin Refund Management API
 */
export function useAdminRefundApi(client: HttpClient) {
  return {
    /** Get refund list */
    getList: (params?: RefundQueryDto) =>
      client.get<PagedList<RefundDto>>(ADMIN_REFUND_BASE, { params }),

    /** Get refund details */
    get: (id: string) =>
      client.get<RefundDto>(`${ADMIN_REFUND_BASE}/${id}`),

    /** Get refunds by trade number */
    getByTradeNo: (tradeNo: string) =>
      client.get<RefundDto[]>(`${ADMIN_REFUND_BASE}/trade/${tradeNo}`),

    /** Approve refund */
    approve: (id: string, data: ApproveRefundDto) =>
      client.post<void>(`${ADMIN_REFUND_BASE}/${id}/approve`, data),

    /** Process refund */
    process: (id: string) =>
      client.post<void>(`${ADMIN_REFUND_BASE}/${id}/process`),

    /** Cancel refund */
    cancel: (id: string, reason?: string) =>
      client.post<void>(`${ADMIN_REFUND_BASE}/${id}/cancel`, { reason }),
  };
}

/**
 * Admin Subscription Management API
 */
export function useAdminSubscriptionApi(client: HttpClient) {
  return {
    /** Get subscription by ID */
    get: (id: string) =>
      client.get<SubscriptionDto>(`${ADMIN_SUBSCRIPTION_BASE}/${id}`),

    /** Get subscription list */
    getList: (params?: SubscriptionQueryDto) =>
      client.get<PagedList<SubscriptionDto>>(ADMIN_SUBSCRIPTION_BASE, { params }),

    /** Get subscription plans */
    getPlans: (activeOnly = false) =>
      client.get<SubscriptionPlanDto[]>(`${ADMIN_SUBSCRIPTION_BASE}/plans`, {
        params: { activeOnly },
      }),

    /** Create subscription plan */
    createPlan: (data: SubscriptionPlanDto) =>
      client.post<SubscriptionPlanDto>(`${ADMIN_SUBSCRIPTION_BASE}/plans`, data),

    /** Update subscription plan */
    updatePlan: (id: string, data: SubscriptionPlanDto) =>
      client.put<void>(`${ADMIN_SUBSCRIPTION_BASE}/plans/${id}`, data),

    /** Delete subscription plan */
    deletePlan: (id: string) =>
      client.delete<void>(`${ADMIN_SUBSCRIPTION_BASE}/plans/${id}`),

    /** Cancel subscription */
    cancel: (id: string, data: CancelSubscriptionDto) =>
      client.post<void>(`${ADMIN_SUBSCRIPTION_BASE}/${id}/cancel`, data),
  };
}

/**
 * Admin Invoice Management API
 */
export function useAdminInvoiceApi(client: HttpClient) {
  return {
    /** Get invoice list */
    getList: (params?: InvoiceQueryDto) =>
      client.get<PagedList<InvoiceDto>>(ADMIN_INVOICE_BASE, { params }),

    /** Get invoice by ID */
    get: (id: string) =>
      client.get<InvoiceDto>(`${ADMIN_INVOICE_BASE}/${id}`),

    /** Create manual invoice */
    createManual: (data: CreateInvoiceDto) =>
      client.post<InvoiceDto>(`${ADMIN_INVOICE_BASE}/manual`, data),

    /** Send invoice */
    send: (id: string, data?: SendInvoiceDto) =>
      client.post<void>(`${ADMIN_INVOICE_BASE}/${id}/send`, data),

    /** Mark invoice as paid */
    markAsPaid: (id: string, data: MarkInvoicePaidDto) =>
      client.post<void>(`${ADMIN_INVOICE_BASE}/${id}/mark-paid`, data),

    /** Cancel invoice */
    cancel: (id: string, reason?: string) =>
      client.post<void>(`${ADMIN_INVOICE_BASE}/${id}/cancel`, { reason }),
  };
}

/**
 * Admin Promotion Management API
 */
export function useAdminCouponApi(client: HttpClient) {
  return {
    /** Get promotion list */
    getList: (params?: PromotionQueryDto) =>
      client.get<PagedList<PromotionDto>>(ADMIN_PROMOTION_BASE, { params }),

    /** Get promotion by ID */
    getById: (id: string) =>
      client.get<PromotionDto>(`${ADMIN_PROMOTION_BASE}/${id}`),

    /** Get promotion by code */
    getByCode: (code: string) =>
      client.get<PromotionDto>(`${ADMIN_PROMOTION_BASE}/by-code/${encodeURIComponent(code)}`),

    /** Create promotion */
    create: (data: CreatePromotionDto) =>
      client.post<PromotionDto>(ADMIN_PROMOTION_BASE, data),

    /** Update promotion */
    update: (id: string, data: UpdatePromotionDto) =>
      client.put<void>(`${ADMIN_PROMOTION_BASE}/${id}`, data),

    /** Deactivate promotion */
    deactivate: (id: string) =>
      client.post<void>(`${ADMIN_PROMOTION_BASE}/${id}/deactivate`),

    /** Enable/disable promotion */
    setEnabled: (id: string, isEnabled: boolean) =>
      isEnabled
        ? client.put<void>(`${ADMIN_PROMOTION_BASE}/${id}`, { isActive: true })
        : client.post<void>(`${ADMIN_PROMOTION_BASE}/${id}/deactivate`),

    /** Sync promotion to Stripe */
    syncToStripe: (id: string) =>
      client.post<void>(`${ADMIN_PROMOTION_BASE}/${id}/sync-stripe`),

    /** Create redemption code(s) */
    createRedemptionCodes: (promotionId: string, quantity: number) =>
      client.post<string>(`${ADMIN_PROMOTION_BASE}/redemption-codes`, { promotionId, quantity }),
  };
}

/**
 * Admin Payment Statistics API
 */
export function useAdminPaymentStatisticsApi(client: HttpClient) {
  return {
    /** Get payment statistics overview */
    getStatistics: (params?: StatisticsQueryDto) =>
      client.get<PaymentStatisticsDto>(ADMIN_STATISTICS_BASE, { params }),

    /** Get revenue trend */
    getRevenueTrend: (params?: RevenueTrendQueryDto) =>
      client.get<RevenueTrendPointDto[]>(`${ADMIN_STATISTICS_BASE}/revenue-trend`, { params }),

    /** Get subscription metrics (MRR, churn rate, ARPU, etc.) */
    getSubscriptionMetrics: () =>
      client.get<SubscriptionMetricsDto>(`${ADMIN_STATISTICS_BASE}/subscription-metrics`),

    /** Get promotion analytics */
    getPromotionAnalytics: (params?: { topN?: number; startDate?: Date | string; endDate?: Date | string }) =>
      client.get<PromotionAnalyticsDto[]>(`${ADMIN_STATISTICS_BASE}/promotion-analytics`, { params }),

    /** Get refund analytics */
    getRefundAnalytics: (params?: { startDate?: Date | string; endDate?: Date | string }) =>
      client.get<RefundAnalyticsDto>(`${ADMIN_STATISTICS_BASE}/refund-analytics`, { params }),

    /** Export reconciliation report (CSV download) */
    exportReconciliation: (params?: ReconciliationQueryDto) =>
      client.download(`${ADMIN_STATISTICS_BASE}/reconciliation/export`, { params }),

    /** Get reconciliation summary (no file download) */
    getReconciliationSummary: (params?: ReconciliationQueryDto) =>
      client.get<ReconciliationExportResultDto>(`${ADMIN_STATISTICS_BASE}/reconciliation/summary`, { params }),
  };
}
