

namespace Tnzi.Identity.Services;

/// <summary>
/// JWT令牌服务接口：只管令牌的<b>构造</b>（访问令牌、刷新令牌的随机值），不签发登录。
/// </summary>
/// <remarks>
/// ★ 要为一次自定义登录发令牌，走 <see cref="IAuthService.IssueTokenAsync"/>：那是唯一的签发出口，
/// 登录守卫、2FA 判定、会话、落库的刷新令牌与登录事件都在那条路上。直接拿这里的两个方法拼出来的令牌
/// 没有会话、刷新令牌也没存，刷新时一律被拒。
/// </remarks>
public interface ITokenService
{
    /// <summary>
    /// 生成JWT令牌
    /// </summary>
    /// <param name="user">令牌主体用户</param>
    /// <param name="roles">用户角色名列表</param>
    /// <param name="extraClaims">可选的自定义 claim（如桥接登录的 ai_roles/user_type 等）；与框架保留类型（subject/name/jti/role/tenant_id/session_id）冲突的项会被忽略</param>
    /// <param name="sessionId">可选的登录会话ID；非空且非 <see cref="Guid.Empty"/> 时写入受保护的 <c>session_id</c> claim，供服务端每请求校验会话有效性（撤销即踢下线）</param>
    string GenerateToken(User user, IList<string> roles, IEnumerable<Claim>? extraClaims = null, Guid? sessionId = null);

    /// <summary>
    /// 生成JWT令牌，且令牌的过期时间不晚于 <paramref name="notAfter"/>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 委派会话（<see cref="IAuthService.IssueDelegatedSessionAsync"/>）用它：那条会话有一个刷新也推不动的绝对到期时间，
    /// 按部署配置的 access token 寿命签出的令牌可能比它活得更久。
    /// </para>
    /// <para>
    /// ★ 默认实现<b>抛 <see cref="NotSupportedException"/></b>，不退回到不设上限的那个重载：
    /// 静默忽略 <paramref name="notAfter"/> 会签出一枚比会话活得更久的令牌，而调用方看不出来。
    /// 自定义 <see cref="ITokenService"/> 不覆写它时，委派会话签发一律失败，普通登录不受影响。
    /// </para>
    /// </remarks>
    /// <param name="user">令牌主体用户</param>
    /// <param name="roles">用户角色名列表</param>
    /// <param name="extraClaims">可选的自定义 claim，规则同另一个重载</param>
    /// <param name="sessionId">登录会话ID</param>
    /// <param name="notAfter">过期时间的上限（UTC）；为 null 时与另一个重载相同</param>
    string GenerateToken(User user, IList<string> roles, IEnumerable<Claim>? extraClaims, Guid? sessionId, DateTime? notAfter)
        => throw new NotSupportedException(
            $"{GetType().Name} does not support capping the lifetime of an access token, which delegated sessions require.");

    /// <summary>
    /// 生成刷新令牌
    /// </summary>
    string GenerateRefreshToken();

}

/// <summary>
/// 令牌结果
/// </summary>
public class TokenResult : IRefreshTokenCarrier
{
    /// <summary>
    /// 访问令牌
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// 刷新令牌
    /// </summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>
    /// 过期时间
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// 过期秒数
    /// </summary>
    public int ExpiresIn { get; set; }

    /// <summary>
    /// 刷新令牌过期秒数（如果未启用RefreshToken则为null）
    /// </summary>
    public int? RefreshTokenExpiresIn { get; set; }

    /// <summary>
    /// 是否需要邮箱确认（注册成功但需要确认邮箱时为 true）
    /// 当此值为 true 时，AccessToken 和 RefreshToken 为空
    /// </summary>
    public bool RequireEmailConfirmation { get; set; }

    /// <summary>
    /// 用户邮箱（用于前端显示提示信息，仅当 RequireEmailConfirmation=true 时有值）
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// 用户ID（用于重发确认邮件等操作，仅当 RequireEmailConfirmation=true 时有值）
    /// </summary>
    public Guid? UserId { get; set; }

    /// <inheritdoc />
    string? IRefreshTokenCarrier.ReadRefreshToken() => RefreshToken;

    /// <inheritdoc />
    int? IRefreshTokenCarrier.ReadRefreshTokenLifetimeSeconds() => RefreshTokenExpiresIn;

    /// <inheritdoc />
    void IRefreshTokenCarrier.ClearRefreshToken() => RefreshToken = string.Empty;
}
