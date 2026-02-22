namespace Tnzi.Notification.Controllers.Admin;

/// <summary>
/// 通知管理控制器基类
/// 提供通知创建、发送、查询、重试等管理端API端点
/// </summary>
[Route("admin/notifications")]
public abstract class NotificationAdminControllerBase : ApiAdminControllerBase
{
    protected readonly INotificationService NotificationService;
    protected readonly INotificationQueryService QueryService;
    protected readonly INotificationRetryService RetryService;

    protected NotificationAdminControllerBase(
        INotificationService notificationService,
        INotificationQueryService queryService,
        INotificationRetryService retryService)
    {
        NotificationService = Check.NotNull(notificationService);
        QueryService = Check.NotNull(queryService);
        RetryService = Check.NotNull(retryService);
    }

    /// <summary>
    /// 根据ID获取通知
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<NotificationInfo>> GetById(Guid id)
    {
        var result = await QueryService.GetByIdAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建通知（不发送）
    /// </summary>
    [HttpPost]
    public virtual async Task<ApiResult<NotificationInfo>> Create([FromBody] CreateNotificationRequest request)
    {
        var result = await NotificationService.CreateAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建并发送通知
    /// </summary>
    [HttpPost("create-and-send")]
    public virtual async Task<ApiResult<NotificationInfo>> CreateAndSend([FromBody] CreateNotificationRequest request)
    {
        var result = await NotificationService.CreateAndSendAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 发送通知
    /// </summary>
    [HttpPost("{id:guid}/send")]
    public virtual async Task<ApiResult> Send(Guid id)
    {
        var result = await NotificationService.SendAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 查询通知
    /// </summary>
    [HttpPost("query")]
    public virtual async Task<ApiResult<IPagedList<NotificationInfo>>> Query([FromBody] QueryNotificationRequest request)
    {
        var result = await QueryService.QueryAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 重试失败的通知
    /// </summary>
    [HttpPost("{id:guid}/retry")]
    public virtual async Task<ApiResult> Retry(Guid id)
    {
        var result = await RetryService.RetryAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量重试失败的通知
    /// </summary>
    [HttpPost("retry-failed")]
    public virtual async Task<ApiResult> RetryFailed(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var result = await RetryService.RetryFailedAsync(startDate, endDate);
        return result.ToApiResult();
    }

    /// <summary>
    /// 取消通知
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    public virtual async Task<ApiResult> Cancel(Guid id)
    {
        var result = await NotificationService.CancelAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除（取消）通知
    /// </summary>
    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await NotificationService.CancelAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取通知统计信息
    /// </summary>
    [HttpGet("statistics")]
    public virtual async Task<ApiResult<NotificationStatisticsDto>> GetStatistics(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var result = await QueryService.GetStatisticsAsync(startDate, endDate);
        return result.ToApiResult();
    }

    /// <summary>
    /// 按渠道获取通知统计
    /// </summary>
    [HttpGet("statistics/by-channel")]
    public virtual async Task<ApiResult<IEnumerable<ChannelStatisticsDto>>> GetStatisticsByChannel(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var result = await QueryService.GetStatisticsByChannelAsync(startDate, endDate);
        return result.ToApiResult();
    }

    /// <summary>
    /// 按状态获取通知统计
    /// </summary>
    [HttpGet("statistics/by-status")]
    public virtual async Task<ApiResult<IEnumerable<StatusStatisticsDto>>> GetStatisticsByStatus(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var result = await QueryService.GetStatisticsByStatusAsync(startDate, endDate);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取失败的通知列表
    /// </summary>
    [HttpGet("failed")]
    public virtual async Task<ApiResult<IEnumerable<NotificationInfo>>> GetFailedNotifications(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int top = 100)
    {
        var result = await QueryService.GetFailedNotificationsAsync(startDate, endDate, top);
        return result.ToApiResult();
    }
}
