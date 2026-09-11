
namespace Tnzi.System.Controllers.Admin;

/// <summary>
/// 配置管理控制器
/// 提供配置CRUD等API端点，所有方法支持重写
/// </summary>
[DefaultController]
[Route("admin/settings")]
[ApiAuthorize(PermissionName = "system.parameter.view")]
public class DefaultSettingAdminController : ApiAdminControllerBase
{
    protected readonly ISettingService SettingService;

    /// <summary>
    /// 初始化配置管理控制器
    /// </summary>
    public DefaultSettingAdminController(ISettingService settingService)
    {
        SettingService = Check.NotNull(settingService);
    }

    /// <summary>
    /// 获取配置列表（按分组 / 作用域）。默认返回 Global 行 + 调用者本租户的 Tenant 行；
    /// <c>scope=User</c> 时租户内调用者必须给出 <c>scopeId</c>（用户 id）。租户归属由身份决定，
    /// 请求别的租户返回 403。
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IEnumerable<SettingDto>>> GetSettings(
        [FromQuery] string? group = null,
        [FromQuery] SettingScope? scope = null,
        [FromQuery] string? scopeId = null)
    {
        var result = await SettingService.GetSettingsAsync(group, scope, scopeId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 根据ID获取配置
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<SettingDto>> GetById(Guid id)
    {
        var result = await SettingService.GetSettingByIdAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建配置
    /// </summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "system.parameter.create")]
    public virtual async Task<ApiResult<SettingDto>> Create([FromBody] CreateSettingDto input)
    {
        var result = await SettingService.CreateSettingAsync(input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新配置
    /// </summary>
    [HttpPut("{id:guid}")]
    [ApiAuthorize(PermissionName = "system.parameter.update")]
    public virtual async Task<ApiResult<SettingDto>> Update(Guid id, [FromBody] UpdateSettingDto input)
    {
        var result = await SettingService.UpdateSettingAsync(id, input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除配置
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "system.parameter.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await SettingService.DeleteSettingAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量删除配置
    /// </summary>
    [HttpDelete("batch")]
    [ApiAuthorize(PermissionName = "system.parameter.delete")]
    public virtual async Task<ApiResult> DeleteSettings([FromBody] IEnumerable<Guid> ids)
    {
        var result = await SettingService.DeleteSettingsAsync(ids);
        return result.ToApiResult();
    }

    /// <summary>
    /// Get system information (version, loaded modules, uptime, environment)
    /// </summary>
    [HttpGet("system-info")]
    public virtual async Task<ApiResult<SystemInfoDto>> GetSystemInfo()
    {
        var result = await SettingService.GetSystemInfoAsync();
        return result.ToApiResult();
    }

    /// <summary>
    /// Set an encrypted setting value
    /// </summary>
    [HttpPut("{group}/{key}/encrypted")]
    [ApiAuthorize(PermissionName = "system.parameter.update")]
    public virtual async Task<ApiResult> SetEncrypted(string group, string key, [FromBody] SetEncryptedSettingDto input)
    {
        var result = await SettingService.SetEncryptedAsync(group, key, input.Value, input.Description);
        return result.ToApiResult();
    }

    /// <summary>
    /// Get decrypted setting value. Returns SECRETS IN PLAINTEXT - gated at
    /// write-level trust (system.parameter.update, the same code that sets
    /// encrypted values), not the read-level .view the rest of this
    /// controller's GETs use.
    /// </summary>
    [HttpGet("{group}/{key}/decrypted")]
    [ApiAuthorize(PermissionName = "system.parameter.update")]
    public virtual async Task<ApiResult<string?>> GetDecrypted(string group, string key)
    {
        var result = await SettingService.GetDecryptedAsync(group, key);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取配置分组列表（分组名称 + 每组配置数量）
    /// </summary>
    [HttpGet("groups")]
    public virtual async Task<ApiResult<List<SettingGroupDto>>> GetSettingGroups(
        [FromQuery] SettingScope? scope = null,
        [FromQuery] string? scopeId = null)
    {
        var result = await SettingService.GetSettingGroupsAsync(scope, scopeId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 重排同一分组内的配置项（拖拽排序）
    /// </summary>
    /// <remarks>请求体是当前可见的顺序，不必是全量：范围外的配置项保持原位。</remarks>
    [HttpPost("reorder")]
    [ApiAuthorize(PermissionName = "system.parameter.update")]
    public virtual async Task<ApiResult> Reorder([FromBody] ReorderRequestDto request, [FromQuery] string? group = null)
    {
        var result = await SettingService.ReorderAsync(request.Ids, group);
        return result.ToApiResult();
    }
}
