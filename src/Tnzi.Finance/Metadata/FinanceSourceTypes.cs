namespace Tnzi.Finance.Metadata;

/// <summary>
/// 凭证来源类型（<see cref="Entities.JournalEntry.SourceType"/> / <see cref="Entities.JournalLine"/> 的回链令牌）常量
/// </summary>
/// <remarks>
/// <para>
/// 每张由业务单据投影出来的凭证都带 <c>SourceType</c> + <c>SourceId</c> 回链单据。该令牌是
/// **wire 契约**：它进数据库、进报表、进消费应用的 resolver。所以这里是字符串字面量而非
/// <c>nameof(Invoice)</c>——实体类型改名不得静默改变存量数据里的令牌值与消费方的分支判断
/// （<c>nameof</c> 版本改名后编译照过，消费方 resolver 却全部落空）。
/// </para>
/// <para>
/// 令牌值恒等于当前实体名，是历史事实，不是可依赖的推导规则。消费应用可自定义令牌
/// （编程式过账 <c>ILedgerPostingService</c> 的 <c>SourceType</c> 自由字符串）；本类只声明框架自己写入的那些。
/// </para>
/// </remarks>
public static class FinanceSourceTypes
{
    /// <summary>销售发票</summary>
    public const string Invoice = "Invoice";

    /// <summary>采购账单</summary>
    public const string Bill = "Bill";

    /// <summary>贷项通知单</summary>
    public const string CreditMemo = "CreditMemo";

    /// <summary>费用单（直付）</summary>
    public const string Expense = "Expense";

    /// <summary>收付款单</summary>
    public const string PaymentEntry = "PaymentEntry";

    /// <summary>核销记录（仅 realized FX 残差凭证由其产生）</summary>
    public const string PaymentApplication = "PaymentApplication";

    /// <summary>资金划转单</summary>
    public const string Transfer = "Transfer";

    /// <summary>期末汇兑重估（SourceId = 基准日 yyyy-MM-dd，非实体 Id）</summary>
    public const string Revaluation = "Revaluation";

    /// <summary>框架写入的全部来源令牌（消费方自定义令牌不在内）</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Invoice, Bill, CreditMemo, Expense, PaymentEntry, PaymentApplication, Transfer, Revaluation
    ];
}
