
namespace Tnzi.System.Controllers.Admin;

/// <summary>
/// 配置管理控制器基类
/// 提供配置CRUD等API端点，所有方法支持重写
/// </summary>
[Route("admin/settings")]
public abstract class SettingAdminControllerBase : ApiAdminControllerBase
{
    protected readonly ISettingService SettingService;

    /// <summary>
    /// 初始化配置管理控制器基类
    /// </summary>
    protected SettingAdminControllerBase(ISettingService settingService)
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
}
