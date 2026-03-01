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
        // 验证 Agent 存在
        var agent = await _agentRepository.GetAsync(input.AgentId);
        if (agent == null)
        {
            return Fail<AgentThreadDto>("Agent not found", 404, ErrorCodes.AgentNotFound);
        }

        var entity = new AgentThreadEntity
        {
            AgentId = input.AgentId,
            Title = input.Title ?? $"Thread {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
            LastActivityTime = DateTime.UtcNow
        };

        await _repository.InsertAsync(entity);

        Logger.LogInformation("Agent thread created: {ThreadId}, AgentId: {AgentId}", entity.Id, entity.AgentId);

        return Ok(entity.MapTo<AgentThreadDto>());
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

        var pagedList = await q
            .OrderByDescending(t => t.LastActivityTime)
            .ProjectTo<AgentThread, AgentThreadDto>()
            .CreateAsync(query);

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

        Logger.LogInformation("Agent thread deleted: {ThreadId}", id);
        return Ok();
    }

    public async Task<ConversationContext> GetOrCreateThreadAsync(Guid? threadId, Guid agentId, CancellationToken ct = default)
    {
        // 验证 Agent 存在
        var agentDef = await _agentRepository.GetAsync(agentId, ct);
        if (agentDef == null)
        {
            throw new BusinessException("Agent not found", ErrorCodes.AgentNotFound, 404);
        }

        // 如果提供了 threadId，尝试从数据库加载
        if (threadId.HasValue)
        {
            var threadEntity = await _repository.GetAsync(threadId.Value, ct);
            if (threadEntity == null || threadEntity.AgentId != agentId)
            {
                Logger.LogWarning("Thread not found or agent mismatch: ThreadId={ThreadId}, AgentId={AgentId}", threadId.Value, agentId);
                throw new BusinessException("Thread not found", ErrorCodes.ThreadNotFound, 404);
            }

            // 如果有序列化数据，尝试反序列化恢复
            if (!string.IsNullOrWhiteSpace(threadEntity.SerializedData))
            {
                var context = ConversationContext.Deserialize(threadEntity.SerializedData);
                if (context != null)
                {
                    Logger.LogDebug("Deserialized conversation context from database: {ThreadId}", threadId.Value);
                    return context;
                }

                Logger.LogWarning("Failed to deserialize conversation context: {ThreadId}. Rebuilding from history.", threadId.Value);
            }

            // 无序列化数据或反序列化失败，从历史消息重建
            return await RebuildContextFromHistoryAsync(threadEntity.Id, ct);
        }

        // 无 threadId，创建新的线程和空的 ConversationContext
        var newEntity = new AgentThreadEntity
        {
            AgentId = agentId,
            Title = $"Thread {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
            LastActivityTime = DateTime.UtcNow
        };

        await _repository.InsertAsync(newEntity);

        Logger.LogDebug("Created new thread: {ThreadId} for Agent: {AgentId}", newEntity.Id, agentId);

        var newContext = new ConversationContext();

        // 序列化并保存
        await SaveThreadSerializedDataAsync(newEntity.Id, newContext, ct);

        return newContext;
    }

    /// <summary>
    /// 保存消息到线程
    /// </summary>
    public async Task SaveMessageAsync(Guid threadId, string role, string content, string? toolCalls = null, string? usage = null, CancellationToken ct = default)
    {
        var thread = await _repository.GetAsync(threadId, ct);
        if (thread == null)
        {
            Logger.LogWarning("Thread not found when saving message: {ThreadId}", threadId);
            return;
        }

        // 获取当前消息数量作为顺序
        var messageCount = await _messageRepository
            .Where(m => m.ThreadId == threadId)
            .CountAsync(ct);

        var message = new AgentThreadMessage
        {
            ThreadId = threadId,
            Role = role,
            Content = content,
            ToolCalls = toolCalls,
            Usage = usage,
            Order = messageCount + 1
        };

        await _messageRepository.InsertAsync(message);

        // 更新线程最后活动时间
        thread.LastActivityTime = DateTime.UtcNow;
        await _repository.UpdateAsync(thread);

        Logger.LogDebug("Message saved to thread: {ThreadId}, Role: {Role}, Order: {Order}", threadId, role, message.Order);
    }

    /// <summary>
    /// 获取线程消息历史
    /// </summary>
    public async Task<List<ChatMessage>> GetMessageHistoryAsync(Guid threadId, int? limit = null, CancellationToken ct = default)
    {
        var query = _messageRepository
            .Where(m => m.ThreadId == threadId)
            .OrderBy(m => m.Order);

        var messages = limit.HasValue
            ? await query.Take(limit.Value).ToListAsync(ct)
            : await query.ToListAsync(ct);

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
        sb.AppendLine($"- **Agent**: {entity.Agent?.Name ?? entity.AgentId.ToString()}");
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
