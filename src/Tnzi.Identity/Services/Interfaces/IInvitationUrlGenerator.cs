namespace Tnzi.Identity.Services;

/// <summary>
/// 邀请链接的拼装方式。消费应用可实现此接口来完全接管。
/// </summary>
/// <remarks>
/// 与 <see cref="IResetPasswordUrlGenerator"/> 同一形状、同一理由：接受邀请的页面
/// 住在消费应用的前端里，它的路径、查询参数名、是否带租户前缀，框架无从知道。
/// 默认实现按 <c>Identity:Invitation:AcceptUrlTemplate</c> 或 <c>App:FrontendUrl</c> 拼，
/// 覆盖不了的情况就换掉整个实现。
/// </remarks>
public interface IInvitationUrlGenerator
{
    /// <summary>
    /// 生成接受邀请的完整链接。
    /// </summary>
    /// <param name="user">被邀请的账号。</param>
    /// <param name="token">邀请令牌<b>明文</b>（此后不再可读，只有这一次机会把它放进链接）。</param>
    /// <returns>可直接发给被邀请人的绝对地址。</returns>
    string GenerateUrl(User user, string token);
}
