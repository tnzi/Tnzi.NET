namespace Tnzi.AI.Services;

/// <summary>
/// Quota check service implementation.
/// </summary>
public class QuotaService : ApplicationService, IQuotaService
{
    private readonly IRepository<UserQuota, Guid> _quotaRepository;

    public QuotaService(
        IRepository<UserQuota, Guid> quotaRepository,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _quotaRepository = Check.NotNull(quotaRepository);
    }

    #region 私有辅助方法

    /// <summary>
    /// 重置配额
    /// </summary>
    private static void ResetQuotaIfNeeded(UserQuota quota)
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
        }
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

            // 重置配额（如果需要）
            ResetQuotaIfNeeded(quota);
            await _quotaRepository.UpdateAsync(quota);

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

            // 配额足够，返回剩余配额信息
            var remainingDaily = quota.DailyTokenLimit - quota.CurrentDailyUsage - estimatedTokens;
            var remainingMonthly = quota.MonthlyTokenLimit - quota.CurrentMonthlyUsage - estimatedTokens;

            return Ok(QuotaCheckResult.Allow(remainingDaily, remainingMonthly));
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
            var quota = await GetOrCreateQuotaAsync(userId, ct);

            // 增加使用量
            quota.CurrentDailyUsage += actualTokens;
            quota.CurrentMonthlyUsage += actualTokens;
            await _quotaRepository.UpdateAsync(quota);

            Logger.LogDebug("Updated quota for user {UserId}. Added {Tokens} tokens. Daily: {Daily}, Monthly: {Monthly}",
                userId, actualTokens, quota.CurrentDailyUsage, quota.CurrentMonthlyUsage);

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

            // 重置配额（如果需要）
            ResetQuotaIfNeeded(quota);
            await _quotaRepository.UpdateAsync(quota);

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
    public async Task<Result<UserQuotaDto>> SetQuotaAsync(Guid userId, long dailyLimit, long monthlyLimit, CancellationToken ct = default)
    {
        try
        {
            var quota = await _quotaRepository.AsQueryable()
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
                    IsEnabled = true
                };
                await _quotaRepository.InsertAsync(quota);

                LogInformation("Created quota for user {UserId}. Daily: {Daily}, Monthly: {Monthly}",
                    userId, dailyLimit, monthlyLimit);
            }
            else
            {
                // 更新现有配额
                quota.DailyTokenLimit = dailyLimit;
                quota.MonthlyTokenLimit = monthlyLimit;
                await _quotaRepository.UpdateAsync(quota);

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
            var quota = await _quotaRepository.AsQueryable()
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
            await _quotaRepository.UpdateAsync(quota);

            return Ok();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error resetting quota for user {UserId}", userId);
            return Fail("Quota reset failed", 500, ErrorCodes.QuotaResetFailed);
        }
    }

    /// <summary>
    /// 原子预留配额：在单次操作中检查并扣减预估 Token
    /// </summary>
    public async Task<Result<QuotaReservation>> ReserveQuotaAsync(Guid userId, long estimatedTokens, CancellationToken ct = default)
    {
        try
        {
            var quota = await GetOrCreateQuotaAsync(userId, ct);

            // 重置配额（如果需要）
            ResetQuotaIfNeeded(quota);

            // 如果未启用配额限制，直接允许
            if (!quota.IsEnabled)
            {
                // 不实际扣减，返回空预留
                await _quotaRepository.UpdateAsync(quota);
                return Ok(new QuotaReservation { ReservedTokens = 0, ReservedAt = DateTime.UtcNow });
            }

            // 检查每日和每月配额
            if (IsExceedDailyLimit(quota, estimatedTokens))
            {
                return Fail<QuotaReservation>(
                    $"Daily quota exceeded. Current: {quota.CurrentDailyUsage}, Limit: {quota.DailyTokenLimit}",
                    429, ErrorCodes.QuotaExceeded);
            }

            if (IsExceedMonthlyLimit(quota, estimatedTokens))
            {
                return Fail<QuotaReservation>(
                    $"Monthly quota exceeded. Current: {quota.CurrentMonthlyUsage}, Limit: {quota.MonthlyTokenLimit}",
                    429, ErrorCodes.QuotaExceeded);
            }

            // 原子扣减：在同一次 UpdateAsync 中完成检查和扣减
            quota.CurrentDailyUsage += estimatedTokens;
            quota.CurrentMonthlyUsage += estimatedTokens;
            await _quotaRepository.UpdateAsync(quota);

            Logger.LogDebug(
                "Reserved {Tokens} tokens for user {UserId}. Daily: {Daily}/{DailyLimit}, Monthly: {Monthly}/{MonthlyLimit}",
                estimatedTokens, userId, quota.CurrentDailyUsage, quota.DailyTokenLimit,
                quota.CurrentMonthlyUsage, quota.MonthlyTokenLimit);

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
    /// </summary>
    public async Task<Result> SettleQuotaAsync(Guid userId, QuotaReservation reservation, long actualTokens, CancellationToken ct = default)
    {
        try
        {
            // 如果预留为 0（未启用配额时），只需根据实际使用量更新
            if (reservation.ReservedTokens == 0)
            {
                return Ok();
            }

            var difference = actualTokens - reservation.ReservedTokens;
            if (difference == 0)
            {
                return Ok(); // 精确预估，无需调整
            }

            var quota = await _quotaRepository.AsQueryable()
                .FirstOrDefaultAsync(q => q.UserId == userId, ct);

            if (quota == null)
            {
                return Fail("User quota not found", 404, ErrorCodes.QuotaNotFound);
            }

            // 补偿差值：如果实际多则增加，如果实际少则减少（退还多预留的部分）
            quota.CurrentDailyUsage = Math.Max(0, quota.CurrentDailyUsage + difference);
            quota.CurrentMonthlyUsage = Math.Max(0, quota.CurrentMonthlyUsage + difference);
            await _quotaRepository.UpdateAsync(quota);

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
    /// 获取或创建用户配额（内部方法）
    /// </summary>
    private async Task<UserQuota> GetOrCreateQuotaAsync(Guid userId, CancellationToken ct = default)
    {
        var quota = await _quotaRepository.AsQueryable()
            .FirstOrDefaultAsync(q => q.UserId == userId, ct);

        if (quota == null)
        {
            // 创建默认配额
            quota = new UserQuota
            {
                UserId = userId,
                DailyTokenLimit = 100000,      // 默认每日 10 万 Token
                MonthlyTokenLimit = 3000000,   // 默认每月 300 万 Token
                CurrentDailyUsage = 0,
                CurrentMonthlyUsage = 0,
                LastResetDate = DateTime.UtcNow,
                IsEnabled = true
            };
            await _quotaRepository.InsertAsync(quota);

            LogInformation("Created default quota for user {UserId}", userId);
        }

        return quota;
    }
}
