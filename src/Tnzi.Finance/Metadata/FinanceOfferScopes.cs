namespace Tnzi.Finance.Metadata;

/// <summary>
/// 报价单 / 采购订单的**连续编号作用域**键。
/// </summary>
/// <remarks>
/// 刻意与 <see cref="FinanceSourceTypes"/> 分开：那些是**总账来源令牌**，回答
/// "这条分录是哪种单据投影来的"，而报价单与采购订单从不投影总账。把它们混进
/// 来源令牌表，只会让总账的来源筛选下拉里出现永远搜不到东西的选项。
///
/// 这些字符串是持久化契约（<c>DocumentSequence.Scope</c> 的行键），改名等于把
/// 号段重置为 1，与来源令牌同等严肃：**永远不要用 nameof**。
/// </remarks>
public static class FinanceOfferScopes
{
    /// <summary>报价单号段</summary>
    public const string Estimate = "Estimate";

    /// <summary>采购订单号段</summary>
    public const string PurchaseOrder = "PurchaseOrder";
}
