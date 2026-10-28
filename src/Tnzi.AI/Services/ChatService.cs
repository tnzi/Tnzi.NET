namespace Tnzi.AI.Services;

/// <summary>
/// 聊天服务实现 - 委托给 <see cref="IAgentDispatchFacade"/> 执行，
/// Quota/Guardrails/History/UsageLogging 全部由中间件处理
/// </summary>
/// <remarks>
/// <para>
/// 注入的是路由门面而不是 <see cref="IAgentRuntime"/>：门面签名与 Runtime 逐字相同，
/// 但会在 Agent 绑定了外部 CLI 运行时时改走外部执行域。本类因此**不知道**、也不需要知道
/// 底下走了哪条路 —— 这正是把分支收在门面里的目的。
/// </para>
/// <para>
/// 本类自己负责的是聊天语义：谁能与哪个 Agent 对话（<see cref="IChatAgentPolicy"/>）、编辑与重新生成
/// （回退线程后再跑一轮，两条执行路径同一个做法）、以及把门面的块翻译成线缆上的 <see cref="StreamEvent"/>。
/// </para>
/// </remarks>
public class ChatService : ApplicationService, IChatService
{
    private readonly IAgentDispatchFacade _runtime;
    private readonly IOptionsMonitor<AIOptions> _options;
    private readonly IChatAgentPolicy? _agentPolicy;
    private readonly IAgentThreadService? _threadService;
    private readonly IAgentThreadInternalService? _threadInternal;
    private readonly IAgentAttachmentService? _attachments;
    private readonly IOptionsMonitor<AiChatOptions>? _chatOptions;
    private readonly IUsageLimitService? _usageLimits;
    private readonly IUsagePolicyService? _usagePolicy;

    public ChatService(
        IAgentDispatchFacade runtime,
        IOptionsMonitor<AIOptions> options,
        IServiceProvider serviceProvider,
        IChatAgentPolicy? agentPolicy = null,
        IAgentThreadService? threadService = null,
        IAgentThreadInternalService? threadInternal = null,
        IAgentAttachmentService? attachments = null,
        IOptionsMonitor<AiChatOptions>? chatOptions = null,
        IUsageLimitService? usageLimits = null,
        IUsagePolicyService? usagePolicy = null)
        : base(serviceProvider)
    {
        _runtime = Check.NotNull(runtime);
        _options = Check.NotNull(options);
        _agentPolicy = agentPolicy;
        _threadService = threadService;
        _threadInternal = threadInternal;
        _attachments = attachments;
        _chatOptions = chatOptions;
        _usageLimits = usageLimits;
        _usagePolicy = usagePolicy;
    }

    public async Task<Result<ChatResponseDto>> ChatAsync(ChatRequestDto request, CancellationToken ct = default)
    {
        try
        {
            var prepared = await PrepareTurnAsync(request, ct);
            if (!prepared.Succeeded)
            {
                return Fail<ChatResponseDto>(prepared.Message!, prepared.Code ?? 400, prepared.ErrorCode, prepared.ErrorDetails);
            }

            var runRequest = prepared.Data!;
            var result = await _runtime.RunAsync(runRequest, ct);
            if (TryMapFailure(result, out var statusCode, out var errorCode))
            {
                // 用量上限带上窗口与重置时间：客户端据此说「哪个上限到了、大约几点恢复」，而不必去解析那句话
                object? details = errorCode == ErrorCodes.UsageLimitReached
                    ? new UsageLimitDenialDto { Window = result.UsageLimitWindow ?? UsageLimitWindow.Session, ResetsAt = result.UsageLimitResetsAt }
                    : result.UsageLimitResetsAt is { } resetsAt
                        ? new Dictionary<string, object> { ["usageLimitResetsAt"] = resetsAt }
                        : null;
                return Fail<ChatResponseDto>(result.Response, statusCode, errorCode, details);
            }

            return Ok(new ChatResponseDto
            {
                Content = result.Response,
                FinishReason = result.FinishReason,
                Model = result.Model ?? request.Model,
                Usage = result.Usage,
                ThreadId = result.ThreadId,
                Citations = result.Citations,
                HandoffPath = result.HandoffPath,
                Reasoning = result.Reasoning,
                UserMessageId = result.UserMessageId,
                AssistantMessageId = result.AssistantMessageId
            });
        }
        catch (BusinessException ex)
        {
            Logger.LogWarning(ex,
                "Chat request failed with business exception: Provider={Provider}, Model={Model}, UserId={UserId}, ThreadId={ThreadId}",
                request.Provider ?? _options.CurrentValue.DefaultProvider,
                request.Model ?? "default",
                request.UserId,
                request.ThreadId);

            return Fail<ChatResponseDto>(ex.Message, ex.HttpStatusCode, ex.Code, ex.ErrorDetails);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "Chat request failed: Provider={Provider}, Model={Model}, UserId={UserId}, ThreadId={ThreadId}",
                request.Provider ?? _options.CurrentValue.DefaultProvider,
                request.Model ?? "default",
                request.UserId,
                request.ThreadId);

            return Fail<ChatResponseDto>("Chat request failed.", 500, ErrorCodes.ChatFailed);
        }
    }

    private static bool TryMapFailure(AgentRunResult result, out int statusCode, out string errorCode)
        => AgentStreamMapper.TryMapFailure(result, ErrorCodes.ChatFailed, out statusCode, out errorCode);

    public async IAsyncEnumerable<StreamEvent> ChatStreamingAsync(ChatRequestDto request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        // 准备（策略、回退）失败以 BusinessException 抛出：此时一个事件都还没写出，客户端拿到带状态码的普通 HTTP 错误
        var prepared = await PrepareTurnAsync(request, ct);
        if (!prepared.Succeeded)
        {
            throw new BusinessException(prepared.Message!, prepared.ErrorCode ?? ErrorCodes.ChatFailed, prepared.Code ?? 400)
            {
                ErrorDetails = prepared.ErrorDetails
            };
        }

        var runRequest = prepared.Data!;

        // HistoryMiddleware / 门面在首批块到达前已解析出真实线程并回填到请求上，直接从 runRequest 读取。
        await foreach (var evt in ToStreamEventsAsync(
                           _runtime.RunStreamingAsync(runRequest, ct), () => runRequest.ThreadId, request.Model, ct))
        {
            yield return evt;
        }
    }

    /// <inheritdoc />
    public async Task<Result<List<ChatRunDto>>> GetActiveRunsAsync(Guid threadId, Guid userId, CancellationToken ct = default)
    {
        if (_threadService is null || !await _threadService.IsOwnerAsync(threadId, userId))
        {
            return Fail<List<ChatRunDto>>("Thread not found", 404, ErrorCodes.ThreadNotFound);
        }

        var runs = await _runtime.GetActiveRunsAsync(threadId, ct);
        return Ok(runs.ToList());
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<StreamEvent> AttachStreamingAsync(
        Guid runId, int fromSequence = 0, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var run = await _runtime.GetRunAsync(runId, ct);
        if (run is null)
        {
            // 与「不是你的」同一个答案；此时一个事件都还没写出，客户端拿到 404
            throw new BusinessException("Run not found.", ErrorCodes.ChatRunNotFound, 404);
        }

        await foreach (var evt in ToStreamEventsAsync(
                           _runtime.AttachStreamingAsync(runId, fromSequence, ct), () => run.ThreadId, model: null, ct))
        {
            yield return evt;
        }
    }

    /// <inheritdoc />
    public Task<Result> CancelRunAsync(Guid runId, CancellationToken ct = default)
        => _runtime.CancelRunAsync(runId, ct);

    /// <inheritdoc />
    public Task<Result> AnswerRunPermissionAsync(Guid runId, Guid requestId, CliPermissionAnswerDto answer, CancellationToken ct = default)
        => _runtime.AnswerRunPermissionAsync(runId, requestId, Check.NotNull(answer), ct);

    /// <summary>
    /// 一轮开始前的聊天规则：能不能与这个 Agent 对话，以及编辑 / 重新生成要先回退线程。
    /// 返回要交给门面的运行请求。
    /// </summary>
    private async Task<Result<AgentRunRequest>> PrepareTurnAsync(ChatRequestDto request, CancellationToken ct)
    {
        Check.NotNull(request);

        if (_agentPolicy is not null)
        {
            var allowed = await _agentPolicy.CheckAsync(request.AgentId, request.UserId, ct);
            if (!allowed.Succeeded)
            {
                return Fail<AgentRunRequest>(
                    allowed.Message ?? "This agent is not available for chat.",
                    allowed.Code ?? 403,
                    allowed.ErrorCode ?? ErrorCodes.ChatAgentNotAllowed);
            }
        }

        // 先校验元数据再回退线程：一份不合规的元数据不该让用户先丢掉被替换的那几轮
        var metadata = ChatTurnMetadata.Validate(request.Metadata, _chatOptions?.CurrentValue.Metadata ?? new ChatMetadataOptions());
        if (!metadata.Succeeded)
        {
            return Fail<AgentRunRequest>(metadata.Message!, metadata.Code ?? 400, metadata.ErrorCode);
        }

        // 用量上限同理，排在回退与任何写入之前：被拦下的一轮不留线程、不留用户消息，编辑 / 重新生成也不先删掉被替换的那几轮。
        // 这里只查不开窗口：窗口在这一轮真的开始时才开（内建路径在 QuotaMiddleware，外部 CLI 路径在执行器认领之后）
        if (request.UserId is { } userId && _usageLimits is not null
            && await _usageLimits.CheckAsync(userId, ct) is { } denial)
        {
            return Fail<AgentRunRequest>(UsageLimitMessage(denial), 429, ErrorCodes.UsageLimitReached, denial);
        }

        // 这一轮用哪个模型：请求点名的必须在这个用户被允许的模型里，没点名用他的默认模型（没配模型清单时原样不动）
        var model = request.Model;
        if (_usagePolicy is not null)
        {
            var resolved = await _usagePolicy.ResolveChatModelAsync(request.UserId, request.AgentId, request.Provider, request.Model, ct);
            if (!resolved.Succeeded)
            {
                return Fail<AgentRunRequest>(resolved.Message!, resolved.Code ?? 403, resolved.ErrorCode ?? ErrorCodes.ModelNotAllowed);
            }

            model = resolved.Data;
        }

        var message = request.Message;
        var content = request.Content;
        IReadOnlyDictionary<string, JsonElement> turnMetadata = metadata.Data!;
        var rewound = false;

        if (request.EditMessageId.HasValue || request.Regenerate)
        {
            var rewind = await RewindAsync(request, ct);
            if (!rewind.Succeeded)
            {
                return Fail<AgentRunRequest>(rewind.Message!, rewind.Code ?? 400, rewind.ErrorCode);
            }

            (message, content, var storedMetadata) = rewind.Data!;
            rewound = true;

            // 重新生成没带元数据时沿用那条消息存下的那份：同一个问题再问一次，指向的也该是同一批东西
            if (request.Regenerate && turnMetadata.Count == 0)
            {
                turnMetadata = ChatTurnMetadata.Parse(storedMetadata) ?? turnMetadata;
            }
        }

        return Ok(new AgentRunRequest
        {
            OperationType = AIOperationType.Chat,
            AgentId = request.AgentId,
            Provider = request.Provider,
            Model = model,
            ToolGroups = request.ToolGroups,
            UserMessage = message,
            ContentParts = content,
            ThreadId = request.ThreadId,
            UserId = request.UserId,
            ReasoningEffort = request.ReasoningEffort,
            RewoundTurns = rewound,
            TurnMetadata = turnMetadata.Count > 0 ? turnMetadata : null
        });
    }

    /// <summary>用量上限的拒绝说明（英文：面向用户的错误消息；客户端按错误码与详情显示本地化的提示）。</summary>
    private static string UsageLimitMessage(UsageLimitDenialDto denial)
    {
        var window = denial.Window == UsageLimitWindow.Session ? "5-hour" : "weekly";
        return denial.ResetsAt is { } resetsAt
            ? $"You have reached your {window} usage limit. It resets at {resetsAt:yyyy-MM-dd HH:mm} UTC."
            : $"You have reached your {window} usage limit.";
    }

    /// <summary>回退之后这一轮要发的内容，以及被替换的那条用户消息存下的元数据（重新生成时沿用）。</summary>
    private sealed record RewoundTurn(string? Message, List<ContentPartDto>? Content, string? StoredMetadataJson);

    /// <summary>
    /// 编辑 / 重新生成：核线程与目标消息，确认线程上没有在跑的运行，删掉目标消息及其后的全部消息，
    /// 返回这一轮要发的内容（编辑 = 请求带来的新内容；重新生成 = 那条消息原来的文本与附件）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 两条执行路径同一个做法：内建引擎从线程加载历史，删掉之后历史里就没有它们了；外部 CLI 运行从「它所回答的
    /// 那条用户消息之前」的最后一轮续接会话（截断续接），被替换的轮次也就不在 agent 的会话里了。
    /// </para>
    /// <para>
    /// 先删后跑：这一轮随后被拒（配额、处理器）时，被替换的轮次已经没了，那正是用户要的（他要替换它们），
    /// 只是新的这一轮没跑成，再发一次即可。线程上还有运行在跑时一律 409：删掉一条运行正在回答的消息，
    /// 它的回复会写到一个不存在的提问后面。
    /// </para>
    /// </remarks>
    private async Task<Result<RewoundTurn>> RewindAsync(ChatRequestDto request, CancellationToken ct)
    {
        if (request.EditMessageId.HasValue && request.Regenerate)
        {
            return Fail<RewoundTurn>(
                "Use either EditMessageId or Regenerate, not both.", 400, ErrorCodes.InvalidContent);
        }

        if (request.ThreadId is not { } threadId)
        {
            return Fail<RewoundTurn>(
                "ThreadId is required to edit or regenerate a message.", 400, ErrorCodes.InvalidContent);
        }

        if (_threadService is null || _threadInternal is null)
        {
            return Fail<RewoundTurn>(
                "Editing and regenerating messages is not available in this deployment.", 501, ErrorCodes.InternalError);
        }

        if (request.UserId is not { } userId || !await _threadService.IsOwnerAsync(threadId, userId))
        {
            return Fail<RewoundTurn>("Thread not found", 404, ErrorCodes.ThreadNotFound);
        }

        if (request.Regenerate && (!string.IsNullOrEmpty(request.Message) || request.Content is { Count: > 0 }))
        {
            return Fail<RewoundTurn>(
                "Regenerate sends the last user message again: leave Message and Content empty.", 400, ErrorCodes.InvalidContent);
        }

        var active = await _runtime.GetActiveRunsAsync(threadId, ct);
        if (active.Count > 0)
        {
            return Fail<RewoundTurn>(
                "This conversation has a reply in progress. Stop it before editing or regenerating.",
                409, ErrorCodes.ThreadRunInProgress);
        }

        var target = await _threadInternal.FindUserMessageAsync(threadId, request.EditMessageId, ct);
        if (target is null)
        {
            return Fail<RewoundTurn>(
                request.Regenerate ? "There is no message to regenerate." : "Only your own messages in this conversation can be edited.",
                400, ErrorCodes.MessageNotEditable);
        }

        string? message = request.Message;
        var content = request.Content;
        if (request.Regenerate)
        {
            (message, content) = await RebuildContentAsync(target, ct);
        }

        await _threadInternal.DeleteMessagesFromAsync(threadId, target.Order, ct);
        return Ok(new RewoundTurn(message, content, target.MetadataJson));
    }

    /// <summary>
    /// 一条已存的用户消息原样还原成请求内容：文本，加上随它存档的附件（图片与 PDF 以内联数据带回）。
    /// </summary>
    private async Task<(string? Message, List<ContentPartDto>? Content)> RebuildContentAsync(ThreadMessageRef target, CancellationToken ct)
    {
        var stored = _attachments is null
            ? []
            : (await _attachments.GetForMessageAsync(target.Id, ct)).Where(a => a.Origin == AttachmentOrigin.Upload).ToList();

        if (stored.Count == 0)
        {
            return (target.Content, null);
        }

        var parts = new List<ContentPartDto> { new TextContentPartDto { Text = target.Content } };
        foreach (var attachment in stored)
        {
            var data = Convert.ToBase64String(attachment.Content);
            parts.Add(attachment.MediaType.StartsWith("image/", StringComparison.Ordinal)
                ? new ImageContentPartDto { Base64Data = data, MediaType = attachment.MediaType, FileName = attachment.FileName }
                : new DocumentContentPartDto { Base64Data = data, MediaType = attachment.MediaType, FileName = attachment.FileName });
        }

        return (target.Content, parts);
    }

    /// <summary>
    /// 门面的块 → 线缆上的事件。终止块之后立刻补一个 <c>IsDone</c> 事件（赶在中间件的落库收尾之前），
    /// 一个终止块都没有时在流尾补发。
    /// </summary>
    private async IAsyncEnumerable<StreamEvent> ToStreamEventsAsync(
        IAsyncEnumerable<AgentStreamChunk> chunks, Func<Guid?> threadIdOf, string? model,
        [EnumeratorCancellation] CancellationToken ct)
    {
        int inputTokens = 0;
        int outputTokens = 0;
        Guid? userMessageId = null;
        Guid? assistantMessageId = null;
        Guid? runId = null;
        string? streamErrorMessage = null;
        string? streamErrorCode = null;
        DateTime? usageLimitResetsAt = null;
        UsageLimitWindow? usageLimitWindow = null;
        string? streamFinishReason = null;
        string? streamModel = model;
        List<CitationDto>? citations = null;
        bool earlyDoneSent = false;

        await foreach (var chunk in chunks.WithCancellation(ct))
        {
            var currentThreadId = threadIdOf();

            // 累加 Token 用量：每个 chunk 的 Usage 是一次 LLM 调用的增量（StreamUsage 口径）
            var (inp, outp) = ChatMessageHelper.ExtractStreamingUsage(chunk);
            inputTokens += inp;
            outputTokens += outp;

            // 收集 Citations（通常只在最终 chunk 中包含）
            if (chunk.Citations != null)
            {
                citations = chunk.Citations;
            }
            if (!string.IsNullOrWhiteSpace(chunk.Model))
            {
                streamModel = chunk.Model;
            }

            // 发送 delta 事件、工具调用状态事件或错误事件（guardrail 拦截等）
            if (chunk.Error != null)
            {
                streamErrorMessage = chunk.Error;
                streamFinishReason = chunk.FinishReason;
            }

            // Capture persisted message ids stamped onto the terminal chunk by HistoryMiddleware.
            if (chunk.UserMessageId.HasValue) userMessageId = chunk.UserMessageId;
            if (chunk.AssistantMessageId.HasValue) assistantMessageId = chunk.AssistantMessageId;
            if (chunk.RunId.HasValue) runId = chunk.RunId;
            if (chunk.UsageLimitResetsAt.HasValue) usageLimitResetsAt = chunk.UsageLimitResetsAt;
            if (chunk.UsageLimitWindow.HasValue) usageLimitWindow = chunk.UsageLimitWindow;

            var streamEvent = AgentStreamMapper.BuildStreamEvent(chunk, currentThreadId, ErrorCodes.ChatFailed);
            if (streamEvent != null)
            {
                if (streamEvent.IsError)
                {
                    streamErrorCode = streamEvent.ErrorCode;
                }

                yield return streamEvent;
            }

            // Yield isDone early when ANY terminal FinishReason is detected, so the client
            // receives it BEFORE the next MoveNextAsync() triggers middleware cleanup (DB writes).
            // All non-null FinishReasons are terminal for the current stream.
            if (!earlyDoneSent && chunk.FinishReason != null)
            {
                earlyDoneSent = true;
                yield return DoneEvent(chunk.FinishReason, streamErrorMessage, streamErrorCode, streamModel, currentThreadId,
                    inputTokens, outputTokens, citations, userMessageId, assistantMessageId, runId, usageLimitResetsAt, usageLimitWindow);
            }
        }

        // Fallback: send isDone if it wasn't already sent early (e.g., error paths or missing FinishReason)
        if (!earlyDoneSent)
        {
            var hasStreamError = streamErrorMessage != null;
            yield return DoneEvent(hasStreamError ? (streamFinishReason ?? "error") : "stop", streamErrorMessage, streamErrorCode,
                streamModel, threadIdOf(), inputTokens, outputTokens, citations, userMessageId, assistantMessageId, runId,
                usageLimitResetsAt, usageLimitWindow);
        }
    }

    private static StreamEvent DoneEvent(
        string finishReason, string? errorMessage, string? errorCode, string? model, Guid? threadId,
        int inputTokens, int outputTokens, List<CitationDto>? citations,
        Guid? userMessageId, Guid? assistantMessageId, Guid? runId, DateTime? usageLimitResetsAt, UsageLimitWindow? usageLimitWindow)
    {
        var hasError = errorMessage != null;
        return new StreamEvent
        {
            IsDone = true,
            FinishReason = finishReason,
            IsError = hasError,
            ErrorMessage = hasError ? errorMessage : null,
            ErrorCode = hasError ? errorCode : null,
            Model = model,
            ThreadId = threadId,
            Usage = new TokenUsageDto
            {
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                TotalTokens = inputTokens + outputTokens
            },
            Citations = citations,
            UserMessageId = userMessageId,
            AssistantMessageId = assistantMessageId,
            RunId = runId,
            UsageLimitResetsAt = hasError ? usageLimitResetsAt : null,
            UsageLimitWindow = hasError ? usageLimitWindow : null
        };
    }
}
