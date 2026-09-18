namespace Tnzi.Finance.Services;

/// <summary>
/// 贡献「这些来源令牌的凭证只能经单据自己的作废路径撤销」的清单。
/// </summary>
/// <remarks>
/// <b>为什么存在</b>：总账冲销端点（<c>POST admin/finance/journal-entries/{id}/reverse</c>）对
/// 单据投影出来的凭证一律拒绝 —— 从那里冲销一张发票的凭证，总账被抵消而发票仍是 Posted、仍进账龄，
/// 控制科目与子账从此对不上且两侧各自看起来都正常。核心自己那几种单据的令牌写在
/// <c>JournalEntryService</c> 里；子模块（发薪批次）与消费应用的自定义单据住在核心之外，
/// 让核心去认识它们等于反向依赖。手法与 <see cref="IMasterDataUsageProvider"/> 一致：
/// 核心只认契约，外面的模块回答事实。
/// <br/><br/>
/// <b>缺省方向</b>：未注册任何实现时清单就是核心那几种，与引入本契约之前逐字一致；
/// 每多一个实现只会<b>增加</b>拒绝，绝不放宽任何既有守卫。
/// <br/><br/>
/// <b>配套</b>：贡献了令牌的单据，自己的作废流程必须改走
/// <see cref="ILedgerPostingService.ReverseOnBehalfOfDocumentAsync"/> —— 普通的
/// <see cref="ILedgerPostingService.ReverseAsync"/> 与总账端点是同一道门，会把自己也拦在外面。
/// <br/><br/>
/// 多个实现可并存（<c>IEnumerable</c> 注入），取并集。令牌按序数比较，与凭证上的 <c>SourceType</c> 同口径。
/// </remarks>
public interface IDocumentProjectedSourceTypeProvider
{
    /// <summary>
    /// 本实现所辖的、有单据级作废路径的来源令牌。
    /// </summary>
    IReadOnlyCollection<string> SourceTypes { get; }
}
