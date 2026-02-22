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
    /// 解绑OAuth账户
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="provider">提供者名称</param>
    Task<Result> UnlinkOAuthAccountAsync(Guid userId, string provider);
}
