namespace Tnzi.Finance.Offers.Services.Internal;

/// <summary>
/// 报价单 / 采购订单上由人填写的字符串字段的列宽。
/// </summary>
/// <remarks>
/// <para>
/// ★ 测试库是 SQLite，不检查列宽：超长值在那里「保存成功」，在 SQL Server / PostgreSQL 上是一个不点名字段的截断 500。
/// 所以列宽在服务入口先查一遍，超长答 400 并点名字段；常量由实体配置直接引用，两处不可能漂移。
/// </para>
/// <para>
/// 与会计核心同一口径（<see cref="FinanceFieldLimits"/>）：摘要与行描述沿用核心的列宽，转成发票 / 账单草稿时原样带过去；
/// 人填的值拒绝而不截断，只有从目录项抄过来的行描述（派生值）收进列宽。
/// </para>
/// </remarks>
internal static class OfferFieldLimits
{
    /// <summary>摘要（<c>Memo</c>）列宽，与会计核心单据一致（转换时原样带到发票 / 账单）。</summary>
    internal const int MemoMaxLength = FinanceFieldLimits.MemoMaxLength;

    /// <summary>内部备注（<c>InternalNote</c>）列宽。</summary>
    internal const int InternalNoteMaxLength = 1000;

    /// <summary>采购订单收货地址（<c>ShipTo</c>）列宽。</summary>
    internal const int ShipToMaxLength = 500;

    /// <summary>单据行描述列宽，与会计核心单据行一致。</summary>
    internal const int LineDescriptionMaxLength = FinanceFieldLimits.LineDescriptionMaxLength;

    /// <summary>币种代码列宽。</summary>
    internal const int CurrencyMaxLength = 8;
}
