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
            var remainingDaily = Math.Max(0, quota.DailyTokenLimit - quota.CurrentDailyUsage - estimatedTokens);
            var remainingMonthly = Math.Max(0, quota.MonthlyTokenLimit - quota.CurrentMonthlyUsage - estimatedTokens);

            var dailyPct = quota.DailyTokenLimit > 0
                ? (decimal)(quota.CurrentDailyUsage + estimatedTokens) / quota.DailyTokenLimit
                : 0m;
            var monthlyPct = quota.MonthlyTokenLimit > 0
                ? (decimal)(quota.CurrentMonthlyUsage + estimatedTokens) / quota.MonthlyTokenLimit
                : 0m;
            var maxPct = Math.Max(dailyPct, monthlyPct);

            var warningLevel = maxPct >= quota.CriticalThreshold
                ? QuotaWarningLevel.Critical
                : maxPct >= quota.WarningThreshold
                    ? QuotaWarningLevel.Warning
                    : QuotaWarningLevel.None;

            // 命中预警/严重阈值时发布事件（静默失败，不影响配额检查主流程）
            if (warningLevel != QuotaWarningLevel.None)
            {
                try
                {
                    await (EventBus?.PublishAsync(new QuotaThresholdReachedEvent
                    {
                        UserId = userId,
                        Level = warningLevel.ToString(),
                        DailyUsagePercentage = dailyPct,
                        MonthlyUsagePercentage = monthlyPct,
                        RemainingDailyQuota = remainingDaily,
                        RemainingMonthlyQuota = remainingMonthly
                    }) ?? Task.CompletedTask);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to publish QuotaThresholdReachedEvent for user {UserId}", userId);
                }
            }

            return Ok(QuotaCheckResult.Allow(remainingDaily, remainingMonthly, dailyPct, monthlyPct, warningLevel));
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
            await _quotaRepository.AsQueryable()
                .Where(q => q.UserId == userId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(q => q.CurrentDailyUsage, q => q.CurrentDailyUsage + actualTokens)
                    .SetProperty(q => q.CurrentMonthlyUsage, q => q.CurrentMonthlyUsage + actualTokens), ct);

            Logger.LogDebug("Updated quota for user {UserId}. Added {Tokens} tokens", userId, actualTokens);
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
                // 重新读取最新值以提供准确的错误信息
                var current = await _quotaRepository.AsQueryable()
                    .Where(q => q.UserId == userId)
                    .Select(q => new { q.CurrentDailyUsage, q.DailyTokenLimit, q.CurrentMonthlyUsage, q.MonthlyTokenLimit })
                    .FirstOrDefaultAsync(ct);

                if (current != null && current.CurrentDailyUsage + estimatedTokens > current.DailyTokenLimit)
                {
                    return Fail<QuotaReservation>(
                        $"Daily quota exceeded. Current: {current.CurrentDailyUsage}, Limit: {current.DailyTokenLimit}",
                        429, ErrorCodes.QuotaExceeded);
                }

                return Fail<QuotaReservation>(
                    $"Monthly quota exceeded. Current: {current?.CurrentMonthlyUsage}, Limit: {current?.MonthlyTokenLimit}",
                    429, ErrorCodes.QuotaExceeded);
            }

            Logger.LogDebug(
                "Reserved {Tokens} tokens for user {UserId}",
                estimatedTokens, userId);

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
    /// 使用 try/catch 处理并发插入竞态：两个请求同时发现 null 并尝试插入时，
    /// 第二个请求捕获唯一约束异常后重新查询
    /// </summary>
    private async Task<UserQuota> GetOrCreateQuotaAsync(Guid userId, CancellationToken ct = default)
    {
        var quota = await _quotaRepository.AsQueryable()
            .FirstOrDefaultAsync(q => q.UserId == userId, ct);

        if (quota != null) return quota;

        var quotaOptions = _options.CurrentValue.Quota;
        quota = new UserQuota
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
            LogInformation("Created default quota for user {UserId}", userId);
        }
        catch (DbUpdateException)
        {
            // 并发插入竞态：另一个请求已创建该用户的配额，重新查询
            quota = await _quotaRepository.AsQueryable()
                .FirstOrDefaultAsync(q => q.UserId == userId, ct)
                ?? throw new InvalidOperationException($"Failed to get or create quota for user {userId}");
            Logger.LogDebug("Quota already created by concurrent request for user {UserId}", userId);
        }

        return quota;
    }
}
