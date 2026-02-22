namespace Tnzi.AI.Infrastructure;

/// <summary>
/// AI 对话执行管道 — ChatService 和 AgentService 共享的核心执行逻辑
/// </summary>
public class ChatExecutionPipeline
{
    private readonly IAgentFactory _agentFactory;
    private readonly IAgentThreadInternalService _threadService;
    private readonly IUsageLogService _usageLogService;
    private readonly IQuotaService _quotaService;
    private readonly IOptions<AIOptions> _options;
    private readonly IRepository<Agent, Guid> _agentRepository;
    private readonly ILogger<ChatExecutionPipeline> _logger;

    public ChatExecutionPipeline(
        IAgentFactory agentFactory,
        IAgentThreadInternalService threadService,
        IUsageLogService usageLogService,
        IQuotaService quotaService,
        IOptions<AIOptions> options,
        IRepository<Agent, Guid> agentRepository,
        ILogger<ChatExecutionPipeline> logger)
    {
        _agentFactory = Check.NotNull(agentFactory);
        _threadService = Check.NotNull(threadService);
        _usageLogService = Check.NotNull(usageLogService);
        _quotaService = Check.NotNull(quotaService);
        _options = Check.NotNull(options);
        _agentRepository = Check.NotNull(agentRepository);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 解析 Agent：根据 agentId / provider / model / toolGroups 创建 AgentExecutor
    /// </summary>
    public async Task<AgentResolution> ResolveAgentAsync(Guid? agentId, string? provider, string? model, List<string>? toolGroups, CancellationToken ct)
    {
        var defaultProvider = provider ?? _options.Value.DefaultProvider;

        // 1. 优先使用 AgentId（加载已定义的 Agent）
        if (agentId.HasValue)
        {
            var entity = await _agentRepository.GetAsync(agentId.Value, ct);
            if (entity == null || entity.IsDeleted)
            {
                return AgentResolution.Failure(defaultProvider, model, agentId, ErrorCodes.AgentNotFound);
            }
            if (!entity.IsEnabled)
            {
                return AgentResolution.Failure(defaultProvider, model, agentId, ErrorCodes.AgentDisabled);
            }

            var entityToolGroups = string.IsNullOrWhiteSpace(entity.ToolGroups)
                ? null
                : JsonSerializer.Deserialize<List<string>>(entity.ToolGroups);
            var executor = await _agentFactory.CreateAgentAsync(
                entity.Provider,
                entity.Model,
                entity.Instructions,
                entity.Name,
                entityToolGroups,
                entity.Temperature,
                entity.MaxTokens,
                options: null,
                ct).ConfigureAwait(false);
            return AgentResolution.Success(executor, entity.Provider, entity.Model, agentId);
        }

        // 2. 使用 ToolGroups（无 AgentId 但有工具组）
        if (toolGroups != null && toolGroups.Count > 0)
        {
            var executor = await _agentFactory.CreateAgentAsync(defaultProvider, model, null, null, toolGroups, options: null, ct: ct).ConfigureAwait(false);
            return AgentResolution.Success(executor, defaultProvider, model, null);
        }

        // 3. 仅 Provider/Model（无 AgentId 也无 ToolGroups）
        var defaultExecutor = await _agentFactory.CreateAgentAsync(defaultProvider, model, options: null, ct: ct).ConfigureAwait(false);
        return AgentResolution.Success(defaultExecutor, defaultProvider, model, null);
    }

    /// <summary>
    /// 获取或创建 ConversationContext
    /// </summary>
    public async Task<(ConversationContext? context, Guid? resolvedThreadId)> PrepareThreadAsync(Guid? threadId, Guid agentId, CancellationToken ct)
    {
        if (!threadId.HasValue)
        {
            return (null, null);
        }

        var context = await _threadService.GetOrCreateThreadAsync(threadId, agentId, ct);
        return (context, threadId);
    }

    /// <summary>
    /// 加载历史消息（结构化 ChatMessage 列表，保留角色信息）
    /// </summary>
    public async Task<List<ChatMessage>?> LoadHistoryAsync(Guid threadId, CancellationToken ct)
    {
        if (_options.Value.History.Store.Enabled) return null;

        var historyMessages = await _threadService.GetMessageHistoryAsync(threadId, null, ct);
        return historyMessages.Count > 0 ? historyMessages : null;
    }

    /// <summary>
    /// 执行非流式对话：构建消息列表，调用 AgentExecutor，返回响应
    /// </summary>
    public async Task<AgentResponse> ExecuteAsync(AgentExecutor agent, string message, ConversationContext? context, Guid? threadId, CancellationToken ct)
    {
        // 构建消息列表：context 历史 + 新用户消息
        var messages = new List<ChatMessage>();

        // 如果有线程和上下文，加载历史消息
        if (threadId.HasValue && context != null)
        {
            var history = await LoadHistoryAsync(threadId.Value, ct);
            if (history != null)
            {
                messages.AddRange(history);
            }
            else if (context.Messages.Count > 0)
            {
                // 使用 context 中缓存的消息
                messages.AddRange(context.Messages);
            }
        }

        // 添加新的用户消息
        messages.Add(new ChatMessage(ChatRole.User, message));

        // 调用 AgentExecutor
        var response = await agent.ExecuteAsync(messages, ct);

        // 更新 context 的消息列表
        if (context != null)
        {
            context.Messages = response.Messages;
        }

        return response;
    }

    /// <summary>
    /// 执行流式对话：构建消息列表，调用 AgentExecutor 流式接口
    /// </summary>
    public async IAsyncEnumerable<AgentStreamChunk> ExecuteStreamingAsync(AgentExecutor agent, string message, ConversationContext? context, Guid? threadId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        // 构建消息列表：context 历史 + 新用户消息
        var messages = new List<ChatMessage>();

        // 如果有线程和上下文，加载历史消息
        if (threadId.HasValue && context != null)
        {
            var history = await LoadHistoryAsync(threadId.Value, ct);
            if (history != null)
            {
                messages.AddRange(history);
            }
            else if (context.Messages.Count > 0)
            {
                messages.AddRange(context.Messages);
            }
        }

        // 添加新的用户消息
        messages.Add(new ChatMessage(ChatRole.User, message));

        // 调用 AgentExecutor 流式接口
        await foreach (var chunk in agent.ExecuteStreamingAsync(messages, ct).WithCancellation(ct))
        {
            yield return chunk;
        }

        // 注意：流式执行后 context 的更新由调用方通过 PersistAfterRunAsync 处理
    }

    /// <summary>
    /// 持久化消息（对话结束后保存用户消息和助手回复）
    /// </summary>
    public async Task PersistAfterRunAsync(Guid? threadId, ConversationContext? context, string userMessage, string assistantMessage, CancellationToken ct)
    {
        if (!threadId.HasValue || context == null) return;

        // 仅当未启用 ChatMessageStore 时才手动保存消息
        if (!_options.Value.History.Store.Enabled)
        {
            await _threadService.SaveMessageAsync(threadId.Value, MessageRole.User, userMessage, ct: ct);
            await _threadService.SaveMessageAsync(threadId.Value, MessageRole.Assistant, assistantMessage, ct: ct);
        }

        // ConversationContext 序列化数据始终需要更新
        await _threadService.SaveThreadSerializedDataAsync(threadId.Value, context, ct);
    }

    /// <summary>
    /// 记录使用日志
    /// </summary>
    public async Task LogUsageAsync(string operationType, string provider, string model, int inputTokens, int outputTokens, long durationMs, bool isSuccess, string? errorMessage = null, Guid? agentId = null, Guid? threadId = null, CancellationToken ct = default)
    {
        await _usageLogService.LogUsageAsync(operationType, provider, model, inputTokens, outputTokens, durationMs, isSuccess, errorMessage, agentId, threadId, ct);
    }

    /// <summary>
    /// 原子配额预留（流式版本）：检查并扣减预估 Token，失败时抛出异常
    /// </summary>
    /// <returns>非 null 表示预留成功，null 表示用户无需配额管理</returns>
    public async Task<QuotaReservation?> ReserveQuotaOrThrowAsync(Guid? userId, string message, CancellationToken ct)
    {
        if (!userId.HasValue) return null;

        var estimatedTokens = message.Length / 4 + 500;
        var result = await _quotaService.ReserveQuotaAsync(userId.Value, estimatedTokens, ct);

        if (!result.Succeeded)
        {
            throw new BusinessException(
                result.Message ?? "Quota reservation failed",
                result.Code == 429 ? ErrorCodes.QuotaExceeded : ErrorCodes.QuotaCheckFailed,
                result.Code ?? 500);
        }

        return result.Data;
    }

    /// <summary>
    /// 原子配额预留（Result 版本）：检查并扣减预估 Token
    /// </summary>
    public async Task<(QuotaReservation? reservation, Result<T>? error)> ReserveQuotaAsync<T>(Guid? userId, string message, CancellationToken ct)
    {
        if (!userId.HasValue) return (null, null);

        var estimatedTokens = message.Length / 4 + 500;
        var result = await _quotaService.ReserveQuotaAsync(userId.Value, estimatedTokens, ct);

        if (!result.Succeeded)
        {
            var code = result.Code == 429 ? ErrorCodes.QuotaExceeded : ErrorCodes.QuotaCheckFailed;
            return (null, Result<T>.Failure(result.Message ?? "Quota reservation failed", result.Code ?? 500, code));
        }

        return (result.Data, null);
    }

    /// <summary>
    /// 配额结算：根据实际使用量调整预留差值
    /// </summary>
    public async Task SettleQuotaAsync(Guid? userId, QuotaReservation? reservation, int actualTokens, CancellationToken ct)
    {
        if (!userId.HasValue || reservation == null) return;

        await _quotaService.SettleQuotaAsync(userId.Value, reservation, actualTokens, ct);
    }

    /// <summary>
    /// 将 UsageDetails 映射为 TokenUsageDto
    /// </summary>
    public static TokenUsageDto? MapUsage(UsageDetails? usage)
    {
        if (usage == null) return null;

        return new TokenUsageDto
        {
            PromptTokens = (int)(usage.InputTokenCount ?? 0),
            CompletionTokens = (int)(usage.OutputTokenCount ?? 0),
            TotalTokens = (int)(usage.TotalTokenCount ?? 0)
        };
    }

    /// <summary>
    /// 从流式 chunk 中提取 Token 使用信息
    /// </summary>
    public static (int inputTokens, int outputTokens) ExtractStreamingUsage(AgentStreamChunk chunk)
    {
        if (chunk.Usage != null)
        {
            return ((int)(chunk.Usage.InputTokenCount ?? 0), (int)(chunk.Usage.OutputTokenCount ?? 0));
        }
        return (0, 0);
    }

    /// <summary>
    /// 构建用户消息：支持纯文本和多模态内容（图片、文件）
    /// </summary>
    /// <param name="message">纯文本消息（与 content 二选一）</param>
    /// <param name="content">多模态内容部分列表</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>用于发送给 Agent 的消息文本</returns>
    public Task<string> BuildChatMessageAsync(string? message, List<ContentPartDto>? content, CancellationToken ct)
    {
        // 1. 纯文本模式（向后兼容）
        if (content == null || content.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new BusinessException("Either Message or Content must be provided", ErrorCodes.InvalidContent, 400);
            }
            return Task.FromResult(message!);
        }

        // 2. 多模态模式：将内容部分转换为结构化文本
        var sb = new StringBuilder();

        foreach (var part in content)
        {
            switch (part)
            {
                case TextContentPartDto textPart:
                    if (!string.IsNullOrWhiteSpace(textPart.Text))
                    {
                        sb.AppendLine(textPart.Text);
                    }
                    break;

                case ImageContentPartDto imagePart:
                    if (!string.IsNullOrWhiteSpace(imagePart.Url))
                    {
                        // 验证 URL 格式
                        if (!Uri.TryCreate(imagePart.Url, UriKind.Absolute, out var imageUri) ||
                            (imageUri.Scheme != Uri.UriSchemeHttp && imageUri.Scheme != Uri.UriSchemeHttps && !imageUri.Scheme.Equals("data", StringComparison.OrdinalIgnoreCase)))
                        {
                            throw new BusinessException($"Invalid image URL: must be HTTP(S) or data URI", ErrorCodes.InvalidContent, 400);
                        }
                        sb.AppendLine($"[Image: {imagePart.Url}]");
                    }
                    else if (!string.IsNullOrWhiteSpace(imagePart.Base64Data))
                    {
                        var mediaType = imagePart.MediaType ?? "image/png";
                        sb.AppendLine($"[Image: data:{mediaType};base64,{imagePart.Base64Data[..Math.Min(50, imagePart.Base64Data.Length)]}...]");
                    }
                    else
                    {
                        throw new BusinessException("Image content must have either Url or Base64Data", ErrorCodes.InvalidContent, 400);
                    }
                    break;

                case FileContentPartDto filePart:
                    if (filePart.FileId == Guid.Empty)
                    {
                        throw new BusinessException("File content must have a valid FileId", ErrorCodes.InvalidContent, 400);
                    }
                    var fileName = filePart.FileName ?? filePart.FileId.ToString();
                    sb.AppendLine($"[File: {fileName} (ID: {filePart.FileId})]");
                    break;

                default:
                    _logger.LogWarning("Unknown content part type: {Type}", part.GetType().Name);
                    break;
            }
        }

        if (sb.Length == 0)
        {
            throw new BusinessException("Content must contain at least one non-empty part", ErrorCodes.InvalidContent, 400);
        }

        return Task.FromResult(sb.ToString().TrimEnd());
    }
}

/// <summary>
/// Agent 解析结果
/// </summary>
public class AgentResolution
{
    /// <summary>创建的 AgentExecutor 实例</summary>
    public AgentExecutor? Agent { get; init; }

    /// <summary>提供商名称</summary>
    public string Provider { get; init; } = string.Empty;

    /// <summary>模型名称</summary>
    public string? Model { get; init; }

    /// <summary>Agent ID（当通过已定义 Agent 创建时非 null）</summary>
    public Guid? AgentId { get; init; }

    /// <summary>错误码（仅失败时非 null）</summary>
    public string? ErrorCode { get; init; }

    /// <summary>是否解析成功</summary>
    public bool IsSuccess => Agent != null;

    /// <summary>
    /// 创建成功结果
    /// </summary>
    public static AgentResolution Success(AgentExecutor agent, string provider, string? model, Guid? agentId)
    {
        return new AgentResolution { Agent = agent, Provider = provider, Model = model, AgentId = agentId };
    }

    /// <summary>
    /// 创建失败结果
    /// </summary>
    public static AgentResolution Failure(string provider, string? model, Guid? agentId, string errorCode)
    {
        return new AgentResolution { Provider = provider, Model = model, AgentId = agentId, ErrorCode = errorCode };
    }
}
