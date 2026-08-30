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

// 订阅域的 5 个枚举（SubscriptionStatus / SubscriptionBillingPurpose / BillingCycleType /
// SubscriptionChangeType / SubscriptionChangeStatus）随订阅域搬去了可选子模块
// Tnzi.Payment.Subscriptions（类 SubscriptionEnums，取值一字未动）。
// 刻意**留在这里**的两个同名成员是支付域与促销域自己的：BusinessType.Subscription
// 标记一笔支付的业务类型，ProductType.Subscription 标记一张促销券的适用产品类型 ——
// 它们在不加载续费包的宿主上照样有意义（一笔支付可以是别的系统发起的订阅收款）。

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

#region Promotion Enums

// 促销域自己的五个枚举（PromotionType / ApplyScope / RedemptionCodeType /
// RedemptionCodeStatus / UserCouponStatus）随促销域搬去了可选子模块 Tnzi.Payment.Promotions
// （Metadata/PromotionEnums.cs，数值一个不改）。留在这里的两个不是促销独有的：
// DiscountType 被父模块的 PaymentChannelCouponDto 带着出境（IPaymentChannelCouponSync 的入参，
// 实现在渠道包），ProductType 由父模块的 PaymentService 从 BusinessType 现算并写进
// 同样留在父模块的 CouponApplyContext。搬走任何一个都会让只加载「支付 + 渠道包」的宿主编不过。

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

#endregion

#region 回调

/// <summary>
/// 渠道回调事件的种类。渠道 webhook 上推的不只有支付状态，
/// 把它们全部当成支付事件解析会让"支付方式被撤销"这类通知无处安放。
/// </summary>
public enum PaymentCallbackKind
{
    /// <summary>
    /// 支付状态变更（建单成功 / 收款完成 / 收款被拒）
    /// </summary>
    Payment = 0,

    /// <summary>
    /// 已保存的支付方式在渠道侧被移除：付款人自己撤销了授权，或商户在渠道后台删掉了它。
    /// 本地必须跟着失效，否则后台会拿一个已经作废的凭据反复扣款失败。
    /// </summary>
    PaymentMethodRevoked = 1
}

#endregion
