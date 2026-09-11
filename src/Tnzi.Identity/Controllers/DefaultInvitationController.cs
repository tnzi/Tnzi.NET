namespace Tnzi.Identity.Controllers;

/// <summary>
/// 被邀请人这一侧：打开链接、补齐信息、激活账号。
/// </summary>
/// <remarks>
/// <para>
/// <strong>两个端点都是匿名的</strong> —— 拿着邀请令牌的人按定义还没有账号可登。
/// 令牌本身就是全部凭据，因此它的随机性（256 位）与「库里只存哈希」这两件事，
/// 就是这条链路的全部安全性。
/// </para>
/// <para>
/// ⚠ <strong>部署方需要确认匿名限流真的生效。</strong>框架的限流是全局中间件
/// （<c>RateLimit:Enabled</c> + <c>RateLimitingMiddleware</c>），不是挂在这里的特性；
/// 而它在取不到分区键时是<b>放行</b>的。这两个端点是可被穷举的入口，
/// 限流一旦失效就没有任何东西在拦截批量试探。
/// </para>
/// </remarks>
[DefaultController]
[Route("invitations")]
[ApiExplorerSettings(GroupName = "auth")]
public class DefaultInvitationController : ApiControllerBase
{
    protected readonly IInvitationService InvitationService;

    /// <summary>
    /// 初始化邀请控制器
    /// </summary>
    /// <param name="invitationService">邀请服务</param>
    public DefaultInvitationController(IInvitationService invitationService)
    {
        InvitationService = Check.NotNull(invitationService);
    }

    /// <summary>
    /// 打开邀请链接时读取要展示的信息。<b>不消费令牌</b>。
    /// </summary>
    /// <remarks>
    /// 返回的邮箱与手机号是掩码的：一条链接如果能换出完整邮箱，
    /// 那么链接泄露就等于员工邮箱泄露。
    /// </remarks>
    /// <param name="token">邀请令牌</param>
    /// <returns>邀请展示信息</returns>
    [HttpGet("{token}")]
    [AllowAnonymous]
    public virtual async Task<ApiResult<InvitationPreviewDto>> Preview(string token)
    {
        var result = await InvitationService.PreviewAsync(token);
        return result.ToApiResult();
    }

    /// <summary>
    /// 接受邀请。
    /// </summary>
    /// <remarks>
    /// 返回 <c>completed = false</c> 时账号<b>仍未激活</b>，<c>remainingSteps</c> 说明还差什么；
    /// 此时令牌没有被消费，可以带着同一条链接回来继续。
    /// </remarks>
    /// <param name="input">接受信息（令牌、密码、消费应用自定义载荷）</param>
    /// <returns>接受结果</returns>
    [HttpPost("accept")]
    [AllowAnonymous]
    public virtual async Task<ApiResult<AcceptInvitationResultDto>> Accept([FromBody] AcceptInvitationDto input)
    {
        var result = await InvitationService.AcceptAsync(input);
        return result.ToApiResult();
    }
}
