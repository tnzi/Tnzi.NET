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
/// 经 <c>Identity.External</c> cookie 签名往返到回调；回调消费它，但<b>不在那里关联</b>。
/// 与 passkey 注册令牌同一形状（借 <c>AuthToken</c> 表存哈希，零迁移）。
/// </para>
/// <para>
/// ★★★ <b>回调只暂存，关联要回到发起绑定的那个会话里完成。</b>令牌是一个能交给任何人的 URL 参数，
/// 回调又是匿名的：攻击者给自己签一枚令牌，把 <c>auth/oauth/github/login?linkToken=…</c> 发给受害者，
/// 受害者在 GitHub 那边早已授权过、整个流程静默走完，受害者的 GitHub 身份就挂到了攻击者账号上：
/// 此后受害者「用 GitHub 登录」登进的是攻击者的账号，在里面留下的东西攻击者都看得到。
/// 所以回调把提供商证实的外部身份暂存在签发令牌的账号名下（<see cref="StagePendingLinkAsync"/>），
/// 换出一枚一次性的完成码交回浏览器；前端再以<b>已认证</b>身份调完成端点（<see cref="ConsumePendingLinkAsync"/>），
/// 只有当前登录的就是签发令牌的那个账号时才真正关联。受害者的浏览器里要么没有会话（什么都完成不了），
/// 要么登录的是受害者自己（对不上，拒绝）。
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

    /// <summary>
    /// 回调端点用：把提供商证实的外部身份暂存在 <paramref name="userId"/>（签发绑定令牌的账号）名下，
    /// 返回一枚一次性的完成码。<b>此时还没有关联任何东西。</b>
    /// </summary>
    /// <remarks>
    /// 完成码短期有效、库里只存哈希；同一账号对同一提供商同时只有一份暂存，再暂存一次就顶掉上一份。
    /// 提供商回调里取不到用户标识时 400。
    /// </remarks>
    /// <param name="userId">签发绑定令牌的账号（<see cref="ConsumeAsync"/> 的结果）</param>
    /// <param name="provider">提供商名（不区分大小写）</param>
    /// <param name="principal">提供商回调的 ClaimsPrincipal</param>
    Task<Result<string>> StagePendingLinkAsync(Guid userId, string provider, ClaimsPrincipal principal);

    /// <summary>
    /// 完成端点用：取出暂存在<b>当前登录账号</b>名下、与 <paramref name="code"/> 匹配的那份外部身份，成功即作废。
    /// </summary>
    /// <remarks>
    /// 只按当前账号去找：别的账号暂存的绑定从这里根本够不着，所以「完成码对、账号不对」与
    /// 「完成码不对」「已用」「过期」是同一个回答（400）。
    /// </remarks>
    /// <param name="userId">当前已认证的用户</param>
    /// <param name="provider">提供商名（不区分大小写）</param>
    /// <param name="code">回调页交回浏览器的完成码</param>
    Task<Result<PendingOAuthLinkDto>> ConsumePendingLinkAsync(Guid userId, string provider, string code);
}
