namespace Tnzi.System.Controllers.Admin;

/// <summary>
/// 外观管理控制器。
/// 维护各前端产品（scope）的全局主题快照：保存后对该 scope 下全体用户生效。
/// deny-by-default 下默认仅超级管理员可达；亦可显式授出 system.appearance.* 委托维护。
/// <para>
/// 权限不按 scope 细分：能配管理端外观的人也就是能配对话端外观的人，为每个 scope 再切一组
/// 权限码只会让目录膨胀而不增加任何实际隔离。
/// </para>
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

    /// <summary>获取某个 scope 的全局主题快照。</summary>
    [HttpGet("theme/{scope}")]
    public virtual async Task<ApiResult<ThemeSnapshotDto>> GetTheme(string scope)
    {
        var result = await Appearance.GetThemeAsync(scope);
        return result.ToApiResult();
    }

    /// <summary>保存某个 scope 的全局主题快照（覆盖式整体保存，立即对该 scope 全体用户生效）。</summary>
    [HttpPut("theme/{scope}")]
    [ApiAuthorize(PermissionName = "system.appearance.update")]
    public virtual async Task<ApiResult<ThemeSnapshotDto>> SaveTheme(string scope, [FromBody] SaveThemeSnapshotDto input)
    {
        var result = await Appearance.SaveThemeAsync(scope, input);
        return result.ToApiResult();
    }

    /// <summary>清除某个 scope 的全局主题（该 scope 的客户端回退本地默认值）。</summary>
    [HttpDelete("theme/{scope}")]
    [ApiAuthorize(PermissionName = "system.appearance.update")]
    public virtual async Task<ApiResult> ResetTheme(string scope)
    {
        var result = await Appearance.ResetThemeAsync(scope);
        return result.ToApiResult();
    }

    // -- Pre-scope aliases ---------------------------------------------------
    // Kept so a published admin front-end keeps working across the upgrade.
    // They are the `admin` scope, spelled the old way.

    /// <summary>获取全局管理端主题快照（<c>theme/admin</c> 的别名）。</summary>
    [HttpGet("theme")]
    public virtual Task<ApiResult<ThemeSnapshotDto>> GetAdminTheme() => GetTheme(IAppearanceService.AdminScope);

    /// <summary>保存全局管理端主题快照（<c>theme/admin</c> 的别名）。</summary>
    [HttpPut("theme")]
    [ApiAuthorize(PermissionName = "system.appearance.update")]
    public virtual Task<ApiResult<ThemeSnapshotDto>> SaveAdminTheme([FromBody] SaveThemeSnapshotDto input)
        => SaveTheme(IAppearanceService.AdminScope, input);

    /// <summary>清除全局管理端主题（<c>theme/admin</c> 的别名）。</summary>
    [HttpDelete("theme")]
    [ApiAuthorize(PermissionName = "system.appearance.update")]
    public virtual Task<ApiResult> ResetAdminTheme() => ResetTheme(IAppearanceService.AdminScope);
}
