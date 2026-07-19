namespace Tnzi.Payment.Metadata;

#region Payment Enums

/// <summary>
/// 支付状态
/// </summary>
public enum PaymentStatus
{
    /// <summary>
    /// 待支付
    /// </summary>
    Pending = 0,

    /// <summary>
    /// 处理中
    /// </summary>
    Processing = 1,

    /// <summary>
    /// 支付成功
    /// </summary>
    Succeeded = 2,

    /// <summary>
    /// 支付失败
    /// </summary>
    Failed = 3,

    /// <summary>
    /// 已关闭
    /// </summary>
    Closed = 4,

    /// <summary>
    /// 已取消
    /// </summary>
    Cancelled = 5,

    /// <summary>
    /// 已过期
    /// </summary>
    Expired = 6,

    /// <summary>
    /// 已全额退款
    /// </summary>
    Refunded = 7,

    /// <summary>
    /// 已部分退款
    /// </summary>
    PartialRefunded = 8
}

/// <summary>
/// 支付方式
/// </summary>
public enum PaymentMethod
{
    /// <summary>
    /// 信用卡
    /// </summary>
    CreditCard = 1,

    /// <summary>
    /// 借记卡
    /// </summary>
    DebitCard = 2,

    /// <summary>
    /// PayPal
    /// </summary>
    PayPal = 3,

    /// <summary>
    /// Apple Pay
    /// </summary>
    ApplePay = 4,

    /// <summary>
    /// Google Pay
    /// </summary>
    GooglePay = 5,

    /// <summary>
    /// 银行转账
    /// </summary>
    BankTransfer = 6,

    /// <summary>
    /// 线下汇款
    /// </summary>
    Offline = 7
}

/// <summary>
/// 业务类型
/// </summary>
public enum BusinessType
{
    /// <summary>
    /// 普通订单
    /// </summary>
    Order = 1,

    /// <summary>
    /// 订阅
    /// </summary>
    Subscription = 2,

    /// <summary>
    /// 充值
    /// </summary>
    Recharge = 3,

    /// <summary>
    /// 其他
    /// </summary>
    Other = 99
}

#endregion

#region Subscription Enums

/// <summary>
/// 订阅状态
/// </summary>
public enum SubscriptionStatus
{
    /// <summary>
    /// 待激活
    /// </summary>
    Pending = 0,

    /// <summary>
    /// 试用中
    /// </summary>
    Trial = 1,

    /// <summary>
    /// 活动中
    /// </summary>
    Active = 2,

    /// <summary>
    /// 待续费
    /// </summary>
    PendingRenewal = 3,

    /// <summary>
    /// 已暂停
    /// </summary>
    Paused = 4,

    /// <summary>
    /// 已取消
    /// </summary>
    Cancelled = 5,

    /// <summary>
    /// 已过期
    /// </summary>
    Expired = 6,

    /// <summary>
    /// 逾期欠费（续费/试用转正扣款失败，宽限期内等待重试，超期则过期）
    /// </summary>
    PastDue = 7
}

/// <summary>
/// 订阅计费支付用途（用于将支付完成事件路由回订阅状态机）
/// </summary>
public enum SubscriptionBillingPurpose
{
    /// <summary>
    /// 首次开通付款
    /// </summary>
    Initial = 0,

    /// <summary>
    /// 周期续费
    /// </summary>
    Renewal = 1,

    /// <summary>
    /// 试用转正付款
    /// </summary>
    TrialConversion = 2,

    /// <summary>
    /// 升级补差价
    /// </summary>
    Proration = 3
}

/// <summary>
/// 计费周期类型
/// </summary>
public enum BillingCycleType
{
    /// <summary>
    /// 按天
    /// </summary>
    Day = 1,

    /// <summary>
    /// 按周
    /// </summary>
    Week = 2,

    /// <summary>
    /// 按月
    /// </summary>
    Month = 3,

    /// <summary>
    /// 按年
    /// </summary>
    Year = 4,

    /// <summary>
    /// 一次性
    /// </summary>
    OneTime = 5
}

/// <summary>
/// 订阅变更类型
/// </summary>
public enum SubscriptionChangeType
{
    /// <summary>
    /// 升级
    /// </summary>
    Upgrade = 1,

    /// <summary>
    /// 降级
    /// </summary>
    Downgrade = 2,

    /// <summary>
    /// 平级变更
    /// </summary>
    CrossGrade = 3
}

/// <summary>
/// 订阅变更状态
/// </summary>
public enum SubscriptionChangeStatus
{
    /// <summary>
    /// 待生效
    /// </summary>
    Pending = 0,

    /// <summary>
    /// 已生效
    /// </summary>
    Applied = 1,

    /// <summary>
    /// 已取消
    /// </summary>
    Cancelled = 2
}

#endregion

#region Refund Enums

/// <summary>
/// 退款状态
/// </summary>
public enum RefundStatus
{
    /// <summary>
    /// 待审批
    /// </summary>
    Pending = 0,

    /// <summary>
    /// 审批中
    /// </summary>
    Processing = 1,

    /// <summary>
    /// 审批通过
    /// </summary>
    Approved = 2,

    /// <summary>
    /// 审批拒绝
    /// </summary>
    Rejected = 3,

    /// <summary>
    /// 退款中
    /// </summary>
    Refunding = 4,

    /// <summary>
    /// 退款成功
    /// </summary>
    Succeeded = 5,

    /// <summary>
    /// 退款失败
    /// </summary>
    Failed = 6,

    /// <summary>
    /// 已取消
    /// </summary>
    Cancelled = 7
}

/// <summary>
/// 退款类型
/// </summary>
public enum RefundType
{
    /// <summary>
    /// 全额退款
    /// </summary>
    Full = 1,

    /// <summary>
    /// 部分退款
    /// </summary>
    Partial = 2
}

#endregion

#region Invoice Enums

/// <summary>
/// 发票状态
/// </summary>
public enum InvoiceStatus
{
    /// <summary>
    /// 草稿
    /// </summary>
    Draft = 0,

    /// <summary>
    /// 待发送
    /// </summary>
    Pending = 1,

    /// <summary>
    /// 已发送
    /// </summary>
    Sent = 2,

    /// <summary>
    /// 已支付
    /// </summary>
    Paid = 3,

    /// <summary>
    /// 已过期
    /// </summary>
    Overdue = 4,

    /// <summary>
    /// 已取消
    /// </summary>
    Cancelled = 5
}

/// <summary>
/// 发票类型
/// </summary>
public enum InvoiceType
{
    /// <summary>
    /// 普通发票
    /// </summary>
    Standard = 1,

    /// <summary>
    /// 增值税专用发票
    /// </summary>
    Vat = 2,

    /// <summary>
    /// 收据
    /// </summary>
    Receipt = 3
}

#endregion

#region Promotion Enums

/// <summary>
/// 促销类型
/// </summary>
public enum PromotionType
{
    /// <summary>
    /// 百分比折扣
    /// </summary>
    PercentageDiscount = 1,

    /// <summary>
    /// 固定金额减免
    /// </summary>
    FixedAmountDiscount = 2,

    /// <summary>
    /// 首次订阅专属
    /// </summary>
    FirstSubscription = 3,

    /// <summary>
    /// 限时折扣
    /// </summary>
    LimitedTime = 4,

    /// <summary>
    /// 满减活动
    /// </summary>
    ThresholdDiscount = 5
}

/// <summary>
/// 折扣类型
/// </summary>
public enum DiscountType
{
    /// <summary>
    /// 百分比
    /// </summary>
    Percentage = 1,

    /// <summary>
    /// 固定金额
    /// </summary>
    Fixed = 2
}

/// <summary>
/// 应用范围
/// </summary>
public enum ApplyScope
{
    /// <summary>
    /// 全局
    /// </summary>
    Global = 0,

    /// <summary>
    /// 指定计划
    /// </summary>
    Plan = 1,

    /// <summary>
    /// 指定产品
    /// </summary>
    Product = 2
}

/// <summary>
/// 产品类型
/// </summary>
public enum ProductType
{
    /// <summary>
    /// 订阅
    /// </summary>
    Subscription = 1,

    /// <summary>
    /// 一次性产品
    /// </summary>
    OneTime = 2,

    /// <summary>
    /// 充值
    /// </summary>
    Recharge = 3,

    /// <summary>
    /// 全部
    /// </summary>
    All = 99
}

/// <summary>
/// 兑换码类型
/// </summary>
public enum RedemptionCodeType
{
    /// <summary>
    /// 唯一码
    /// </summary>
    Unique = 1,

    /// <summary>
    /// 通用码
    /// </summary>
    General = 2
}

/// <summary>
/// 兑换码状态
/// </summary>
public enum RedemptionCodeStatus
{
    /// <summary>
    /// 有效
    /// </summary>
    Active = 1,

    /// <summary>
    /// 已停用
    /// </summary>
    Inactive = 2,

    /// <summary>
    /// 已过期
    /// </summary>
    Expired = 3
}

#endregion
