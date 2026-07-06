/**
 * Payment Module Metadata
 *
 * Enums are declared as STRING enums whose member value equals the C# member
 * name. The backend serialises every enum with `JsonStringEnumConverter`
 * (PascalCase member name) and accepts either the name or the number on input,
 * so string-valued members let status maps / comparisons / select options all
 * key off the exact wire value (`PaymentStatus.Succeeded === 'Succeeded'`).
 *
 * `TrendGranularity` stays numeric: it is only ever sent as a query-string
 * param, where ASP.NET Core's enum model binder accepts the number.
 */

/**
 * Payment status — mirrors Tnzi.Payment.Metadata.PaymentStatus.
 */
export enum PaymentStatus {
  Pending = 'Pending',
  Processing = 'Processing',
  Succeeded = 'Succeeded',
  Failed = 'Failed',
  Closed = 'Closed',
  Cancelled = 'Cancelled',
  Expired = 'Expired',
  Refunded = 'Refunded',
  PartialRefunded = 'PartialRefunded',
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
    case PaymentStatus.Refunded:
      return 'Refunded';
    case PaymentStatus.PartialRefunded:
      return 'Partial Refunded';
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
 * Payment method — mirrors Tnzi.Payment.Metadata.PaymentMethod.
 */
export enum PaymentMethod {
  CreditCard = 'CreditCard',
  DebitCard = 'DebitCard',
  PayPal = 'PayPal',
  ApplePay = 'ApplePay',
  GooglePay = 'GooglePay',
  BankTransfer = 'BankTransfer',
  Offline = 'Offline',
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
    case PaymentMethod.Offline:
      return 'Offline Payment';
    default:
      return 'Unknown';
  }
}

/**
 * Business type for payment — mirrors Tnzi.Payment.Metadata.BusinessType.
 */
export enum BusinessType {
  Order = 'Order',
  Subscription = 'Subscription',
  Recharge = 'Recharge',
  Other = 'Other',
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
    case BusinessType.Other:
      return 'Other';
    default:
      return 'Unknown';
  }
}

/**
 * Refund status — mirrors Tnzi.Payment.Metadata.RefundStatus.
 */
export enum RefundStatus {
  Pending = 'Pending',
  Processing = 'Processing',
  Approved = 'Approved',
  Rejected = 'Rejected',
  Refunding = 'Refunding',
  Succeeded = 'Succeeded',
  Failed = 'Failed',
  Cancelled = 'Cancelled',
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
 * Subscription status — mirrors Tnzi.Payment.Metadata.SubscriptionStatus.
 */
export enum SubscriptionStatus {
  Pending = 'Pending',
  Trial = 'Trial',
  Active = 'Active',
  PendingRenewal = 'PendingRenewal',
  Paused = 'Paused',
  Cancelled = 'Cancelled',
  Expired = 'Expired',
  PastDue = 'PastDue',
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
    case SubscriptionStatus.PastDue:
      return 'Past Due';
    default:
      return 'Unknown';
  }
}

/**
 * Billing cycle type — mirrors Tnzi.Payment.Metadata.BillingCycleType.
 */
export enum BillingCycleType {
  Day = 'Day',
  Week = 'Week',
  Month = 'Month',
  Year = 'Year',
  OneTime = 'OneTime',
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
 * Invoice status — mirrors Tnzi.Payment.Metadata.InvoiceStatus.
 */
export enum InvoiceStatus {
  Draft = 'Draft',
  Pending = 'Pending',
  Sent = 'Sent',
  Paid = 'Paid',
  Overdue = 'Overdue',
  Cancelled = 'Cancelled',
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
 * Invoice type — mirrors Tnzi.Payment.Metadata.InvoiceType.
 */
export enum InvoiceType {
  Standard = 'Standard',
  Vat = 'Vat',
  Receipt = 'Receipt',
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
 * Promotion type — mirrors Tnzi.Payment.Metadata.PromotionType.
 */
export enum PromotionType {
  PercentageDiscount = 'PercentageDiscount',
  FixedAmountDiscount = 'FixedAmountDiscount',
  FirstSubscription = 'FirstSubscription',
  LimitedTime = 'LimitedTime',
  ThresholdDiscount = 'ThresholdDiscount',
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
 * Discount type — mirrors Tnzi.Payment.Metadata.DiscountType.
 */
export enum DiscountType {
  Percentage = 'Percentage',
  Fixed = 'Fixed',
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
 * Product type (for promotion scope) — mirrors Tnzi.Payment.Metadata.ProductType.
 */
export enum ProductType {
  Subscription = 'Subscription',
  OneTime = 'OneTime',
  Recharge = 'Recharge',
  All = 'All',
}

/**
 * Apply scope (for promotions) — mirrors Tnzi.Payment.Metadata.ApplyScope.
 */
export enum ApplyScope {
  Global = 'Global',
  Plan = 'Plan',
  Product = 'Product',
}

/**
 * Trend granularity (for revenue trend queries). Sent as a query-string param
 * only — kept numeric because ASP.NET Core's enum model binder accepts the
 * number for `RevenueTrendQueryDto.Granularity`.
 */
export enum TrendGranularity {
  Day = 1,
  Week = 2,
  Month = 3,
}

/**
 * Coupon type (legacy compatibility for the admin promotion listing DTOs).
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
