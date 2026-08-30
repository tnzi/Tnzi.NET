namespace Tnzi.Payment.Promotions.Metadata;

// 促销域自己的五个枚举。数值与拆分前逐一相同（线上格式是 int，库里存的也是 int），
// 变的只有命名空间 —— R1 不允许跨程序集占用别人的命名空间。
//
// ★ 同一个 #region 里的另外两个枚举**刻意留在父模块**，它们不是促销独有的：
//   · DiscountType —— 父模块的 PaymentChannelCouponDto 带着它出境（IPaymentChannelCouponSync
//     的入参），而那个契约的唯一实现住在渠道包 Tnzi.Payment.Stripe。搬走它，
//     一个只加载「支付 + Stripe」的宿主会连编译都过不去。
//   · ProductType —— 父模块 PaymentService 由 BusinessType 现算出它，
//     并写进留在父模块的 CouponApplyContext。父模块自己在用，不能搬。

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

/// <summary>
/// 用户持有的优惠券状态
/// </summary>
public enum UserCouponStatus
{
    /// <summary>
    /// 可用
    /// </summary>
    Available = 0,

    /// <summary>
    /// 已使用
    /// </summary>
    Used = 1,

    /// <summary>
    /// 已过期
    /// </summary>
    Expired = 2,

    /// <summary>
    /// 已作废（管理员回收）
    /// </summary>
    Revoked = 3
}
