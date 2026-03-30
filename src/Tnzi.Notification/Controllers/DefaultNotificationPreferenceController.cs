namespace Tnzi.Notification.Controllers;

/// <summary>
/// 用户通知偏好控制器
/// 提供通知渠道偏好管理（启用/禁用、静默时段、频率限制）
/// </summary>
[DefaultController]
[ApiAuthorize]
[Route("notification/preferences")]
[ApiExplorerSettings(GroupName = "notification")]
public class DefaultNotificationPreferenceController : ApiControllerBase
{
    protected readonly INotificationPreferenceService PreferenceService;

    public DefaultNotificationPreferenceController(INotificationPreferenceService preferenceService)
    {
        PreferenceService = Check.NotNull(preferenceService);
    }

    /// <summary>
    /// 获取当前用户的通知偏好列表
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<List<NotificationPreferenceDto>>> GetMyPreferences()
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await PreferenceService.GetUserPreferencesAsync(userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 设置通知偏好（存在则更新，不存在则创建）
    /// </summary>
    [HttpPut]
    public virtual async Task<ApiResult<NotificationPreferenceDto>> SetPreference([FromBody] SetNotificationPreferenceDto input)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await PreferenceService.SetPreferenceAsync(userId, input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除指定通知偏好
    /// </summary>
    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> DeletePreference(Guid id)
    {
        var result = await PreferenceService.DeletePreferenceAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 重置为默认偏好（删除所有自定义偏好）
    /// </summary>
    [HttpPost("reset")]
    public virtual async Task<ApiResult> ResetToDefault()
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await PreferenceService.ResetToDefaultAsync(userId);
        return result.ToApiResult();
    }
}
