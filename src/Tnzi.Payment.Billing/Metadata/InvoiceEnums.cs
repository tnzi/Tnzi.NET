namespace Tnzi.Payment.Billing.Metadata;

// 发票的两个枚举随发票域搬到本程序集：拆分前它们与支付 / 退款 / 订阅 / 促销的枚举同住
// Tnzi.Payment.Metadata.PaymentEnums.cs，而核心侧没有任何一处读它们（已逐个 grep 确认）。
// **数值一字未动** —— 它们是 Payment_Invoice.Type / .Status 两列的存储值，改数值等于静默改写历史数据的含义。
// 命名空间从 Tnzi.Payment.Metadata 变成 Tnzi.Payment.Billing.Metadata（R1 不允许跨程序集占名），
// 这是本次拆分唯一一处源码可见的破坏：直接 using Tnzi.Payment.Metadata 取这两个枚举的消费方
// 需要多写一行 using。序列化形态（int）与两个 DTO 的字段类型都不变，前端与已存数据不受影响。

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
