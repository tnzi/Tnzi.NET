namespace Tnzi.Identity.Services;

/// <summary>
/// 个人中心「绑定第三方账号」的一次性绑定令牌：由已登录用户签发、绑定本人与提供商、短期、消费即失效。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>为什么需要它。</b>OAuth 的发起端点与回调端点都是 <c>[AllowAnonymous]</c> 的整页跳转，
/// 请求上没有 bearer，回调里连「谁在绑定」都无从知道。此前个人中心的「绑定」按钮直接重走匿名登录流程，
/// 回调从头到尾不读当前用户：第三方邮箱与本站不同（企业邮箱 vs 私人邮箱，常态）时凭空建出一个
/// 无密码无角色的孤儿账号、给它签发令牌，并让那个 provider key 从此归它所有。
/// </para>
/// <para>
/// 令牌把「当前用户是谁」带过这两次跳转：前端先在<b>已认证</b>的端点上取令牌，再把它作为 <c>linkToken</c>
/// 查询参数发起 OAuth；发起端点校验（不消费）后把它写进 <c>AuthenticationProperties.Items</c>，
/// 经 <c>Identity.External</c> cookie 签名往返到回调；回调消费它并把外部登录挂到签发它的账号上，
/// 不签发令牌、不新建账号。与 passkey 注册令牌同一形状（借 <c>AuthToken</c> 表存哈希，零迁移）。
/// </para>
/// </remarks>
public interface IOAuthLinkTokenService
{
    /// <summary>为当前用户签发一枚只能用于绑定 <paramref name="provider"/> 的令牌。</summary>
    /// <param name="userId">签发给谁（必须是已认证的当前用户）</param>
    /// <param name="provider">提供商名（不区分大小写，按小写存）</param>
    Task<Result<OAuthLinkTokenDto>> IssueAsync(Guid userId, string provider);

    /// <summary>
    /// 校验但不消费：令牌存在、未用、未过期且提供商匹配时返回签发它的用户。
    /// 发起端点用它把配错的请求当场拒掉（400），而不是让用户走完提供商同意页才失败。
    /// </summary>
    Task<Result<Guid>> PeekAsync(string token, string provider);

    /// <summary>
    /// 消费：条件同 <see cref="PeekAsync"/>，成功即作废（并发下只有一次成功）。回调端点用它。
    /// </summary>
    Task<Result<Guid>> ConsumeAsync(string token, string provider);
}
