namespace Tnzi.Identity.Services;

/// <summary>
/// 待办义务的完成：凭登录时拿到的临时令牌把欠的事办完，办完拿令牌。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ <strong>每一个义务位都必须在这里有一条完成路径。</strong>
/// <see cref="PendingUserActions"/> 是密封枚举，消费应用<b>加不了新位</b>，
/// 只能用框架定义的这几个。所以「定义一个位却不给完成路径」不是留白，是陷阱：
/// 应用置上它之后，用户会收到挑战、却永远办不完，账号从此进不去。
/// 由 <c>PendingActionCoverageTests</c> 守着。
/// </para>
/// <para>
/// ★★ <strong>三条路径的收尾完全相同</strong>：清掉刚办完的那一位 → 还欠着别的就再发一个挑战
/// → 全清了才签发令牌。共享这段是必须的：三份手抄会在「清位写成赋 <c>None</c>」
/// 或者「办完就直接签发、不管还欠着什么」上各错一次，而两种错都不会让测试变红。
/// </para>
/// <para>
/// ★ 签发一律走 <c>IAuthService.IssueTokenAsync</c>：账号刚清掉一个义务，
/// 此刻它和任何账号一样要过守卫链（管理员完全可能在这中间把人停掉）。
/// </para>
/// <para>
/// ★ <strong>所有方法都不接受用户标识</strong>：办谁的事由令牌决定。这些端点是匿名可达的
/// （人还没拿到访问令牌），带上 userId 就等于让匿名调用方指定「替谁办」。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "义务位仍可能增加，随之增加完成路径")]
public interface IPendingActionService
{
    /// <summary>
    /// 打开挑战页时读：还欠哪些事，以及办它们需要的材料。
    /// </summary>
    /// <remarks>
    /// <b>不消费令牌</b>。TOTP 那一项会顺带把密钥与 <c>otpauth://</c> 地址带出来，
    /// 否则前端拿到「你得绑个验证器」却没有二维码可扫。
    /// </remarks>
    Task<Result<PendingActionChallengeDto>> DescribeAsync(string tempToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// 完成「必须先改密码」。
    /// </summary>
    Task<Result<PendingActionResultDto>> CompleteChangePasswordAsync(CompletePasswordChangeDto input, CancellationToken cancellationToken = default);

    /// <summary>
    /// 完成「必须先绑定验证器」：提交验证器算出的一次性码。
    /// </summary>
    /// <remarks>
    /// 密钥来自 <see cref="DescribeAsync"/>，与个人中心里的绑定流程是同一套
    /// （<c>ITwoFactorService.EnableTotpAsync</c>），只是授权凭据换成了临时令牌。
    /// </remarks>
    Task<Result<PendingActionResultDto>> CompleteEnrollTotpAsync(CompletePendingActionCodeDto input, CancellationToken cancellationToken = default);

    /// <summary>
    /// 给「必须先确认邮箱」发一封验证码。
    /// </summary>
    /// <remarks>
    /// 地址取自账号本身，<b>不接受调用方指定</b> —— 由一个匿名端点决定把码发到哪，
    /// 等于把这件事交给一个可能已被接管的会话（与 step-up 发码同一判据）。
    /// </remarks>
    Task<Result> SendEmailConfirmationCodeAsync(string tempToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// 完成「必须先确认邮箱」：提交收到的验证码。
    /// </summary>
    Task<Result<PendingActionResultDto>> CompleteConfirmEmailAsync(CompletePendingActionCodeDto input, CancellationToken cancellationToken = default);
}
