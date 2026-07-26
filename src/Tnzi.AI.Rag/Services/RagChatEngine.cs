namespace Tnzi.AI.Rag.Services;

/// <summary>
/// RAG 聊天引擎 - 多轮对话，带历史上下文
/// <para>
/// 委托给 IAgentRuntime.RunAsync/RunStreamingAsync，通过 ThreadId 维护对话历史，
/// 结合 RAG 检索上下文注入到用户消息中。支持完整的 AI 中间件管道。
/// </para>
/// </summary>
public class RagChatEngine : ApplicationService, IRagChatEngine
{
    private readonly IRagRetriever _retriever;
    private readonly IAgentRuntime _agentRuntime;

    public RagChatEngine(
        IServiceProvider serviceProvider,
        IRagRetriever retriever,
        IAgentRuntime agentRuntime) : base(serviceProvider)
    {
        _retriever = Check.NotNull(retriever);
        _agentRuntime = Check.NotNull(agentRuntime);
    }

    /// <inheritdoc />
    public async Task<Result<RagChatResult>> ChatAsync(RagChatRequest request, CancellationToken ct = default)
    {
        Check.NotNull(request);

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return Fail<RagChatResult>("Query cannot be empty", 400);
        }

        // 1. 检索相关文档
        var retrievalResults = await RetrieveContextAsync(request, ct);

        // 2. 构建增强的用户消息（RAG 上下文 + 原始问题）
        var augmentedMessage = BuildAugmentedMessage(request.Query, retrievalResults);

        // 3. 委托给 AgentRuntime（完整中间件管道 + 对话历史）
        var runRequest = new AgentRunRequest
        {
            AgentId = request.AgentId,
            UserMessage = augmentedMessage,
            ThreadId = request.ThreadId
        };

        var runResult = await _agentRuntime.RunAsync(runRequest, ct);

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

        // 1. 检索相关文档（在流式开始前完成）
        var retrievalResults = await RetrieveContextAsync(request, ct);

        // 2. 构建增强的用户消息
        var augmentedMessage = BuildAugmentedMessage(request.Query, retrievalResults);

        // 3. 委托给 AgentRuntime 流式执行
        var runRequest = new AgentRunRequest
        {
            AgentId = request.AgentId,
            UserMessage = augmentedMessage,
            ThreadId = request.ThreadId
        };

        // 4. 转发流式事件，在最终事件中注入 citations
        var citations = request.IncludeCitations ? RagRetriever.BuildCitations(retrievalResults) : null;

        await foreach (var chunk in _agentRuntime.RunStreamingAsync(runRequest, ct))
        {
            // 将 AgentStreamChunk 转换为 StreamEvent
            var evt = new StreamEvent
            {
                Delta = chunk.Text,
                FinishReason = chunk.FinishReason,
                Usage = chunk.Usage,
                IsDone = chunk.FinishReason != null
            };

            // 在最终事件中注入 citations（拷贝一份：直接挂共享列表再 AddRange 会把 chunk 的
            // citations 永久写进共享列表，多个终止事件时不断累积重复项）
            if (evt.IsDone && citations is { Count: > 0 })
            {
                var eventCitations = new List<CitationDto>(citations);
                if (chunk.Citations is { Count: > 0 })
                {
                    eventCitations.AddRange(chunk.Citations);
                }

                evt.Citations = eventCitations;
            }

            yield return evt;
        }
    }

    /// <summary>
    /// 执行 RAG 检索
    /// </summary>
    private async Task<List<RetrievalResult>> RetrieveContextAsync(RagQueryRequest request, CancellationToken ct)
    {
        var options = new RagRetrievalOptions
        {
            KnowledgeBaseIds = request.KnowledgeBaseIds,
            TopK = request.TopK,
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
