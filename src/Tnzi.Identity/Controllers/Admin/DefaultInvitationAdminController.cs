namespace Tnzi.Identity.Controllers.Admin;

/// <summary>
/// 邀请注册管理控制器：开号发链接 / 重发 / 撤销。
/// </summary>
/// <remarks>
/// <para>
/// ★ <strong>没有「邀请列表」端点</strong>，那是刻意的：邀请就是一个处于
/// <see cref="PendingUserActions.InvitationPending"/> 的账号，用既有的用户列表加一个
/// <c>pendingAction</c> 筛选就能回答「谁还没接受」。为它再造一套列表、分页、
/// 排序与权限，只会得到第二份需要各自维护、迟早互相矛盾的视图。
/// </para>
/// <para>
/// 权限沿用 <c>user.*</c> 而不是新开一组 <c>invitation.*</c>：这些动作操作的对象
/// 本来就是用户（开号、改状态、删号），能邀请人的人就是能建用户的人。
/// 多一组码只会让管理员在授权界面上多面对一组需要自己想明白关系的东西。
/// </para>
/// </remarks>
[DefaultController]
[Route("admin/invitations")]
[ApiAuthorize(PermissionName = "user.view")]
public class DefaultInvitationAdminController : ApiAdminControllerBase
{
    protected readonly IInvitationService InvitationService;

    /// <summary>
    /// 初始化邀请管理控制器
    /// </summary>
    /// <param name="invitationService">邀请服务</param>
    public DefaultInvitationAdminController(IInvitationService invitationService)
    {
        InvitationService = Check.NotNull(invitationService);
    }

    /// <summary>
    /// 开一个账号并发出邀请。
    /// </summary>
    /// <remarks>
    /// 返回体里的 <c>acceptUrl</c> <b>仅此一次可读</b>。想自己发信（企业微信、钉钉、
    /// 当面给）就用它；想让应用统一发，订阅 <c>UserInvitedEvent</c>。
    /// </remarks>
    /// <param name="input">邀请信息</param>
    /// <returns>邀请结果（含接受链接）</returns>
    [HttpPost]
    [ApiAuthorize(PermissionName = "user.create")]
    public virtual async Task<ApiResult<InvitationDto>> Create([FromBody] CreateInvitationDto input)
    {
        var result = await InvitationService.InviteAsync(input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 重发邀请，<b>上一条链接立即失效</b>。
    /// </summary>
    /// <param name="userId">被邀请的账号</param>
    /// <param name="lifetimeHours">有效期（小时），不给则用配置值</param>
    /// <returns>新的邀请结果</returns>
    [HttpPost("{userId}/resend")]
    [ApiAuthorize(PermissionName = "user.update")]
    public virtual async Task<ApiResult<InvitationDto>> Resend(Guid userId, [FromQuery] int? lifetimeHours = null)
    {
        var result = await InvitationService.ResendAsync(userId, lifetimeHours);
        return result.ToApiResult();
    }

    /// <summary>
    /// 撤销邀请：删掉这个还没被接受的账号，链接随之失效。
    /// </summary>
    /// <param name="userId">被邀请的账号</param>
    /// <returns>操作结果</returns>
    [HttpDelete("{userId}")]
    [ApiAuthorize(PermissionName = "user.delete")]
    public virtual async Task<ApiResult> Revoke(Guid userId)
    {
        var result = await InvitationService.RevokeAsync(userId);
        return result.ToApiResult();
    }
}
