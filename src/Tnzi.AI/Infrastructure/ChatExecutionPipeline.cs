namespace Tnzi.AI.Infrastructure;

/// <summary>
/// AI 对话执行管道 — ChatService 和 AgentService 共享的核心执行逻辑
/// </summary>
public class ChatExecutionPipeline : IChatExecutionPipeline
{
    private readonly IAgentFactory _agentFactory;
    private readonly IAgentThreadInternalService _threadService;
    private readonly IUsageLogService _usageLogService;
    private readonly IQuotaService _quotaService;
    private readonly IQuotaProvider? _quotaProvider;
    private readonly IConversationStore? _conversationStore;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly GuardrailRunner _guardrailRunner;
    private readonly IOptions<AIOptions> _options;
    private readonly IRepository<Agent, Guid> _agentRepository;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ChatExecutionPipeline> _logger;

    public ChatExecutionPipeline(
        IAgentFactory agentFactory,
        IAgentThreadInternalService threadService,
        IUsageLogService usageLogService,
        IQuotaService quotaService,
        ITokenEstimator tokenEstimator,
        GuardrailRunner guardrailRunner,
        IOptions<AIOptions> options,
        IRepository<Agent, Guid> agentRepository,
        IServiceProvider serviceProvider,
        ILogger<ChatExecutionPipeline> logger,
        IQuotaProvider? quotaProvider = null,
        IConversationStore? conversationStore = null)
    {
        _agentFactory = Check.NotNull(agentFactory);
        _threadService = Check.NotNull(threadService);
        _usageLogService = Check.NotNull(usageLogService);
        _quotaService = Check.NotNull(quotaService);
        _tokenEstimator = Check.NotNull(tokenEstimator);
        _guardrailRunner = Check.NotNull(guardrailRunner);
        _quotaProvider = quotaProvider;
        _conversationStore = conversationStore;
        _options = Check.NotNull(options);
        _agentRepository = Check.NotNull(agentRepository);
        _serviceProvider = Check.NotNull(serviceProvider);
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
            if (entity == null)
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
            var userPermissions = await ResolveUserPermissionsAsync(entityToolGroups, ct);
            var executor = await _agentFactory.CreateAgentAsync(
                entity.Provider,
                entity.Model,
                entity.Instructions,
                entity.Name,
                entityToolGroups,
                entity.Temperature,
                entity.MaxTokens,
                options: null,
                userPermissions: userPermissions,
                ct: ct).ConfigureAwait(false);
            return AgentResolution.Success(executor, entity.Provider, entity.Model, agentId);
        }

        // 2. 使用 ToolGroups（无 AgentId 但有工具组）
        if (toolGroups != null && toolGroups.Count > 0)
        {
            var userPermissions = await ResolveUserPermissionsAsync(toolGroups, ct);
            var executor = await _agentFactory.CreateAgentAsync(defaultProvider, model, null, null, toolGroups, options: null, userPermissions: userPermissions, ct: ct).ConfigureAwait(false);
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
    private async Task<List<ChatMessage>?> LoadHistoryAsync(Guid threadId, CancellationToken ct)
    {
        if (!_options.Value.History.Store.Enabled) return null;

        var historyMessages = await _threadService.GetMessageHistoryAsync(threadId, null, ct);
        return historyMessages.Count > 0 ? historyMessages : null;
    }

    /// <summary>
    /// 执行非流式对话：构建消息列表，调用 AgentExecutor，返回响应
    /// </summary>
    public async Task<AgentResponse> ExecuteAsync(AgentExecutor agent, ChatMessage userMessage, ConversationContext? context, Guid? threadId, CancellationToken ct)
    {
        // 1. 输入 Guardrails — 检查用户输入
        var inputText = userMessage.Text ?? string.Empty;
        try
        {
            var (validatedInput, inputRejection) = await _guardrailRunner.RunInputGuardrailsAsync(inputText, ct);

            if (inputRejection != null)
            {
                return new AgentResponse
                {
                    Text = $"[Blocked by {inputRejection.GuardrailName}] {inputRejection.Reason}",
                    FinishReason = "guardrail_rejected"
                };
            }

            // 如果输入被修改，替换用户消息中的文本内容
            if (validatedInput != inputText)
            {
                userMessage = new ChatMessage(ChatRole.User, validatedInput);
            }
        }
        catch (TripwireGuardrailException ex)
        {
            _logger.LogWarning("Tripwire guardrail triggered: {GuardrailName} - {Reason}", ex.GuardrailName, ex.Message);
            return new AgentResponse
            {
                Text = $"[Blocked by {ex.GuardrailName}] {ex.Message}",
                FinishReason = "guardrail_rejected"
            };
        }

        // 2. 构建消息列表：context 历史 + 新用户消息
        var messages = new List<ChatMessage>();

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

        messages.Add(userMessage);

        // 3. 建立工具上下文（让 Singleton 工具访问 Scoped 服务）并调用 AgentExecutor
        AgentResponse response;
        using (ToolContext.Establish(_serviceProvider, ct))
        {
            response = await agent.ExecuteAsync(messages, ct);
        }

        // 4. 输出 Guardrails — 检查 AI 响应
        if (!string.IsNullOrEmpty(response.Text))
        {
            try
            {
                var (validatedOutput, outputRejection) = await _guardrailRunner.RunOutputGuardrailsAsync(response.Text, ct);

                if (outputRejection != null)
                {
                    response.Text = $"[Blocked by {outputRejection.GuardrailName}] {outputRejection.Reason}";
                    response.FinishReason = "guardrail_rejected";
                }
                else if (validatedOutput != response.Text)
                {
                    response.Text = validatedOutput;
                }
            }
            catch (TripwireGuardrailException ex)
            {
                _logger.LogWarning("Tripwire guardrail triggered on output: {GuardrailName} - {Reason}", ex.GuardrailName, ex.Message);
                response.Text = $"[Blocked by {ex.GuardrailName}] {ex.Message}";
                response.FinishReason = "guardrail_rejected";
            }
        }

        // 5. 更新 context 的消息列表
        if (context != null)
        {
            context.Messages = response.Messages;
        }

        return response;
    }

    /// <summary>
    /// 执行流式对话：构建消息列表，调用 AgentExecutor 流式接口
    /// </summary>
    public async IAsyncEnumerable<AgentStreamChunk> ExecuteStreamingAsync(AgentExecutor agent, ChatMessage userMessage, ConversationContext? context, Guid? threadId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        // 1. 输入 Guardrails — 检查用户输入
        var inputText = userMessage.Text ?? string.Empty;
        string validatedInput;
        AgentStreamChunk? inputBlockedChunk = null;

        try
        {
            var (input, inputRejection) = await _guardrailRunner.RunInputGuardrailsAsync(inputText, ct);
            validatedInput = input;

            if (inputRejection != null)
            {
                inputBlockedChunk = new AgentStreamChunk
                {
                    Error = $"[Blocked by {inputRejection.GuardrailName}] {inputRejection.Reason}",
                    FinishReason = "guardrail_rejected"
                };
            }
        }
        catch (TripwireGuardrailException ex)
        {
            _logger.LogWarning("Tripwire guardrail triggered: {GuardrailName} - {Reason}", ex.GuardrailName, ex.Message);
            validatedInput = inputText;
            inputBlockedChunk = new AgentStreamChunk
            {
                Error = $"[Blocked by {ex.GuardrailName}] {ex.Message}",
                FinishReason = "guardrail_rejected"
            };
        }

        if (inputBlockedChunk != null)
        {
            yield return inputBlockedChunk;
            yield break;
        }

        if (validatedInput != inputText)
        {
            userMessage = new ChatMessage(ChatRole.User, validatedInput);
        }

        // 2. 构建消息列表：context 历史 + 新用户消息
        var messages = new List<ChatMessage>();

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

        messages.Add(userMessage);

        // 3. 建立工具上下文并流式执行
        using var toolContextScope = ToolContext.Establish(_serviceProvider, ct);

        var guardrailsEnabled = _options.Value.Guardrails.Enabled;
        var bufferSize = _options.Value.Guardrails.StreamingBufferSize;

        if (!guardrailsEnabled || bufferSize <= 0)
        {
            // 未启用输出 guardrails 或禁用缓冲 — 直接转发（原始行为）
            await foreach (var chunk in agent.ExecuteStreamingAsync(messages, ct).WithCancellation(ct))
            {
                yield return chunk;
            }
        }
        else
        {
            // 启用输出 guardrails — 滑动缓冲区：每累积 bufferSize 字符执行一次检查，通过后释放
            var accumulatedText = new StringBuilder();
            var bufferedChunks = new List<AgentStreamChunk>();
            var lastCheckedLength = 0;

            await foreach (var chunk in agent.ExecuteStreamingAsync(messages, ct).WithCancellation(ct))
            {
                bufferedChunks.Add(chunk);
                if (chunk.Text != null)
                {
                    accumulatedText.Append(chunk.Text);
                }

                // 每累积 bufferSize 字符执行一次增量检查
                if (accumulatedText.Length - lastCheckedLength >= bufferSize)
                {
                    var partialText = accumulatedText.ToString();
                    var rejection = await RunOutputGuardrailSafeAsync(partialText, ct);
                    if (rejection != null)
                    {
                        yield return rejection;
                        yield break;
                    }
                    lastCheckedLength = accumulatedText.Length;

                    // 检查通过 — 释放已缓冲的 chunk
                    foreach (var buffered in bufferedChunks)
                    {
                        yield return buffered;
                    }
                    bufferedChunks.Clear();
                }
            }

            // 流结束 — 最终检查剩余缓冲内容
            if (accumulatedText.Length > lastCheckedLength)
            {
                var fullOutput = accumulatedText.ToString();
                var rejection = await RunOutputGuardrailSafeAsync(fullOutput, ct);
                if (rejection != null)
                {
                    yield return rejection;
                    yield break;
                }
            }

            // 最终检查通过 — 释放剩余 chunk
            foreach (var buffered in bufferedChunks)
            {
                yield return buffered;
            }
        }
    }

    /// <summary>
    /// 解析当前用户已授权的工具权限集合
    /// </summary>
    /// <remarks>
    /// 从 IToolRegistry 获取工具组中所有工具的 RequiredPermissions，
    /// 逐一通过 IPermissionChecker 检查，返回已授权权限集合。
    /// 当 Identity 模块未加载（无 IPermissionChecker）或工具无权限要求时返回 null（不过滤）。
    /// </remarks>
    private async Task<IEnumerable<string>?> ResolveUserPermissionsAsync(IEnumerable<string>? toolGroups, CancellationToken ct)
    {
        if (toolGroups == null) return null;

        var permissionChecker = _serviceProvider.GetService<IPermissionChecker>();
        if (permissionChecker == null) return null;

        var toolRegistry = _serviceProvider.GetRequiredService<IToolRegistry>();
        var toolDefinitions = toolRegistry.GetToolsByGroups(toolGroups);

        // 收集所有工具声明的权限要求
        var allRequiredPermissions = toolDefinitions
            .SelectMany(t => t.RequiredPermissions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (allRequiredPermissions.Count == 0) return null;

        // 逐一检查权限，构建已授权集合
        var grantedPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var permission in allRequiredPermissions)
        {
            if (await permissionChecker.IsGrantedAsync(permission))
            {
                grantedPermissions.Add(permission);
            }
        }

        return grantedPermissions;
    }

    /// <summary>
    /// 安全执行输出 Guardrails：返回 null 表示通过，返回 chunk 表示被拒绝
    /// </summary>
    private async Task<AgentStreamChunk?> RunOutputGuardrailSafeAsync(string text, CancellationToken ct)
    {
        try
        {
            var (_, outputRejection) = await _guardrailRunner.RunOutputGuardrailsAsync(text, ct);
            if (outputRejection != null)
            {
                return new AgentStreamChunk
                {
                    Error = $"[Output blocked by {outputRejection.GuardrailName}] {outputRejection.Reason}",
                    FinishReason = "guardrail_rejected"
                };
            }
        }
        catch (TripwireGuardrailException ex)
        {
            _logger.LogWarning("Tripwire guardrail triggered on output: {GuardrailName} - {Reason}", ex.GuardrailName, ex.Message);
            return new AgentStreamChunk
            {
                Error = $"[Blocked by {ex.GuardrailName}] {ex.Message}",
                FinishReason = "guardrail_rejected"
            };
        }
        return null;
    }

    /// <summary>
    /// 持久化消息（对话结束后保存用户消息和助手回复）
    /// </summary>
    public async Task PersistAfterRunAsync(Guid? threadId, ConversationContext? context, ChatMessage userMessage, string assistantMessage, CancellationToken ct)
    {
        if (!threadId.HasValue || context == null) return;

        // 仅当未启用 ChatMessageStore 时才手动保存消息
        if (!_options.Value.History.Store.Enabled)
        {
            // 提取用户消息的文本内容用于持久化
            var userText = userMessage.Text ?? string.Empty;
            await _threadService.SaveMessageAsync(threadId.Value, MessageRole.User, userText, ct: ct);
            await _threadService.SaveMessageAsync(threadId.Value, MessageRole.Assistant, assistantMessage, ct: ct);
        }

        // ConversationContext 序列化数据始终需要更新
        await _threadService.SaveThreadSerializedDataAsync(threadId.Value, context, ct);
    }

    /// <summary>
    /// 记录使用日志并上报 OTel 指标
    /// </summary>
    public async Task LogUsageAsync(string operationType, string provider, string model, int inputTokens, int outputTokens, long durationMs, bool isSuccess, string? errorMessage = null, Guid? agentId = null, Guid? threadId = null, CancellationToken ct = default)
    {
        await _usageLogService.LogUsageAsync(operationType, provider, model, inputTokens, outputTokens, durationMs, isSuccess, errorMessage, agentId, threadId, ct);

        // OTel 指标上报
        Observability.AIActivitySource.RecordChatRequest(provider, model, operationType);
        if (isSuccess)
        {
            Observability.AIActivitySource.RecordTokenUsage(provider, model, inputTokens, outputTokens);
            Observability.AIActivitySource.RecordChatLatency(provider, model, durationMs / 1000.0);
        }
        else if (errorMessage != null)
        {
            Observability.AIActivitySource.RecordError(provider, model, errorMessage);
        }
    }

    /// <summary>
    /// 原子配额预留（流式版本）：检查并扣减预估 Token，失败时抛出异常
    /// </summary>
    /// <returns>非 null 表示预留成功，null 表示用户无需配额管理</returns>
    public async Task<QuotaReservation?> ReserveQuotaOrThrowAsync(Guid? userId, string message, CancellationToken ct)
    {
        if (!userId.HasValue) return null;

        var estimatedTokens = _tokenEstimator.Estimate(message);
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

        var estimatedTokens = _tokenEstimator.Estimate(message);
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
    /// 从流式 chunk 中提取 Token 使用信息
    /// </summary>
    internal static (int inputTokens, int outputTokens) ExtractStreamingUsage(AgentStreamChunk chunk)
    {
        if (chunk.Usage != null)
        {
            return (chunk.Usage.PromptTokens, chunk.Usage.CompletionTokens);
        }
        return (0, 0);
    }

    /// <summary>
    /// 构建用户消息：支持纯文本和多模态内容（图片、文件）
    /// </summary>
    /// <param name="message">纯文本消息（与 content 二选一）</param>
    /// <param name="content">多模态内容部分列表</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>包含正确 AIContent 类型的 ChatMessage（文本返回 TextContent，图片返回 DataContent）</returns>
    public Task<ChatMessage> BuildChatMessageAsync(string? message, List<ContentPartDto>? content, CancellationToken ct)
    {
        // 1. 纯文本模式（向后兼容）
        if (content == null || content.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new BusinessException("Either Message or Content must be provided", ErrorCodes.InvalidContent, 400);
            }
            return Task.FromResult(new ChatMessage(ChatRole.User, message!));
        }

        // 2. 多模态模式：构建包含正确 AIContent 类型的 ChatMessage
        var contents = new List<AIContent>();

        foreach (var part in content)
        {
            switch (part)
            {
                case TextContentPartDto textPart:
                    if (!string.IsNullOrWhiteSpace(textPart.Text))
                    {
                        contents.Add(new TextContent(textPart.Text));
                    }
                    break;

                case ImageContentPartDto imagePart:
                    contents.Add(BuildImageContent(imagePart));
                    break;

                case FileContentPartDto filePart:
                    if (filePart.FileId == Guid.Empty)
                    {
                        throw new BusinessException("File content must have a valid FileId", ErrorCodes.InvalidContent, 400);
                    }
                    // 文件引用暂以文本占位符表示（需要 Storage 模块集成后实现完整的文件内容加载）
                    var fileName = filePart.FileName ?? filePart.FileId.ToString();
                    contents.Add(new TextContent($"[File: {fileName} (ID: {filePart.FileId})]"));
                    break;

                default:
                    _logger.LogWarning("Unknown content part type: {Type}", part.GetType().Name);
                    break;
            }
        }

        if (contents.Count == 0)
        {
            throw new BusinessException("Content must contain at least one non-empty part", ErrorCodes.InvalidContent, 400);
        }

        return Task.FromResult(new ChatMessage(ChatRole.User, contents));
    }

    /// <summary>
    /// 从 ImageContentPartDto 构建 MEAI DataContent（MEAI 中图片即 DataContent + image/* mediaType）
    /// </summary>
    private static DataContent BuildImageContent(ImageContentPartDto imagePart)
    {
        if (!string.IsNullOrWhiteSpace(imagePart.Url))
        {
            // URL 模式：验证后构建 DataContent
            if (!Uri.TryCreate(imagePart.Url, UriKind.Absolute, out var imageUri) ||
                (imageUri.Scheme != Uri.UriSchemeHttp && imageUri.Scheme != Uri.UriSchemeHttps && !imageUri.Scheme.Equals("data", StringComparison.OrdinalIgnoreCase)))
            {
                throw new BusinessException("Invalid image URL: must be HTTP(S) or data URI", ErrorCodes.InvalidContent, 400);
            }
            return new DataContent(imageUri, imagePart.MediaType ?? "image/png");
        }

        if (!string.IsNullOrWhiteSpace(imagePart.Base64Data))
        {
            // Base64 模式：解码后构建 DataContent
            var bytes = Convert.FromBase64String(imagePart.Base64Data);
            return new DataContent(bytes, imagePart.MediaType ?? "image/png");
        }

        throw new BusinessException("Image content must have either Url or Base64Data", ErrorCodes.InvalidContent, 400);
    }
}
