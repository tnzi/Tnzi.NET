
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
    /// 获取所有配置（按分组）
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IEnumerable<SettingDto>>> GetSettings([FromQuery] string? group = null)
    {
        var result = await SettingService.GetSettingsAsync(group);
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
    public virtual async Task<ApiResult<SettingDto>> Create([FromBody] CreateSettingDto input)
    {
        var result = await SettingService.CreateSettingAsync(input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新配置
    /// </summary>
    [HttpPut("{id:guid}")]
    public virtual async Task<ApiResult<SettingDto>> Update(Guid id, [FromBody] UpdateSettingDto input)
    {
        var result = await SettingService.UpdateSettingAsync(id, input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除配置
    /// </summary>
    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await SettingService.DeleteSettingAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量删除配置
    /// </summary>
    [HttpDelete("batch")]
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
    public virtual async Task<ApiResult> SetEncrypted(string group, string key, [FromBody] SetEncryptedSettingDto input)
    {
        var result = await SettingService.SetEncryptedAsync(group, key, input.Value, input.Description);
        return result.ToApiResult();
    }

    /// <summary>
    /// Get decrypted setting value
    /// </summary>
    [HttpGet("{group}/{key}/decrypted")]
    public virtual async Task<ApiResult<string?>> GetDecrypted(string group, string key)
    {
        var result = await SettingService.GetDecryptedAsync(group, key);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取配置分组列表（分组名称 + 每组配置数量）
    /// </summary>
    [HttpGet("groups")]
    public virtual async Task<ApiResult<List<SettingGroupDto>>> GetSettingGroups()
    {
        var result = await SettingService.GetSettingGroupsAsync();
        return result.ToApiResult();
    }
}
