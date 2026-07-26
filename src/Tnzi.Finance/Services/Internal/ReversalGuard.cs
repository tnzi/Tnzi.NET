namespace Tnzi.Finance.Services.Internal;

/// <summary>
/// 冲销受阻判定（<b>唯一</b>判定源）：冲销漏斗 <see cref="LedgerPostingEngine.BuildReversalAsync"/> 的守卫，
/// 与 <see cref="ILedgerPostingService.GetReversibilityAsync"/> 的只读查询共用本类
/// </summary>
/// <remarks>
/// <para>
/// 抽成一段共享代码的理由只有一条：<b>两处规则不得漂移</b>。守卫在冲销漏斗里拒绝、查询在呈现端
/// 渲染时给理由，若各写一份，迟早变成"查询说能冲、真冲吃 409"。
/// </para>
/// <para>
/// 本类<b>只读</b>，不产生任何写入，因此可以安全地放在引擎的任何写入动作（凭证号分配、余额桶累加）之前。
/// </para>
/// <para>
/// 覆盖三项：会计期间封账、已完成银行对账、已匹配银行流水。凭证状态（草稿 / 已冲销）<b>不</b>在这里判定——
/// 那是单据状态机的事，冲销漏斗上游（各 <c>VoidAsync</c> 与 <c>IJournalEntryService.ReverseAsync</c>）
/// 已经在做，重复一遍反而多一处会漂移的规则。
/// </para>
/// </remarks>
public sealed class ReversalGuard
{
    private readonly IFiscalYearService _fiscalYearService;
    private readonly IReadOnlyRepository<ReconciliationLine, Guid> _reconciliationLineRepository;
    private readonly IReadOnlyRepository<Reconciliation, Guid> _reconciliationRepository;
    /// <summary>
    /// 账本之外的持有者（银行流水等）。未注册任何实现时视为"无人持有" ——
    /// 本契约只会**增加**拒绝，不会放宽任何既有守卫，故这是安全的缺省。
    /// </summary>
    private readonly IEnumerable<IJournalLineHoldProvider> _holdProviders;

    public ReversalGuard(
        IFiscalYearService fiscalYearService,
        IReadOnlyRepository<ReconciliationLine, Guid> reconciliationLineRepository,
        IReadOnlyRepository<Reconciliation, Guid> reconciliationRepository,
        IEnumerable<IJournalLineHoldProvider>? holdProviders = null)
    {
        _fiscalYearService = Check.NotNull(fiscalYearService);
        _reconciliationLineRepository = Check.NotNull(reconciliationLineRepository);
        _reconciliationRepository = Check.NotNull(reconciliationRepository);
        _holdProviders = holdProviders ?? Enumerable.Empty<IJournalLineHoldProvider>();
    }

    /// <summary>
    /// 判定这张已过账凭证能否冲销到指定日期；返回 null 表示放行
    /// </summary>
    /// <param name="original">原凭证（<see cref="JournalEntry.Lines"/> 必须已加载——行 id 是判定输入）</param>
    /// <param name="postingDate">冲销凭证的过账日期（各 <c>VoidAsync</c> 传原凭证记账日=回填到原始记账日）</param>
    public async Task<ReversalBlock?> EvaluateAsync(JournalEntry original, DateTime postingDate, CancellationToken cancellationToken = default)
    {
        Check.NotNull(original);

        var date = postingDate.ToUtcDate();
        var dateResult = await _fiscalYearService.ValidatePostingDateAsync(date, cancellationToken);
        if (!dateResult.Succeeded)
        {
            return new ReversalBlock(
                ReversalBlockReasons.ClosedPeriod,
                dateResult.Message ?? "The posting date is not allowed.",
                dateResult.Code ?? 400);
        }

        // 未持久化的行（Id 尚未生成）不可能被对账勾选或被流水匹配，排除后避免构造出恒不命中的 IN (00000...)
        var lineIds = original.Lines
            .Select(l => l.Id)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        if (lineIds.Count == 0)
            return null;

        // ① 已完成对账：勾选行子查询下推数据库，单次查询判定（引擎是热路径，禁止逐行查）
        var clearedInReconciliations = _reconciliationLineRepository.AsQueryable()
            .Where(rl => lineIds.Contains(rl.JournalLineId))
            .Select(rl => rl.ReconciliationId);

        var reconciliation = await _reconciliationRepository.AsQueryable()
            .Where(r => r.Status == ReconciliationStatus.Completed && clearedInReconciliations.Contains(r.Id))
            .OrderBy(r => r.StatementDate)
            .Select(r => new { r.Id, r.StatementDate })
            .FirstOrDefaultAsync(cancellationToken);

        if (reconciliation != null)
        {
            return new ReversalBlock(
                ReversalBlockReasons.Reconciled,
                $"These lines are locked by a completed bank reconciliation (statement dated {reconciliation.StatementDate:yyyy-MM-dd}, id {reconciliation.Id}). "
                + "A completed reconciliation cannot be reopened, so reversing into it would leave it permanently out of balance. "
                + "Post a correcting entry dated in an open period instead.",
                409);
        }

        // ② 被账本之外的东西持有（目前唯一的持有者是已匹配的银行流水）：只拒绝并指路，
        //    不自动解开——那是在无声地丢弃别人的对账工作。经 IJournalLineHoldProvider 询问，
        //    使会计内核不必认识银行域的类型。
        foreach (var provider in _holdProviders)
        {
            var holds = await provider.GetHoldsAsync(lineIds, cancellationToken);
            var hold = holds.FirstOrDefault();
            if (hold != null)
                return new ReversalBlock(hold.ReasonCode, hold.Detail, 409);
        }

        return null;
    }
}

/// <summary>
/// 冲销受阻的判定结果（<see cref="ReversalGuard.EvaluateAsync"/> 返回 null 表示放行）
/// </summary>
/// <param name="Reason">受阻原因代码，取值见 <see cref="ReversalBlockReasons"/></param>
/// <param name="Detail">面向操作员的说明（英文，含补救办法）</param>
/// <param name="Code">冲销漏斗按此码拒绝（对账/流水冲突 409，期间封账沿用年度服务给出的码）</param>
public sealed record ReversalBlock(string Reason, string Detail, int Code);
