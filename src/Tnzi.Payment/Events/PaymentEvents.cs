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
