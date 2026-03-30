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
  Approved = 2,
  Rejected = 3,
  Refunding = 4,
  Succeeded = 5,
  Failed = 6,
  Cancelled = 7,
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
    case RefundStatus.Approved:
      return 'Approved';
    case RefundStatus.Rejected:
      return 'Rejected';
    case RefundStatus.Refunding:
      return 'Refunding';
    case RefundStatus.Succeeded:
      return 'Succeeded';
    case RefundStatus.Failed:
      return 'Failed';
    case RefundStatus.Cancelled:
      return 'Cancelled';
    default:
      return 'Unknown';
  }
}

/**
 * Subscription status
 */
export enum SubscriptionStatus {
  Pending = 0,
  Trial = 1,
  Active = 2,
  PendingRenewal = 3,
  Paused = 4,
  Cancelled = 5,
  Expired = 6,
}

/**
 * Get subscription status label
 */
export function getSubscriptionStatusLabel(status: SubscriptionStatus): string {
  switch (status) {
    case SubscriptionStatus.Pending:
      return 'Pending';
    case SubscriptionStatus.Trial:
      return 'Trial';
    case SubscriptionStatus.Active:
      return 'Active';
    case SubscriptionStatus.PendingRenewal:
      return 'Pending Renewal';
    case SubscriptionStatus.Paused:
      return 'Paused';
    case SubscriptionStatus.Cancelled:
      return 'Cancelled';
    case SubscriptionStatus.Expired:
      return 'Expired';
    default:
      return 'Unknown';
  }
}

/**
 * Billing cycle type
 */
export enum BillingCycleType {
  Day = 1,
  Week = 2,
  Month = 3,
  Year = 4,
  OneTime = 5,
}

/**
 * Get billing cycle type label
 */
export function getBillingCycleTypeLabel(type: BillingCycleType): string {
  switch (type) {
    case BillingCycleType.Day:
      return 'Daily';
    case BillingCycleType.Week:
      return 'Weekly';
    case BillingCycleType.Month:
      return 'Monthly';
    case BillingCycleType.Year:
      return 'Yearly';
    case BillingCycleType.OneTime:
      return 'One Time';
    default:
      return 'Unknown';
  }
}

/**
 * Invoice status
 */
export enum InvoiceStatus {
  Draft = 0,
  Pending = 1,
  Sent = 2,
  Paid = 3,
  Overdue = 4,
  Cancelled = 5,
}

/**
 * Get invoice status label
 */
export function getInvoiceStatusLabel(status: InvoiceStatus): string {
  switch (status) {
    case InvoiceStatus.Draft:
      return 'Draft';
    case InvoiceStatus.Pending:
      return 'Pending';
    case InvoiceStatus.Sent:
      return 'Sent';
    case InvoiceStatus.Paid:
      return 'Paid';
    case InvoiceStatus.Overdue:
      return 'Overdue';
    case InvoiceStatus.Cancelled:
      return 'Cancelled';
    default:
      return 'Unknown';
  }
}

/**
 * Invoice type
 */
export enum InvoiceType {
  Standard = 1,
  Vat = 2,
  Receipt = 3,
}

/**
 * Get invoice type label
 */
export function getInvoiceTypeLabel(type: InvoiceType): string {
  switch (type) {
    case InvoiceType.Standard:
      return 'Standard';
    case InvoiceType.Vat:
      return 'VAT';
    case InvoiceType.Receipt:
      return 'Receipt';
    default:
      return 'Unknown';
  }
}

/**
 * Promotion type
 */
export enum PromotionType {
  PercentageDiscount = 1,
  FixedAmountDiscount = 2,
  FirstSubscription = 3,
  LimitedTime = 4,
  ThresholdDiscount = 5,
}

/**
 * Get promotion type label
 */
export function getPromotionTypeLabel(type: PromotionType): string {
  switch (type) {
    case PromotionType.PercentageDiscount:
      return 'Percentage Discount';
    case PromotionType.FixedAmountDiscount:
      return 'Fixed Amount Discount';
    case PromotionType.FirstSubscription:
      return 'First Subscription';
    case PromotionType.LimitedTime:
      return 'Limited Time';
    case PromotionType.ThresholdDiscount:
      return 'Threshold Discount';
    default:
      return 'Unknown';
  }
}

/**
 * Discount type
 */
export enum DiscountType {
  Percentage = 1,
  Fixed = 2,
}

/**
 * Get discount type label
 */
export function getDiscountTypeLabel(type: DiscountType): string {
  switch (type) {
    case DiscountType.Percentage:
      return 'Percentage';
    case DiscountType.Fixed:
      return 'Fixed Amount';
    default:
      return 'Unknown';
  }
}

/**
 * Product type (for promotion scope)
 */
export enum ProductType {
  Subscription = 1,
  OneTime = 2,
  Recharge = 3,
  All = 99,
}

/**
 * Apply scope (for promotions)
 */
export enum ApplyScope {
  Global = 0,
  Plan = 1,
  Product = 2,
}

/**
 * Trend granularity (for revenue trend queries)
 */
export enum TrendGranularity {
  Day = 1,
  Week = 2,
  Month = 3,
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
