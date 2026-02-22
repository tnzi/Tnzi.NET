
namespace Tnzi.AI.Services;

/// <summary>
/// Agent 管理服务实现
/// </summary>
public class AgentService : ApplicationService, IAgentService
{
    private readonly IRepository<Agent, Guid> _repository;
    private readonly ChatExecutionPipeline _pipeline;

    public AgentService(
        IRepository<Agent, Guid> repository,
        ChatExecutionPipeline pipeline,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _pipeline = Check.NotNull(pipeline);
    }

    public async Task<Result<AgentDto>> CreateAsync(CreateAgentDto input)
    {
        var entity = input.MapTo<Agent>();
        entity.ToolGroups = input.ToolGroups != null ? JsonSerializer.Serialize(input.ToolGroups) : null;

        await _repository.InsertAsync(entity);
        return Ok(MapToDto(entity));
    }

    public async Task<Result<AgentDto>> UpdateAsync(Guid id, UpdateAgentDto input)
    {
        var entity = await _repository.GetAsync(id);
        if (entity == null || entity.IsDeleted) return Fail<AgentDto>("Agent not found", 404, ErrorCodes.AgentNotFound);

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
        if (entity == null || entity.IsDeleted) return Fail("Agent not found", 404, ErrorCodes.AgentNotFound);
        await _repository.DeleteAsync(entity);
        return Ok();
    }

    public async Task<Result<AgentDto>> GetByIdAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        if (entity == null || entity.IsDeleted) return Fail<AgentDto>("Agent not found", 404, ErrorCodes.AgentNotFound);
        return Ok(MapToDto(entity));
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

        // 1. 构建消息（支持多模态）
        var resolvedMessage = await _pipeline.BuildChatMessageAsync(message, content, ct);

        // 2. 解析 Agent（固定 agentId，无 provider/model/toolGroups 覆盖）
        var resolution = await _pipeline.ResolveAgentAsync(agentId, null, null, null, ct);
        if (!resolution.IsSuccess)
        {
            return resolution.ErrorCode == ErrorCodes.AgentDisabled
                ? Fail<AgentResponseDto>("Agent is disabled", 400, ErrorCodes.AgentDisabled)
                : Fail<AgentResponseDto>("Agent not found", 404, ErrorCodes.AgentNotFound);
        }

        // 3. 原子预留配额
        var (reservation, quotaError) = await _pipeline.ReserveQuotaAsync<AgentResponseDto>(userId, resolvedMessage, ct);
        if (quotaError != null) return quotaError;

        try
        {
            // 4. 获取或创建线程
            var (context, resolvedThreadId) = await _pipeline.PrepareThreadAsync(threadId, agentId, ct);

            // 5. 执行对话
            var response = await _pipeline.ExecuteAsync(resolution.Agent!, resolvedMessage, context, resolvedThreadId, ct);

            // 6. 持久化消息历史
            await _pipeline.PersistAfterRunAsync(resolvedThreadId, context, resolvedMessage, response.Text ?? string.Empty, ct);

            // 7. 记录使用日志
            var actualTokens = (int)(response.Usage?.TotalTokenCount ?? 0);
            await _pipeline.LogUsageAsync(
                AIOperationType.AgentRun,
                resolution.Provider,
                resolution.Model ?? "default",
                (int)(response.Usage?.InputTokenCount ?? 0),
                (int)(response.Usage?.OutputTokenCount ?? 0),
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
                Usage = ChatExecutionPipeline.MapUsage(response.Usage)
            });
        }
        catch (BusinessException ex)
        {
            return Fail<AgentResponseDto>(ex.Message, ex.HttpStatusCode, ex.Code);
        }
        catch (InvalidOperationException ex) when (ex.Message?.Contains("quota", StringComparison.OrdinalIgnoreCase) == true)
        {
            return Fail<AgentResponseDto>(ex.Message, 429, ErrorCodes.QuotaExceeded);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "Agent run failed: AgentId={AgentId}, ThreadId={ThreadId}, Message={Message}",
                agentId, threadId, ex.Message);
            await _pipeline.LogUsageAsync(AIOperationType.AgentRun, resolution.Provider, resolution.Model ?? "default", 0, 0, stopwatch.ElapsedMilliseconds, false, ex.Message, agentId, threadId, ct);
            return Fail<AgentResponseDto>($"Run failed: {ex.Message}", 500, ErrorCodes.AgentRunFailed);
        }
    }

    public async IAsyncEnumerable<StreamEvent> RunStreamingAsync(Guid agentId, string? message, List<ContentPartDto>? content = null, Guid? threadId = null, Guid? userId = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        // 1. 构建消息（支持多模态）
        var resolvedMessage = await _pipeline.BuildChatMessageAsync(message, content, ct);

        // 2. 解析 Agent
        var resolution = await _pipeline.ResolveAgentAsync(agentId, null, null, null, ct);
        if (!resolution.IsSuccess)
        {
            var errorMessage = resolution.ErrorCode == ErrorCodes.AgentDisabled ? "Agent is disabled" : "Agent not found";
            var statusCode = resolution.ErrorCode == ErrorCodes.AgentDisabled ? 400 : 404;
            throw new BusinessException(errorMessage, resolution.ErrorCode ?? ErrorCodes.AgentNotFound, statusCode);
        }

        // 3. 原子预留配额
        var reservation = await _pipeline.ReserveQuotaOrThrowAsync(userId, resolvedMessage, ct);

        // 4. 获取或创建线程
        var (context, resolvedThreadId) = await _pipeline.PrepareThreadAsync(threadId, agentId, ct);

        // 5. 流式执行（delta 模型 — 每个事件只包含增量内容）
        var fullContent = new StringBuilder();
        int inputTokens = 0, outputTokens = 0;
        AgentStreamChunk? lastChunk = null;

        await foreach (var chunk in _pipeline.ExecuteStreamingAsync(resolution.Agent!, resolvedMessage, context, resolvedThreadId, ct).WithCancellation(ct))
        {
            if (chunk.Text != null) fullContent.Append(chunk.Text);

            lastChunk = chunk;

            // 提取 Token 使用信息
            var (inp, outp) = ChatExecutionPipeline.ExtractStreamingUsage(chunk);
            if (inp > 0 || outp > 0) { inputTokens = inp; outputTokens = outp; }

            // 仅在有增量文本时发送 delta 事件
            if (chunk.Text != null)
            {
                yield return new StreamEvent
                {
                    Delta = chunk.Text,
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

        // 7. 持久化消息历史
        await _pipeline.PersistAfterRunAsync(resolvedThreadId, context, resolvedMessage, fullContent.ToString(), ct);

        // 8. 记录日志
        await _pipeline.LogUsageAsync(AIOperationType.AgentRunStreaming, resolution.Provider, resolution.Model ?? "default", inputTokens, outputTokens, stopwatch.ElapsedMilliseconds, true, agentId: agentId, threadId: resolvedThreadId, ct: ct);

        // 9. 结算配额
        await _pipeline.SettleQuotaAsync(userId, reservation, totalTokens, ct);
    }

    private static AgentDto MapToDto(Agent entity)
    {
        var dto = entity.MapTo<AgentDto>();
        dto.ToolGroups = string.IsNullOrWhiteSpace(entity.ToolGroups) ? null : JsonSerializer.Deserialize<List<string>>(entity.ToolGroups);
        return dto;
    }
}
