namespace Tnzi.AI.Services;

/// <summary>
/// Quota check service implementation.
/// </summary>
public class QuotaService : ApplicationService, IQuotaService, IQuotaProvider
{
    private readonly IRepository<UserQuota, Guid> _quotaRepository;
    private readonly IOptionsMonitor<AIOptions> _options;

    public QuotaService(
        IRepository<UserQuota, Guid> quotaRepository,
        IOptionsMonitor<AIOptions> options,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _quotaRepository = Check.NotNull(quotaRepository);
        _options = Check.NotNull(options);
    }

    #region 私有辅助方法

    /// <summary>
    /// 重置配额（如果需要），返回是否有变更
    /// </summary>
    private static bool ResetQuotaIfNeeded(UserQuota quota)
    {
        var now = DateTime.UtcNow;
        var lastReset = quota.LastResetDate;

        var resetDaily = now.Date > lastReset.Date;
        var resetMonthly = now.Year > lastReset.Year || now.Month > lastReset.Month;

        if (resetDaily)
        {
            quota.CurrentDailyUsage = 0;
        }

        if (resetMonthly)
        {
            quota.CurrentMonthlyUsage = 0;
        }

        if (resetDaily || resetMonthly)
        {
            quota.LastResetDate = now;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 检查是否超过每日配额
    /// </summary>
    private static bool IsExceedDailyLimit(UserQuota quota, long additionalTokens)
    {
        if (!quota.IsEnabled) return false;
        return quota.CurrentDailyUsage + additionalTokens > quota.DailyTokenLimit;
    }

    /// <summary>
    /// 检查是否超过每月配额
    /// </summary>
    private static bool IsExceedMonthlyLimit(UserQuota quota, long additionalTokens)
    {
        if (!quota.IsEnabled) return false;
        return quota.CurrentMonthlyUsage + additionalTokens > quota.MonthlyTokenLimit;
    }

    #endregion

    /// <summary>
    /// 检查用户配额是否足够
    /// </summary>
    public async Task<Result<QuotaCheckResult>> CheckQuotaAsync(Guid userId, long estimatedTokens, CancellationToken ct = default)
    {
        try
        {
            var quota = await GetOrCreateQuotaAsync(userId, ct);

            // 重置配额（如果需要），仅在有变更时更新数据库
            if (ResetQuotaIfNeeded(quota))
            {
                await _quotaRepository.UpdateAsync(quota, ct);
            }

            // 如果未启用配额限制，直接允许
            if (!quota.IsEnabled)
            {
                return Ok(QuotaCheckResult.Allow(long.MaxValue, long.MaxValue));
            }

            // 检查每日配额
            if (IsExceedDailyLimit(quota, estimatedTokens))
            {
                LogWarning("User {UserId} exceeded daily quota. Current: {Current}, Limit: {Limit}, Requested: {Requested}",
                    userId, quota.CurrentDailyUsage, quota.DailyTokenLimit, estimatedTokens);

                return Ok(QuotaCheckResult.Deny($"Daily quota exceeded. Current: {quota.CurrentDailyUsage}, Limit: {quota.DailyTokenLimit}"));
            }

            // 检查每月配额
            if (IsExceedMonthlyLimit(quota, estimatedTokens))
            {
                LogWarning("User {UserId} exceeded monthly quota. Current: {Current}, Limit: {Limit}, Requested: {Requested}",
                    userId, quota.CurrentMonthlyUsage, quota.MonthlyTokenLimit, estimatedTokens);

                return Ok(QuotaCheckResult.Deny($"Monthly quota exceeded. Current: {quota.CurrentMonthlyUsage}, Limit: {quota.MonthlyTokenLimit}"));
            }

            // 配额足够，计算使用率和预警级别
            var projected = ProjectUsage(quota, estimatedTokens);
            await PublishThresholdIfCrossedAsync(userId, quota, projected);

            return Ok(QuotaCheckResult.Allow(
                projected.RemainingDaily, projected.RemainingMonthly, projected.DailyPct, projected.MonthlyPct, projected.Level));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error checking quota for user {UserId}", userId);
            return Fail<QuotaCheckResult>("Quota check failed", 500, ErrorCodes.QuotaCheckFailed);
        }
    }

    /// <summary>
    /// 更新用户配额使用量
    /// </summary>
    public async Task<Result> UpdateUsageAsync(Guid userId, long actualTokens, CancellationToken ct = default)
    {
        try
        {
            // 事务内的裸 SQL 前置：不强开物理事务，这条累加会在自动提交模式下执行，
            // 与它对应的预留却随请求回滚 —— 两边不一致比少记一次更难查。
            await _quotaRepository.EnsureTransactionStartedAsync(ct);

            // 加量前的快照：阈值事件只在等级上升的那一次发布，须拿加量前后两个等级比较
            var before = await ReadQuotaSnapshotAsync(userId, ct);

            await _quotaRepository.AsQueryable()
                .Where(q => q.UserId == userId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(q => q.CurrentDailyUsage, q => q.CurrentDailyUsage + actualTokens)
                    .SetProperty(q => q.CurrentMonthlyUsage, q => q.CurrentMonthlyUsage + actualTokens), ct);

            Logger.LogDebug("Updated quota for user {UserId}. Added {Tokens} tokens", userId, actualTokens);

            if (before is { IsEnabled: true })
            {
                await PublishThresholdIfCrossedAsync(userId, before, ProjectUsage(before, actualTokens));
            }

            return Ok();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error updating usage for user {UserId}", userId);
            return Fail("Quota update failed", 500, ErrorCodes.QuotaUpdateFailed);
        }
    }

    /// <summary>
    /// 获取用户配额信息
    /// </summary>
    public async Task<Result<UserQuotaDto>> GetQuotaAsync(Guid userId, CancellationToken ct = default)
    {
        try
        {
            var quota = await GetOrCreateQuotaAsync(userId, ct);

            // 重置配额（如果需要），仅在有变更时更新数据库
            if (ResetQuotaIfNeeded(quota))
            {
                await _quotaRepository.UpdateAsync(quota, ct);
            }

            var dto = quota.MapTo<UserQuotaDto>();
            return Ok(dto);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error getting quota for user {UserId}", userId);
            return Fail<UserQuotaDto>("Failed to get quota", 500, ErrorCodes.QuotaGetFailed);
        }
    }

    /// <summary>
    /// 创建或更新用户配额
    /// </summary>
    public async Task<Result<UserQuotaDto>> SetQuotaAsync(Guid userId, long dailyLimit, long monthlyLimit, decimal? warningThreshold = null, decimal? criticalThreshold = null, CancellationToken ct = default)
    {
        try
        {
            var quota = await _quotaRepository.AsQueryable(withTracking: true)
                .FirstOrDefaultAsync(q => q.UserId == userId, ct);

            if (quota == null)
            {
                // 创建新配额
                quota = new UserQuota
                {
                    UserId = userId,
                    DailyTokenLimit = dailyLimit,
                    MonthlyTokenLimit = monthlyLimit,
                    CurrentDailyUsage = 0,
                    CurrentMonthlyUsage = 0,
                    LastResetDate = DateTime.UtcNow,
                    IsEnabled = true,
                    WarningThreshold = warningThreshold ?? 0.8m,
                    CriticalThreshold = criticalThreshold ?? 0.95m
                };
                await _quotaRepository.InsertAsync(quota, ct);

                LogInformation("Created quota for user {UserId}. Daily: {Daily}, Monthly: {Monthly}",
                    userId, dailyLimit, monthlyLimit);
            }
            else
            {
                // 更新现有配额
                quota.DailyTokenLimit = dailyLimit;
                quota.MonthlyTokenLimit = monthlyLimit;
                if (warningThreshold.HasValue)
                    quota.WarningThreshold = warningThreshold.Value;
                if (criticalThreshold.HasValue)
                    quota.CriticalThreshold = criticalThreshold.Value;
                await _quotaRepository.UpdateAsync(quota, ct);

                LogInformation("Updated quota for user {UserId}. Daily: {Daily}, Monthly: {Monthly}",
                    userId, dailyLimit, monthlyLimit);
            }

            var dto = quota.MapTo<UserQuotaDto>();
            return Ok(dto);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error setting quota for user {UserId}", userId);
            return Fail<UserQuotaDto>("Failed to set quota", 500, ErrorCodes.QuotaSetFailed);
        }
    }

    /// <summary>
    /// 重置用户配额
    /// </summary>
    public async Task<Result> ResetQuotaAsync(Guid userId, bool resetDaily, bool resetMonthly, CancellationToken ct = default)
    {
        try
        {
            var quota = await _quotaRepository.AsQueryable(withTracking: true)
                .FirstOrDefaultAsync(q => q.UserId == userId, ct);

            if (quota == null)
            {
                return Fail("User quota not found", 404, ErrorCodes.QuotaNotFound);
            }

            if (resetDaily)
            {
                quota.CurrentDailyUsage = 0;
                LogInformation("Reset daily quota for user {UserId}", userId);
            }

            if (resetMonthly)
            {
                quota.CurrentMonthlyUsage = 0;
                LogInformation("Reset monthly quota for user {UserId}", userId);
            }

            quota.LastResetDate = DateTime.UtcNow;
            await _quotaRepository.UpdateAsync(quota, ct);

            return Ok();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error resetting quota for user {UserId}", userId);
            return Fail("Quota reset failed", 500, ErrorCodes.QuotaResetFailed);
        }
    }

    /// <summary>
    /// 原子预留配额：在单次 SQL 操作中检查限额并扣减预估 Token，消除 TOCTOU 竞态
    /// 使用 ExecuteUpdateAsync + WHERE 限额条件实现原子 check-and-decrement
    /// </summary>
    public async Task<Result<QuotaReservation>> ReserveQuotaAsync(Guid userId, long estimatedTokens, CancellationToken ct = default)
    {
        try
        {
            // 本方法全程走 ExecuteUpdateAsync（绕过变更跟踪器的裸 SQL）。框架物理事务延迟到
            // 首次 UoW SaveChanges 才 BEGIN，不先强开就会在自动提交模式下执行：行锁不持有到
            // 事务结束、回滚撤不掉扣减，且与 GetOrCreateQuotaAsync 的 flush 不在同一个事务里
            // —— 那样扣减根本看不见刚插入的那一行。
            await _quotaRepository.EnsureTransactionStartedAsync(ct);

            var quota = await GetOrCreateQuotaAsync(userId, ct);

            // 重置配额（如果需要）
            var needsReset = ResetQuotaIfNeeded(quota);
            if (needsReset)
            {
                await _quotaRepository.AsQueryable()
                    .Where(q => q.UserId == userId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(q => q.CurrentDailyUsage, quota.CurrentDailyUsage)
                        .SetProperty(q => q.CurrentMonthlyUsage, quota.CurrentMonthlyUsage)
                        .SetProperty(q => q.LastResetDate, quota.LastResetDate), ct);
            }

            // 如果未启用配额限制，直接允许
            if (!quota.IsEnabled)
            {
                return Ok(new QuotaReservation { ReservedTokens = 0, ReservedAt = DateTime.UtcNow });
            }

            // 原子 check-and-decrement：单条 SQL 同时检查限额并扣减，避免 TOCTOU 竞态
            // WHERE 条件确保仅在配额充足时才更新，返回受影响行数判断是否成功
            var affectedRows = await _quotaRepository.AsQueryable()
                .Where(q => q.UserId == userId
                    && q.CurrentDailyUsage + estimatedTokens <= q.DailyTokenLimit
                    && q.CurrentMonthlyUsage + estimatedTokens <= q.MonthlyTokenLimit)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(q => q.CurrentDailyUsage, q => q.CurrentDailyUsage + estimatedTokens)
                    .SetProperty(q => q.CurrentMonthlyUsage, q => q.CurrentMonthlyUsage + estimatedTokens), ct);

            if (affectedRows == 0)
            {
                // 扣减不到不等于配额耗尽。重新读取最新值，把三种原因分开报，
                // 因为它们的处置完全不同：调额度 / 修配额记录 / 重试。
                var current = await _quotaRepository.AsQueryable()
                    .Where(q => q.UserId == userId)
                    .Select(q => new { q.CurrentDailyUsage, q.DailyTokenLimit, q.CurrentMonthlyUsage, q.MonthlyTokenLimit })
                    .FirstOrDefaultAsync(ct);

                if (current == null)
                {
                    // ★ 配额记录缺失或读不出来。报 429「配额耗尽」会把排查方向整个带偏
                    //   （去查额度配置），而真正该看的是这一行为什么不在。
                    Logger.LogError(
                        "Quota row for user {UserId} is missing right after reservation; the reservation was not recorded.",
                        userId);

                    return Fail<QuotaReservation>(
                        "Quota record is unavailable, so the reservation could not be recorded. This is not a quota limit.",
                        500, ErrorCodes.QuotaCheckFailed);
                }

                if (current.CurrentDailyUsage + estimatedTokens > current.DailyTokenLimit)
                {
                    return Fail<QuotaReservation>(
                        $"Daily quota exceeded. Current: {current.CurrentDailyUsage}, Limit: {current.DailyTokenLimit}",
                        429, ErrorCodes.QuotaExceeded);
                }

                if (current.CurrentMonthlyUsage + estimatedTokens > current.MonthlyTokenLimit)
                {
                    return Fail<QuotaReservation>(
                        $"Monthly quota exceeded. Current: {current.CurrentMonthlyUsage}, Limit: {current.MonthlyTokenLimit}",
                        429, ErrorCodes.QuotaExceeded);
                }

                // 两条限额都还有余量却扣减不到：并发方在扣减与回读之间改动了该行。可重试。
                Logger.LogWarning(
                    "Quota reservation for user {UserId} matched no row while both limits still had headroom (daily {Daily}/{DailyLimit}, monthly {Monthly}/{MonthlyLimit}).",
                    userId, current.CurrentDailyUsage, current.DailyTokenLimit, current.CurrentMonthlyUsage, current.MonthlyTokenLimit);

                return Fail<QuotaReservation>(
                    "Quota reservation lost a concurrent update. This is not a quota limit; please retry.",
                    409, ErrorCodes.QuotaConcurrencyConflict);
            }

            Logger.LogDebug(
                "Reserved {Tokens} tokens for user {UserId}",
                estimatedTokens, userId);

            // 预留是运行时唯一的「越线」时刻（QuotaMiddleware 只走这里，不走 CheckQuotaAsync）：
            // 文档承诺的 80% / 95% 预警事件必须从这条路径发出，否则宿主订阅了也一条收不到，
            // 用户直接从「无预警」跳到 429。quota 是扣减前读到的快照，据此算扣减前后的等级，
            // 只在等级上升的那一次发布。
            await PublishThresholdIfCrossedAsync(userId, quota, ProjectUsage(quota, estimatedTokens));

            return Ok(new QuotaReservation
            {
                ReservedTokens = estimatedTokens,
                ReservedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error reserving quota for user {UserId}", userId);
            return Fail<QuotaReservation>("Quota reservation failed", 500, ErrorCodes.QuotaCheckFailed);
        }
    }

    /// <summary>
    /// 结算配额：根据实际使用量调整已预留的配额（补偿差值）
    /// 使用 ExecuteUpdateAsync 绕过 ChangeTracker，避免与同请求中的 ReserveQuotaAsync 跟踪冲突
    /// </summary>
    public async Task<Result> SettleQuotaAsync(Guid userId, QuotaReservation reservation, long actualTokens, CancellationToken ct = default)
    {
        try
        {
            // 如果预留为 0（未启用配额时），无需调整
            if (reservation.ReservedTokens == 0)
            {
                return Ok();
            }

            var difference = actualTokens - reservation.ReservedTokens;
            if (difference == 0)
            {
                return Ok(); // 精确预估，无需调整
            }

            // 事务内的裸 SQL 前置：结算必须与 ReserveQuotaAsync 的扣减落在同一个事务里。
            // 预留随请求回滚而结算不回滚，会把补偿差值（通常是负数）应用到一次并未发生的
            // 预留上，用量被压到真实值以下。
            await _quotaRepository.EnsureTransactionStartedAsync(ct);

            // ★ 结算也是一次「越线」时刻：实际用量超过预估是长补全的常态，越线发生在这里时预留那次
            //   看不到（还在阈值下）；这里不投影就一条不发，而且下一次预留的「加量前」快照已经在阈值
            //   之上，事件从此被永久抑制 —— 宿主照样从「无预警」直接跳到 429。
            var before = await ReadQuotaSnapshotAsync(userId, ct);

            // 使用 ExecuteUpdateAsync 原子性补偿差值，绕过 ChangeTracker
            // SQL: SET CurrentDailyUsage = GREATEST(0, CurrentDailyUsage + @diff)
            await _quotaRepository.AsQueryable()
                .Where(q => q.UserId == userId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(q => q.CurrentDailyUsage, q => Math.Max(0, q.CurrentDailyUsage + difference))
                    .SetProperty(q => q.CurrentMonthlyUsage, q => Math.Max(0, q.CurrentMonthlyUsage + difference)), ct);

            Logger.LogDebug(
                "Settled quota for user {UserId}. Difference: {Difference} (Reserved: {Reserved}, Actual: {Actual})",
                userId, difference, reservation.ReservedTokens, actualTokens);

            if (before != null && difference > 0)
            {
                await PublishThresholdIfCrossedAsync(userId, before, ProjectUsage(before, difference));
            }

            return Ok();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error settling quota for user {UserId}", userId);
            return Fail("Quota settlement failed", 500, ErrorCodes.QuotaUpdateFailed);
        }
    }

    /// <summary>
    /// 分页查询用户配额列表
    /// </summary>
    public async Task<Result<IPagedList<UserQuotaDto>>> GetPagedListAsync(UserQuotaQueryDto query, CancellationToken ct = default)
    {
        Check.NotNull(query);

        try
        {
            var queryable = _quotaRepository
                .WhereIf(q => q.UserId == query.UserId!.Value, query.UserId.HasValue)
                .WhereIf(q => q.IsEnabled == query.IsEnabled!.Value, query.IsEnabled.HasValue)
                .OrderByDescending(q => q.CreationTime);

            var pagedList = await queryable.ProjectTo<UserQuota, UserQuotaDto>().CreateAsync(query);
            return Ok(pagedList);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error querying user quotas");
            return Fail<IPagedList<UserQuotaDto>>("Failed to query user quotas", 500, ErrorCodes.QuotaGetFailed);
        }
    }

    #region IQuotaProvider 实现

    /// <inheritdoc />
    async Task<QuotaCheckResult> IQuotaProvider.CheckAsync(Guid userId, long estimatedTokens, CancellationToken ct)
    {
        var result = await CheckQuotaAsync(userId, estimatedTokens, ct);
        return result.Data ?? QuotaCheckResult.Deny("Quota check failed");
    }

    /// <inheritdoc />
    async Task IQuotaProvider.ConsumeAsync(Guid userId, long actualTokens, CancellationToken ct)
    {
        await UpdateUsageAsync(userId, actualTokens, ct);
    }

    /// <inheritdoc />
    async Task<QuotaReservation?> IQuotaProvider.ReserveAsync(Guid userId, long estimatedTokens, CancellationToken ct)
    {
        var result = await ReserveQuotaAsync(userId, estimatedTokens, ct);
        return result.Succeeded ? result.Data : null;
    }

    /// <inheritdoc />
    async Task IQuotaProvider.SettleAsync(Guid userId, QuotaReservation reservation, long actualTokens, CancellationToken ct)
    {
        await SettleQuotaAsync(userId, reservation, actualTokens, ct);
    }

    #endregion

    /// <summary>
    /// 获取或创建用户配额（内部方法）
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>新建的配额行必须当场 flush</b>。启用事务（典型是 AspNetCore 的
    /// <c>EnableGlobalUnitOfWork</c>）时仓储默认延迟保存，插入只落在变更跟踪器里；
    /// 而 <see cref="ReserveQuotaAsync"/> 紧接着走 <c>ExecuteUpdateAsync</c>
    /// —— 那是绕过变更跟踪器直接发给数据库的 SQL，匹配不到这一行，返回受影响行数 0。
    /// 0 行被读成「配额不足」，于是<b>每个用户的首次 AI 请求必定失败，而且失败原因写着配额耗尽</b>
    /// （默认额度是每日 100 万 / 每月 2000 万 Token，全新账号「已耗尽」根本讲不通，
    /// 排查方向却被指向配额配置）。flush 之后 <c>quota.Id</c> 才被赋值也是同一条的推论。
    /// </para>
    /// <para>
    /// 并发插入竞态由 UserId 唯一索引兜底：第二个请求捕获唯一约束冲突后重新查询。
    /// </para>
    /// </remarks>
    /// <summary>加上 <paramref name="additionalTokens"/> 之后的使用率、余量与预警等级。</summary>
    private static ProjectedUsage ProjectUsage(UserQuota quota, long additionalTokens)
    {
        var remainingDaily = Math.Max(0, quota.DailyTokenLimit - quota.CurrentDailyUsage - additionalTokens);
        var remainingMonthly = Math.Max(0, quota.MonthlyTokenLimit - quota.CurrentMonthlyUsage - additionalTokens);

        var dailyPct = quota.DailyTokenLimit > 0
            ? (decimal)(quota.CurrentDailyUsage + additionalTokens) / quota.DailyTokenLimit
            : 0m;
        var monthlyPct = quota.MonthlyTokenLimit > 0
            ? (decimal)(quota.CurrentMonthlyUsage + additionalTokens) / quota.MonthlyTokenLimit
            : 0m;
        var maxPct = Math.Max(dailyPct, monthlyPct);

        var level = maxPct >= quota.CriticalThreshold
            ? QuotaWarningLevel.Critical
            : maxPct >= quota.WarningThreshold
                ? QuotaWarningLevel.Warning
                : QuotaWarningLevel.None;

        return new ProjectedUsage(dailyPct, monthlyPct, remainingDaily, remainingMonthly, level);
    }

    private readonly record struct ProjectedUsage(
        decimal DailyPct, decimal MonthlyPct, long RemainingDaily, long RemainingMonthly, QuotaWarningLevel Level);

    /// <summary>
    /// 读一份未跟踪的配额快照，供 <c>ExecuteUpdate</c> 之前算「加量前」等级；
    /// 行不存在返回 null（本轮什么也累加不上，也就没有越线可言）。
    /// </summary>
    private Task<UserQuota?> ReadQuotaSnapshotAsync(Guid userId, CancellationToken ct)
        => _quotaRepository.AsQueryable().FirstOrDefaultAsync(q => q.UserId == userId, ct);

    /// <summary>
    /// 只在预警等级<b>上升</b>（None→Warning、Warning→Critical、None→Critical）时发布
    /// <see cref="QuotaThresholdReachedEvent"/>：<paramref name="quota"/> 是加量前的快照，
    /// 已在阈值之上的后续预留不重复告警。发布失败只记日志，不影响配额主流程。
    /// </summary>
    private async Task PublishThresholdIfCrossedAsync(Guid userId, UserQuota quota, ProjectedUsage after)
    {
        if (after.Level == QuotaWarningLevel.None)
        {
            return;
        }

        var before = ProjectUsage(quota, 0).Level;
        if (after.Level <= before)
        {
            return;
        }

        try
        {
            await (EventBus?.PublishAsync(new QuotaThresholdReachedEvent
            {
                UserId = userId,
                Level = after.Level.ToString(),
                DailyUsagePercentage = after.DailyPct,
                MonthlyUsagePercentage = after.MonthlyPct,
                RemainingDailyQuota = after.RemainingDaily,
                RemainingMonthlyQuota = after.RemainingMonthly
            }) ?? Task.CompletedTask);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to publish QuotaThresholdReachedEvent for user {UserId}", userId);
        }
    }

    private async Task<UserQuota> GetOrCreateQuotaAsync(Guid userId, CancellationToken ct = default)
    {
        var existing = await _quotaRepository.AsQueryable()
            .FirstOrDefaultAsync(q => q.UserId == userId, ct);

        if (existing != null) return existing;

        var quotaOptions = _options.CurrentValue.Quota;
        var quota = new UserQuota
        {
            UserId = userId,
            DailyTokenLimit = quotaOptions.DefaultDailyTokenLimit,
            MonthlyTokenLimit = quotaOptions.DefaultMonthlyTokenLimit,
            CurrentDailyUsage = 0,
            CurrentMonthlyUsage = 0,
            LastResetDate = DateTime.UtcNow,
            IsEnabled = true
        };

        try
        {
            await _quotaRepository.InsertAsync(quota, ct);
            // 显式 flush：让这一行对后续的集合式 SQL（ExecuteUpdate）可见。
            // 事务已启用时经由 UnitOfWork 保存，因此写入仍在事务内、随请求一起回滚。
            await _quotaRepository.SaveChangesAsync(ct);
            LogInformation("Created default quota for user {UserId}", userId);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            // 并发插入竞态：另一个请求已创建该用户的配额。
            // ★ 必须 Discard：插入失败的实体仍是 Added 留在变更跟踪器里，
            //   本作用域下一次 SaveChanges 会重放它，异常落在完全无关的位置。
            _quotaRepository.Discard(quota);
            Logger.LogDebug("Quota already created by concurrent request for user {UserId}", userId);
        }

        // ★ 一律重新读回，而不是返回刚插入的那个实例。
        //   flush 过的实体是<b>被跟踪</b>的，而查得到的那条路径返回的是未跟踪副本 ——
        //   两条路径给出不同跟踪状态，调用方就得知道自己走的是哪条。
        //   更具体的坑：本类随后用 ExecuteUpdate 改这一行，被跟踪实例会就此变成过期副本，
        //   谁再改它一下（比如跨天触发 ResetQuotaIfNeeded），提交时就会把 ExecuteUpdate
        //   刚写进去的用量原样覆盖回去。
        return await _quotaRepository.AsQueryable()
            .FirstOrDefaultAsync(q => q.UserId == userId, ct)
            ?? throw new InvalidOperationException($"Failed to get or create quota for user {userId}");
    }
}
