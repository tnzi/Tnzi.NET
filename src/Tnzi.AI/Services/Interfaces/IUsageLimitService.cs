namespace Tnzi.AI.Services;

/// <summary>
/// 按用户的用量上限：5 小时窗口与每周窗口，按 API 等价费用（美元）计量。
/// </summary>
/// <remarks>
/// <para>
/// 一轮的生命周期：开始前 <see cref="BeginAsync"/>（判上限，开启还没开始的窗口：窗口从第一次使用起算），结束后
/// <see cref="SettleAsync"/>（把这一轮实际的费用记进当前窗口）。一轮开始时没超、跑的过程中超了，照常跑完、照常记账，
/// 下一轮才拦下（与 claude.ai 一样）。内建路径（<c>QuotaMiddleware</c>）与外部 CLI 路径（认领后的执行器）用的是同一对方法。
/// </para>
/// <para>
/// 只想当场告诉调用方「会被拦」而不开窗口的入口（聊天入口、入队）用 <see cref="CheckAsync"/>：它不写库。
/// </para>
/// <para>
/// 上限取自 <see cref="IUsagePolicyService"/>（用户 → 租户默认 → <c>AI:UsageLimits</c>）。不限的用户照样计量，
/// 管理端看得到他用了多少，改成有上限时窗口里已有的用量立刻算数。
/// </para>
/// </remarks>
public interface IUsageLimitService
{
    /// <summary>
    /// 只读判定：这个用户现在发一轮会不会被拦下。放行返回 null。
    /// </summary>
    Task<UsageLimitDenialDto?> CheckAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// 一轮开始：判上限，并开启还没开始的窗口。
    /// </summary>
    /// <returns>
    /// 成功时返回要在一轮结束后交给 <see cref="SettleAsync"/> 的预留。到上限时失败：429 <c>AI_USAGE_LIMIT_REACHED</c>，
    /// <c>ErrorDetails</c> 是 <see cref="UsageLimitDenialDto"/>（哪个窗口、什么时候重置）。计量子系统自身出错时 500 <c>AI_USAGE_LIMIT_CHECK_FAILED</c>，
    /// 调用方同样不跑（失败关闭），但不说成「到上限了」。
    /// </returns>
    Task<Result<UsageLimitReservation>> BeginAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// 一轮结束：把这一轮实际的费用记进当前的窗口。每个预留恰好调一次，无论一轮怎么结束（取消、失败也记已经花掉的那部分）。
    /// </summary>
    /// <remarks>费用为 0 或负数时什么都不写。窗口在这一轮期间过期了，费用记进新开的窗口。</remarks>
    Task<Result> SettleAsync(UsageLimitReservation reservation, decimal costUsd, CancellationToken ct = default);

    /// <summary>这个用户当前各窗口的用量与重置时间（带金额，管理端用；用户端投影成 <see cref="MyUsageLimitsDto"/>）。</summary>
    Task<UsageLimitStatusDto> GetStatusAsync(Guid userId, CancellationToken ct = default);

    /// <summary>当前用户自己的用量（只有百分比与重置时间）。</summary>
    Task<MyUsageLimitsDto> GetMyStatusAsync(Guid userId, CancellationToken ct = default);

    /// <summary>管理端计量列表：用过 AI 的用户和他们当前的用量，最近用过的在前。</summary>
    Task<Result<IPagedList<UserUsageSummaryDto>>> GetUsersAsync(UsageMeterQueryDto query, CancellationToken ct = default);

    /// <summary>清零这个用户当前的两个窗口（管理员手动放行）；下一次使用重新开窗口。</summary>
    Task<Result> ResetAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>
/// 一轮开始时拿到的预留，结束时交回 <see cref="IUsageLimitService.SettleAsync"/>。
/// </summary>
/// <param name="UserId">计量到谁头上。</param>
/// <param name="BegunAt">一轮开始的时间（UTC）。</param>
public sealed record UsageLimitReservation(Guid UserId, DateTime BegunAt);
