namespace Tnzi.AI.Rag.Services;

/// <summary>
/// RAG 聊天引擎 - 多轮对话，带历史上下文
/// <para>
/// 委托给 <see cref="IAgentDispatchFacade"/>（签名与 <c>IAgentRuntime</c> 相同，Agent 绑了外部 CLI 运行时则改走外部执行域），
/// 通过 ThreadId 维护对话历史，结合 RAG 检索上下文注入到用户消息中。支持完整的 AI 中间件管道。
/// </para>
/// <para>
/// ★ 运行请求必须带上调用者（<c>UserId</c>），与 <c>ChatService</c> 同口径：配额 / 预算中间件见到空用户直接放行，
/// 绑了用户的工具权限规则在空用户下一律不匹配。少了它，这个端点就是绕开按用户额度与工具限制的一扇侧门。
/// </para>
/// </summary>
public class RagChatEngine : ApplicationService, IRagChatEngine
{
    private readonly IRagRetriever _retriever;
    private readonly IAgentDispatchFacade _dispatcher;
    private readonly IRagAccessAuthorizer _authorizer;
    private readonly AIRagOptions _ragOptions;

    public RagChatEngine(
        IServiceProvider serviceProvider,
        IRagRetriever retriever,
        IAgentDispatchFacade dispatcher,
        IRagAccessAuthorizer authorizer,
        IOptionsSnapshot<AIRagOptions> ragOptions) : base(serviceProvider)
    {
        _retriever = Check.NotNull(retriever);
        _dispatcher = Check.NotNull(dispatcher);
        _authorizer = Check.NotNull(authorizer);
        _ragOptions = Check.NotNull(ragOptions).Value;
    }

    /// <inheritdoc />
    public async Task<Result<RagChatResult>> ChatAsync(RagChatRequest request, CancellationToken ct = default)
    {
        Check.NotNull(request);

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return Fail<RagChatResult>("Query cannot be empty", 400);
        }

        // 0. 知识库级授权（与 RagQueryEngine 同一处判定）
        var authorized = await _authorizer.AuthorizeQueryAsync(request.KnowledgeBaseIds, ct);
        if (!authorized.Succeeded)
        {
            return Fail<RagChatResult>(authorized.Message ?? "Access denied", authorized.Code ?? 403, authorized.ErrorCode);
        }

        // 1. 检索相关文档
        var retrievalResults = await RetrieveContextAsync(request, authorized.Data!, ct);

        // 2. 构建增强的用户消息（RAG 上下文 + 原始问题）
        var augmentedMessage = BuildAugmentedMessage(request.Query, retrievalResults);

        // 3. 委托给路由门面（完整中间件管道 + 对话历史）
        var runResult = await _dispatcher.RunAsync(BuildRunRequest(request, augmentedMessage), ct);

        // 配额 / 预算 / 护栏拒绝不是一个回答：按 ChatService 同一张表映射成 429 / 400 / 500，
        // 而不是把「Quota exceeded」当成 200 的 answer 发回去
        if (AgentStreamMapper.TryMapFailure(runResult, ErrorCodes.ChatFailed, out var statusCode, out var errorCode))
        {
            return Fail<RagChatResult>(runResult.Response, statusCode, errorCode);
        }

        // 4. 构建引用列表
        var citations = request.IncludeCitations
            ? RagRetriever.BuildCitations(retrievalResults)
            : [];

        // 合并 Runtime 返回的 citations
        if (runResult.Citations is { Count: > 0 })
        {
            citations.AddRange(runResult.Citations);
        }

        return Ok(new RagChatResult
        {
            Answer = runResult.Response,
            ThreadId = runResult.ThreadId ?? Guid.Empty,
            Citations = citations,
            Usage = runResult.Usage
        });
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<StreamEvent> ChatStreamingAsync(
        RagChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        Check.NotNull(request);

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            yield return new StreamEvent
            {
                IsError = true,
                IsDone = true,
                ErrorMessage = "Query cannot be empty"
            };
            yield break;
        }

        // 0. 知识库级授权（与非流式路径同一处判定 —— 流式端点少了它就是同一个洞的另一扇门）
        var authorized = await _authorizer.AuthorizeQueryAsync(request.KnowledgeBaseIds, ct);
        if (!authorized.Succeeded)
        {
            yield return new StreamEvent
            {
                IsError = true,
                IsDone = true,
                ErrorMessage = authorized.Message ?? "Access denied",
                ErrorCode = authorized.ErrorCode
            };
            yield break;
        }

        // 1. 检索相关文档（在流式开始前完成）
        var retrievalResults = await RetrieveContextAsync(request, authorized.Data!, ct);

        // 2. 构建增强的用户消息
        var augmentedMessage = BuildAugmentedMessage(request.Query, retrievalResults);

        // 3. 委托给路由门面流式执行
        var runRequest = BuildRunRequest(request, augmentedMessage);

        // 4. 转发流式事件（与 /api/ai/chat/stream 同一映射：文本、推理、工具调用、错误都带上线程 Id），
        //    见到结束原因就紧跟一个结束事件，带上结局、用量与 citations
        var retrievalCitations = request.IncludeCitations ? RagRetriever.BuildCitations(retrievalResults) : null;
        var outcome = new StreamOutcome();
        var doneSent = false;

        await foreach (var chunk in _dispatcher.RunStreamingAsync(runRequest, ct))
        {
            outcome.Observe(chunk);

            // 线程 Id 每块现读：首轮不带 threadId 时由线程解析中间件建线程并回写到运行请求上
            var evt = AgentStreamMapper.BuildStreamEvent(chunk, runRequest.ThreadId, ErrorCodes.ChatFailed);
            if (evt != null)
            {
                yield return evt;
            }

            // 结束事件在下一次 MoveNextAsync 之前发出，客户端先拿到结局；循环继续跑完，
            // 让中间件在终止块之后的收尾（落库消息、记用量）照常执行（与 ChatService 同一做法）
            if (!doneSent && chunk.FinishReason != null)
            {
                doneSent = true;
                yield return outcome.BuildDoneEvent(chunk.FinishReason, runRequest.ThreadId, retrievalCitations);
            }
        }

        // 流在没有结束原因的情况下结束：仍以一个结束事件收尾，客户端不必靠连接关闭来猜
        if (!doneSent)
        {
            yield return outcome.BuildDoneEvent(finishReason: null, runRequest.ThreadId, retrievalCitations);
        }
    }

    /// <summary>
    /// 流式运行到目前为止的结局：最近一次的错误、累计用量、模型与运行时给出的 citations。
    /// </summary>
    private sealed class StreamOutcome
    {
        private string? _error;
        private string? _errorFinishReason;
        private string? _model;
        private TokenUsageDto? _usage;
        private List<CitationDto>? _runtimeCitations;

        public void Observe(AgentStreamChunk chunk)
        {
            if (chunk.Error != null)
            {
                _error = chunk.Error;
                _errorFinishReason = chunk.FinishReason;
            }

            // 每个 chunk 的用量是一次 LLM 调用的增量（StreamUsage 口径），结束事件给出累计值
            _usage = StreamUsage.Add(_usage, chunk.Usage);

            if (!string.IsNullOrWhiteSpace(chunk.Model))
            {
                _model = chunk.Model;
            }

            if (chunk.Citations is { Count: > 0 })
            {
                _runtimeCitations = chunk.Citations;
            }
        }

        /// <summary>
        /// 结束事件。citations 每次新建一份：检索 citations 是整次请求共用的列表，直接挂上去再追加运行时的会把后者永久写进去。
        /// </summary>
        public StreamEvent BuildDoneEvent(string? finishReason, Guid? threadId, List<CitationDto>? retrievalCitations)
        {
            var isError = _error != null;
            var citations = new List<CitationDto>();
            if (retrievalCitations is { Count: > 0 })
            {
                citations.AddRange(retrievalCitations);
            }

            if (_runtimeCitations is { Count: > 0 })
            {
                citations.AddRange(_runtimeCitations);
            }

            return new StreamEvent
            {
                IsDone = true,
                FinishReason = finishReason ?? (isError ? _errorFinishReason ?? FinishReasons.Error : FinishReasons.Stop),
                IsError = isError,
                ErrorMessage = _error,
                ErrorCode = !isError
                    ? null
                    : _errorFinishReason == FinishReasons.GuardrailRejected ? ErrorCodes.GuardrailRejected : ErrorCodes.ChatFailed,
                Model = _model,
                ThreadId = threadId,
                Usage = _usage,
                Citations = citations.Count > 0 ? citations : null
            };
        }
    }

    /// <summary>
    /// 构造运行请求：调用者身份取自 <see cref="ApplicationService.CurrentUser"/>（请求体里没有、也不该有这个字段），
    /// 操作类型与 <c>ChatService</c> 同口径（流式与否由用量中间件区分）。
    /// </summary>
    private AgentRunRequest BuildRunRequest(RagChatRequest request, string augmentedMessage)
        => new()
        {
            OperationType = AIOperationType.Chat,
            AgentId = request.AgentId,
            UserMessage = augmentedMessage,
            ThreadId = request.ThreadId,
            UserId = CurrentUser?.Id
        };

    /// <summary>
    /// 执行 RAG 检索
    /// </summary>
    private async Task<List<RetrievalResult>> RetrieveContextAsync(
        RagQueryRequest request, IReadOnlyList<Guid> authorizedKnowledgeBaseIds, CancellationToken ct)
    {
        // TopK 静默 clamp 到 MaxTopK：Chat 与 ChatStreaming 共用这一处，流式端点少了它就是同一个洞的另一扇门
        var options = new RagRetrievalOptions
        {
            KnowledgeBaseIds = authorizedKnowledgeBaseIds.ToList(),
            TopK = Math.Clamp(request.TopK, 1, _ragOptions.MaxTopK),
            MinRelevance = request.MinRelevance
        };

        return await _retriever.RetrieveAsync(request.Query, options, ct);
    }

    /// <summary>
    /// 构建包含 RAG 上下文的增强用户消息
    /// </summary>
    private static string BuildAugmentedMessage(string query, List<RetrievalResult> results)
    {
        if (results.Count == 0)
        {
            return query;
        }

        var sb = new StringBuilder();
        sb.AppendLine("The following context from the knowledge base may be relevant to answering the question.");
        sb.AppendLine("Use it to provide an accurate, well-sourced answer.");
        sb.AppendLine();

        for (var i = 0; i < results.Count; i++)
        {
            var r = results[i];
            sb.AppendLine($"[Source {i + 1}] (relevance: {r.Score:F2})");
            sb.AppendLine(r.Content);
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine($"Question: {query}");

        return sb.ToString();
    }

}
