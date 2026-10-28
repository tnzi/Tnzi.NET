namespace Tnzi.Identity.Services;

/// <summary>
/// 登录会话协调器：在**签发令牌之前**同步建立登录会话，并按 <c>Identity:MultiLogin</c> 策略
/// 处理多设备/单设备/并发上限。所有令牌签发路径（密码登录、刷新令牌登录、2FA 验证、OAuth、
/// 注册后自动登录）统一经此建立会话，拿到会话ID后写入 access token 的 <c>session_id</c> claim
/// 并绑定刷新令牌，从而使会话撤销能真正令该设备下线。
/// </summary>
public interface ILoginSessionCoordinator
{
    /// <summary>
    /// 为一次成功的身份校验建立登录会话，返回新会话ID。
    /// </summary>
    /// <param name="userId">已通过身份校验的用户ID</param>
    /// <returns>
    /// 成功：新会话ID（会话服务缺失时为 <see cref="Guid.Empty"/>，表示不做会话绑定）。
    /// 失败：Reject 策略下已达上限时返回 403 失败结果（阻止本次登录）。
    /// </returns>
    Task<Result<Guid>> EstablishAsync(Guid userId);

    /// <summary>
    /// 为应用代码签发的委派会话建立会话记录（<see cref="IAuthService.IssueDelegatedSessionAsync"/>）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>不走多登录策略</b>：委派会话既不占并发名额（Reject 不会因它拒绝本人的下一次登录），
    /// 建立时也不挤掉本人的其它会话；反过来，本人的登录也不会把它挤掉。它的寿命只由自己的到期时间
    /// 与显式撤销决定。
    /// </para>
    /// <para>
    /// 默认实现返回 501 失败：自定义协调器不实现它时，委派会话一律签发不出来，而不是退化成一条普通会话。
    /// </para>
    /// </remarks>
    /// <param name="userId">会话主人</param>
    /// <param name="request">已通过 <see cref="DelegatedSessionRules.Validate"/> 的签发请求</param>
    /// <returns>新会话ID；失败时带原因</returns>
    Task<Result<Guid>> EstablishDelegatedAsync(Guid userId, DelegatedSessionRequest request)
        => Task.FromResult(Result<Guid>.Failure(
            $"{GetType().Name} cannot establish delegated sessions.", 501, ErrorCodes.CONFIGURATION_ERROR));
}
