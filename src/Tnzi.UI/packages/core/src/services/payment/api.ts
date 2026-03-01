/**
 * Payment Module API - Payment, Refund, and Coupon operations
 */

import type { HttpClient } from '../../http/http';
import type { PagedList } from '../../types/pagination';
import type {
  PaymentDto,
  PaymentOrderResultDto,
  CreatePaymentDto,
  PaymentQueryDto,
  RefundDto,
  CreateRefundDto,
  CouponDto,
  UserCouponDto,
  ApplyCouponDto,
  CouponValidationDto,
  PaymentParamsDto,
  RefundQueryDto,
} from './types';

const BASE = '/payments';
const ADMIN_BASE = '/admin/payments';
const ADMIN_REFUND_BASE = '/admin/refunds';
const PROMOTION_BASE = '/promotions';
const ADMIN_PROMOTION_BASE = '/admin/promotions';

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
    close: (tradeNo: string, reason: string) =>
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
  const REFUND_BASE = '/refunds';
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
 * Promotion API (User)
 */
export function useCouponApi(client: HttpClient) {
  return {
    /** Get my available coupons */
    getMyCoupons: () =>
      client.get<UserCouponDto[]>(`${PROMOTION_BASE}/my-coupons`),

    /** Get my used coupons */
    getUsedCoupons: () =>
      client.get<UserCouponDto[]>(`${PROMOTION_BASE}/used-coupons`),

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

    /** Redeem coupon code */
    redeem: (code: string) =>
      client.post<UserCouponDto>(`${PROMOTION_BASE}/redeem`, { code }),

    /** Check if first subscription discount is available */
    canUseFirstSubscriptionDiscount: () =>
      client.get<boolean>(`${PROMOTION_BASE}/first-subscription-check`),
  };
}

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

    /** Approve refund */
    approve: (id: string, approvedAmount?: number, remark?: string) =>
      client.post<void>(`${ADMIN_REFUND_BASE}/${id}/approve`, { approvedAmount, remark }),

    /** Process refund */
    process: (id: string) =>
      client.post<void>(`${ADMIN_REFUND_BASE}/${id}/process`),

    /** Cancel refund */
    cancel: (id: string, reason: string) =>
      client.post<void>(`${ADMIN_REFUND_BASE}/${id}/cancel`, { reason }),
  };
}

/**
 * Admin Promotion Management API
 */
export function useAdminCouponApi(client: HttpClient) {
  return {
    /** Get promotion list */
    getList: (params?: Record<string, unknown>) =>
      client.get<PagedList<CouponDto>>(ADMIN_PROMOTION_BASE, { params }),

    /** Get promotion by ID */
    getById: (id: string) =>
      client.get<CouponDto>(`${ADMIN_PROMOTION_BASE}/${id}`),

    /** Get promotion by code */
    getByCode: (code: string) =>
      client.get<CouponDto>(`${ADMIN_PROMOTION_BASE}/by-code/${encodeURIComponent(code)}`),

    /** Create promotion */
    create: (data: Partial<CouponDto>) =>
      client.post<CouponDto>(ADMIN_PROMOTION_BASE, data),

    /** Update promotion */
    update: (id: string, data: Partial<CouponDto>) =>
      client.put<CouponDto>(`${ADMIN_PROMOTION_BASE}/${id}`, data),

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
