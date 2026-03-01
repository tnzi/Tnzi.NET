
namespace Tnzi.AI.Services;

/// <summary>
/// Agent 管理服务实现
/// </summary>
public class AgentService : ApplicationService, IAgentService
{
    private readonly IRepository<Agent, Guid> _repository;
    private readonly IRepository<AgentVersion, Guid> _versionRepository;
    private readonly IChatExecutionPipeline _pipeline;

    public AgentService(
        IRepository<Agent, Guid> repository,
        IRepository<AgentVersion, Guid> versionRepository,
        IChatExecutionPipeline pipeline,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _versionRepository = Check.NotNull(versionRepository);
        _pipeline = Check.NotNull(pipeline);
    }

    public async Task<Result<AgentDto>> CreateAsync(CreateAgentDto input)
    {
        Check.NotNull(input);
        var entity = input.MapTo<Agent>();
        entity.ToolGroups = input.ToolGroups != null ? JsonSerializer.Serialize(input.ToolGroups) : null;

        await _repository.InsertAsync(entity);
        return Ok(MapToDto(entity));
    }

    public async Task<Result<AgentDto>> UpdateAsync(Guid id, UpdateAgentDto input)
    {
        Check.NotNull(input);
        var entity = await _repository.GetAsync(id);
        if (entity == null) return Fail<AgentDto>("Agent not found", 404, ErrorCodes.AgentNotFound);

        // Create version snapshot before applying changes
        await CreateVersionSnapshotAsync(entity, input.ChangeNote);

        if (input.Name != null) entity.Name = input.Name;
        if (input.Description != null) entity.Description = input.Description;
        if (input.Instructions != null) entity.Instructions = input.Instructions;
        if (input.Provider != null) entity.Provider = input.Provider;
        if (input.Model != null) entity.Model = input.Model;
        if (input.ToolGroups != null) entity.ToolGroups = JsonSerializer.Serialize(input.ToolGroups);
        if (input.Temperature.HasValue) entity.Temperature = input.Temperature;
        if (input.MaxTokens.HasValue) entity.MaxTokens = input.MaxTokens;
        if (input.TimeoutSeconds.HasValue) entity.TimeoutSeconds = input.TimeoutSeconds;
        if (input.IsEnabled.HasValue) entity.IsEnabled = input.IsEnabled.Value;

        await _repository.UpdateAsync(entity);
        return Ok(MapToDto(entity));
    }

    public async Task<Result> DeleteAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        if (entity == null) return Fail("Agent not found", 404, ErrorCodes.AgentNotFound);
        await _repository.DeleteAsync(entity);
        return Ok();
    }

    public async Task<Result<AgentDto>> GetByIdAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        if (entity == null) return Fail<AgentDto>("Agent not found", 404, ErrorCodes.AgentNotFound);
        return Ok(MapToDto(entity));
    }

    public async Task<Result<AgentDto>> CloneAsync(Guid id, string? newName = null)
    {
        var source = await _repository.GetAsync(id);
        if (source == null) return Fail<AgentDto>("Agent not found", 404, ErrorCodes.AgentNotFound);

        var clone = new Agent
        {
            Name = newName ?? $"{source.Name} (Copy)",
            Description = source.Description,
            Instructions = source.Instructions,
            Provider = source.Provider,
            Model = source.Model,
            ToolGroups = source.ToolGroups,
            Temperature = source.Temperature,
            MaxTokens = source.MaxTokens,
            TimeoutSeconds = source.TimeoutSeconds,
            IsEnabled = source.IsEnabled,
            Configuration = source.Configuration
        };

        await _repository.InsertAsync(clone);

        Logger.LogInformation("Agent cloned: {SourceId} -> {CloneId}, Name: {Name}", id, clone.Id, clone.Name);
        return Ok(MapToDto(clone));
    }

    public async Task<Result<IPagedList<AgentVersionDto>>> GetVersionsAsync(Guid agentId, AgentVersionQueryDto query)
    {
        var agentExists = await _repository.AnyAsync(a => a.Id == agentId);
        if (!agentExists)
            return Fail<IPagedList<AgentVersionDto>>("Agent not found", 404, ErrorCodes.AgentNotFound);

        var pagedList = await _versionRepository
            .Where(v => v.AgentId == agentId)
            .OrderByDescending(v => v.Version)
            .ProjectTo<AgentVersion, AgentVersionDto>()
            .CreateAsync(query);

        return Ok(pagedList);
    }

    public async Task<Result<AgentVersionDto>> GetVersionAsync(Guid agentId, int version)
    {
        var entity = await _versionRepository
            .Where(v => v.AgentId == agentId && v.Version == version)
            .FirstOrDefaultAsync();

        if (entity == null)
            return Fail<AgentVersionDto>("Agent version not found", 404, ErrorCodes.AgentVersionNotFound);

        return Ok(entity.MapTo<AgentVersionDto>());
    }

    public async Task<Result<AgentDto>> RollbackToVersionAsync(Guid agentId, int version)
    {
        var agent = await _repository.GetAsync(agentId);
        if (agent == null)
            return Fail<AgentDto>("Agent not found", 404, ErrorCodes.AgentNotFound);

        var versionEntity = await _versionRepository
            .Where(v => v.AgentId == agentId && v.Version == version)
            .FirstOrDefaultAsync();

        if (versionEntity == null)
            return Fail<AgentDto>("Agent version not found", 404, ErrorCodes.AgentVersionNotFound);

        // Create snapshot of current state before rollback
        await CreateVersionSnapshotAsync(agent, $"Before rollback to version {version}");

        // Restore agent from config snapshot
        var snapshot = JsonSerializer.Deserialize<AgentConfigSnapshot>(versionEntity.ConfigSnapshot);
        if (snapshot == null)
            return Fail<AgentDto>("Failed to deserialize version snapshot", 500, ErrorCodes.InternalError);

        agent.Name = snapshot.Name;
        agent.Description = snapshot.Description;
        agent.Instructions = snapshot.Instructions;
        agent.Provider = snapshot.Provider;
        agent.Model = snapshot.Model;
        agent.ToolGroups = snapshot.ToolGroups;
        agent.Temperature = snapshot.Temperature;
        agent.MaxTokens = snapshot.MaxTokens;
        agent.TimeoutSeconds = snapshot.TimeoutSeconds;
        agent.IsEnabled = snapshot.IsEnabled;
        agent.Configuration = snapshot.Configuration;

        await _repository.UpdateAsync(agent);

        Logger.LogInformation("Agent {AgentId} rolled back to version {Version}", agentId, version);
        return Ok(MapToDto(agent));
    }

    public async Task<Result<IPagedList<AgentDto>>> GetListAsync(AgentListQueryDto query)
    {
        var queryable = _repository
            .WhereIf(a => a.Name.ToLower().Contains(query.Keyword!.ToLower()) || (a.Description != null && a.Description.ToLower().Contains(query.Keyword!.ToLower())),
                !string.IsNullOrWhiteSpace(query.Keyword))
            .WhereIf(a => a.Provider == query.Provider, !string.IsNullOrWhiteSpace(query.Provider))
            .WhereIf(a => a.IsEnabled == query.IsEnabled!.Value, query.IsEnabled.HasValue)
            .OrderByDescending(a => a.CreationTime);

        var pagedList = await queryable.ProjectTo<Agent, AgentDto>().CreateAsync(query);
        return Ok(pagedList);
    }

    public async Task<Result<AgentResponseDto>> RunAsync(Guid agentId, string? message, List<ContentPartDto>? content = null, Guid? threadId = null, Guid? userId = null, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        // 1. 构建消息（支持多模态：返回包含正确 AIContent 的 ChatMessage）
        var userMessage = await _pipeline.BuildChatMessageAsync(message, content, ct);
        var messageText = userMessage.Text ?? string.Empty;

        // 2. 解析 Agent（固定 agentId，无 provider/model/toolGroups 覆盖）
        var resolution = await _pipeline.ResolveAgentAsync(agentId, null, null, null, ct);
        if (!resolution.IsSuccess)
        {
            return resolution.ErrorCode == ErrorCodes.AgentDisabled
                ? Fail<AgentResponseDto>("Agent is disabled", 400, ErrorCodes.AgentDisabled)
                : Fail<AgentResponseDto>("Agent not found", 404, ErrorCodes.AgentNotFound);
        }

        // 3. 原子预留配额
        var (reservation, quotaError) = await _pipeline.ReserveQuotaAsync<AgentResponseDto>(userId, messageText, ct);
        if (quotaError != null) return quotaError;

        try
        {
            // 4. 获取或创建线程
            var (context, resolvedThreadId) = await _pipeline.PrepareThreadAsync(threadId, agentId, ct);

            // 5. 执行对话
            var response = await _pipeline.ExecuteAsync(resolution.Agent!, userMessage, context, resolvedThreadId, ct);

            // 6. 持久化消息历史
            await _pipeline.PersistAfterRunAsync(resolvedThreadId, context, userMessage, response.Text ?? string.Empty, ct);

            // 7. 记录使用日志
            var actualTokens = response.Usage?.TotalTokens ?? 0;
            await _pipeline.LogUsageAsync(
                AIOperationType.AgentRun,
                resolution.Provider,
                resolution.Model ?? "default",
                response.Usage?.PromptTokens ?? 0,
                response.Usage?.CompletionTokens ?? 0,
                stopwatch.ElapsedMilliseconds,
                true,
                agentId: agentId,
                threadId: resolvedThreadId,
                ct: ct);

            // 8. 结算配额（调整预估与实际的差值）
            await _pipeline.SettleQuotaAsync(userId, reservation, actualTokens, ct);

            return Ok(new AgentResponseDto
            {
                Content = response.Text ?? string.Empty,
                Model = resolution.Model,
                Usage = response.Usage
            });
        }
        catch (BusinessException ex)
        {
            return Fail<AgentResponseDto>(ex.Message, ex.HttpStatusCode, ex.Code);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "Agent run failed: AgentId={AgentId}, ThreadId={ThreadId}, Message={Message}",
                agentId, threadId, ex.Message);
            await _pipeline.LogUsageAsync(AIOperationType.AgentRun, resolution.Provider, resolution.Model ?? "default", 0, 0, stopwatch.ElapsedMilliseconds, false, ex.Message, agentId, threadId, ct);
            return Fail<AgentResponseDto>("Agent execution failed.", 500, ErrorCodes.AgentRunFailed);
        }
    }

    public async IAsyncEnumerable<StreamEvent> RunStreamingAsync(Guid agentId, string? message, List<ContentPartDto>? content = null, Guid? threadId = null, Guid? userId = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        // 1. 构建消息（支持多模态：返回包含正确 AIContent 的 ChatMessage）
        var userMessage = await _pipeline.BuildChatMessageAsync(message, content, ct);
        var messageText = userMessage.Text ?? string.Empty;

        // 2. 解析 Agent
        var resolution = await _pipeline.ResolveAgentAsync(agentId, null, null, null, ct);
        if (!resolution.IsSuccess)
        {
            var errorMessage = resolution.ErrorCode == ErrorCodes.AgentDisabled ? "Agent is disabled" : "Agent not found";
            var statusCode = resolution.ErrorCode == ErrorCodes.AgentDisabled ? 400 : 404;
            throw new BusinessException(errorMessage, resolution.ErrorCode ?? ErrorCodes.AgentNotFound, statusCode);
        }

        // 3. 原子预留配额
        var reservation = await _pipeline.ReserveQuotaOrThrowAsync(userId, messageText, ct);

        // 4. 获取或创建线程
        var (context, resolvedThreadId) = await _pipeline.PrepareThreadAsync(threadId, agentId, ct);

        // 5. 流式执行（delta 模型 — 每个事件只包含增量内容）
        var fullContent = new StringBuilder();
        int inputTokens = 0, outputTokens = 0;
        AgentStreamChunk? lastChunk = null;

        await foreach (var chunk in _pipeline.ExecuteStreamingAsync(resolution.Agent!, userMessage, context, resolvedThreadId, ct).WithCancellation(ct))
        {
            if (chunk.Text != null) fullContent.Append(chunk.Text);

            lastChunk = chunk;

            // 提取 Token 使用信息
            var (inp, outp) = ChatExecutionPipeline.ExtractStreamingUsage(chunk);
            if (inp > 0 || outp > 0) { inputTokens = inp; outputTokens = outp; }

            // 发送 delta 事件或工具调用状态事件
            if (chunk.Text != null)
            {
                yield return new StreamEvent
                {
                    Delta = chunk.Text,
                    Model = resolution.Model,
                    ThreadId = resolvedThreadId
                };
            }
            else if (chunk.IsToolCall)
            {
                yield return new StreamEvent
                {
                    IsToolCall = true,
                    Model = resolution.Model,
                    ThreadId = resolvedThreadId
                };
            }
        }

        // 如果流式响应结束时还没有 Token 信息，尝试从最后一条 chunk 中查找
        if (lastChunk is not null && inputTokens == 0 && outputTokens == 0)
        {
            var (inp, outp) = ChatExecutionPipeline.ExtractStreamingUsage(lastChunk);
            if (inp > 0 || outp > 0) { inputTokens = inp; outputTokens = outp; }
        }

        // 6. 发送终止事件（含 Usage 和 FinishReason），先通知客户端再做清理
        var totalTokens = inputTokens + outputTokens;
        yield return new StreamEvent
        {
            IsDone = true,
            FinishReason = "stop",
            Model = resolution.Model,
            ThreadId = resolvedThreadId,
            Usage = new TokenUsageDto
            {
                PromptTokens = inputTokens,
                CompletionTokens = outputTokens,
                TotalTokens = totalTokens
            }
        };

        // 7-9: 使用 CancellationToken.None 防止客户端断连导致配额泄漏和数据丢失
        try
        {
            await _pipeline.PersistAfterRunAsync(resolvedThreadId, context, userMessage, fullContent.ToString(), CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to persist agent streaming history: AgentId={AgentId}, ThreadId={ThreadId}", agentId, resolvedThreadId);
        }

        try
        {
            await _pipeline.LogUsageAsync(AIOperationType.AgentRunStreaming, resolution.Provider, resolution.Model ?? "default", inputTokens, outputTokens, stopwatch.ElapsedMilliseconds, true, agentId: agentId, threadId: resolvedThreadId, ct: CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to log agent streaming usage: AgentId={AgentId}, ThreadId={ThreadId}", agentId, resolvedThreadId);
        }

        try
        {
            await _pipeline.SettleQuotaAsync(userId, reservation, totalTokens, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to settle agent streaming quota: AgentId={AgentId}, UserId={UserId}", agentId, userId);
        }
    }

    private async Task CreateVersionSnapshotAsync(Agent entity, string? changeNote)
    {
        try
        {
            var latestVersion = await _versionRepository
                .Where(v => v.AgentId == entity.Id)
                .OrderByDescending(v => v.Version)
                .Select(v => (int?)v.Version)
                .FirstOrDefaultAsync();

            var nextVersion = (latestVersion ?? 0) + 1;

            var snapshot = new AgentConfigSnapshot
            {
                Name = entity.Name,
                Description = entity.Description,
                Instructions = entity.Instructions,
                Provider = entity.Provider,
                Model = entity.Model,
                ToolGroups = entity.ToolGroups,
                Temperature = entity.Temperature,
                MaxTokens = entity.MaxTokens,
                TimeoutSeconds = entity.TimeoutSeconds,
                IsEnabled = entity.IsEnabled,
                Configuration = entity.Configuration
            };

            var versionEntity = new AgentVersion
            {
                AgentId = entity.Id,
                Version = nextVersion,
                ChangeNote = changeNote,
                ConfigSnapshot = JsonSerializer.Serialize(snapshot)
            };

            await _versionRepository.InsertAsync(versionEntity);
            Logger.LogDebug("Created version snapshot {Version} for Agent {AgentId}", nextVersion, entity.Id);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to create version snapshot for Agent {AgentId}", entity.Id);
        }
    }

    private static AgentDto MapToDto(Agent entity)
    {
        var dto = entity.MapTo<AgentDto>();
        dto.ToolGroups = string.IsNullOrWhiteSpace(entity.ToolGroups) ? null : JsonSerializer.Deserialize<List<string>>(entity.ToolGroups);
        return dto;
    }
}

/// <summary>
/// Agent config snapshot serialization model (internal)
/// </summary>
internal class AgentConfigSnapshot
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Instructions { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string? Model { get; set; }
    public string? ToolGroups { get; set; }
    public double? Temperature { get; set; }
    public int? MaxTokens { get; set; }
    public int? TimeoutSeconds { get; set; }
    public bool IsEnabled { get; set; }
    public string? Configuration { get; set; }
}
