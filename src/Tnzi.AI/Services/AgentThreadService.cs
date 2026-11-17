using AgentThreadEntity = Tnzi.AI.Entities.AgentThread;

namespace Tnzi.AI.Services;

/// <summary>
/// Agent 线程管理服务实现 - 支持消息历史持久化
/// </summary>
public class AgentThreadService : ApplicationService, IAgentThreadService, IAgentThreadInternalService
{
    private readonly IRepository<AgentThreadEntity, Guid> _repository;
    private readonly IRepository<AgentThreadMessage, Guid> _messageRepository;
    private readonly IRepository<Agent, Guid> _agentRepository;
    private readonly ICurrentTenant? _currentTenant;
    private readonly IOptions<MultiTenancyOptions>? _multiTenancyOptions;
    private readonly IRepository<AgentAttachment, Guid>? _attachmentRepository;

    /// <summary>
    /// 按 threadId 的消息写入互斥锁，防止并发写入产生相同 Order
    /// </summary>
    private static readonly KeyedAsyncLock _messageOrderLock = new();

    public AgentThreadService(
        IRepository<AgentThreadEntity, Guid> repository,
        IRepository<AgentThreadMessage, Guid> messageRepository,
        IRepository<Agent, Guid> agentRepository,
        IServiceProvider serviceProvider,
        ICurrentTenant? currentTenant = null,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null,
        IRepository<AgentAttachment, Guid>? attachmentRepository = null)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _messageRepository = Check.NotNull(messageRepository);
        _agentRepository = Check.NotNull(agentRepository);
        _currentTenant = currentTenant;
        _multiTenancyOptions = multiTenancyOptions;
        _attachmentRepository = attachmentRepository;
    }

    /// <summary>
    /// 按 Id 找调用者可用的 Agent：本租户的，外加（租户调用者）宿主级共享定义 ——
    /// 与 <c>AgentResolver</c> 同一条可见性，否则租户能解析共享 Agent 却建不出它的会话线程。
    /// </summary>
    private async Task<Agent?> FindVisibleAgentAsync(Guid agentId, CancellationToken ct = default)
    {
        var scope = SharedAgentScope.Resolve(_multiTenancyOptions, _currentTenant, CurrentUser);
        return scope.IsTenantCaller
            ? await scope.Apply(_agentRepository.AsQueryable()).FirstOrDefaultAsync(a => a.Id == agentId, ct)
            : await _agentRepository.GetAsync(agentId, ct);
    }

    public async Task<Result<AgentThreadDto>> CreateAsync(CreateAgentThreadDto input)
    {
        Check.NotNull(input);

        var origin = ThreadOrigin.Normalize(input.Origin);
        if (!origin.Succeeded)
        {
            return Fail<AgentThreadDto>(origin.Message!, origin.Code ?? 400, origin.ErrorCode);
        }

        // Listing 不从请求体读（[JsonIgnore]），只有服务端代码给：不认识的值是调用方的缺陷
        if (!Enum.IsDefined(input.Listing))
        {
            throw new ArgumentOutOfRangeException(nameof(input), input.Listing, "Unknown thread listing.");
        }

        // 仅当提供 AgentId 时验证 Agent 存在
        Agent? agent = null;
        if (input.AgentId.HasValue)
        {
            agent = await FindVisibleAgentAsync(input.AgentId.Value);
            if (agent == null)
            {
                return Fail<AgentThreadDto>("Agent not found", 404, ErrorCodes.AgentNotFound);
            }
        }

        var entity = new AgentThreadEntity
        {
            AgentId = input.AgentId,
            Title = input.Title ?? $"Thread {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
            Origin = origin.Data,
            Listing = input.Listing,
            LastActivityTime = DateTime.UtcNow
        };

        await _repository.InsertAsync(entity);

        Logger.LogInformation("Agent thread created: {ThreadId}, AgentId: {AgentId}", entity.Id, entity.AgentId);

        var dto = entity.MapTo<AgentThreadDto>();
        dto.AgentName = agent?.Name;
        return Ok(dto);
    }

    public async Task<bool> IsOwnerAsync(Guid threadId, Guid userId)
    {
        return await _repository.Where(t => t.Id == threadId && t.CreatorId == userId).AnyAsync();
    }

    public async Task<Result<AgentThreadDto>> GetByIdAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        if (entity == null)
        {
            return Fail<AgentThreadDto>("Thread not found", 404, ErrorCodes.ThreadNotFound);
        }

        var dto = ToThreadDto(entity);
        dto.MessageCount = await _messageRepository.Where(m => m.ThreadId == id).CountAsync();
        dto.Running = await IsRunningAsync(id);
        dto.AwaitingOwner = await IsAwaitingOwnerAsync(id, entity.CreatorId);

        if (entity.AgentId.HasValue)
        {
            var agent = await FindVisibleAgentAsync(entity.AgentId.Value);
            dto.AgentName = agent?.Name;
        }

        return Ok(dto);
    }

    public async Task<Result<AgentThreadDetailDto>> GetDetailAsync(Guid id, int messageLimit = 50)
    {
        var entity = await _repository.AsQueryable()
            .Include(t => t.Agent)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (entity == null)
        {
            return Fail<AgentThreadDetailDto>("Thread not found", 404, ErrorCodes.ThreadNotFound);
        }

        var totalMessageCount = await _messageRepository
            .Where(m => m.ThreadId == id)
            .CountAsync();

        // 获取最近 N 条消息（取降序 top N，再内存中反转为正序）
        var recentMessages = await _messageRepository
            .Where(m => m.ThreadId == id)
            .OrderByDescending(m => m.Order)
            .Take(messageLimit)
            .ToListAsync();
        recentMessages.Reverse();

        var dto = new AgentThreadDetailDto
        {
            Id = entity.Id,
            AgentId = entity.AgentId,
            AgentName = entity.Agent?.Name,
            Title = entity.Title,
            TitlePending = entity.TitleSource == ThreadTitleSource.Generating,
            Origin = entity.Origin,
            Metadata = entity.Metadata,
            MessageCount = totalMessageCount,
            LastActivityTime = entity.LastActivityTime,
            LastUserMessageTime = entity.LastUserMessageTime,
            CreationTime = entity.CreationTime,
            ArchivedTime = entity.ArchivedTime,
            Listing = entity.Listing,
            Running = await IsRunningAsync(id),
            AwaitingOwner = await IsAwaitingOwnerAsync(id, entity.CreatorId),
            Unread = IsUnread(entity.ReplyTime, entity.ReadTime),
            Messages = ToMessageDtos(recentMessages)
        };

        await FillAttachmentsAsync(dto.Messages);
        return Ok(dto);
    }

    /// <inheritdoc />
    public async Task<Result<ThreadMessagePageDto>> GetMessagesAsync(Guid id, int? beforeOrder, int limit)
    {
        var take = Math.Clamp(limit, 1, 200);

        var query = _messageRepository.Where(m => m.ThreadId == id);
        if (beforeOrder is { } before)
        {
            query = query.Where(m => m.Order < before);
        }

        // 多取一条判断还有没有更早的，免得客户端为了知道「到头了」再发一次请求
        var page = await query
            .OrderByDescending(m => m.Order)
            .Take(take + 1)
            .ToListAsync();

        var hasMore = page.Count > take;
        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        page.Reverse();
        var messages = ToMessageDtos(page);
        await FillAttachmentsAsync(messages);

        return Ok(new ThreadMessagePageDto { Messages = messages, HasMore = hasMore });
    }

    /// <summary>
    /// 消息实体 → DTO：存档的每轮元数据从 JSON 读回成对象（名字不同，映射器不会碰它）。
    /// </summary>
    private static List<ThreadMessageDto> ToMessageDtos(List<AgentThreadMessage> entities)
    {
        var dtos = entities.MapToList<ThreadMessageDto>();
        for (var i = 0; i < dtos.Count; i++)
        {
            dtos[i].Metadata = ChatTurnMetadata.Parse(entities[i].MetadataJson);
        }

        return dtos;
    }

    /// <summary>
    /// 给一页消息补上附件清单（一次查询，不带内容）。
    /// </summary>
    private async Task FillAttachmentsAsync(List<ThreadMessageDto> messages)
    {
        if (_attachmentRepository is null || messages.Count == 0)
        {
            return;
        }

        var ids = messages.Select(m => m.Id).ToList();
        var attachments = await _attachmentRepository.AsQueryable()
            .Where(a => a.MessageId != null && ids.Contains(a.MessageId.Value))
            .OrderBy(a => a.CreationTime)
            .ThenBy(a => a.Id)
            .Select(a => new
            {
                MessageId = a.MessageId!.Value,
                Dto = new AttachmentDto
                {
                    Id = a.Id,
                    Origin = a.Origin,
                    MediaType = a.MediaType,
                    FileName = a.FileName,
                    Size = a.Size,
                    ToolCallId = a.ToolCallId
                }
            })
            .ToListAsync();

        var byMessage = attachments.ToLookup(a => a.MessageId, a => a.Dto);
        foreach (var message in messages)
        {
            var list = byMessage[message.Id].ToList();
            message.Attachments = list.Count > 0 ? list : null;
        }
    }

    /// <inheritdoc />
    public async Task<ThreadMessageRef?> FindUserMessageAsync(Guid threadId, Guid? messageId, CancellationToken ct = default)
    {
        var userMessages = _messageRepository.Where(m => m.ThreadId == threadId && m.Role == MessageRole.User);

        var message = messageId is { } id
            ? await userMessages.Where(m => m.Id == id)
                .Select(m => new ThreadMessageRef(m.Id, m.Order, m.Content) { MetadataJson = m.MetadataJson })
                .FirstOrDefaultAsync(ct)
            : await userMessages.OrderByDescending(m => m.Order)
                .Select(m => new ThreadMessageRef(m.Id, m.Order, m.Content) { MetadataJson = m.MetadataJson })
                .FirstOrDefaultAsync(ct);

        return message;
    }

    /// <inheritdoc />
    /// <remarks>
    /// 与 <see cref="SaveMessageAsync"/> 共用同一把按线程的锁：回退与追加交错时，追加算出的 MAX(Order)+1
    /// 可能落在正被删掉的区间里。附件先删（它们挂在这些消息上），两条删除都是不经变更跟踪器的语句，先加入物理事务。
    /// </remarks>
    public async Task<int> DeleteMessagesFromAsync(Guid threadId, int fromOrder, CancellationToken ct = default)
    {
        await using var _ = await _messageOrderLock.LockAsync($"thread-msg:{threadId:N}", ct);

        var removed = await _messageRepository
            .Where(m => m.ThreadId == threadId && m.Order >= fromOrder)
            .Select(m => m.Id)
            .ToListAsync(ct);
        if (removed.Count == 0)
        {
            return 0;
        }

        await _messageRepository.EnsureTransactionStartedAsync(ct);

        if (_attachmentRepository is not null)
        {
            await _attachmentRepository.AsQueryable()
                .Where(a => a.MessageId != null && removed.Contains(a.MessageId.Value))
                .ExecuteDeleteAsync(ct);
        }

        var deleted = await _messageRepository.AsQueryable()
            .Where(m => m.ThreadId == threadId && m.Order >= fromOrder)
            .ExecuteDeleteAsync(ct);

        Logger.LogInformation("Thread {ThreadId} rewound: removed {Count} messages from order {Order}", threadId, deleted, fromOrder);
        return deleted;
    }

    public async Task<Result<IPagedList<AgentThreadDto>>> GetListAsync(ThreadListQueryDto query)
    {
        var q = _repository.AsQueryable();

        if (query.CreatorId.HasValue)
        {
            q = q.Where(t => t.CreatorId == query.CreatorId.Value);
        }

        if (query.AgentId.HasValue)
        {
            q = q.Where(t => t.AgentId == query.AgentId.Value);
        }

        if (query.AgentIds is { Count: > 0 })
        {
            var agentIds = query.AgentIds.Distinct().ToList();
            q = q.Where(t => t.AgentId != null && agentIds.Contains(t.AgentId.Value));
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.ToLower();
            q = q.Where(t => t.Title != null && t.Title.ToLower().Contains(keyword));
        }

        if (query.StartTime.HasValue)
        {
            q = q.Where(t => (t.LastUserMessageTime ?? t.CreationTime) >= query.StartTime.Value);
        }

        if (query.EndTime.HasValue)
        {
            q = q.Where(t => (t.LastUserMessageTime ?? t.CreationTime) <= query.EndTime.Value);
        }

        // 用户端列表只有在列表里的线程；服务端建的、还没等到主人说话的线程（以及永远不进列表的）只按 id 可用。
        // 管理端列表置 IncludeUnlisted 看全部
        if (!query.IncludeUnlisted)
        {
            q = q.Where(t => t.Listing == ThreadListing.Listed);
        }

        q = query.Status switch
        {
            ThreadListStatus.Active => q.Where(t => t.ArchivedTime == null),
            ThreadListStatus.Archived => q.Where(t => t.ArchivedTime != null),
            _ => q
        };

        // 先分层（在等主人 → 在跑 → 其余），排序在数据库里做：在等主人与在跑的线程整体排在最前，翻页不会把它们漏到后面的页上。
        // 两个集合都只是此刻还没了结的那一小撮（等回答的请求、活着的运行），作为参数集合带进查询
        var awaiting = await GetAwaitingOwnerThreadIdsAsync(query.CreatorId, null);
        var running = await GetRunningThreadIdsAsync(null);
        var ordered = OrderAsList(q, awaiting, running, query.Sort);
        // Project explicitly so AgentName resolves through the Agent navigation
        // (LEFT JOIN; null when the agent was deleted or the thread is agent-less).
        var pagedList = await ordered
            .Select(t => new AgentThreadDto
            {
                Id = t.Id,
                AgentId = t.AgentId,
                AgentName = t.Agent != null ? t.Agent.Name : null,
                Title = t.Title,
                TitlePending = t.TitleSource == ThreadTitleSource.Generating,
                Origin = t.Origin,
                LastActivityTime = t.LastActivityTime,
                LastUserMessageTime = t.LastUserMessageTime,
                CreationTime = t.CreationTime,
                ArchivedTime = t.ArchivedTime,
                Listing = t.Listing,
                Unread = t.ReplyTime != null && (t.ReadTime == null || t.ReadTime < t.ReplyTime)
            })
            .CreateAsync(query);

        // Batch-resolve message counts for the current page with a single GROUP BY
        // query (avoids a per-thread COUNT N+1).
        var threadIds = pagedList.Items.Select(t => t.Id).ToList();
        if (threadIds.Count > 0)
        {
            var messageCounts = await _messageRepository
                .Where(m => threadIds.Contains(m.ThreadId))
                .GroupBy(m => m.ThreadId)
                .Select(g => new { ThreadId = g.Key, Count = g.Count() })
                .ToListAsync();
            var countByThreadId = messageCounts.ToDictionary(c => c.ThreadId, c => c.Count);

            foreach (var dto in pagedList.Items)
            {
                dto.MessageCount = countByThreadId.GetValueOrDefault(dto.Id);
                // 与排序用的是同一份答案：行上的标记与它所在的层不会对不上
                dto.Running = running.Contains(dto.Id);
                dto.AwaitingOwner = awaiting.Contains(dto.Id);
            }
        }

        return Ok(pagedList);
    }

    public async Task<Result<AgentThreadDto>> UpdateTitleAsync(Guid id, string title)
    {
        var entity = await _repository.GetAsync(id);
        if (entity == null)
        {
            return Fail<AgentThreadDto>("Thread not found", 404, ErrorCodes.ThreadNotFound);
        }

        // 用户起的名字钉住：之后的自动标题（回退 / AI）都不再替换它。
        // 只写这两列（不走整行 UpdateAsync）：后台起标题与本请求各自持有一份实体，整行写会互相覆盖
        entity.Title = title;
        entity.TitleSource = ThreadTitleSource.User;
        await _repository.SaveChangesAsync();

        Logger.LogInformation("Agent thread title updated: {ThreadId}, Title: {Title}", id, title);

        return Ok(ToThreadDto(entity));
    }

    public async Task<Result<AgentThreadDto>> SetArchivedAsync(Guid id, bool archived)
    {
        var entity = await _repository.GetAsync(id);
        if (entity == null)
        {
            return Fail<AgentThreadDto>("Thread not found", 404, ErrorCodes.ThreadNotFound);
        }

        var archivedTime = archived ? entity.ArchivedTime ?? DateTime.UtcNow : (DateTime?)null;
        if (archivedTime != entity.ArchivedTime)
        {
            entity.ArchivedTime = archivedTime;
            // 只写改了的列：整行写会把后台刚起好的标题改回这份实体读到时的样子
            await _repository.SaveChangesAsync();
            Logger.LogInformation("Agent thread {Action}: {ThreadId}", archived ? "archived" : "unarchived", id);
        }

        return Ok(ToThreadDto(entity));
    }

    /// <inheritdoc />
    public async Task<Result> MarkReadAsync(Guid id)
    {
        var now = DateTime.UtcNow;
        await _repository.EnsureTransactionStartedAsync();
        // 只写这一列、只往后走：两个标签页同时标记，晚到的那个旧时间不会把已读往回拨
        var written = await _repository.AsQueryable()
            .Where(t => t.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ReadTime, t => t.ReadTime == null || t.ReadTime < now ? now : t.ReadTime));
        return written == 0 ? Fail("Thread not found", 404, ErrorCodes.ThreadNotFound) : Ok();
    }

    /// <inheritdoc />
    public async Task<Result<ThreadAttentionDto>> GetAttentionAsync(ThreadAttentionQueryDto query)
    {
        Check.NotNull(query);

        // 与用户端列表同一个范围：不在列表里的线程不计（在跑也好、未读也好），否则计数指向列表里找不到的对话
        var q = _repository.AsQueryable().Where(t => t.Listing == ThreadListing.Listed);
        if (query.CreatorId.HasValue)
        {
            q = q.Where(t => t.CreatorId == query.CreatorId.Value);
        }

        if (query.AgentIds is { Count: > 0 })
        {
            var agentIds = query.AgentIds.Distinct().ToList();
            q = q.Where(t => t.AgentId != null && agentIds.Contains(t.AgentId.Value));
        }

        var unread = q.Where(t => t.ReplyTime != null && (t.ReadTime == null || t.ReadTime < t.ReplyTime));
        var unreadCount = await unread.CountAsync();
        var unreadIds = unreadCount == 0
            ? []
            : await unread
                .OrderByDescending(t => t.ReplyTime)
                .ThenByDescending(t => t.Id)
                .Select(t => t.Id)
                .Take(ThreadAttentionDto.MaxThreadIds)
                .ToListAsync();

        // 在等主人与在跑的线程都没几条：先问提供者与调度器有哪些，再按主人与 Agent 筛，按列表的顺序给出
        var awaiting = await GetAwaitingOwnerThreadIdsAsync(query.CreatorId, null);
        var awaitingIds = awaiting.Count == 0
            ? []
            : await OrderWithinTier(q.Where(t => awaiting.Contains(t.Id)), ThreadListSort.LastUserMessage)
                .Select(t => t.Id)
                .ToListAsync();

        var running = await GetRunningThreadIdsAsync(null);
        var runningIds = running.Count == 0
            ? []
            : await OrderWithinTier(q.Where(t => running.Contains(t.Id)), ThreadListSort.LastUserMessage)
                .Select(t => t.Id)
                .ToListAsync();

        return Ok(new ThreadAttentionDto
        {
            AwaitingOwnerCount = awaitingIds.Count,
            RunningCount = runningIds.Count,
            UnreadCount = unreadCount,
            AwaitingOwnerThreadIds = awaitingIds.Take(ThreadAttentionDto.MaxThreadIds).ToList(),
            RunningThreadIds = runningIds.Take(ThreadAttentionDto.MaxThreadIds).ToList(),
            UnreadThreadIds = unreadIds
        });
    }

    /// <inheritdoc />
    public async Task<Result> SetListingAsync(Guid threadId, ThreadListing listing, CancellationToken ct = default)
    {
        // 只有服务端代码给这个值：不认识的值是调用方的缺陷，不是请求错误
        if (!Enum.IsDefined(listing))
        {
            throw new ArgumentOutOfRangeException(nameof(listing), listing, "Unknown thread listing.");
        }

        await _repository.EnsureTransactionStartedAsync(ct);
        // 只写这一列：整行写会把后台刚起好的标题改回这份实体读到时的样子
        var written = await _repository.AsQueryable()
            .Where(t => t.Id == threadId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Listing, listing), ct);
        if (written == 0)
        {
            return Fail("Thread not found", 404, ErrorCodes.ThreadNotFound);
        }

        Logger.LogInformation("Agent thread {ThreadId} listing set to {Listing}", threadId, listing);
        return Ok();
    }

    /// <inheritdoc />
    /// <remarks>
    /// 一条带条件的 <c>UPDATE</c>：只动这位主人的、还在等主人说话的线程。并发的两轮只有一轮写下它，事件也只发一次；
    /// 别人的线程 id 在这里什么都不改（归属在条件里，不靠调用方先查）。
    /// </remarks>
    public async Task<bool> ListOnUserMessageAsync(Guid threadId, Guid userId, CancellationToken ct = default)
    {
        await _repository.EnsureTransactionStartedAsync(ct);
        var written = await _repository.AsQueryable()
            .Where(t => t.Id == threadId && t.CreatorId == userId && t.Listing == ThreadListing.UntilUserMessage)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Listing, ThreadListing.Listed), ct);
        if (written == 0)
        {
            return false;
        }

        Logger.LogInformation("Agent thread {ThreadId} listed: its owner wrote in it", threadId);
        try
        {
            if (EventBus is { } eventBus)
            {
                await eventBus.PublishAsync(new ThreadListedEvent { ThreadId = threadId, UserId = userId });
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "Failed to publish ThreadListedEvent for thread {ThreadId}", threadId);
        }

        return true;
    }

    /// <inheritdoc />
    public async Task MarkRepliedAsync(Guid threadId, DateTime repliedAt, CancellationToken ct = default)
    {
        await _repository.EnsureTransactionStartedAsync(ct);
        await _repository.AsQueryable()
            .IgnoreQueryFilters()
            .Where(t => t.Id == threadId && (t.ReplyTime == null || t.ReplyTime < repliedAt))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ReplyTime, (DateTime?)repliedAt), ct);
    }

    /// <summary>实体 → 线程 DTO，带上未读（在跑要问调度器，由调用方需要时补）。</summary>
    private static AgentThreadDto ToThreadDto(AgentThreadEntity entity)
    {
        var dto = entity.MapTo<AgentThreadDto>();
        dto.Unread = IsUnread(entity.ReplyTime, entity.ReadTime);
        return dto;
    }

    /// <summary>服务端写下的回答晚于主人最近一次看过这条线程。与列表与关注状态里的查询谓词是同一条判定。</summary>
    private static bool IsUnread(DateTime? replyTime, DateTime? readTime)
        => replyTime is { } replied && (readTime is not { } read || read < replied);

    private async Task<bool> IsRunningAsync(Guid threadId)
        => (await GetRunningThreadIdsAsync([threadId])).Contains(threadId);

    private async Task<bool> IsAwaitingOwnerAsync(Guid threadId, Guid? ownerId)
        => (await GetAwaitingOwnerThreadIdsAsync(ownerId, [threadId])).Contains(threadId);

    /// <summary>
    /// 列表的顺序：先分层 —— 在等主人的（0）、有一轮在跑的（1）、其余的（2）—— 再按 <paramref name="sort"/> 排层内。
    /// 客户端（<c>@tnzi/ui-ai</c> 的 <c>compareThreads</c>）按同一条规则排它手里的行，乐观更新因此落在下一次读取会放的位置。
    /// </summary>
    private static IOrderedQueryable<AgentThreadEntity> OrderAsList(
        IQueryable<AgentThreadEntity> query, IReadOnlySet<Guid> awaiting, IReadOnlySet<Guid> running, ThreadListSort sort)
    {
        if (awaiting.Count == 0 && running.Count == 0)
        {
            return OrderWithinTier(query, sort);
        }

        var awaitingIds = awaiting.ToList();
        var runningIds = running.Where(id => !awaiting.Contains(id)).ToList();
        var tiered = query.OrderBy(t => awaitingIds.Contains(t.Id) ? 0 : runningIds.Contains(t.Id) ? 1 : 2);
        return sort switch
        {
            ThreadListSort.CreationTime => tiered.ThenByDescending(t => t.CreationTime).ThenByDescending(t => t.Id),
            ThreadListSort.Title => tiered.ThenBy(t => t.Title == null ? null : t.Title.ToLower())
                .ThenByDescending(t => t.CreationTime).ThenByDescending(t => t.Id),
            _ => tiered.ThenByDescending(t => t.LastUserMessageTime ?? t.CreationTime)
                .ThenByDescending(t => t.CreationTime).ThenByDescending(t => t.Id)
        };
    }

    /// <summary>一层之内的顺序；同值时新建的在前，再以 Id 收尾，同一时刻 / 同名的行在两页之间不漂移。</summary>
    private static IOrderedQueryable<AgentThreadEntity> OrderWithinTier(IQueryable<AgentThreadEntity> query, ThreadListSort sort)
        => sort switch
        {
            ThreadListSort.CreationTime => query.OrderByDescending(t => t.CreationTime).ThenByDescending(t => t.Id),
            ThreadListSort.Title => query.OrderBy(t => t.Title == null ? null : t.Title.ToLower())
                .ThenByDescending(t => t.CreationTime).ThenByDescending(t => t.Id),
            _ => query.OrderByDescending(t => t.LastUserMessageTime ?? t.CreationTime)
                .ThenByDescending(t => t.CreationTime).ThenByDescending(t => t.Id)
        };

    /// <summary>
    /// 哪些线程此刻在等主人：所有 <see cref="IThreadAwaitingOwnerProvider"/> 的答案的并集。
    /// 用时才解析：应用的提供者可能依赖线程服务，构造时注入会成环。一个提供者读不出来时按它什么也没报处理。
    /// </summary>
    private async Task<IReadOnlySet<Guid>> GetAwaitingOwnerThreadIdsAsync(Guid? ownerId, IReadOnlyCollection<Guid>? threadIds)
    {
        var result = new HashSet<Guid>();
        if (threadIds is { Count: 0 })
        {
            return result;
        }

        var query = new ThreadAwaitingOwnerQuery(ownerId, threadIds);
        foreach (var provider in ServiceProvider.GetServices<IThreadAwaitingOwnerProvider>())
        {
            try
            {
                result.UnionWith(await provider.GetThreadIdsAsync(query) ?? []);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 在等主人只是一个排序与提示：读不出来时列表照常返回，只是不标
                Logger.LogWarning(ex, "Could not read which threads await their owner from {Provider}", provider.GetType().Name);
            }
        }

        return result;
    }

    /// <summary>
    /// 哪些线程上有一轮在服务端跑；调度器说不出来（没加载外部执行、或自定义调度器没实现）时为空。
    /// 用时才解析调度器：它的实现依赖线程服务，构造时注入会成环。
    /// </summary>
    private async Task<IReadOnlySet<Guid>> GetRunningThreadIdsAsync(IReadOnlyCollection<Guid>? threadIds)
    {
        var dispatcher = ServiceProvider.GetService<ICliAgentDispatcher>();
        if (dispatcher is null)
        {
            return new HashSet<Guid>();
        }

        try
        {
            return await dispatcher.GetRunningThreadIdsAsSystemAsync(threadIds) ?? new HashSet<Guid>();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 在跑只是一个提示：读不出来时列表照常返回，只是不标
            Logger.LogWarning(ex, "Could not read which threads have a run going on");
            return new HashSet<Guid>();
        }
    }

    public async Task<Result> DeleteAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        if (entity == null)
        {
            return Fail("Thread not found", 404, ErrorCodes.ThreadNotFound);
        }

        // 旧版本在这一列里存过一份对话正文的副本（早已无人读取）；软删除只留下行，不能把正文也留下
        entity.SerializedData = null;
        await _repository.DeleteAsync(entity);

        // 发布 ThreadDeletedEvent 触发级联清理（静默失败）
        try
        {
            var eventBus = EventBus;
            if (eventBus != null)
            {
                await eventBus.PublishAsync(new ThreadDeletedEvent
                {
                    ThreadId = id,
                    UserId = entity.CreatorId,
                    TenantId = entity.TenantId,
                    AgentId = entity.AgentId
                });
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to publish ThreadDeletedEvent for thread {ThreadId}", id);
        }

        Logger.LogInformation("Agent thread deleted: {ThreadId}", id);
        return Ok();
    }

    public async Task<(Guid threadId, bool isNewThread)> GetOrCreateThreadAsync(
        Guid? threadId, Guid? agentId, string? origin, ThreadListing listing = ThreadListing.Listed, CancellationToken ct = default)
    {
        // 仅当提供 agentId 时验证 Agent 存在
        if (agentId.HasValue)
        {
            var agentDef = await FindVisibleAgentAsync(agentId.Value, ct);
            if (agentDef == null)
            {
                throw new BusinessException("Agent not found", ErrorCodes.AgentNotFound, 404);
            }
        }

        // 如果提供了 threadId，尝试从数据库加载
        if (threadId.HasValue)
        {
            var threadEntity = await _repository.GetAsync(threadId.Value, ct);

            // 所有权检查：AgentId 匹配 + CreatorId 匹配当前用户
            if (threadEntity == null)
            {
                Logger.LogWarning("Thread not found: ThreadId={ThreadId}", threadId.Value);
                throw new BusinessException("Thread not found", ErrorCodes.ThreadNotFound, 404);
            }

            // 用户归属校验：已认证用户只能访问自己创建的线程
            // CreatorId 为空的线程视为无主线程，已认证用户不可访问（防止跨用户泄漏）
            var currentUserId = ResolveRunOriginatorId();
            if (currentUserId.HasValue && threadEntity.CreatorId != currentUserId)
            {
                Logger.LogWarning("Thread ownership mismatch: ThreadId={ThreadId}, CreatorId={CreatorId}, CurrentUserId={CurrentUserId}", threadId.Value, threadEntity.CreatorId, currentUserId);
                throw new BusinessException("Thread not found", ErrorCodes.ThreadNotFound, 404);
            }

            if (agentId.HasValue)
            {
                // Agent-bound 模式：AgentId 必须匹配
                if (threadEntity.AgentId != agentId.Value)
                {
                    Logger.LogWarning("Thread agent mismatch: ThreadId={ThreadId}, Expected={AgentId}, Actual={ThreadAgentId}", threadId.Value, agentId.Value, threadEntity.AgentId);
                    throw new BusinessException("Thread not found", ErrorCodes.ThreadNotFound, 404);
                }
            }
            else
            {
                // Agent-less 模式：线程 AgentId 也必须为 null
                if (threadEntity.AgentId.HasValue)
                {
                    Logger.LogWarning("Thread is agent-bound but no agentId provided: ThreadId={ThreadId}, ThreadAgentId={ThreadAgentId}", threadId.Value, threadEntity.AgentId);
                    throw new BusinessException("Thread not found", ErrorCodes.ThreadNotFound, 404);
                }
            }

            // 历史由 HistoryMiddleware 按 AI:History:Store:MaxLoadedMessages 单独加载，这里只做解析与归属校验
            return (threadEntity.Id, false);
        }

        // 无 threadId，创建新的线程。来源先校验：形状不合规的值不该留下一条空线程
        var validOrigin = ThreadOrigin.Normalize(origin);
        if (!validOrigin.Succeeded)
        {
            throw new BusinessException(validOrigin.Message!, validOrigin.ErrorCode!, validOrigin.Code ?? 400);
        }

        if (!Enum.IsDefined(listing))
        {
            throw new ArgumentOutOfRangeException(nameof(listing), listing, "Unknown thread listing.");
        }

        // ★ CreatorId 显式按运行发起人写，不交给审计钩子：钩子只在为 null 时填环境用户，而后台子运行
        //   （spawn_agent）在父请求返回后建线程时作用域里没有环境用户，线程会落成无主；
        //   上面的读路径与续跑前置的 IsOwnerAsync 都按发起人比对，无主线程对它们永远不满足。
        var newEntity = new AgentThreadEntity
        {
            AgentId = agentId,
            CreatorId = ResolveRunOriginatorId(),
            Title = $"Thread {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
            Origin = validOrigin.Data,
            Listing = listing,
            LastActivityTime = DateTime.UtcNow
        };

        await _repository.InsertAsync(newEntity);

        Logger.LogDebug("Created new thread: {ThreadId} for Agent: {AgentId}", newEntity.Id, agentId);

        return (newEntity.Id, true);
    }

    /// <summary>
    /// 本轮运行的发起人：取当前运行请求的 UserId（DefaultChatController 已按环境用户收口；续跑以运行归属人的
    /// 身份跑，后台子运行没有环境用户只有请求用户），没有正在执行的请求才退回环境用户。
    /// 读路径的归属比对与新线程的 CreatorId 必须出自同一个答案，否则写进去的和查出来的不是同一个人。
    /// </summary>
    private Guid? ResolveRunOriginatorId()
        => ServiceProvider.GetService<IAgentExecutionContextAccessor>()?.CurrentRequest?.UserId ?? CurrentUser?.Id;

    /// <summary>
    /// 保存消息到线程。<paramref name="messageId"/> 非空时使用该 ID 持久化；为空时由
    /// framework SaveChangesAsync 自动生成 sequential GUID。返回最终持久化的 message ID。
    /// </summary>
    public Task<Guid> SaveMessageAsync(Guid threadId, string role, string content, string? toolCalls = null, string? usage = null, Guid? messageId = null, CancellationToken ct = default)
        => SaveMessageCoreAsync(threadId, role, content, toolCalls, usage, messageId, metadataJson: null, ct);

    /// <inheritdoc />
    public Task<Guid> SaveUserMessageAsync(
        Guid threadId, string content, IReadOnlyDictionary<string, JsonElement>? metadata, Guid? messageId = null, DateTime? sentAt = null, CancellationToken ct = default)
        => SaveMessageCoreAsync(threadId, MessageRole.User, content, toolCalls: null, usage: null, messageId,
            ChatTurnMetadata.Serialize(metadata), ct, sentAt);

    /// <inheritdoc />
    public async Task MarkUserMessageAsync(Guid threadId, DateTime sentAt, CancellationToken ct = default)
    {
        await _repository.EnsureTransactionStartedAsync(ct);
        await _repository.AsQueryable()
            .IgnoreQueryFilters()
            .Where(t => t.Id == threadId)
            .ExecuteUpdateAsync(s => s.SetProperty(
                t => t.LastUserMessageTime,
                t => t.LastUserMessageTime == null || t.LastUserMessageTime < sentAt ? sentAt : t.LastUserMessageTime), ct);
    }

    private async Task<Guid> SaveMessageCoreAsync(
        Guid threadId, string role, string content, string? toolCalls, string? usage, Guid? messageId, string? metadataJson, CancellationToken ct,
        DateTime? sentAt = null)
    {
        // Existence probe via GetAsync (DbSet.FindAsync). FindAsync consults the
        // ChangeTracker first - a ChangeTracker hit returns the entity without issuing a
        // SQL query (and therefore without applying global query filters). This matters
        // for streaming pipelines where the thread was InsertAsync'd within the SAME
        // UnitOfWork transaction and is not yet flushed to DB under
        // EnableGlobalUnitOfWork=true. A DB fallback still applies query filters, so the
        // caller must have arranged ownership/tenant alignment before reaching here.
        var existing = await _repository.GetAsync(threadId, ct);
        if (existing == null)
        {
            Logger.LogWarning("Thread not found when saving message: {ThreadId}", threadId);
            return Guid.Empty;
        }

        // 按 threadId 加锁，确保 MAX(Order)+1 读写原子性（进程内互斥）
        // 唯一索引 (ThreadId, Order) 作为多实例部署的最后防线
        await using var _ = await _messageOrderLock.LockAsync($"thread-msg:{threadId:N}", ct);

        var maxOrder = await _messageRepository
            .Where(m => m.ThreadId == threadId)
            .IgnoreQueryFilters()
            .Select(m => (int?)m.Order)
            .MaxAsync(ct) ?? 0;

        var message = new AgentThreadMessage
        {
            ThreadId = threadId,
            Role = role,
            Content = content,
            ToolCalls = toolCalls,
            Usage = usage,
            MetadataJson = metadataJson,
            Order = maxOrder + 1
        };
        if (messageId.HasValue && messageId.Value != Guid.Empty)
        {
            message.Id = messageId.Value;
        }

        await _messageRepository.InsertAsync(message);
        // Force-flush the INSERT so that the next SaveMessageAsync call within the same
        // UnitOfWork transaction can see this message when it queries MAX(Order).
        // Without this, ShouldSaveImmediately() returns false under an active UoW
        // transaction and both user + assistant messages end up with Order=1, violating
        // the (ThreadId, Order) unique index.
        await _messageRepository.SaveChangesAsync(ct);

        // 更新线程最后活动时间 - 同样跳过 query filter（见上方注释）。
        // 用户在已归档的线程里接着说话 = 这段对话又在进行，取消归档；否则它是屏幕上开着的会话，却不在「最近对话」里。
        // 只认用户消息：归档时还在写的回复落库不应把它拉回来。
        // 主人最近发消息的时间同样只认用户消息、只往后走（排队消息按排下的时间写，排下时已经记过一次）
        var now = DateTime.UtcNow;
        var sent = sentAt ?? now;
        var thread = _repository.AsQueryable().IgnoreQueryFilters().Where(t => t.Id == threadId);
        if (role == MessageRole.User)
        {
            await thread.ExecuteUpdateAsync(s => s
                .SetProperty(t => t.LastActivityTime, now)
                .SetProperty(t => t.ArchivedTime, (DateTime?)null)
                .SetProperty(
                    t => t.LastUserMessageTime,
                    t => t.LastUserMessageTime == null || t.LastUserMessageTime < sent ? sent : t.LastUserMessageTime), ct);
        }
        else
        {
            await thread.ExecuteUpdateAsync(s => s.SetProperty(t => t.LastActivityTime, now), ct);
        }

        Logger.LogDebug("Message saved to thread: {ThreadId}, Role: {Role}, Order: {Order}, MessageId: {MessageId}", threadId, role, message.Order, message.Id);
        return message.Id;
    }

    /// <summary>
    /// 获取线程消息历史
    /// </summary>
    public async Task<List<ChatMessage>> GetMessageHistoryAsync(Guid threadId, int? limit = null, CancellationToken ct = default)
    {
        List<AgentThreadMessage> messages;
        if (limit.HasValue)
        {
            // 取最新的 N 条消息，然后按时间顺序排列
            messages = await _messageRepository
                .Where(m => m.ThreadId == threadId)
                .OrderByDescending(m => m.Order)
                .Take(limit.Value)
                .OrderBy(m => m.Order)
                .ToListAsync(ct);
        }
        else
        {
            messages = await _messageRepository
                .Where(m => m.ThreadId == threadId)
                .OrderBy(m => m.Order)
                .ToListAsync(ct);
        }

        var result = new List<ChatMessage>();
        foreach (var m in messages)
        {
            var role = m.Role switch
            {
                MessageRole.System => ChatRole.System,
                MessageRole.User => ChatRole.User,
                MessageRole.Assistant => ChatRole.Assistant,
                MessageRole.Tool => ChatRole.Tool,
                _ => ChatRole.User
            };
            result.Add(new ChatMessage(role, m.Content));
        }
        return result;
    }

    public async Task<Result<ThreadExportDto>> ExportAsJsonAsync(Guid id)
    {
        var entity = await _repository.AsQueryable()
            .Include(t => t.Agent)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (entity == null)
        {
            return Fail<ThreadExportDto>("Thread not found", 404, ErrorCodes.ThreadNotFound);
        }

        var allMessages = await _messageRepository
            .Where(m => m.ThreadId == id)
            .OrderBy(m => m.Order)
            .ToListAsync();

        var dto = new ThreadExportDto
        {
            Id = entity.Id,
            AgentId = entity.AgentId,
            AgentName = entity.Agent?.Name,
            Title = entity.Title,
            Origin = entity.Origin,
            Metadata = entity.Metadata,
            MessageCount = allMessages.Count,
            LastActivityTime = entity.LastActivityTime,
            CreationTime = entity.CreationTime,
            ExportedAt = DateTime.UtcNow,
            Messages = ToMessageDtos(allMessages)
        };

        await FillAttachmentsAsync(dto.Messages);
        return Ok(dto);
    }

    public async Task<Result<string>> ExportAsMarkdownAsync(Guid id)
    {
        var entity = await _repository.AsQueryable()
            .Include(t => t.Agent)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (entity == null)
        {
            return Fail<string>("Thread not found", 404, ErrorCodes.ThreadNotFound);
        }

        var allMessages = await _messageRepository
            .Where(m => m.ThreadId == id)
            .OrderBy(m => m.Order)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine($"# {entity.Title ?? "Untitled Thread"}");
        sb.AppendLine();
        sb.AppendLine($"- **Agent**: {entity.Agent?.Name ?? entity.AgentId?.ToString() ?? "(none)"}");
        sb.AppendLine($"- **Thread ID**: {entity.Id}");
        sb.AppendLine($"- **Created**: {entity.CreationTime:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"- **Last Activity**: {entity.LastActivityTime:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"- **Messages**: {allMessages.Count}");
        sb.AppendLine($"- **Exported**: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        foreach (var msg in allMessages)
        {
            var roleLabel = msg.Role switch
            {
                MessageRole.System => "System",
                MessageRole.User => "User",
                MessageRole.Assistant => "Assistant",
                MessageRole.Tool => "Tool",
                _ => msg.Role
            };

            sb.AppendLine($"### {roleLabel} (#{msg.Order})");
            sb.AppendLine();
            sb.AppendLine(msg.Content);
            sb.AppendLine();
        }

        return Ok<string>(sb.ToString());
    }


    /// <inheritdoc />
    public async Task<string?> GetMetadataValueAsync(Guid threadId, string key, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(key);

        var threadEntity = await _repository.GetAsync(threadId, ct);
        if (threadEntity == null || string.IsNullOrWhiteSpace(threadEntity.Metadata))
            return null;

        var metadata = ParseMetadata(threadEntity.Metadata);
        return metadata != null && metadata.TryGetValue(key, out var value) ? value.GetRawText() : null;
    }

    /// <inheritdoc />
    public async Task SetMetadataValueAsync(Guid threadId, string key, string? valueJson, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(key);

        var threadEntity = await _repository.GetAsync(threadId, ct);
        if (threadEntity == null)
        {
            Logger.LogWarning("Thread not found when writing metadata key {Key}: {ThreadId}", key, threadId);
            return;
        }

        var metadata = ParseMetadata(threadEntity.Metadata) ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (valueJson == null)
        {
            if (!metadata.Remove(key)) return;
        }
        else
        {
            using var doc = JsonDocument.Parse(valueJson);
            metadata[key] = doc.RootElement.Clone();
        }

        threadEntity.Metadata = metadata.Count == 0 ? null : JsonSerializer.Serialize(metadata);
        // 只写 Metadata 一列：同一作用域里这份实体建线程时就被跟踪着，整行写会把后台起好的标题改回占位标题
        await _repository.SaveChangesAsync(ct);
    }

    /// <summary>
    /// 把 <c>Metadata</c> 列解析成键值字典；不是 JSON 对象（历史数据 / 消费方自写的其它形状）时返回 null，
    /// 写入方会从空字典重新开始 —— 这会覆盖掉非对象形状的旧值，故只在写入时发生并记日志。
    /// </summary>
    private Dictionary<string, JsonElement>? ParseMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
        }
        catch (JsonException ex)
        {
            Logger.LogWarning(ex, "Thread metadata is not a JSON object; it will be replaced on the next write");
            return null;
        }
    }
}
