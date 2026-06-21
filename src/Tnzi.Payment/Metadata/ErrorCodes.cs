namespace Tnzi.Payment.Metadata;

/// <summary>
/// Payment 模块错误码常量定义
/// 命名规则: {MODULE}_{FEATURE}_{ERROR}
/// </summary>
public static class ErrorCodes
{
    #region Payment 错误码

    /// <summary>
    /// 支付记录不存在
    /// </summary>
    public const string PaymentNotFound = "PAYMENT_NOT_FOUND";

    /// <summary>
    /// 支付创建失败
    /// </summary>
    public const string PaymentCreationFailed = "PAYMENT_CREATION_FAILED";

    /// <summary>
    /// 支付渠道不支持
    /// </summary>
    public const string PaymentChannelNotSupported = "PAYMENT_CHANNEL_NOT_SUPPORTED";

    /// <summary>
    /// 无效的签名
    /// </summary>
    public const string PaymentInvalidSignature = "PAYMENT_INVALID_SIGNATURE";

    /// <summary>
    /// 只有待处理或处理中的支付才能关闭
    /// </summary>
    public const string PaymentCannotClose = "PAYMENT_CANNOT_CLOSE";

    /// <summary>
    /// 只有成功的支付才能退款
    /// </summary>
    public const string PaymentCannotRefund = "PAYMENT_CANNOT_REFUND";

    /// <summary>
    /// 退款金额不能超过已付金额
    /// </summary>
    public const string PaymentRefundExceedAmount = "PAYMENT_REFUND_EXCEED_AMOUNT";

    /// <summary>
    /// 金额必须大于0
    /// </summary>
    public const string PaymentInvalidAmount = "PAYMENT_INVALID_AMOUNT";

    /// <summary>
    /// 渠道不支持 off-session 自动扣款
    /// </summary>
    public const string PaymentOffSessionNotSupported = "PAYMENT_OFFSESSION_NOT_SUPPORTED";

    /// <summary>
    /// off-session 自动扣款失败
    /// </summary>
    public const string PaymentOffSessionChargeFailed = "PAYMENT_OFFSESSION_CHARGE_FAILED";

    /// <summary>
    /// 测试渠道（Null）未启用（需开启 Payment:AllowTestProvider）
    /// </summary>
    public const string PaymentTestProviderDisabled = "PAYMENT_TEST_PROVIDER_DISABLED";

    /// <summary>
    /// 回调到账金额与订单应付金额不一致
    /// </summary>
    public const string PaymentAmountMismatch = "PAYMENT_AMOUNT_MISMATCH";

    #endregion

    #region Stripe Provider 错误码

    /// <summary>
    /// Stripe支付创建失败
    /// </summary>
    public const string StripePaymentFailed = "STRIPE_PAYMENT_FAILED";

    /// <summary>
    /// Stripe支付查询失败
    /// </summary>
    public const string StripePaymentQueryFailed = "STRIPE_PAYMENT_QUERY_FAILED";

    /// <summary>
    /// Stripe退款失败
    /// </summary>
    public const string StripeRefundFailed = "STRIPE_REFUND_FAILED";

    /// <summary>
    /// Stripe退款查询失败
    /// </summary>
    public const string StripeRefundQueryFailed = "STRIPE_REFUND_QUERY_FAILED";

    #endregion

    #region PayPal Provider 错误码

    /// <summary>
    /// PayPal支付创建失败
    /// </summary>
    public const string PayPalPaymentFailed = "PAYPAL_PAYMENT_FAILED";

    /// <summary>
    /// PayPal支付查询失败
    /// </summary>
    public const string PayPalPaymentQueryFailed = "PAYPAL_PAYMENT_QUERY_FAILED";

    /// <summary>
    /// PayPal退款失败
    /// </summary>
    public const string PayPalRefundFailed = "PAYPAL_REFUND_FAILED";

    #endregion

    #region Invoice 错误码

    /// <summary>
    /// 发票不存在
    /// </summary>
    public const string InvoiceNotFound = "INVOICE_NOT_FOUND";

    /// <summary>
    /// 只能从成功的支付创建发票
    /// </summary>
    public const string InvoicePaymentNotSucceeded = "INVOICE_PAYMENT_NOT_SUCCEEDED";

    /// <summary>
    /// 发票已发送
    /// </summary>
    public const string InvoiceAlreadySent = "INVOICE_ALREADY_SENT";

    /// <summary>
    /// 收件人邮箱必填
    /// </summary>
    public const string InvoiceRecipientEmailRequired = "INVOICE_RECIPIENT_EMAIL_REQUIRED";

    /// <summary>
    /// 发票已支付
    /// </summary>
    public const string InvoiceAlreadyPaid = "INVOICE_ALREADY_PAID";

    /// <summary>
    /// 已支付的发票不能取消
    /// </summary>
    public const string InvoiceCannotCancel = "INVOICE_CANNOT_CANCEL";

    /// <summary>
    /// 用户邮箱不存在
    /// </summary>
    public const string InvoiceUserEmailNotFound = "INVOICE_USER_EMAIL_NOT_FOUND";

    #endregion

    #region Subscription 错误码

    /// <summary>
    /// 订阅计划不存在
    /// </summary>
    public const string SubscriptionPlanNotFound = "SUBSCRIPTION_PLAN_NOT_FOUND";

    /// <summary>
    /// 订阅计划未激活
    /// </summary>
    public const string SubscriptionPlanNotActive = "SUBSCRIPTION_PLAN_NOT_ACTIVE";

    /// <summary>
    /// 用户已有活跃订阅
    /// </summary>
    public const string SubscriptionAlreadyActive = "SUBSCRIPTION_ALREADY_ACTIVE";

    /// <summary>
    /// 订阅不存在
    /// </summary>
    public const string SubscriptionNotFound = "SUBSCRIPTION_NOT_FOUND";

    /// <summary>
    /// 订阅已取消或已过期
    /// </summary>
    public const string SubscriptionAlreadyCancelledOrExpired = "SUBSCRIPTION_ALREADY_CANCELLED_OR_EXPIRED";

    /// <summary>
    /// 只能取消或暂停的订阅才能恢复
    /// </summary>
    public const string SubscriptionCannotResume = "SUBSCRIPTION_CANNOT_RESUME";

    /// <summary>
    /// 新的订阅计划不存在
    /// </summary>
    public const string SubscriptionNewPlanNotFound = "SUBSCRIPTION_NEW_PLAN_NOT_FOUND";

    /// <summary>
    /// Stripe 提供商未找到
    /// </summary>
    public const string SubscriptionProviderNotFound = "SUBSCRIPTION_PROVIDER_NOT_FOUND";

    /// <summary>
    /// 无法删除有活跃订阅的计划
    /// </summary>
    public const string SubscriptionPlanHasActiveSubscriptions = "SUBSCRIPTION_PLAN_HAS_ACTIVE_SUBSCRIPTIONS";

    /// <summary>
    /// 新旧计划相同
    /// </summary>
    public const string SubscriptionSamePlan = "SUBSCRIPTION_SAME_PLAN";

    /// <summary>
    /// 订阅变更记录不存在
    /// </summary>
    public const string SubscriptionChangeNotFound = "SUBSCRIPTION_CHANGE_NOT_FOUND";

    /// <summary>
    /// 只有待生效的变更才能取消
    /// </summary>
    public const string SubscriptionChangeCannotCancel = "SUBSCRIPTION_CHANGE_CANNOT_CANCEL";

    /// <summary>
    /// 存在待生效的变更
    /// </summary>
    public const string SubscriptionChangePending = "SUBSCRIPTION_CHANGE_PENDING";

    /// <summary>
    /// 新旧计划币种不一致
    /// </summary>
    public const string SubscriptionCurrencyMismatch = "SUBSCRIPTION_CURRENCY_MISMATCH";

    /// <summary>
    /// 订阅缺少已保存的支付方式（无法 off-session 自动扣款）
    /// </summary>
    public const string SubscriptionPaymentMethodMissing = "SUBSCRIPTION_PAYMENT_METHOD_MISSING";

    #endregion

    #region Refund 错误码

    /// <summary>
    /// 退款记录不存在
    /// </summary>
    public const string RefundNotFound = "REFUND_NOT_FOUND";

    /// <summary>
    /// 只有待处理的退款才能审批
    /// </summary>
    public const string RefundCannotApprove = "REFUND_CANNOT_APPROVE";

    /// <summary>
    /// 只有已审批的退款才能处理
    /// </summary>
    public const string RefundCannotProcess = "REFUND_CANNOT_PROCESS";

    /// <summary>
    /// 只有待处理或已审批的退款才能取消
    /// </summary>
    public const string RefundCannotCancel = "REFUND_CANNOT_CANCEL";

    /// <summary>
    /// 超出每日退款限额
    /// </summary>
    public const string RefundDailyLimitExceeded = "REFUND_DAILY_LIMIT_EXCEEDED";

    #endregion

    #region Coupon 错误码

    /// <summary>
    /// 优惠券不存在
    /// </summary>
    public const string CouponNotFound = "COUPON_NOT_FOUND";

    /// <summary>
    /// 优惠券已过期
    /// </summary>
    public const string CouponExpired = "COUPON_EXPIRED";

    /// <summary>
    /// 优惠券已使用
    /// </summary>
    public const string CouponAlreadyUsed = "COUPON_ALREADY_USED";

    /// <summary>
    /// 优惠券不适用于当前订单
    /// </summary>
    public const string CouponNotApplicable = "COUPON_NOT_APPLICABLE";

    /// <summary>
    /// 优惠券无效
    /// </summary>
    public const string CouponInvalid = "COUPON_INVALID";

    /// <summary>
    /// 优惠券尚未激活
    /// </summary>
    public const string CouponNotYetActive = "COUPON_NOT_YET_ACTIVE";

    /// <summary>
    /// 优惠券使用次数已达上限
    /// </summary>
    public const string CouponUsageLimitReached = "COUPON_USAGE_LIMIT_REACHED";

    /// <summary>
    /// 您已使用过此优惠券
    /// </summary>
    public const string CouponAlreadyUsedByUser = "COUPON_ALREADY_USED_BY_USER";

    /// <summary>
    /// 最低订单金额未满足
    /// </summary>
    public const string CouponMinimumAmountNotMet = "COUPON_MINIMUM_AMOUNT_NOT_MET";

    #endregion

    #region Promotion 错误码

    /// <summary>
    /// 促销活动不存在
    /// </summary>
    public const string PromotionNotFound = "PROMOTION_NOT_FOUND";

    /// <summary>
    /// 促销代码已存在
    /// </summary>
    public const string PromotionCodeAlreadyExists = "PROMOTION_CODE_ALREADY_EXISTS";

    #endregion

    #region Redemption Code 错误码

    /// <summary>
    /// 兑换码不存在
    /// </summary>
    public const string RedemptionCodeNotFound = "REDEMPTION_CODE_NOT_FOUND";

    /// <summary>
    /// 兑换码已过期
    /// </summary>
    public const string RedemptionCodeExpired = "REDEMPTION_CODE_EXPIRED";

    /// <summary>
    /// 兑换码未激活
    /// </summary>
    public const string RedemptionCodeNotActive = "REDEMPTION_CODE_NOT_ACTIVE";

    /// <summary>
    /// 兑换码已达到使用限制
    /// </summary>
    public const string RedemptionCodeLimitReached = "REDEMPTION_CODE_LIMIT_REACHED";

    /// <summary>
    /// 您已达到此兑换码的使用限制
    /// </summary>
    public const string RedemptionCodeUserLimitReached = "REDEMPTION_CODE_USER_LIMIT_REACHED";

    #endregion
}
