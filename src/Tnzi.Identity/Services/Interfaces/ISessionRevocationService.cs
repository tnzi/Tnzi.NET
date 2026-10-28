namespace Tnzi.Identity.Services;

/// <summary>
/// 撤销一条会话的原因。写进日志与 <see cref="Events.SessionsRevokedEvent"/>，
/// 使「谁把这些人踢下线的」在事后查得到。
/// </summary>
public enum SessionRevocationReason
{
    /// <summary>用户主动登出当前设备</summary>
    Logout = 0,

    /// <summary>用户在个人中心主动登出其它设备</summary>
    UserRequested = 1,

    /// <summary>管理员在会话管理里撤销</summary>
    AdminRevoked = 2,

    /// <summary>账号被停用 / 注销</summary>
    AccountDisabled = 3,

    /// <summary>账号被锁定</summary>
    AccountLocked = 4,

    /// <summary>账号被删除</summary>
    AccountDeleted = 5,

    /// <summary>用户修改了密码</summary>
    PasswordChanged = 6,

    /// <summary>密码被重置（自助找回或管理员重置）</summary>
    PasswordReset = 7,

    /// <summary>检测到刷新令牌重放</summary>
    RefreshTokenReuse = 8,

    /// <summary>请求的设备特征与会话建立时不一致</summary>
    BindingMismatch = 9,

    /// <summary>刷新时被登录守卫否决（账号已不具备登录资格）</summary>
    GuardDenied = 10,

    /// <summary>
    /// 被多登录策略挤掉（<c>Identity:MultiLogin:OnConflict = Replace</c>：单设备登录，
    /// 或已达 <c>MaxConcurrentSessions</c> 后踢掉最旧的那条）。
    /// </summary>
    MultiLoginReplaced = 11,

    /// <summary>
    /// 应用按分组撤销了它签发的委派会话（<see cref="ISessionRevocationService.RevokeSessionGroupAsync"/>），
    /// 典型原因是用户撤销了换取它的口令、或口令到期。
    /// </summary>
    DelegationRevoked = 12,
}

/// <summary>
/// 会话撤销的<b>唯一出口</b>：撤销会话记录，并连带删除绑定在这些会话上的刷新令牌。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <b>存在的理由是这两件事从来没有被同时做过。</b>「撤销会话」与「让刷新令牌失效」
/// 在此前是两个分开的动作，而调用方一律只做了前者：
/// <c>UserService</c> 停用账号、<c>PasswordService</c> 改密码、个人中心「登出所有设备」，
/// 三处都只改了会话行的 <c>IsRevoked</c>，刷新令牌原样留在库里。
/// 这在 <c>Identity:Session:EnforceSessionValidation</c> 开着时侥幸没出事
/// （刷新会先查会话），一旦那个逃生开关被关掉，全部撤销动作<b>一起变成装饰</b>，
/// 而外观、日志、接口返回没有任何区别。
/// </para>
/// <para>
/// ★ 收口成一个服务而不是在每处各写两行，判据与 <see cref="LockedAccountLoginGuard"/> 同源：
/// 逐处修补挡不住<b>下一个</b>调用点出现，而「撤销」这个动作以后一定还会有新的触发者
/// （风控、合规、租户停用）。一处实现覆盖全部，新调用点自动拿到正确语义。
/// </para>
/// <para>
/// ★ 返回值是<b>受影响的会话数</b>而不是 <c>Result</c>：调用方几乎都在别的动作
/// （停用账号、改密码）成功之后顺带调用，撤销失败不应把主动作也判失败，但调用方
/// 需要知道「到底有没有踢到人」以便写日志。
/// </para>
/// </remarks>
public interface ISessionRevocationService
{
    /// <summary>
    /// 撤销单条会话，并删除绑定其上的刷新令牌。
    /// </summary>
    /// <param name="sessionId">会话ID；<see cref="Guid.Empty"/> 直接返回 0</param>
    /// <param name="reason">撤销原因</param>
    /// <returns>实际撤销的会话数（0 或 1）</returns>
    Task<int> RevokeSessionAsync(Guid sessionId, SessionRevocationReason reason);

    /// <summary>
    /// 撤销某用户的全部会话，并删除绑定其上的刷新令牌。
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="reason">撤销原因</param>
    /// <param name="excludeSessionId">
    /// 保留的会话（一般是发起本次操作的当前会话）。
    /// ★ 这个参数是 ASVS 7.4.3「terminate all <b>other</b> active sessions」能否被表达的关键 ——
    /// 缺了它，「改完密码把别的设备踢掉」就只能实现成连自己一起踢，
    /// 而一个会把自己也登出的按钮，用户是不会去点的。
    /// </param>
    /// <returns>实际撤销的会话数</returns>
    Task<int> RevokeUserSessionsAsync(Guid userId, SessionRevocationReason reason, Guid? excludeSessionId = null);

    /// <summary>
    /// 撤销某用户名下分组键为 <paramref name="groupKey"/> 的全部委派会话，并删除绑定其上的刷新令牌。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 委派会话由应用代码签发（<see cref="IAuthService.IssueDelegatedSessionAsync"/>）；撤销它们是应用的事，
    /// 这里是那件事的出口。按用户限定范围：分组键只在一个用户之内有意义，而按用户能沿用会话服务现成的索引
    /// （Redis 模式下没有按分组的索引）与租户范围判断。
    /// </para>
    /// <para>
    /// ★ 生效时刻：带 <c>session_id</c> 的 access token 在下一个请求上就过不了每请求会话校验
    /// （<c>Identity:Session:EnforceSessionValidation</c>，默认开）；数据库会话模式下其它实例最多滞后
    /// <c>ValidationCacheSeconds</c>（默认 30 秒），本实例即时。刷新令牌在这里一并删除。
    /// 已经建立的 SignalR 连接只在连接时认证一次，撤销不会断开它 —— 订阅
    /// <see cref="Events.SessionsRevokedEvent"/>（带 <see cref="Events.SessionsRevokedEvent.GroupKey"/>）自己断开。
    /// </para>
    /// <para>
    /// ★ 与签发并发时：枚举之后才建出来的那条会话不在这一次撤销之内。应用应当<b>先</b>把自己的口令记录
    /// 标成已撤销（之后的签发与每请求校验都会在应用那一侧失败），<b>再</b>调用本方法。
    /// </para>
    /// <para>
    /// 默认实现抛 <see cref="NotSupportedException"/>：自定义实现不支持时调用方必须知道撤销没有发生，
    /// 而一个返回 0 的默认实现读起来与「本来就没有会话」一样。
    /// </para>
    /// </remarks>
    /// <param name="userId">会话主人</param>
    /// <param name="groupKey">签发时给的分组键（按序数比较）</param>
    /// <param name="reason">撤销原因，一般是 <see cref="SessionRevocationReason.DelegationRevoked"/></param>
    /// <returns>实际撤销的会话数</returns>
    Task<int> RevokeSessionGroupAsync(Guid userId, string groupKey, SessionRevocationReason reason)
        => throw new NotSupportedException($"{GetType().Name} cannot revoke delegated session groups.");

    /// <summary>
    /// 清扫长期失活的会话：撤销它们，并删除绑定其上的刷新令牌。供后台维护任务调用。
    /// </summary>
    /// <remarks>
    /// ★ 后台清扫也必须走这个出口。它此前只调 <c>ISessionService.CleanExpiredSessionsAsync</c>，
    /// 而那个方法只改会话行 —— 阈值等于刷新令牌周期时侥幸无害（会话失活的同时令牌也刚好过期，
    /// 由清理过期令牌那一步收走），但阈值一旦收窄到闲置超时，就会造出一批
    /// 「会话已撤销、刷新令牌还活着」的记录，最长可达一个刷新令牌周期。
    /// </remarks>
    /// <param name="inactiveThreshold">不活跃时间阈值</param>
    /// <returns>被撤销的会话数</returns>
    Task<int> RevokeInactiveSessionsAsync(TimeSpan inactiveThreshold);
}
