namespace Tnzi.Finance.Services;

/// <summary>
/// 科目期间余额汇总的重建与校验（运维工具；作用于当前租户）
/// </summary>
/// <remarks>
/// 触发时机：存量账本启用 <see cref="Options.FinanceOptions.UseBalanceSummary"/> 前先重建、
/// verify 出差异后重建、迁移后、手工修数后。二者都对 JournalEntry 序列行取 X 锁把操作对全部
/// 过账串行化（无缺口编号已把每租户过账串行化，锁序无环）。
/// </remarks>
public interface IBalanceSummaryService
{
    /// <summary>
    /// 从总账全量重建当前租户的余额汇总桶（幂等：清空后按 (科目, yyyyMM, 币种) 重算）。
    /// </summary>
    Task<Result<BalanceSummaryRebuildDto>> RebuildAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 校验汇总桶与总账聚合的一致性（诊断 Missing/Extra/Mismatch 差异，不修复；差异明细截断至前 100 条）。
    /// </summary>
    Task<Result<BalanceSummaryVerifyDto>> VerifyAsync(CancellationToken cancellationToken = default);
}
