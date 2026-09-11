namespace Tnzi.Identity.Services;

/// <summary>
/// 一次会话校验的结论。
/// </summary>
public enum SessionValidationResult
{
    /// <summary>会话有效，且请求特征与建立时一致</summary>
    Valid = 0,

    /// <summary>会话不存在、已撤销、已过期，或已超过闲置/绝对生命周期</summary>
    Invalid = 1,

    /// <summary>
    /// 会话本身还在，但请求的设备特征与建立时对不上 —— 令牌很可能已被搬到别的机器上使用。
    /// </summary>
    BindingMismatch = 2,
}

/// <summary>
/// 校验一次请求时可比对的客户端特征。
/// </summary>
/// <remarks>
/// 取的是 OWASP <i>Cookie Theft Mitigation</i> 建议留存的那几项里、框架已经在会话建立时
/// 记录下来的两项。不引入新的采集面：<see cref="UserSession.IpAddress"/> 与
/// <see cref="UserSession.UserAgent"/> 早就存着了，此前只是从来没有人拿它们比对过。
/// </remarks>
/// <param name="UserAgent">本次请求的 User-Agent（可能为 null）</param>
/// <param name="IpAddress">本次请求的来源地址（可能为 null，例如部署方关闭了地址采集）</param>
public sealed record SessionValidationContext(string? UserAgent, string? IpAddress);

/// <summary>
/// 会话管理服务接口
/// </summary>
public interface ISessionService
{
    /// <summary>
    /// 校验会话，并比对请求特征。JWT Bearer 的 <c>OnTokenValidated</c> 每请求调用。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>默认实现只回答「会话还在不在」，不做任何比对</b>，与
    /// <see cref="IsSessionValidAsync"/> 逐字等价。这是刻意的向后兼容：
    /// 自定义实现在没有实现本方法之前，行为与升级前完全一致，不会因为框架新增了
    /// 一道检查而在某个部署上突然开始把人踢下线。框架自带的两个实现都覆写了它。
    /// </para>
    /// <para>
    /// 顺带承担<b>活动时间续期</b>：闲置超时要有意义，就得有人在普通请求上更新
    /// <see cref="UserSession.LastActivityTime"/>，而这里是唯一每请求都会经过的地方。
    /// 实现应当对写入做节流（按校验缓存窗口），不要每请求一次数据库写。
    /// </para>
    /// </remarks>
    /// <param name="sessionId">会话ID</param>
    /// <param name="context">本次请求的客户端特征</param>
    async Task<SessionValidationResult> ValidateAsync(Guid sessionId, SessionValidationContext context)
        => await IsSessionValidAsync(sessionId) ? SessionValidationResult.Valid : SessionValidationResult.Invalid;

    /// <summary>
    /// 按ID取一条会话（含已撤销的）。找不到返回 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// 默认实现返回 <c>null</c>，仅为向后兼容自定义实现。框架内唯一的消费者是
    /// <see cref="ISessionRevocationService"/>，它拿这条记录只为在撤销事件里填上 UserId ——
    /// 取不到时事件的 UserId 为空，撤销动作本身不受影响。
    /// </remarks>
    Task<UserSessionDto?> GetSessionAsync(Guid sessionId) => Task.FromResult<UserSessionDto?>(null);

    /// <summary>
    /// 创建会话
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="deviceInfo">设备信息</param>
    /// <param name="ipAddress">IP地址</param>
    /// <param name="userAgent">UserAgent</param>
    /// <param name="expiresAt">会话硬过期时间（绑定刷新令牌生命周期）；null 表示不过期（遗留语义）。Redis 模式据此设置记录 TTL。</param>
    /// <returns>会话ID</returns>
    Task<Guid> CreateSessionAsync(Guid userId, string? deviceInfo, string? ipAddress, string? userAgent, DateTime? expiresAt = null);

    /// <summary>
    /// 校验会话是否仍然有效（存在、未撤销、未过期）。
    /// 供 JWT Bearer 的 <c>OnTokenValidated</c> 每请求校验、以及刷新令牌时校验；
    /// 实现应对高频调用做缓存（数据库模式）或直接读缓存（Redis 模式）。
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <returns>有效返回 true；不存在/已撤销/已过期返回 false</returns>
    Task<bool> IsSessionValidAsync(Guid sessionId);

    /// <summary>
    /// 获取用户的所有会话
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="includeRevoked">是否包含已撤销的会话</param>
    /// <returns>会话列表</returns>
    Task<Result<IEnumerable<UserSessionDto>>> GetUserSessionsAsync(Guid userId, bool includeRevoked = false);

    /// <summary>
    /// 分页查询会话列表 - UserId 可选；不传时返回全局会话列表（按最后活动时间倒序），
    /// 返回的 DTO 含 UserName（批量关联用户表）。
    /// </summary>
    /// <param name="query">查询条件（UserId 可选 + IncludeRevoked + 分页）</param>
    /// <returns>分页会话列表</returns>
    Task<Result<IPagedList<UserSessionDto>>> GetSessionsAsync(SessionQueryDto query);

    /// <summary>
    /// 撤销会话
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    Task<Result> RevokeSessionAsync(Guid sessionId);

    /// <summary>
    /// 撤销用户的所有会话
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="excludeSessionId">排除的会话ID（可选）</param>
    Task<Result> RevokeAllSessionsAsync(Guid userId, Guid? excludeSessionId = null);

    /// <summary>
    /// 更新会话活动时间
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    Task<Result> UpdateActivityTimeAsync(Guid sessionId);

    /// <summary>
    /// 续期会话：刷新令牌轮换时调用，滑动延长会话硬过期时间并更新活动时间，
    /// 使活跃用户的会话随刷新持续存活（否则会话在首登录 + 刷新令牌生命周期后被判定失效）。
    /// 会话不存在或已撤销时不生效。
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <param name="expiresAt">新的硬过期时间</param>
    Task<Result> RenewSessionAsync(Guid sessionId, DateTime expiresAt);

    /// <summary>
    /// 清理超过指定时间未活跃的会话
    /// </summary>
    /// <param name="inactiveThreshold">不活跃时间阈值</param>
    /// <returns>清理的会话数量</returns>
    Task<Result<int>> CleanExpiredSessionsAsync(TimeSpan inactiveThreshold);

    /// <summary>
    /// 同 <see cref="CleanExpiredSessionsAsync"/>，但返回<b>被撤销的会话ID</b>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 存在的理由是调用方还得做后半件事：撤销一条会话必须连带删掉绑定其上的刷新令牌，
    /// 而只拿到一个数字是删不了的。<see cref="ISessionRevocationService.RevokeInactiveSessionsAsync"/>
    /// 用它把两半合起来。
    /// </para>
    /// <para>
    /// ★ 默认实现委托给 <see cref="CleanExpiredSessionsAsync"/> 并返回空集合：自定义实现在
    /// 覆写它之前，会话照常被撤销（行为不变），只是令牌要等自然过期才被清掉 ——
    /// 少清理，不会少撤销。框架自带的两个实现都覆写了它。
    /// </para>
    /// </remarks>
    /// <param name="inactiveThreshold">不活跃时间阈值</param>
    async Task<Result<IReadOnlyCollection<Guid>>> CleanInactiveSessionsAsync(TimeSpan inactiveThreshold)
    {
        var result = await CleanExpiredSessionsAsync(inactiveThreshold);
        return result.Succeeded
            ? Result<IReadOnlyCollection<Guid>>.Success(Array.Empty<Guid>())
            : Result<IReadOnlyCollection<Guid>>.Failure(result.Message ?? "Cleanup failed", result.Code ?? 500);
    }

    /// <summary>
    /// 获取会话统计信息（活跃会话数、在线用户数、设备分布 Top 5）
    /// </summary>
    /// <returns>会话统计信息</returns>
    Task<Result<SessionStatisticsDto>> GetSessionStatisticsAsync();

    /// <summary>
    /// 获取活跃用户列表（按最后活跃时间倒序，含会话计数和用户名），用于 admin "活跃用户"下拉
    /// </summary>
    /// <param name="top">返回的最大用户数（默认 50）</param>
    /// <returns>活跃用户摘要列表</returns>
    Task<Result<IEnumerable<ActiveUserSummaryDto>>> GetActiveUsersAsync(int top = 50);
}
