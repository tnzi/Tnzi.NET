namespace Tnzi.System.Controllers;

/// <summary>
/// 外观配置控制器（用户侧）。
/// 读取全局管理端主题，用于启动时统一渲染管理界面。
/// 读端点匿名开放：全局主题是部署级公开外观（仅颜色/布局/圆角等展示令牌，无任何机密），
/// 与 <c>GET /auth/config</c> 同类；这样登录页与顶层异常页（403/404/500，渲染在已认证
/// shell 之外）刷新后也能应用超管配置的主题，而不是回落内置调色板。
/// </summary>
[DefaultController]
[ApiAuthorize]
[Route("appearance")]
[ApiExplorerSettings(GroupName = "user")]
public class DefaultAppearanceController : ApiControllerBase
{
    protected readonly IAppearanceService Appearance;

    public DefaultAppearanceController(IAppearanceService appearance)
    {
        Appearance = Check.NotNull(appearance);
    }

    /// <summary>
    /// 获取全局管理端主题快照（未配置时 theme 为 null，客户端回退本地默认值）。
    /// 匿名可读（覆盖类级 <see cref="ApiAuthorizeAttribute"/>）：主题在登录前即需应用于登录页。
    /// </summary>
    [AllowAnonymous]
    [HttpGet("admin-theme")]
    public virtual async Task<ApiResult<AdminThemeDto>> GetAdminTheme()
    {
        var result = await Appearance.GetAdminThemeAsync();
        return result.ToApiResult();
    }
}
