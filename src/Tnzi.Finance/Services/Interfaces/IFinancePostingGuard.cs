namespace Tnzi.Finance.Services.Interfaces;

/// <summary>
/// 过账前校验钩子 —— 消费应用以此在过账/作废/冲销前拦截并否决操作
/// </summary>
/// <remarks>
/// 财务核心刻意不内置审批工作流（Draft → Posted 状态机保持不变）：
/// 审批模型差异极大（单人/多级/金额分档），任何内置实现都会成为错误的抽象。
/// 消费应用注册本接口实现（可多个，任一失败即整体拒绝）即可实现自己的审批门：
/// 审批状态、审批人留痕等存放在消费应用自己的伴生表，钩子里查表决定放行与否。
/// <para>
/// 触发点是**业务操作**而非底层凭证：过账发票触发一次（DocType = "Invoice"），
/// 其内部生成的凭证不再重复触发；手工凭证与 <see cref="ILedgerPostingService.PostAsync"/>
/// 的编程式过账各自触发（DocType 分别为 "JournalEntry" 与请求的 SourceType）。
/// 未注册任何实现时零开销直接放行。
/// </para>
/// </remarks>
public interface IFinancePostingGuard
{
    /// <summary>
    /// 校验操作是否放行。返回失败 Result（建议携带 403/409 状态码与原因）即否决，
    /// 操作以该 Result 原样返回给调用方且不产生任何写入
    /// </summary>
    Task<Result> CheckAsync(FinancePostingGuardContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// 过账前校验上下文
/// </summary>
public class FinancePostingGuardContext
{
    /// <summary>
    /// 单据类型（"Invoice" / "Bill" / "Expense" / "CreditMemo" / "PaymentEntry" / "JournalEntry"，
    /// 或 <see cref="ILedgerPostingService"/> 编程式过账时请求的 SourceType）
    /// </summary>
    public required string DocType { get; init; }

    /// <summary>单据ID（实体主键的字符串形式；编程式过账时为请求的 SourceId）</summary>
    public required string DocId { get; init; }

    /// <summary>操作类型</summary>
    public required FinancePostingOperation Operation { get; init; }

    /// <summary>
    /// 单据对象（框架单据为实体实例，编程式过账为 <c>LedgerPostingRequest</c>；
    /// 仅供检查，钩子内不得修改）
    /// </summary>
    public required object Document { get; init; }
}
