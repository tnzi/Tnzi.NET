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
            .Select(ListRow)
            .CreateAsync(request, cancellationToken);

        return Ok(paged);
    }

    /// <summary>
    /// 列表一行：消息本身的全部字段，<b>不带</b>收件人与附件（两者在列表行上恒为空集合）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★★ 此前两个分页查询都 <c>ProjectTo&lt;Message, NotificationInfo&gt;()</c>，而 DTO 上挂着两个集合：
    /// 投影把它们一起翻成 SQL，每一行带出它的<b>全部</b>收件人（附件再乘一遍），整份 HTML 正文按收件人行数重复。
    /// 一个跑过几场两万人群发的租户，打开一页 20 行的消息列表就要搬几十万行：而管理端列表只用成功数 / 总数两个计数。
    /// 要看哪些人、为什么，有投递报告；要一条消息的完整收件人，有按 Id 取详情。
    /// </para>
    /// <para>
    /// ★ 显式列出字段而不是交给映射器：要排除的恰恰是映射器会自动带上的那两个集合。新增一个标量字段时要在这里补一行：
    /// <c>NotificationListRowTests</c> 用反射对照详情接口逐个字段比，漏补即红。
    /// </para>
    /// </remarks>
    internal static readonly Expression<Func<Message, NotificationInfo>> ListRow = m => new NotificationInfo
    {
        Id = m.Id,
        Type = m.Type,
        Subject = m.Subject,
        Content = m.Content,
        IsHtml = m.IsHtml,
        Status = m.Status,
        SentTime = m.SentTime,
        FailureReason = m.FailureReason,
        RetryCount = m.RetryCount,
        MaxRetryCount = m.MaxRetryCount,
        TotalRecipientCount = m.TotalRecipientCount,
        SuccessCount = m.SuccessCount,
        FailureCount = m.FailureCount,
        CreationTime = m.CreationTime,
        Priority = m.Priority,
        SenderId = m.SenderId,
        Category = m.Category,
        IsTransactional = m.IsTransactional,
        TemplateName = m.TemplateName,
        ProviderKey = m.ProviderKey,
        ScheduledTime = m.ScheduledTime,
    };

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

        // 与 QueryAsync 同一行形状：不带收件人与附件（理由见 ListRow）。
        var paged = await query
            .Select(ListRow)
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

        // ★ 在库里按自然日聚合，内存里只折叠日汇总。此前把区间内每一条消息读进内存、再对每个桶整表过滤一遍：
        //   区间不设上限，一年的按日趋势在日发五万条的部署上是一千八百万行、扫 365 遍。
        //   三种粒度的桶都对齐到自然日边界（日 / ISO 周一 / 月一日），所以一天的汇总整天落在同一个桶里，折叠结果与逐行分桶相同。
        var dailyAggregates = await _notificationRepository.AsQueryable().AsNoTracking()
            .Where(n => n.CreationTime >= startDate && n.CreationTime <= endDate)
            .GroupBy(n => n.CreationTime.Date)
            .Select(g => new
            {
                Date = g.Key,
                TotalCount = g.Count(),
                SentCount = g.Count(n => n.Status == NotificationStatus.Sent || n.Status == NotificationStatus.PartiallySent),
                FailedCount = g.Count(n => n.Status == NotificationStatus.Failed)
            })
            .ToListAsync(cancellationToken);

        // 分桶与标签统一走核心 TimeBucket：桶对齐到自然日 / ISO 周（周一起）/ 自然月，
        // 而不是「从 startDate 起每 N 天」—— 后者会让"本周"随查询起点漂移，而标签却写着周序号。
        var dataPoints = new List<TrendDataPoint>();

        foreach (var bucketStart in TimeBucket.Enumerate(startDate, endDate, interval, endInclusive: false))
        {
            var bucketEnd = TimeBucket.Next(bucketStart, interval);

            var days = dailyAggregates
                .Where(d => d.Date >= bucketStart && d.Date < bucketEnd)
                .ToList();

            dataPoints.Add(new TrendDataPoint
            {
                Label = TimeBucket.Label(bucketStart, interval),
                StartTime = bucketStart,
                TotalCount = days.Sum(d => d.TotalCount),
                SentCount = days.Sum(d => d.SentCount),
                FailedCount = days.Sum(d => d.FailedCount)
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

    /// <summary>
    /// 投递报告一次最多列出的收件人行数：失败的排最前，其次被拦下的（<c>Cancelled</c>），然后其余。
    /// </summary>
    /// <remarks>
    /// ★ 计数（总数 / 已送达 / 失败 / 待发 / 已读）始终按<b>全部</b>收件人在库里聚合，只有明细行受这个上限约束。
    /// 一次两万人群发的报告此前把两万行连同每一行的全部字段读进内存、整份序列化出去，而管理端抽屉只渲染前 200 行。
    /// 报告要回答的是「哪些人、为什么」，失败的那些排在最前就已经答完了；明细是否完整，看
    /// <see cref="DeliveryReportDto.TotalRecipients"/> 与返回的行数是否相等。完整名单走按 Id 取详情。
    /// </remarks>
    internal const int MaxReportRecipients = 200;

    public async Task<Result<DeliveryReportDto>> GetDeliveryReportAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.AsQueryable().AsNoTracking()
            .Where(m => m.Id == messageId)
            .Select(m => new { m.Id, m.Subject, m.Type })
            .FirstOrDefaultAsync(cancellationToken);

        if (notification == null)
            return Fail<DeliveryReportDto>("Notification not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        var recipients = _notificationRepository.AsQueryable().AsNoTracking()
            .Where(m => m.Id == messageId)
            .SelectMany(m => m.Recipients);

        // 计数在库里聚合，不把收件人搬进内存。
        var counts = await recipients
            .GroupBy(r => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Sent = g.Count(r => r.Status == NotificationStatus.Sent),
                Failed = g.Count(r => r.Status == NotificationStatus.Failed),
                Pending = g.Count(r => r.Status == NotificationStatus.Pending || r.Status == NotificationStatus.Scheduled),
                Read = g.Count(r => r.IsRead),
            })
            .FirstOrDefaultAsync(cancellationToken);

        var rows = await recipients
            .OrderBy(r => r.Status == NotificationStatus.Failed ? 0 : r.Status == NotificationStatus.Cancelled ? 1 : 2)
            .ThenBy(r => r.Id)
            .Take(MaxReportRecipients)
            .ToListAsync(cancellationToken);

        var report = new DeliveryReportDto
        {
            MessageId = notification.Id,
            Subject = notification.Subject,
            Type = notification.Type,
            TotalRecipients = counts?.Total ?? 0,
            SentCount = counts?.Sent ?? 0,
            FailedCount = counts?.Failed ?? 0,
            PendingCount = counts?.Pending ?? 0,
            ReadCount = counts?.Read ?? 0,
            Recipients = rows.MapToList<RecipientOutput>()
        };

        report.SuccessRate = report.TotalRecipients > 0
            ? (double)report.SentCount / report.TotalRecipients
            : 0;

        return Ok(RecipientAddressMask.Apply(report));
    }
}
