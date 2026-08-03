namespace Tnzi.Payment.Events;

/// <summary>
/// 支付完成事件
/// </summary>
public class PaymentCompletedEvent : EventBase
{
    /// <summary>
    /// 支付ID
    /// </summary>
    public Guid PaymentId { get; set; }

    /// <summary>
    /// 交易流水号
    /// </summary>
    public string TradeNo { get; set; } = string.Empty;

    /// <summary>
    /// 业务订单号
    /// </summary>
    public string BusinessOrderNo { get; set; } = string.Empty;

    /// <summary>
    /// 支付金额
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// 币种
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// 支付渠道
    /// </summary>
    public string ChannelCode { get; set; } = string.Empty;

    /// <summary>
    /// 支付完成时间
    /// </summary>
    public DateTime PaidTime { get; set; }

    /// <summary>
    /// 外部交易流水号
    /// </summary>
    public string? ExternalTradeNo { get; set; }

    /// <summary>
    /// 业务类型（用于将事件路由到对应业务状态机，如订阅）
    /// </summary>
    public BusinessType BusinessType { get; set; }

    /// <summary>
    /// 扩展数据（JSON，承载业务用途，如订阅计费 purpose）
    /// </summary>
    public string? ExtraData { get; set; }
}

/// <summary>
/// 支付失败事件
/// </summary>
public class PaymentFailedEvent : EventBase
{
    /// <summary>
    /// 支付ID
    /// </summary>
    public Guid PaymentId { get; set; }

    /// <summary>
    /// 交易流水号
    /// </summary>
    public string TradeNo { get; set; } = string.Empty;

    /// <summary>
    /// 业务订单号
    /// </summary>
    public string BusinessOrderNo { get; set; } = string.Empty;

    /// <summary>
    /// 失败原因
    /// </summary>
    public string FailReason { get; set; } = string.Empty;

    /// <summary>
    /// 错误代码
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// 业务类型（用于将事件路由到对应业务状态机，如订阅）
    /// </summary>
    public BusinessType BusinessType { get; set; }

    /// <summary>
    /// 扩展数据（JSON，承载业务用途，如订阅计费 purpose）
    /// </summary>
    public string? ExtraData { get; set; }
}

/// <summary>
/// 支付过期事件
/// </summary>
public class PaymentExpiredEvent : EventBase
{
    /// <summary>
    /// 支付ID
    /// </summary>
    public Guid PaymentId { get; set; }

    /// <summary>
    /// 交易流水号
    /// </summary>
    public string TradeNo { get; set; } = string.Empty;

    /// <summary>
    /// 业务订单号
    /// </summary>
    public string BusinessOrderNo { get; set; } = string.Empty;

    /// <summary>
    /// 过期时间
    /// </summary>
    public DateTime ExpiredTime { get; set; }

    /// <summary>
    /// 业务类型（用于将事件路由到对应业务状态机，如订阅）
    /// </summary>
    public BusinessType BusinessType { get; set; }

    /// <summary>
    /// 扩展数据（JSON，承载业务用途，如订阅计费 purpose）
    /// </summary>
    public string? ExtraData { get; set; }
}

/// <summary>
/// 退款处理完成事件
/// </summary>
public class RefundProcessedEvent : EventBase
{
    /// <summary>
    /// 退款ID
    /// </summary>
    public Guid RefundId { get; set; }

    /// <summary>
    /// 退款流水号
    /// </summary>
    public string RefundNo { get; set; } = string.Empty;

    /// <summary>
    /// 关联支付ID
    /// </summary>
    public Guid PaymentId { get; set; }

    /// <summary>
    /// 交易流水号
    /// </summary>
    public string TradeNo { get; set; } = string.Empty;

    /// <summary>
    /// 退款金额
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// 币种
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// 退款完成时间
    /// </summary>
    public DateTime CompletedTime { get; set; }

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Succeeded { get; set; }

    /// <summary>
    /// 失败原因
    /// </summary>
    public string? FailReason { get; set; }
}

/// <summary>
/// 已保存的支付方式在渠道侧被撤销事件。
/// </summary>
/// <remarks>
/// 由渠道 webhook 触发（付款人在 PayPal / Stripe 自己撤销了授权，或商户在渠道后台删除）。
/// 框架已经把本地记录置失效并清掉订阅上的快照；这个事件是留给消费方"告诉用户"的钩子——
/// 用户是在渠道那边操作的，多半没意识到自己顺手关掉了这里的自动续费。
/// </remarks>
public class PaymentMethodRevokedEvent : EventBase
{
    /// <summary>
    /// 已保存支付方式ID
    /// </summary>
    public Guid PaymentMethodId { get; set; }

    /// <summary>
    /// 持有用户ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 支付渠道代码
    /// </summary>
    public string ChannelCode { get; set; } = string.Empty;

    /// <summary>
    /// 卡组织 / 钱包类型（展示用）
    /// </summary>
    public string? Brand { get; set; }

    /// <summary>
    /// 卡号尾四位（展示用）
    /// </summary>
    public string? Last4 { get; set; }

    /// <summary>
    /// 因此失去支付方式的订阅数量。大于 0 意味着这些订阅的下次续费会失败。
    /// </summary>
    public int AffectedSubscriptionCount { get; set; }
}
