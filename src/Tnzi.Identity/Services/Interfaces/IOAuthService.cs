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
    /// 关联OAuth账户到现有用户。provider key 已属于另一个账号时 409，两边都不动。
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="provider">提供者名称</param>
    /// <param name="providerKey">提供者用户ID</param>
    /// <param name="displayName">显示名称</param>
    Task<Result> LinkOAuthAccountAsync(Guid userId, string provider, string providerKey, string? displayName = null);

    /// <summary>
    /// 把一个提供商证实的外部身份挂到 <paramref name="userId"/> 上。
    /// 只做关联 —— 不签发令牌、不新建账号、不按邮箱认领；provider key 已属于另一个账号时 409。
    /// </summary>
    /// <remarks>
    /// ⚠ 调用方负责证明 <paramref name="userId"/> 就是正在完成这次授权的人。个人中心的绑定流程
    /// 因此不在匿名回调里调它，而是把外部身份暂存（<see cref="IOAuthLinkTokenService.StagePendingLinkAsync"/>），
    /// 等发起绑定的会话回来确认。
    /// </remarks>
    /// <param name="userId">要挂到的账号</param>
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
