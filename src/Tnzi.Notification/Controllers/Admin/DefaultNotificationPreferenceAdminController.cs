namespace Tnzi.Notification.Controllers.Admin;

/// <summary>
/// Admin controller for user notification preferences (aka subscriptions).
/// Wraps <see cref="INotificationPreferenceService"/> with userId taken from
/// the URL rather than the current session, so an operator can inspect or
/// adjust any user's opt-in/out state per channel + category.
///
/// This is the backend side of the Phase 3 'NotificationSubscription' admin
/// page — the Preference entity IS the subscription model, just with a richer
/// name (it also carries quiet-hours and rate-limit fields).
/// </summary>
[DefaultController]
[Route("admin/notification-preferences")]
[ApiAuthorize(PermissionName = "notification.subscription.view")]
public class DefaultNotificationPreferenceAdminController : ApiAdminControllerBase
{
    protected readonly INotificationPreferenceService PreferenceService;

    public DefaultNotificationPreferenceAdminController(INotificationPreferenceService preferenceService)
    {
        PreferenceService = Check.NotNull(preferenceService);
    }

    /// <summary>
    /// Canonical paged list of notification preferences (aka subscriptions)
    /// across all users. Used by the NotificationSubscription admin page.
    /// Supports optional filters on user / channel / category / enabled state
    /// via query string.
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<NotificationPreferenceDto>>> GetList([FromQuery] NotificationPreferenceQueryDto query)
    {
        var result = await PreferenceService.GetPagedListAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// List all notification preferences for a specific user.
    /// </summary>
    [HttpGet("user/{userId:guid}")]
    public virtual async Task<ApiResult<List<NotificationPreferenceDto>>> GetByUser(Guid userId)
    {
        var result = await PreferenceService.GetUserPreferencesAsync(userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// Create or update a notification preference for the given user.
    /// Upsert semantics: existing (userId, channel, category) tuple is updated;
    /// otherwise a new row is inserted.
    /// </summary>
    [HttpPut("user/{userId:guid}")]
    public virtual async Task<ApiResult<NotificationPreferenceDto>> SetPreference(
        Guid userId,
        [FromBody] SetNotificationPreferenceDto input)
    {
        var result = await PreferenceService.SetPreferenceAsync(userId, input);
        return result.ToApiResult();
    }

    /// <summary>
    /// Delete a notification preference row by id.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await PreferenceService.DeletePreferenceAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// Reset a user's preferences to the platform defaults by removing all
    /// their custom rows. Subsequent channel checks will return true (enabled)
    /// until new preferences are set.
    /// </summary>
    [HttpPost("user/{userId:guid}/reset")]
    public virtual async Task<ApiResult> ResetToDefault(Guid userId)
    {
        var result = await PreferenceService.ResetToDefaultAsync(userId);
        return result.ToApiResult();
    }
}
