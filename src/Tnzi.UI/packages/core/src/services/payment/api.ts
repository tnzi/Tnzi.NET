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
  SubscriptionCreateResultDto,
  CreateSubscriptionDto,
  SubscriptionQueryDto,
  SubscriptionPlanDto,
  CancelSubscriptionDto,
  PauseSubscriptionDto,
  ChangeSubscriptionPlanDto,
  SubscriptionChangeDto,
  AttachPaymentMethodDto,
  ConfirmOfflinePaymentDto,
  CreateSetupSessionDto,
  SetupSessionDto,
  BindPaymentMethodDto,
  StoredPaymentMethodDto,
  GrantCouponDto,
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
  ValidateCouponDto,
  CalculateDiscountDto,
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
const PAYMENT_METHOD_BASE = '/payment-methods';
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
    /** Get subscription plans, optionally scoped to one product */
    getPlans: (activeOnly = true, productCode?: string) =>
      client.get<SubscriptionPlanDto[]>(`${SUBSCRIPTION_BASE}/plans`, {
        params: { activeOnly, productCode },
      }),

    /** Create subscription; returns the first payment's credentials when payment is due */
    create: (data: CreateSubscriptionDto) =>
      client.post<SubscriptionCreateResultDto>(SUBSCRIPTION_BASE, data),

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

    /** Pause subscription (auto-resumes at `resumeAt`, or manually) */
    pause: (id: string, data: PauseSubscriptionDto = {}) =>
      client.post<void>(`${SUBSCRIPTION_BASE}/${id}/pause`, data),

    /** Resume subscription */
    resume: (id: string) =>
      client.post<void>(`${SUBSCRIPTION_BASE}/${id}/resume`),

    /** Retry billing immediately (past-due subscription after a card change) */
    retryBilling: (id: string) =>
      client.post<void>(`${SUBSCRIPTION_BASE}/${id}/retry-billing`),

    /** Change subscription plan with proration */
    changePlan: (id: string, data: ChangeSubscriptionPlanDto) =>
      client.post<SubscriptionChangeDto>(`${SUBSCRIPTION_BASE}/${id}/change-plan`, data),

    /** Preview a plan change (prorated amount, effective date) */
    previewPlanChange: (id: string, newPlanId: string) =>
      client.get<SubscriptionChangeDto>(`${SUBSCRIPTION_BASE}/${id}/change-plan-preview`, {
        params: { newPlanId },
      }),

    /** Cancel a pending (not yet effective) plan change */
    cancelPendingChange: (changeId: string) =>
      client.post<void>(`/subscription-changes/${changeId}/cancel`),

    /** Attach or replace the payment method used by this subscription */
    updatePaymentMethod: (id: string, data: AttachPaymentMethodDto) =>
      client.post<SubscriptionDto>(`${SUBSCRIPTION_BASE}/${id}/payment-method`, data),

    /** Update auto-renew setting */
    updateAutoRenew: (id: string, autoRenew: boolean) =>
      client.post<void>(`${SUBSCRIPTION_BASE}/${id}/auto-renew`, { autoRenew }),
  };
}

/**
 * Stored payment method (card-on-file) API (User).
 *
 * Binding a payment method is what makes unattended renewal charges possible;
 * without it every renewal falls back to "no payment method" and goes past due.
 */
export function usePaymentMethodApi(client: HttpClient) {
  return {
    /**
     * Step 1: open a setup session. Returns either a `clientSecret` (collect
     * inline via the channel SDK) or an `approvalUrl` (send the payer there).
     */
    createSetupSession: (data: CreateSetupSessionDto = {}) =>
      client.post<SetupSessionDto>(`${PAYMENT_METHOD_BASE}/setup`, data),

    /**
     * Step 3: register the collected payment method. Step 2 is the channel SDK
     * for inline channels, or the payer's approval redirect for PayPal - where
     * `paymentMethodToken` is the session's `setupId`.
     */
    bind: (data: BindPaymentMethodDto) =>
      client.post<StoredPaymentMethodDto>(PAYMENT_METHOD_BASE, data),

    /** List my saved payment methods */
    getList: () =>
      client.get<StoredPaymentMethodDto[]>(PAYMENT_METHOD_BASE),

    /** Set the default payment method */
    setDefault: (id: string) =>
      client.post<void>(`${PAYMENT_METHOD_BASE}/${id}/default`),

    /** Remove (detach) a payment method */
    remove: (id: string) =>
      client.delete<void>(`${PAYMENT_METHOD_BASE}/${id}`),
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

    /**
     * Preview a coupon's discount.
     *
     * NOTE: this only quotes the discount. Redemption happens server-side when
     * the payment or subscription is created with `couponCode` - pass the code
     * through to `createPayment` / `createSubscription` for it to take effect.
     */
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

    /** Validate coupon (typed, scope-aware) */
    validateCoupon: (data: ValidateCouponDto) =>
      client.post<CouponValidationResultDto>(`${PROMOTION_BASE}/validate-coupon`, data),

    /** Calculate discount (typed, scope-aware) */
    calculateDiscount: (data: CalculateDiscountDto) =>
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

    /**
     * Manually confirm an offline payment (bank transfer, wire, cheque).
     * Rejected for online channels - those must settle through the callback.
     */
    confirm: (tradeNo: string, data: ConfirmOfflinePaymentDto) =>
      client.post<PaymentDto>(`${ADMIN_BASE}/${tradeNo}/confirm`, data),
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

    /** Raise a refund on the customer's behalf (the most common refund path) */
    create: (data: CreateRefundDto) =>
      client.post<RefundDto>(ADMIN_REFUND_BASE, data),

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

    /** Get subscription plans, optionally scoped to one product */
    getPlans: (activeOnly = false, productCode?: string) =>
      client.get<SubscriptionPlanDto[]>(`${ADMIN_SUBSCRIPTION_BASE}/plans`, {
        params: { activeOnly, productCode },
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

    /** Pause subscription on the customer's behalf */
    pause: (id: string, data: PauseSubscriptionDto = {}) =>
      client.post<void>(`${ADMIN_SUBSCRIPTION_BASE}/${id}/pause`, data),

    /** Resume subscription on the customer's behalf */
    resume: (id: string) =>
      client.post<void>(`${ADMIN_SUBSCRIPTION_BASE}/${id}/resume`),

    /** Retry billing now - the usual action when working a past-due ticket */
    retryBilling: (id: string) =>
      client.post<void>(`${ADMIN_SUBSCRIPTION_BASE}/${id}/retry-billing`),

    /** Toggle auto-renew on the customer's behalf */
    updateAutoRenew: (id: string, autoRenew: boolean) =>
      client.post<void>(`${ADMIN_SUBSCRIPTION_BASE}/${id}/auto-renew`, { autoRenew }),
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

    /** Grant a coupon directly to a user (no redemption code needed) */
    grant: (promotionId: string, data: GrantCouponDto) =>
      client.post<UserCouponDto>(`${ADMIN_PROMOTION_BASE}/${promotionId}/grant`, data),
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
