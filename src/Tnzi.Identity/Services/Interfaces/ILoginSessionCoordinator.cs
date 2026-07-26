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
}
