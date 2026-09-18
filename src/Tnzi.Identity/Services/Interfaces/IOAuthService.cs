namespace Tnzi.Identity.Services;

/// <summary>
/// OAuth服务接口
/// </summary>
public interface IOAuthService
{
    /// <summary>
    /// 处理OAuth回调
    /// </summary>
    /// <param name="provider">提供者名称（Google, Microsoft等）</param>
    /// <param name="principal">ClaimsPrincipal（包含OAuth用户信息）</param>
    /// <returns>OAuth回调结果</returns>
    Task<Result<OAuthCallbackResultDto>> HandleOAuthCallbackAsync(string provider, ClaimsPrincipal principal);

    /// <summary>
    /// 关联OAuth账户到现有用户
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="provider">提供者名称</param>
    /// <param name="providerKey">提供者用户ID</param>
    /// <param name="displayName">显示名称</param>
    Task<Result> LinkOAuthAccountAsync(Guid userId, string provider, string providerKey, string? displayName = null);

    /// <summary>
    /// 个人中心发起的绑定在回调里的落地：把提供商证实的外部身份挂到<b>签发绑定令牌的那个账号</b>上。
    /// 只做关联 —— 不签发令牌、不新建账号、不按邮箱认领；provider key 已属于另一个账号时 409。
    /// </summary>
    /// <param name="userId">绑定令牌签发给的用户（<see cref="IOAuthLinkTokenService.ConsumeAsync"/> 的结果）</param>
    /// <param name="provider">提供者名称（小写）</param>
    /// <param name="principal">提供商回调的 ClaimsPrincipal</param>
    Task<Result> LinkExternalLoginAsync(Guid userId, string provider, ClaimsPrincipal principal);

    /// <summary>
    /// 解绑OAuth账户
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="provider">提供者名称</param>
    Task<Result> UnlinkOAuthAccountAsync(Guid userId, string provider);
}
