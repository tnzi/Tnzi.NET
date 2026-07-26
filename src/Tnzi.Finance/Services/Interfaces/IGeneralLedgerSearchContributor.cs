namespace Tnzi.Finance.Services.Interfaces;

/// <summary>
/// 给总账明细的关键字搜索**贡献额外的来源单据命中**。
/// </summary>
/// <remarks>
/// <b>为什么存在</b>：总账明细按关键字搜的是"人能记住的东西" —— 凭证摘要、凭证号、付款参考号、
/// 往来方名称……以及**支票号**。但支票登记簿属于银行域，让报表内核直接 join <c>BankCheck</c>
/// 就把银行域焊死在了会计内核上。
/// <br/><br/>
/// <b>契约形状</b>：返回的是命中的**来源单据 id 字符串集合**（对应 <c>JournalEntry.SourceId</c>），
/// 而不是分录行 —— 因为搜索是在"哪张单据"这个层面命中的，具体落到哪几行由报表内核自己算。
/// 返回 <c>SourceType</c> + id 的组合，让贡献者能命中任意来源类型，而不只是付款单。
/// <br/><br/>
/// <b>可选且可多</b>：<c>IEnumerable</c> 注入，全部贡献者的结果并集参与筛选。未注册任何实现时
/// 搜索范围回到内核自带的那几项 —— 只会**少搜到**，绝不会多返回不该出现的行。
/// </remarks>
public interface IGeneralLedgerSearchContributor
{
    /// <summary>
    /// 按关键字返回命中的 <c>(SourceType, SourceId)</c> 组合。
    /// </summary>
    /// <param name="keyword">已转小写的搜索词</param>
    Task<IReadOnlyList<GeneralLedgerSourceMatch>> MatchAsync(string keyword, CancellationToken cancellationToken = default);
}

/// <summary>
/// 一个来源单据命中。
/// </summary>
/// <param name="SourceType">来源令牌（<see cref="Metadata.FinanceSourceTypes"/>）</param>
/// <param name="SourceId">来源单据 id 的字符串形式，与 <c>JournalEntry.SourceId</c> 逐字一致</param>
public sealed record GeneralLedgerSourceMatch(string SourceType, string SourceId);
