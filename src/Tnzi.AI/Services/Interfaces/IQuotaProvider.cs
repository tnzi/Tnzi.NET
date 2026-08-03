namespace Tnzi.AI.Services;

/// <summary>
/// 配额提供者抽象 - 可插拔的配额策略
/// </summary>
/// <remarks>
/// 默认实现由 QuotaService 提供（数据库配额管理）。
/// 用户可注册自定义实现（按 API Key、按组织、按金额等策略）。
/// 通过 TryAdd 注册，用户可替换默认实现。
/// </remarks>
public interface IQuotaProvider
{
    /// <summary>
    /// 检查用户配额是否足够
    /// </summary>
    /// <param name="userId">用户 ID</param>
    /// <param name="estimatedTokens">预估 Token 数量</param>
    /// <param name="ct">取消令牌</param>
    Task<QuotaCheckResult> CheckAsync(Guid userId, long estimatedTokens, CancellationToken ct = default);

    /// <summary>
    /// 消费配额（直接扣减）
    /// </summary>
    /// <param name="userId">用户 ID</param>
    /// <param name="actualTokens">实际使用的 Token 数量</param>
    /// <param name="ct">取消令牌</param>
    Task ConsumeAsync(Guid userId, long actualTokens, CancellationToken ct = default);

    /// <summary>
    /// 预留配额（原子检查+扣减）
    /// </summary>
    /// <param name="userId">用户 ID</param>
    /// <param name="estimatedTokens">预估 Token 数量</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>预留信息，配额不足返回 null</returns>
    Task<QuotaReservation?> ReserveAsync(Guid userId, long estimatedTokens, CancellationToken ct = default);

    /// <summary>
    /// 结算配额（根据实际使用量调整预留差值）
    /// </summary>
    /// <param name="userId">用户 ID</param>
    /// <param name="reservation">预留信息</param>
    /// <param name="actualTokens">实际使用的 Token 数量</param>
    /// <param name="ct">取消令牌</param>
    Task SettleAsync(Guid userId, QuotaReservation reservation, long actualTokens, CancellationToken ct = default);
}
