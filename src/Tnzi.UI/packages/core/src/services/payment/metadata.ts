/**
 * Payment Module Metadata
 */

/**
 * Payment status
 */
export enum PaymentStatus {
  Pending = 0,
  Processing = 1,
  Succeeded = 2,
  Failed = 3,
  Closed = 4,
  Cancelled = 5,
  Expired = 6,
  Refunding = 7,
  Refunded = 8,
}

/**
 * Get payment status label
 */
export function getPaymentStatusLabel(status: PaymentStatus): string {
  switch (status) {
    case PaymentStatus.Pending:
      return 'Pending';
    case PaymentStatus.Processing:
      return 'Processing';
    case PaymentStatus.Succeeded:
      return 'Succeeded';
    case PaymentStatus.Failed:
      return 'Failed';
    case PaymentStatus.Closed:
      return 'Closed';
    case PaymentStatus.Cancelled:
      return 'Cancelled';
    case PaymentStatus.Expired:
      return 'Expired';
    case PaymentStatus.Refunding:
      return 'Refunding';
    case PaymentStatus.Refunded:
      return 'Refunded';
    default:
      return 'Unknown';
  }
}

/**
 * Check if payment is successful
 */
export function isPaymentSucceeded(status: PaymentStatus): boolean {
  return status === PaymentStatus.Succeeded;
}

/**
 * Check if payment is pending
 */
export function isPaymentPending(status: PaymentStatus): boolean {
  return status === PaymentStatus.Pending || status === PaymentStatus.Processing;
}

/**
 * Check if payment is failed
 */
export function isPaymentFailed(status: PaymentStatus): boolean {
  return status === PaymentStatus.Failed ||
    status === PaymentStatus.Cancelled ||
    status === PaymentStatus.Expired;
}

/**
 * Payment method
 */
export enum PaymentMethod {
  CreditCard = 1,
  DebitCard = 2,
  PayPal = 3,
  ApplePay = 4,
  GooglePay = 5,
  BankTransfer = 6,
  Alipay = 7,
  WeChatPay = 8,
  Offline = 99,
}

/**
 * Get payment method label
 */
export function getPaymentMethodLabel(method: PaymentMethod): string {
  switch (method) {
    case PaymentMethod.CreditCard:
      return 'Credit Card';
    case PaymentMethod.DebitCard:
      return 'Debit Card';
    case PaymentMethod.PayPal:
      return 'PayPal';
    case PaymentMethod.ApplePay:
      return 'Apple Pay';
    case PaymentMethod.GooglePay:
      return 'Google Pay';
    case PaymentMethod.BankTransfer:
      return 'Bank Transfer';
    case PaymentMethod.Alipay:
      return 'Alipay';
    case PaymentMethod.WeChatPay:
      return 'WeChat Pay';
    case PaymentMethod.Offline:
      return 'Offline Payment';
    default:
      return 'Unknown';
  }
}

/**
 * Business type for payment
 */
export enum BusinessType {
  Order = 1,
  Subscription = 2,
  Recharge = 3,
  Donation = 4,
  Service = 5,
  Other = 99,
}

/**
 * Get business type label
 */
export function getBusinessTypeLabel(type: BusinessType): string {
  switch (type) {
    case BusinessType.Order:
      return 'Order';
    case BusinessType.Subscription:
      return 'Subscription';
    case BusinessType.Recharge:
      return 'Recharge';
    case BusinessType.Donation:
      return 'Donation';
    case BusinessType.Service:
      return 'Service';
    case BusinessType.Other:
      return 'Other';
    default:
      return 'Unknown';
  }
}

/**
 * Refund status
 */
export enum RefundStatus {
  Pending = 0,
  Processing = 1,
  Succeeded = 2,
  Failed = 3,
}

/**
 * Get refund status label
 */
export function getRefundStatusLabel(status: RefundStatus): string {
  switch (status) {
    case RefundStatus.Pending:
      return 'Pending';
    case RefundStatus.Processing:
      return 'Processing';
    case RefundStatus.Succeeded:
      return 'Succeeded';
    case RefundStatus.Failed:
      return 'Failed';
    default:
      return 'Unknown';
  }
}

/**
 * Coupon type
 */
export enum CouponType {
  Fixed = 1,
  Percentage = 2,
}

/**
 * Get coupon type label
 */
export function getCouponTypeLabel(type: CouponType): string {
  switch (type) {
    case CouponType.Fixed:
      return 'Fixed Amount';
    case CouponType.Percentage:
      return 'Percentage';
    default:
      return 'Unknown';
  }
}

/**
 * Currency codes
 */
export enum Currency {
  CNY = 'CNY',
  USD = 'USD',
  EUR = 'EUR',
  GBP = 'GBP',
  JPY = 'JPY',
  HKD = 'HKD',
  TWD = 'TWD',
}
