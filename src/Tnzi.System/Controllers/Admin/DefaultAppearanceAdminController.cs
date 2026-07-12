namespace Tnzi.System.Controllers.Admin;

/// <summary>
/// 外观管理控制器。
/// 维护全局管理端主题快照：保存后对全体已登录用户生效。
/// deny-by-default 下默认仅超级管理员可达；亦可显式授出 system.appearance.* 委托维护。
/// </summary>
[DefaultController]
[Route("admin/appearance")]
[ApiAuthorize(PermissionName = "system.appearance.view")]
public class DefaultAppearanceAdminController : ApiAdminControllerBase
{
    protected readonly IAppearanceService Appearance;

    public DefaultAppearanceAdminController(IAppearanceService appearance)
    {
        Appearance = Check.NotNull(appearance);
    }

    /// <summary>获取全局管理端主题快照。</summary>
    [HttpGet("theme")]
    public virtual async Task<ApiResult<AdminThemeDto>> GetTheme()
    {
        var result = await Appearance.GetAdminThemeAsync();
        return result.ToApiResult();
    }

    /// <summary>保存全局管理端主题快照（覆盖式整体保存，立即对所有用户生效）。</summary>
    [HttpPut("theme")]
    [ApiAuthorize(PermissionName = "system.appearance.update")]
    public virtual async Task<ApiResult<AdminThemeDto>> SaveTheme([FromBody] SaveAdminThemeDto input)
    {
        var result = await Appearance.SaveAdminThemeAsync(input);
        return result.ToApiResult();
    }

    /// <summary>清除全局管理端主题（所有客户端回退本地默认值）。</summary>
    [HttpDelete("theme")]
    [ApiAuthorize(PermissionName = "system.appearance.update")]
    public virtual async Task<ApiResult> ResetTheme()
    {
        var result = await Appearance.ResetAdminThemeAsync();
        return result.ToApiResult();
    }
}
