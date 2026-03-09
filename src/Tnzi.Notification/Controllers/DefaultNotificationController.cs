namespace Tnzi.Notification.Controllers;

/// <summary>
/// 用户通知控制器（收件箱）
/// 提供用户端的通知查看、标记已读等功能
/// </summary>
[DefaultController]
[ApiAuthorize]
[Route("notifications")]
[ApiExplorerSettings(GroupName = "user")]
public class DefaultNotificationController : ApiControllerBase
{
    protected readonly IUserNotificationService UserNotificationService;

    public DefaultNotificationController(IUserNotificationService userNotificationService)
    {
        UserNotificationService = Check.NotNull(userNotificationService);
    }

    /// <summary>
    /// 获取当前用户的通知收件箱
    /// </summary>
    [HttpPost("inbox")]
    public virtual async Task<ApiResult<IPagedList<UserNotificationItem>>> GetInbox([FromBody] QueryUserNotificationRequest request)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await UserNotificationService.GetInboxAsync(userId, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取通知详情（自动标记为已读）
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<UserNotificationDetail>> GetDetail(Guid id)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await UserNotificationService.GetDetailAsync(userId, id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 标记通知为已读
    /// </summary>
    [HttpPost("{id:guid}/read")]
    public virtual async Task<ApiResult> MarkAsRead(Guid id)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await UserNotificationService.MarkAsReadAsync(userId, id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 标记全部为已读
    /// </summary>
    [HttpPost("read-all")]
    public virtual async Task<ApiResult> MarkAllAsRead()
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await UserNotificationService.MarkAllAsReadAsync(userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取未读通知数量
    /// </summary>
    [HttpGet("unread-count")]
    public virtual async Task<ApiResult<UnreadCountDto>> GetUnreadCount()
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await UserNotificationService.GetUnreadCountAsync(userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除通知
    /// </summary>
    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await UserNotificationService.DeleteAsync(userId, id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量标记已读
    /// </summary>
    [HttpPost("batch-read")]
    public virtual async Task<ApiResult> BatchMarkAsRead([FromBody] List<Guid> ids)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await UserNotificationService.BatchMarkAsReadAsync(userId, ids);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量删除通知
    /// </summary>
    [HttpPost("batch-delete")]
    public virtual async Task<ApiResult> BatchDelete([FromBody] List<Guid> ids)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;
        var result = await UserNotificationService.BatchDeleteAsync(userId, ids);
        return result.ToApiResult();
    }
}
