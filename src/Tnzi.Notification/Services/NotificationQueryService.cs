using Message = Tnzi.Notification.Entities.Message;

namespace Tnzi.Notification.Services;

/// <summary>
/// 通知查询服务实现（管理端查询+统计）
/// </summary>
public class NotificationQueryService : ApplicationService, INotificationQueryService
{
    private readonly IRepository<Message, Guid> _notificationRepository;

    public NotificationQueryService(IRepository<Message, Guid> notificationRepository, IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _notificationRepository = Check.NotNull(notificationRepository);
    }

    public async Task<Result<IPagedList<NotificationInfo>>> QueryAsync(QueryNotificationRequest request, CancellationToken cancellationToken = default)
    {
        var query = _notificationRepository.AsQueryable().AsNoTracking();

        if (request.Type.HasValue)
            query = query.Where(n => n.Type == request.Type.Value);

        if (request.Status.HasValue)
            query = query.Where(n => n.Status == request.Status.Value);

        if (request.StartTime.HasValue)
            query = query.Where(n => n.CreationTime >= request.StartTime.Value);

        if (request.EndTime.HasValue)
            query = query.Where(n => n.CreationTime <= request.EndTime.Value);

        if (!string.IsNullOrWhiteSpace(request.Category))
            query = query.Where(n => n.Category == request.Category);

        if (request.Priority.HasValue)
            query = query.Where(n => n.Priority == request.Priority.Value);

        if (request.SenderId.HasValue)
            query = query.Where(n => n.SenderId == request.SenderId.Value);

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.ToLower();
            query = query.Where(n => n.Subject.ToLower().Contains(keyword) || n.Content.ToLower().Contains(keyword));
        }

        query = query.OrderByDescending(n => n.CreationTime);

        var paged = await query
            .ProjectTo<Message, NotificationInfo>()
            .CreateAsync(request, cancellationToken);

        return Ok(RecipientAddressMask.Apply(paged));
    }

    public async Task<Result<NotificationInfo>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository
            .AsQueryable()
            .AsNoTracking()
            .Where(n => n.Id == id)
            .Include(n => n.Recipients)
            .Include(n => n.Attachments)
            .FirstOrDefaultAsync(cancellationToken);

        if (notification == null)
            return Fail<NotificationInfo>($"Notification {id} not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        var notificationInfo = notification.MapTo<NotificationInfo>();
        return Ok(RecipientAddressMask.Apply(notificationInfo));
    }

    public async Task<Result<NotificationStatisticsDto>> GetStatisticsAsync(DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default)
    {
        var query = _notificationRepository.AsQueryable();

        if (startDate.HasValue)
            query = query.Where(n => n.CreationTime >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(n => n.CreationTime <= endDate.Value);

        var statusCounts = await query
            .GroupBy(n => n.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var totalNotifications = statusCounts.Sum(s => s.Count);
        var sentCount = statusCounts.FirstOrDefault(s => s.Status == NotificationStatus.Sent)?.Count ?? 0;
        var sendingCount = statusCounts.FirstOrDefault(s => s.Status == NotificationStatus.Sending)?.Count ?? 0;
        var failedCount = statusCounts.FirstOrDefault(s => s.Status == NotificationStatus.Failed)?.Count ?? 0;
        var pendingCount = statusCounts.FirstOrDefault(s => s.Status == NotificationStatus.Pending)?.Count ?? 0;
        var cancelledCount = statusCounts.FirstOrDefault(s => s.Status == NotificationStatus.Cancelled)?.Count ?? 0;

        var successRate = totalNotifications > 0 ? (double)sentCount / totalNotifications * 100 : 0;

        var statistics = new NotificationStatisticsDto
        {
            TotalNotifications = totalNotifications,
            SentCount = sentCount,
            SendingCount = sendingCount,
            FailedCount = failedCount,
            PendingCount = pendingCount,
            CancelledCount = cancelledCount,
            SuccessRate = successRate
        };

        return Ok(statistics);
    }

    public async Task<Result<IEnumerable<ChannelStatisticsDto>>> GetStatisticsByChannelAsync(DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default)
    {
        var query = _notificationRepository.AsQueryable();

        if (startDate.HasValue)
            query = query.Where(n => n.CreationTime >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(n => n.CreationTime <= endDate.Value);

        var rawStatistics = await query
            .GroupBy(n => n.Type)
            .Select(g => new
            {
                Type = g.Key,
                TotalCount = g.Count(),
                SuccessCount = g.Count(n => n.Status == NotificationStatus.Sent),
                FailedCount = g.Count(n => n.Status == NotificationStatus.Failed)
            })
            .ToListAsync(cancellationToken);

        var statistics = rawStatistics.Select(s => new ChannelStatisticsDto
        {
            Channel = s.Type,
            TotalCount = s.TotalCount,
            SuccessCount = s.SuccessCount,
            FailedCount = s.FailedCount,
            SuccessRate = s.TotalCount > 0 ? (double)s.SuccessCount / s.TotalCount * 100 : 0
        }).ToList();

        return Ok((IEnumerable<ChannelStatisticsDto>)statistics);
    }

    public async Task<Result<IEnumerable<StatusStatisticsDto>>> GetStatisticsByStatusAsync(DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default)
    {
        var query = _notificationRepository.AsQueryable();

        if (startDate.HasValue)
            query = query.Where(n => n.CreationTime >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(n => n.CreationTime <= endDate.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        var rawStatistics = await query
            .GroupBy(n => n.Status)
            .Select(g => new
            {
                Status = g.Key,
                Count = g.Count()
            })
            .ToListAsync(cancellationToken);

        var statistics = rawStatistics.Select(s => new StatusStatisticsDto
        {
            Status = s.Status,
            Count = s.Count,
            Percentage = totalCount > 0 ? (double)s.Count / totalCount * 100 : 0
        }).ToList();

        return Ok((IEnumerable<StatusStatisticsDto>)statistics);
    }

    /// <summary><see cref="GetFailedNotificationsAsync"/> 未给或给了非正数时返回的条数。</summary>
    internal const int DefaultFailedTop = 100;

    /// <summary>
    /// <see cref="GetFailedNotificationsAsync"/> 一次最多返回的条数。
    /// </summary>
    /// <remarks>
    /// 每条消息连同它的全部收件人与附件一起加载：<c>top=1000000</c> 在一个大批次失败过的库上就是把整张表
    /// 连带两张子表读进内存再序列化出去，一次 GET 放倒一个进程。超出按上限截断（与分页 DTO 的 PageSize 同一口径）。
    /// </remarks>
    internal const int MaxFailedTop = 200;

    public async Task<Result<IEnumerable<NotificationInfo>>> GetFailedNotificationsAsync(DateTime? startDate = null, DateTime? endDate = null, int top = 100, CancellationToken cancellationToken = default)
    {
        top = top <= 0 ? DefaultFailedTop : Math.Min(top, MaxFailedTop);

        var query = _notificationRepository
            .AsQueryable()
            .Where(n => n.Status == NotificationStatus.Failed);

        if (startDate.HasValue)
            query = query.Where(n => n.CreationTime >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(n => n.CreationTime <= endDate.Value);

        var notifications = await query
            .OrderByDescending(n => n.CreationTime)
            .Take(top)
            .Include(n => n.Recipients)
            .Include(n => n.Attachments)
            .ToListAsync(cancellationToken);

        var notificationInfos = RecipientAddressMask.Apply(notifications.MapToList<NotificationInfo>());

        return Ok((IEnumerable<NotificationInfo>)notificationInfos);
    }

    public async Task<Result<IPagedList<NotificationInfo>>> GetScheduledAsync(QueryNotificationRequest request, CancellationToken cancellationToken = default)
    {
        var query = _notificationRepository.AsQueryable().AsNoTracking()
            .Where(n => n.Status == NotificationStatus.Scheduled);

        if (request.Type.HasValue)
            query = query.Where(n => n.Type == request.Type.Value);

        if (!string.IsNullOrWhiteSpace(request.Category))
            query = query.Where(n => n.Category == request.Category);

        if (request.Priority.HasValue)
            query = query.Where(n => n.Priority == request.Priority.Value);

        if (request.StartTime.HasValue)
            query = query.Where(n => n.ScheduledTime >= request.StartTime.Value);

        if (request.EndTime.HasValue)
            query = query.Where(n => n.ScheduledTime <= request.EndTime.Value);

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.ToLower();
            query = query.Where(n => n.Subject.ToLower().Contains(keyword));
        }

        query = query.OrderBy(n => n.ScheduledTime);

        var paged = await query
            .ProjectTo<Message, NotificationInfo>()
            .CreateAsync(request, cancellationToken);

        return Ok(RecipientAddressMask.Apply(paged));
    }

    public async Task<Result<int>> BatchDeleteAsync(List<Guid> ids, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(ids);

        var notifications = await _notificationRepository
            .AsQueryable()
            .Where(n => ids.Contains(n.Id))
            .ToListAsync(cancellationToken);

        if (notifications.Count == 0)
            return Ok(0, "No notifications found to delete");

        await _notificationRepository.DeleteManyAsync(notifications, cancellationToken);
        return Ok(notifications.Count, $"{notifications.Count} notifications deleted");
    }

    public async Task<Result<NotificationTrendDto>> GetStatisticsTrendAsync(TrendInterval interval, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
    {
        if (endDate <= startDate)
            return Fail<NotificationTrendDto>("End date must be after start date", 400);

        var notifications = await _notificationRepository.AsQueryable().AsNoTracking()
            .Where(n => n.CreationTime >= startDate && n.CreationTime <= endDate)
            .Select(n => new { n.CreationTime, n.Status })
            .ToListAsync(cancellationToken);

        // 分桶与标签统一走核心 TimeBucket：桶对齐到自然日 / ISO 周（周一起）/ 自然月，
        // 而不是「从 startDate 起每 N 天」—— 后者会让"本周"随查询起点漂移，而标签却写着周序号。
        var dataPoints = new List<TrendDataPoint>();

        foreach (var bucketStart in TimeBucket.Enumerate(startDate, endDate, interval, endInclusive: false))
        {
            var bucketEnd = TimeBucket.Next(bucketStart, interval);

            var periodNotifications = notifications
                .Where(n => n.CreationTime >= bucketStart && n.CreationTime < bucketEnd)
                .ToList();

            dataPoints.Add(new TrendDataPoint
            {
                Label = TimeBucket.Label(bucketStart, interval),
                StartTime = bucketStart,
                TotalCount = periodNotifications.Count,
                SentCount = periodNotifications.Count(n => n.Status == NotificationStatus.Sent || n.Status == NotificationStatus.PartiallySent),
                FailedCount = periodNotifications.Count(n => n.Status == NotificationStatus.Failed)
            });
        }

        var trend = new NotificationTrendDto
        {
            Interval = interval,
            StartDate = startDate,
            EndDate = endDate,
            DataPoints = dataPoints
        };

        return Ok(trend);
    }

    public async Task<Result<DeliveryReportDto>> GetDeliveryReportAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.AsQueryable().AsNoTracking()
            .Include(m => m.Recipients)
            .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);

        if (notification == null)
            return Fail<DeliveryReportDto>("Notification not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        var recipients = notification.Recipients.ToList();

        var report = new DeliveryReportDto
        {
            MessageId = notification.Id,
            Subject = notification.Subject,
            Type = notification.Type,
            TotalRecipients = recipients.Count,
            SentCount = recipients.Count(r => r.Status == NotificationStatus.Sent),
            FailedCount = recipients.Count(r => r.Status == NotificationStatus.Failed),
            PendingCount = recipients.Count(r => r.Status == NotificationStatus.Pending || r.Status == NotificationStatus.Scheduled),
            ReadCount = recipients.Count(r => r.IsRead),
            Recipients = recipients.MapToList<RecipientOutput>()
        };

        report.SuccessRate = report.TotalRecipients > 0
            ? (double)report.SentCount / report.TotalRecipients
            : 0;

        return Ok(RecipientAddressMask.Apply(report));
    }
}
