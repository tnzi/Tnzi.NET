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

        return Ok(paged);
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
        return Ok(notificationInfo);
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

    public async Task<Result<IEnumerable<NotificationInfo>>> GetFailedNotificationsAsync(DateTime? startDate = null, DateTime? endDate = null, int top = 100, CancellationToken cancellationToken = default)
    {
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

        var notificationInfos = notifications.MapToList<NotificationInfo>();

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

        return Ok(paged);
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

        var dataPoints = new List<TrendDataPoint>();
        var current = startDate;

        while (current < endDate)
        {
            DateTime periodEnd;
            string label;

            switch (interval)
            {
                case TrendInterval.Daily:
                    periodEnd = current.AddDays(1);
                    label = current.ToString("yyyy-MM-dd");
                    break;
                case TrendInterval.Weekly:
                    periodEnd = current.AddDays(7);
                    var weekOfYear = System.Globalization.CultureInfo.InvariantCulture.Calendar
                        .GetWeekOfYear(current, System.Globalization.CalendarWeekRule.FirstDay, DayOfWeek.Monday);
                    label = $"{current:yyyy}-W{weekOfYear:D2}";
                    break;
                case TrendInterval.Monthly:
                    periodEnd = current.AddMonths(1);
                    label = current.ToString("yyyy-MM");
                    break;
                default:
                    periodEnd = current.AddDays(1);
                    label = current.ToString("yyyy-MM-dd");
                    break;
            }

            var periodNotifications = notifications
                .Where(n => n.CreationTime >= current && n.CreationTime < periodEnd)
                .ToList();

            dataPoints.Add(new TrendDataPoint
            {
                Label = label,
                StartTime = current,
                TotalCount = periodNotifications.Count,
                SentCount = periodNotifications.Count(n => n.Status == NotificationStatus.Sent || n.Status == NotificationStatus.PartiallySent),
                FailedCount = periodNotifications.Count(n => n.Status == NotificationStatus.Failed)
            });

            current = periodEnd;
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
}
