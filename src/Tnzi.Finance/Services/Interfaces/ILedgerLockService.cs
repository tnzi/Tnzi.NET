namespace Tnzi.Finance.Services;

/// <summary>
/// 账本封账锁：设定"账已封到某日"，该日及之前禁止过账与冲销。
/// </summary>
/// <remarks>
/// <b>与会计年度关账正交</b>：年度锁按**区间**（`FiscalYear.IsClosed`），封账日按**截止点**且
/// 可逐月推进。两把锁任一命中即拒绝，判定都在 <see cref="IFiscalYearService.ValidatePostingDateAsync"/>
/// 这个唯一漏斗里（全模块只有 <c>LedgerPostingEngine</c> 与 <c>ReversalGuard</c> 两个调用点）。
/// <br/><br/>
/// <b>刻意不提供"逐笔越权过账"</b>：QuickBooks 允许对单笔交易输口令强过。要在这里实现，
/// 就得把一个凭证从控制器一路穿到过账引擎的每个签名上（`PostAsync`/`VoidAsync`/`ReverseAsync`
/// ×9 个服务），或者藏一个 AsyncLocal 隐式通道 —— 前者污染全部公共 API，后者是隐藏副作用。
/// 受支持的路径是：**把封账日往回推 → 改 → 推回去**，这三步各自留痕，反而比逐笔放行更可审计。
/// <br/><br/>
/// 消费方可整体替换本服务（`TryAddScoped`），例如把封账进度托管给外部审批系统。
/// </remarks>
public interface ILedgerLockService
{
    /// <summary>读取当前封账状态（从未设置过时回一个 <c>ClosingDate = null</c> 的空状态）。</summary>
    Task<Result<LedgerLockDto>> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 设定 / 推进 / 解除封账日，并可同时改口令。
    /// </summary>
    /// <remarks>已设口令时必须提供匹配的 <c>Password</c>，否则 403；变更本身经审计留痕。</remarks>
    Task<Result<LedgerLockDto>> SetAsync(SetLedgerLockDto input, CancellationToken cancellationToken = default);

    /// <summary>
    /// 判断某个记账日是否被封账线挡住。
    /// </summary>
    /// <remarks>
    /// 由 <see cref="IFiscalYearService.ValidatePostingDateAsync"/> 调用，是过账/冲销路径上的
    /// 第二把锁。未封账时恒放行。
    /// </remarks>
    Task<Result> ValidatePostingDateAsync(DateTime postingDate, CancellationToken cancellationToken = default);
}
