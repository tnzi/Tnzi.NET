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

    /// <summary>
    /// 按 threadId 的消息写入互斥锁，防止并发写入产生相同 Order
    /// </summary>
    private static readonly KeyedAsyncLock _messageOrderLock = new();

    public AgentThreadService(
        IRepository<AgentThreadEntity, Guid> repository,
        IRepository<AgentThreadMessage, Guid> messageRepository,
        IRepository<Agent, Guid> agentRepository,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _messageRepository = Check.NotNull(messageRepository);
        _agentRepository = Check.NotNull(agentRepository);
    }

    public async Task<Result<AgentThreadDto>> CreateAsync(CreateAgentThreadDto input)
    {
        // 仅当提供 AgentId 时验证 Agent 存在
        Agent? agent = null;
        if (input.AgentId.HasValue)
        {
            agent = await _agentRepository.GetAsync(input.AgentId.Value);
            if (agent == null)
            {
                return Fail<AgentThreadDto>("Agent not found", 404, ErrorCodes.AgentNotFound);
            }
        }

        var entity = new AgentThreadEntity
        {
            AgentId = input.AgentId,
            Title = input.Title ?? $"Thread {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
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

        var dto = entity.MapTo<AgentThreadDto>();
        dto.MessageCount = await _messageRepository.Where(m => m.ThreadId == id).CountAsync();

        if (entity.AgentId.HasValue)
        {
            var agent = await _agentRepository.GetAsync(entity.AgentId.Value);
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
            Metadata = entity.Metadata,
            MessageCount = totalMessageCount,
            LastActivityTime = entity.LastActivityTime,
            CreationTime = entity.CreationTime,
            Messages = recentMessages.MapToList<ThreadMessageDto>()
        };

        return Ok(dto);
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

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.ToLower();
            q = q.Where(t => t.Title != null && t.Title.ToLower().Contains(keyword));
        }

        if (query.StartTime.HasValue)
        {
            q = q.Where(t => t.LastActivityTime >= query.StartTime.Value);
        }

        if (query.EndTime.HasValue)
        {
            q = q.Where(t => t.LastActivityTime <= query.EndTime.Value);
        }

        // Project explicitly so AgentName resolves through the Agent navigation
        // (LEFT JOIN; null when the agent was deleted or the thread is agent-less).
        var pagedList = await q
            .OrderByDescending(t => t.LastActivityTime)
            .Select(t => new AgentThreadDto
            {
                Id = t.Id,
                AgentId = t.AgentId,
                AgentName = t.Agent != null ? t.Agent.Name : null,
                Title = t.Title,
                LastActivityTime = t.LastActivityTime,
                CreationTime = t.CreationTime
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

        entity.Title = title;
        await _repository.UpdateAsync(entity);

        Logger.LogInformation("Agent thread title updated: {ThreadId}, Title: {Title}", id, title);

        return Ok(entity.MapTo<AgentThreadDto>());
    }

    public async Task<Result> DeleteAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        if (entity == null)
        {
            return Fail("Thread not found", 404, ErrorCodes.ThreadNotFound);
        }

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

    public async Task<(ConversationContext context, Guid threadId, bool isNewThread)> GetOrCreateThreadAsync(Guid? threadId, Guid? agentId, CancellationToken ct = default)
    {
        // 仅当提供 agentId 时验证 Agent 存在
        if (agentId.HasValue)
        {
            var agentDef = await _agentRepository.GetAsync(agentId.Value, ct);
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
            var currentUserId = CurrentUser?.Id;
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

            // 如果有序列化数据，尝试反序列化恢复
            if (!string.IsNullOrWhiteSpace(threadEntity.SerializedData))
            {
                var context = ConversationContext.Deserialize(threadEntity.SerializedData);
                if (context != null)
                {
                    Logger.LogDebug("Deserialized conversation context from database: {ThreadId}", threadId.Value);
                    return (context, threadEntity.Id, false);
                }

                Logger.LogWarning("Failed to deserialize conversation context: {ThreadId}. Rebuilding from history.", threadId.Value);
            }

            // 无序列化数据或反序列化失败，从历史消息重建
            var rebuilt = await RebuildContextFromHistoryAsync(threadEntity.Id, ct);
            return (rebuilt, threadEntity.Id, false);
        }

        // 无 threadId，创建新的线程和空的 ConversationContext
        // 注意：必须在 InsertAsync 之前把 SerializedData 填好，不能在 InsertAsync 之后
        // 再调用 SaveThreadSerializedDataAsync。后者会 GetAsync 拿到尚未持久化的实体
        // （ChangeTracker State=Added），然后 UpdateAsync 把状态翻转成 Modified，导致
        // SaveChanges 时 EF Core 发 UPDATE 而非 INSERT，得到 0 rows affected 并整事务回滚。
        var newContext = new ConversationContext();
        var newEntity = new AgentThreadEntity
        {
            AgentId = agentId,
            Title = $"Thread {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
            LastActivityTime = DateTime.UtcNow,
            SerializedData = newContext.Serialize()
        };

        await _repository.InsertAsync(newEntity);

        Logger.LogDebug("Created new thread: {ThreadId} for Agent: {AgentId}", newEntity.Id, agentId);

        return (newContext, newEntity.Id, true);
    }

    /// <summary>
    /// 保存消息到线程。<paramref name="messageId"/> 非空时使用该 ID 持久化；为空时由
    /// framework SaveChangesAsync 自动生成 sequential GUID。返回最终持久化的 message ID。
    /// </summary>
    public async Task<Guid> SaveMessageAsync(Guid threadId, string role, string content, string? toolCalls = null, string? usage = null, Guid? messageId = null, CancellationToken ct = default)
    {
        // Existence probe via GetAsync (DbSet.FindAsync). FindAsync consults the
        // ChangeTracker first — a ChangeTracker hit returns the entity without issuing a
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

        // 更新线程最后活动时间 — 同样跳过 query filter（见上方注释）
        await _repository.AsQueryable()
            .IgnoreQueryFilters()
            .Where(t => t.Id == threadId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.LastActivityTime, DateTime.UtcNow), ct);

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

    /// <summary>
    /// 保存对话上下文的序列化数据到数据库
    /// </summary>
    public async Task SaveThreadSerializedDataAsync(Guid threadId, ConversationContext context, CancellationToken ct = default)
    {
        Check.NotNull(context);

        try
        {
            var threadEntity = await _repository.GetAsync(threadId, ct);
            if (threadEntity == null)
            {
                Logger.LogWarning("Thread not found when saving serialized data: {ThreadId}", threadId);
                return;
            }

            // 直接序列化 ConversationContext
            threadEntity.SerializedData = context.Serialize();
            threadEntity.LastActivityTime = DateTime.UtcNow;

            await _repository.UpdateAsync(threadEntity);

            Logger.LogDebug("Saved conversation context data: {ThreadId}", threadId);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to save conversation context data: {ThreadId}", threadId);
            // 不抛出异常，允许继续执行
        }
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
            Metadata = entity.Metadata,
            MessageCount = allMessages.Count,
            LastActivityTime = entity.LastActivityTime,
            CreationTime = entity.CreationTime,
            ExportedAt = DateTime.UtcNow,
            Messages = allMessages.MapToList<ThreadMessageDto>()
        };

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

    /// <summary>
    /// 从用户消息生成 fallback 标题（截取前 N 个文本元素）
    /// </summary>
    public static string? GenerateFallbackTitle(string? userMessage, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return null;

        return userMessage.TruncateByTextElements(maxLength);
    }

    /// <summary>
    /// 从历史消息重建 ConversationContext
    /// </summary>
    private async Task<ConversationContext> RebuildContextFromHistoryAsync(Guid threadId, CancellationToken ct)
    {
        var messages = await GetMessageHistoryAsync(threadId, null, ct);

        Logger.LogDebug("Rebuilt conversation context from {MessageCount} history messages for thread: {ThreadId}", messages.Count, threadId);

        var context = new ConversationContext
        {
            Messages = messages
        };

        // 保存重建后的上下文数据
        if (messages.Count > 0)
        {
            await SaveThreadSerializedDataAsync(threadId, context, ct);
        }

        return context;
    }
}
