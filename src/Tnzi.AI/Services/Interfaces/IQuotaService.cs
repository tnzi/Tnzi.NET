namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// 配额检查服务接口
/// </summary>
public interface IQuotaService
{
    /// <summary>
    /// 检查用户配额是否足够
    /// </summary>
    /// <param name="userId">用户 ID</param>
    /// <param name="estimatedTokens">预估的 Token 数量</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>配额检查结果</returns>
    Task<Result<QuotaCheckResult>> CheckQuotaAsync(Guid userId, long estimatedTokens, CancellationToken ct = default);

    /// <summary>
    /// 更新用户配额使用量
    /// </summary>
    /// <param name="userId">用户 ID</param>
    /// <param name="actualTokens">实际使用的 Token 数量</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>更新结果</returns>
    Task<Result> UpdateUsageAsync(Guid userId, long actualTokens, CancellationToken ct = default);

    /// <summary>
    /// 获取用户配额信息
    /// </summary>
    /// <param name="userId">用户 ID</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>配额信息</returns>
    Task<Result<UserQuotaDto>> GetQuotaAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// 创建或更新用户配额
    /// </summary>
    /// <param name="userId">用户 ID</param>
    /// <param name="dailyLimit">每日限额</param>
    /// <param name="monthlyLimit">每月限额</param>
    /// <param name="warningThreshold">预警阈值（0-1，null 保持默认 0.8）</param>
    /// <param name="criticalThreshold">严重预警阈值（0-1，null 保持默认 0.95）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>配额信息</returns>
    Task<Result<UserQuotaDto>> SetQuotaAsync(Guid userId, long dailyLimit, long monthlyLimit, decimal? warningThreshold = null, decimal? criticalThreshold = null, CancellationToken ct = default);

    /// <summary>
    /// 重置用户配额
    /// </summary>
    /// <param name="userId">用户 ID</param>
    /// <param name="resetDaily">是否重置每日配额</param>
    /// <param name="resetMonthly">是否重置每月配额</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>操作结果</returns>
    Task<Result> ResetQuotaAsync(Guid userId, bool resetDaily, bool resetMonthly, CancellationToken ct = default);

    /// <summary>
    /// 原子预留配额：在单次操作中检查并扣减预估 Token
    /// </summary>
    /// <param name="userId">用户 ID</param>
    /// <param name="estimatedTokens">预估 Token 数量</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>预留结果（含预留的 Token 数）</returns>
    Task<Result<QuotaReservation>> ReserveQuotaAsync(Guid userId, long estimatedTokens, CancellationToken ct = default);

    /// <summary>
    /// 结算配额：根据实际使用量调整已预留的配额（补偿差值）
    /// </summary>
    /// <param name="userId">用户 ID</param>
    /// <param name="reservation">预留信息</param>
    /// <param name="actualTokens">实际使用的 Token 数量</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>操作结果</returns>
    Task<Result> SettleQuotaAsync(Guid userId, QuotaReservation reservation, long actualTokens, CancellationToken ct = default);
}

/// <summary>
/// 配额预警级别
/// </summary>
public enum QuotaWarningLevel
{
    /// <summary>配额充足</summary>
    None = 0,
    /// <summary>配额即将用尽（达到 WarningThreshold）</summary>
    Warning = 1,
    /// <summary>配额紧急（达到 CriticalThreshold）</summary>
    Critical = 2
}

/// <summary>
/// 配额检查结果
/// </summary>
public class QuotaCheckResult
{
    /// <summary>
    /// 是否允许继续（配额足够）
    /// </summary>
    public bool IsAllowed { get; set; }

    /// <summary>
    /// 拒绝原因
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// 剩余每日配额
    /// </summary>
    public long RemainingDailyQuota { get; set; }

    /// <summary>
    /// 剩余每月配额
    /// </summary>
    public long RemainingMonthlyQuota { get; set; }

    /// <summary>
    /// 每日使用率（0-1）
    /// </summary>
    public decimal DailyUsagePercentage { get; set; }

    /// <summary>
    /// 每月使用率（0-1）
    /// </summary>
    public decimal MonthlyUsagePercentage { get; set; }

    /// <summary>
    /// 预警级别（基于使用率与阈值比较）
    /// </summary>
    public QuotaWarningLevel WarningLevel { get; set; }

    /// <summary>
    /// 创建允许结果
    /// </summary>
    public static QuotaCheckResult Allow(long remainingDaily, long remainingMonthly, decimal dailyUsagePct = 0, decimal monthlyUsagePct = 0, QuotaWarningLevel warningLevel = QuotaWarningLevel.None)
    {
        return new QuotaCheckResult
        {
            IsAllowed = true,
            RemainingDailyQuota = remainingDaily,
            RemainingMonthlyQuota = remainingMonthly,
            DailyUsagePercentage = dailyUsagePct,
            MonthlyUsagePercentage = monthlyUsagePct,
            WarningLevel = warningLevel
        };
    }

    /// <summary>
    /// 创建拒绝结果
    /// </summary>
    public static QuotaCheckResult Deny(string reason)
    {
        return new QuotaCheckResult
        {
            IsAllowed = false,
            Reason = reason,
            RemainingDailyQuota = 0,
            RemainingMonthlyQuota = 0,
            DailyUsagePercentage = 1,
            MonthlyUsagePercentage = 1,
            WarningLevel = QuotaWarningLevel.Critical
        };
    }
}

/// <summary>
/// 配额预留信息
/// </summary>
public class QuotaReservation
{
    /// <summary>预留的 Token 数量</summary>
    public long ReservedTokens { get; set; }

    /// <summary>预留时间</summary>
    public DateTime ReservedAt { get; set; }
}
