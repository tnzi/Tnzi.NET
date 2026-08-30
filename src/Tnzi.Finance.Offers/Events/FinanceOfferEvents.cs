namespace Tnzi.Finance.Offers.Events;

/// <summary>
/// 报价单 / 采购订单已发出事件
/// </summary>
/// <remarks>
/// 编号在这一刻分配，因此这是"单据对外成为事实"的时点——消费应用挂邮件发送、
/// PDF 归档、CRM 同步等副作用应该订阅它，而不是订阅创建草稿。
///
/// 随单据搬进本模块（原在核心的 <c>Events/FinanceEvents.cs</c>）：只有本模块发布它。
/// 事件类型名与属性一字不变，订阅方改一行 using 即可。
/// </remarks>
public class FinanceOfferSentEvent : EventBase
{
    /// <summary>单据类型（编号作用域键，见 <see cref="FinanceOfferScopes"/>）</summary>
    public string DocType { get; set; } = string.Empty;

    public Guid DocId { get; set; }
    public string Number { get; set; } = string.Empty;

    /// <summary>往来方（客户或供应商）</summary>
    public Guid PartyId { get; set; }

    /// <summary>价税合计（交易币）</summary>
    public decimal Total { get; set; }

    public string Currency { get; set; } = string.Empty;
}

/// <summary>
/// 报价单 / 采购订单已转换为正式单据事件
/// </summary>
public class FinanceOfferConvertedEvent : EventBase
{
    /// <summary>来源单据类型（编号作用域键，见 <see cref="FinanceOfferScopes"/>）</summary>
    public string SourceType { get; set; } = string.Empty;

    public Guid SourceId { get; set; }
    public string? SourceNumber { get; set; }

    /// <summary>目标单据类型（总账来源令牌，见 <c>FinanceSourceTypes</c>）</summary>
    public string TargetType { get; set; } = string.Empty;

    /// <summary>目标单据 Id（草稿）</summary>
    public Guid TargetId { get; set; }
}
