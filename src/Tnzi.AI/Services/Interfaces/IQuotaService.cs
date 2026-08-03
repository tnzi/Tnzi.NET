namespace Tnzi.AI.Services;

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

    /// <summary>
    /// 分页查询用户配额列表
    /// </summary>
    /// <param name="query">查询参数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>分页结果</returns>
    Task<Result<IPagedList<UserQuotaDto>>> GetPagedListAsync(UserQuotaQueryDto query, CancellationToken ct = default);
}
