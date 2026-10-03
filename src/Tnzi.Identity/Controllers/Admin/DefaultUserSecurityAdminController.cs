namespace Tnzi.Identity.Controllers.Admin;

/// <summary>
/// 账号登录安全管理：对另一个账号的二次验证控制，以及登录 IP 允许列表。所有方法支持重写。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么是独立的路由前缀而不是挂在 <c>admin/users/{id}/...</c> 下</b>：<c>admin/users</c> 是
/// <c>DefaultUserAdminController</c> 拥有的模板。消费方一旦用一个独立控制器接管那个模板，
/// 同一模板上的其它默认控制器会被整组抑制 —— 这几个端点会跟着一起消失，而消费方并没有打算动它们。
/// 自己的前缀让它与用户 CRUD 的替换互不牵连。
/// </para>
/// <para>
/// <b>为什么写操作用单独的码 <c>user.security</c> 而不是 <c>user.update</c></b>：改一个人的电话号码
/// 与摘掉一个人的第二因子不是同一件事。持有 <c>user.update</c> 的岗位很宽（资料、角色、组织、启停），
/// 而「能让谁的登录少一道验证、能让谁从任何地方登录」是一次要单独做的授予，也是一次要单独审计的操作。
/// 读仍然只要类级 <c>user.view</c>：看得见状态的人未必该改得动它。
/// </para>
/// </remarks>
[DefaultController]
[Route("admin/user-security")]
[ApiAuthorize(PermissionName = "user.view")]
public class DefaultUserSecurityAdminController : ApiAdminControllerBase
{
    protected readonly IUserTwoFactorAdminService TwoFactor;
    protected readonly IUserSignInPolicyService SignInPolicy;

    public DefaultUserSecurityAdminController(IUserTwoFactorAdminService twoFactor, IUserSignInPolicyService signInPolicy)
    {
        TwoFactor = Check.NotNull(twoFactor);
        SignInPolicy = Check.NotNull(signInPolicy);
    }

    // ── 二次验证 ──

    /// <summary>读取账号的二次验证状态（按方式）。</summary>
    [HttpGet("{userId:guid}/two-factor")]
    public virtual async Task<ApiResult<TwoFactorStatusDto>> GetTwoFactorStatus(Guid userId)
    {
        var result = await TwoFactor.GetStatusAsync(userId);
        return result.ToApiResult();
    }

    /// <summary>总开关关掉，已配置的方式保留。</summary>
    [HttpPost("{userId:guid}/two-factor/suspend")]
    [ApiAuthorize(PermissionName = "user.security")]
    public virtual async Task<ApiResult> SuspendTwoFactor(Guid userId)
    {
        var result = await TwoFactor.SuspendAsync(userId);
        return result.ToApiResult();
    }

    /// <summary>总开关打开，保存着的方式重新生效。</summary>
    [HttpPost("{userId:guid}/two-factor/resume")]
    [ApiAuthorize(PermissionName = "user.security")]
    public virtual async Task<ApiResult> ResumeTwoFactor(Guid userId)
    {
        var result = await TwoFactor.ResumeAsync(userId);
        return result.ToApiResult();
    }

    /// <summary>打开一种基于验证码的方式（短信 / 邮箱）。身份验证器只能由持有人自助登记。</summary>
    [HttpPost("{userId:guid}/two-factor/methods/enable")]
    [ApiAuthorize(PermissionName = "user.security")]
    public virtual async Task<ApiResult> EnableTwoFactorMethod(Guid userId, [FromBody] TwoFactorMethodRequestDto input)
    {
        var result = await TwoFactor.EnableMethodAsync(userId, input.Type);
        return result.ToApiResult();
    }

    /// <summary>关掉一种方式，其它不动。</summary>
    [HttpPost("{userId:guid}/two-factor/methods/disable")]
    [ApiAuthorize(PermissionName = "user.security")]
    public virtual async Task<ApiResult> DisableTwoFactorMethod(Guid userId, [FromBody] TwoFactorMethodRequestDto input)
    {
        var result = await TwoFactor.DisableMethodAsync(userId, input.Type);
        return result.ToApiResult();
    }

    /// <summary>选择登录时优先提供的方式。</summary>
    [HttpPut("{userId:guid}/two-factor/preferred")]
    [ApiAuthorize(PermissionName = "user.security")]
    public virtual async Task<ApiResult> SetPreferredTwoFactor(Guid userId, [FromBody] TwoFactorMethodRequestDto input)
    {
        var result = await TwoFactor.SetPreferredAsync(userId, input.Type);
        return result.ToApiResult();
    }

    /// <summary>全部清掉（含身份验证器密钥）。给「设备丢了」用，持有人从头重新登记。</summary>
    [HttpPost("{userId:guid}/two-factor/reset")]
    [ApiAuthorize(PermissionName = "user.security")]
    public virtual async Task<ApiResult> ResetTwoFactor(Guid userId)
    {
        var result = await TwoFactor.ResetAsync(userId);
        return result.ToApiResult();
    }

    // ── 登录准入策略 ──

    /// <summary>读取账号的登录准入策略（登录 IP 允许列表）。</summary>
    [HttpGet("{userId:guid}/sign-in-policy")]
    public virtual async Task<ApiResult<UserSignInPolicyDto>> GetSignInPolicy(Guid userId)
    {
        var result = await SignInPolicy.GetAsync(userId);
        return result.ToApiResult();
    }

    /// <summary>改写登录 IP 允许列表。开启时至少要有一个有效条目。</summary>
    [HttpPut("{userId:guid}/sign-in-policy/ip-allow-list")]
    [ApiAuthorize(PermissionName = "user.security")]
    public virtual async Task<ApiResult<UserSignInPolicyDto>> SetIpAllowList(Guid userId, [FromBody] SetIpAllowListDto input)
    {
        var result = await SignInPolicy.SetIpAllowListAsync(userId, input);
        return result.ToApiResult();
    }
}
