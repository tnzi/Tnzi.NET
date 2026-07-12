
namespace Tnzi.System.Controllers.Admin;

/// <summary>
/// 配置中心控制器
/// 提供 schema-driven 模块配置定义查询、按组保存与恢复默认 API 端点
/// </summary>
[DefaultController]
[Route("admin/settings-center")]
// Aggregate endpoint spanning many modules' config groups: no single class-level
// module code fits. Authorization is enforced PER-GROUP in SettingsCenterService
// (each group has its own {group}.settings.{slug}.view/update code, super-admin
// bypasses). The controller is a bare authentication boundary - same self-service
// exemption pattern as Menu user-tree / FunctionAuthorization access-profile.
// GET returns only the caller's viewable groups; writes 403 per-group in service.
[ApiAuthorize]
public class DefaultSettingsCenterAdminController : ApiAdminControllerBase
{
    protected readonly ISettingsCenterService SettingsCenterService;

    public DefaultSettingsCenterAdminController(ISettingsCenterService settingsCenterService)
    {
        SettingsCenterService = Check.NotNull(settingsCenterService);
    }

    /// <summary>
    /// 获取全部配置定义（schema + 当前生效值）。只含已加载模块注册的定义。
    /// </summary>
    [HttpGet("definitions")]
    public virtual async Task<ApiResult<List<SettingsCenterGroupDto>>> GetDefinitions()
    {
        var result = await SettingsCenterService.GetDefinitionsAsync(HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// 按组批量保存（body 仅含变更字段；value=null 表示删除覆盖回退默认）。
    /// </summary>
    [HttpPut("groups/{groupKey}")]
    public virtual async Task<ApiResult<SettingsCenterGroupDto>> SaveGroup(string groupKey, [FromBody] Dictionary<string, string?> changedValues)
    {
        var result = await SettingsCenterService.SaveGroupAsync(groupKey, changedValues, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// 恢复默认：删除该组全部覆盖行。
    /// </summary>
    [HttpDelete("groups/{groupKey}/overrides")]
    public virtual async Task<ApiResult<SettingsCenterGroupDto>> ResetGroup(string groupKey)
    {
        var result = await SettingsCenterService.ResetGroupAsync(groupKey, HttpContext.RequestAborted);
        return result.ToApiResult();
    }
}
