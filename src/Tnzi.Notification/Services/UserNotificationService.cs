namespace Tnzi.Notification.Services;

/// <summary>
/// 用户通知收件箱服务实现
/// </summary>
public class UserNotificationService : ApplicationService, IUserNotificationService
{
    private readonly IRepository<Recipient, Guid> _recipientRepository;

    public UserNotificationService(IRepository<Recipient, Guid> recipientRepository, IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _recipientRepository = Check.NotNull(recipientRepository);
    }

    public async Task<Result<IPagedList<UserNotificationItem>>> GetInboxAsync(Guid userId, QueryUserNotificationRequest request, CancellationToken cancellationToken = default)
    {
        IQueryable<Recipient> query = _recipientRepository
            .AsQueryable()
            .AsNoTracking()
            .Include(r => r.Message)
            .Where(r => r.UserId == userId && r.Status == NotificationStatus.Sent);

        if (request.Type.HasValue)
            query = query.Where(r => r.Message.Type == request.Type.Value);

        if (request.IsRead.HasValue)
            query = query.Where(r => r.IsRead == request.IsRead.Value);

        if (!string.IsNullOrWhiteSpace(request.Category))
            query = query.Where(r => r.Message.Category == request.Category);

        if (request.Priority.HasValue)
            query = query.Where(r => r.Message.Priority == request.Priority.Value);

        var paged = await query
            .OrderByDescending(r => r.Message.CreationTime)
            .Select(r => new UserNotificationItem
            {
                Id = r.Id,
                Type = r.Message.Type,
                Subject = r.Message.Subject,
                Category = r.Message.Category,
                Priority = r.Message.Priority,
                IsRead = r.IsRead,
                ReadTime = r.ReadTime,
                CreationTime = r.Message.CreationTime
            })
            .CreateAsync(request, cancellationToken);

        return Ok(paged);
    }

    public async Task<Result<UserNotificationDetail>> GetDetailAsync(Guid userId, Guid recipientId, CancellationToken cancellationToken = default)
    {
        var recipient = await _recipientRepository
            .AsQueryable(withTracking: true)
            .Include(r => r.Message)
            .Where(r => r.Id == recipientId && r.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

        if (recipient == null)
            return Fail<UserNotificationDetail>("Notification not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        // Auto mark as read on detail view
        if (!recipient.IsRead)
        {
            recipient.IsRead = true;
            recipient.ReadTime = DateTime.UtcNow;
            await _recipientRepository.UpdateAsync(recipient, cancellationToken);
        }

        var detail = new UserNotificationDetail
        {
            Id = recipient.Id,
            Type = recipient.Message.Type,
            Subject = recipient.Message.Subject,
            Content = recipient.Message.Content,
            IsHtml = recipient.Message.IsHtml,
            Category = recipient.Message.Category,
            Priority = recipient.Message.Priority,
            IsRead = true,
            ReadTime = recipient.ReadTime,
            CreationTime = recipient.Message.CreationTime
        };

        return Ok(detail);
    }

    public async Task<Result> MarkAsReadAsync(Guid userId, Guid recipientId, CancellationToken cancellationToken = default)
    {
        var recipient = await _recipientRepository
            .AsQueryable()
            .Where(r => r.Id == recipientId && r.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

        if (recipient == null)
            return Fail("Notification not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        if (recipient.IsRead)
            return Ok("Notification already marked as read");

        recipient.IsRead = true;
        recipient.ReadTime = DateTime.UtcNow;
        await _recipientRepository.UpdateAsync(recipient, cancellationToken);

        return Ok("Notification marked as read");
    }

    public async Task<Result> MarkAllAsReadAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var unreadRecipients = await _recipientRepository
            .AsQueryable()
            .Where(r => r.UserId == userId && !r.IsRead && r.Status == NotificationStatus.Sent)
            .ToListAsync(cancellationToken);

        if (unreadRecipients.Count == 0)
            return Ok("No unread notifications");

        var now = DateTime.UtcNow;
        foreach (var recipient in unreadRecipients)
        {
            recipient.IsRead = true;
            recipient.ReadTime = now;
        }

        await _recipientRepository.UpdateManyAsync(unreadRecipients, cancellationToken);
        return Ok($"{unreadRecipients.Count} notifications marked as read");
    }

    public async Task<Result<UnreadCountDto>> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // ★ 总数是各分类之和，不单独 CountAsync：聚合时 Include 被忽略、不连 Message，消息的软删除过滤器也就不生效，
        //   单独数出来的总数会把管理员删掉的消息算进去，而列表与分类明细（经导航访问 Message）不算，
        //   角标永远比列表多、永远消不掉。按分类分组本身就经导航访问 Message，三处因此是同一个集合，还少一次查询。
        var unreadByCategory = await _recipientRepository
            .AsQueryable()
            .AsNoTracking()
            .Where(r => r.UserId == userId && !r.IsRead && r.Status == NotificationStatus.Sent)
            .GroupBy(r => r.Message.Category)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var dto = new UnreadCountDto
        {
            TotalUnread = unreadByCategory.Sum(x => x.Count),
            UnreadByCategory = unreadByCategory.ToDictionary(x => x.Category, x => x.Count)
        };

        return Ok(dto);
    }

    public async Task<Result> DeleteAsync(Guid userId, Guid recipientId, CancellationToken cancellationToken = default)
    {
        var recipient = await _recipientRepository
            .AsQueryable()
            .Where(r => r.Id == recipientId && r.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

        if (recipient == null)
            return Fail("Notification not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        await _recipientRepository.DeleteAsync(recipient, cancellationToken);
        return Ok("Notification deleted");
    }

    public async Task<Result> BatchMarkAsReadAsync(Guid userId, List<Guid> recipientIds, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(recipientIds);

        var recipients = await _recipientRepository
            .AsQueryable()
            .Where(r => recipientIds.Contains(r.Id) && r.UserId == userId && !r.IsRead)
            .ToListAsync(cancellationToken);

        if (recipients.Count == 0)
            return Ok("No unread notifications to mark");

        var now = DateTime.UtcNow;
        foreach (var recipient in recipients)
        {
            recipient.IsRead = true;
            recipient.ReadTime = now;
        }

        await _recipientRepository.UpdateManyAsync(recipients, cancellationToken);
        return Ok($"{recipients.Count} notifications marked as read");
    }

    public async Task<Result> BatchDeleteAsync(Guid userId, List<Guid> recipientIds, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(recipientIds);

        var recipients = await _recipientRepository
            .AsQueryable()
            .Where(r => recipientIds.Contains(r.Id) && r.UserId == userId)
            .ToListAsync(cancellationToken);

        if (recipients.Count == 0)
            return Ok("No notifications to delete");

        await _recipientRepository.DeleteManyAsync(recipients, cancellationToken);
        return Ok($"{recipients.Count} notifications deleted");
    }
}
