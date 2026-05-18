namespace Tnzi.AI.Dtos;

/// <summary>
/// AI 运行结果
/// </summary>
public class AgentRunResult
{
    /// <summary>响应内容</summary>
    public required string Response { get; init; }

    /// <summary>关联的 Run ID（启用追踪时非 null）</summary>
    public Guid? RunId { get; init; }

    /// <summary>对话线程 ID</summary>
    public Guid? ThreadId { get; init; }

    /// <summary>Token 使用量</summary>
    public TokenUsageDto? Usage { get; init; }

    /// <summary>引用来源</summary>
    public List<CitationDto>? Citations { get; init; }

    /// <summary>完成原因</summary>
    public string? FinishReason { get; init; }

    /// <summary>实际执行使用的模型</summary>
    public string? Model { get; init; }

    /// <summary>实际执行使用的提供商</summary>
    public string? Provider { get; init; }

    /// <summary>执行路径（Handoff/Router 等多 Agent 模式）</summary>
    public List<string>? HandoffPath { get; init; }

    /// <summary>最终产生回答的 Agent 名称</summary>
    public string? FinalAgentName { get; init; }

    /// <summary>运行状态（启用追踪时非 null）</summary>
    public AgentRunStatus? Status { get; init; }

    /// <summary>推理/思考过程内容（非流式时填充）</summary>
    public string? Reasoning { get; init; }

    /// <summary>是否需要用户澄清（从 Status 派生）</summary>
    public bool RequiresClarification => Status == AgentRunStatus.RequiresClarification;

    /// <summary>后续建议问题（由 ISuggestionService 生成）</summary>
    public List<string>? Suggestions { get; init; }

    /// <summary>当前 Todo 列表（Plan Mode 下由 TodoMiddleware 填充）</summary>
    public List<TodoItemDto>? Todos { get; init; }

    /// <summary>本次运行产出的文件产物</summary>
    public List<AgentArtifactDto>? Artifacts { get; init; }

    /// <summary>澄清问题（Status=RequiresClarification 时非 null）</summary>
    public string? ClarificationQuestion { get; init; }

    /// <summary>Persisted user message ID for this turn (set by HistoryMiddleware when persisted).</summary>
    public Guid? UserMessageId { get; init; }

    /// <summary>Persisted assistant message ID for this turn (set by HistoryMiddleware when persisted).</summary>
    public Guid? AssistantMessageId { get; init; }

    /// <summary>
    /// 创建副本并覆盖指定字段。用于中间件修改结果时保留所有原始字段。
    /// </summary>
    public AgentRunResult CloneWith(
        string? response = null,
        Guid? runId = null,
        Guid? threadId = null,
        TokenUsageDto? usage = null,
        List<CitationDto>? citations = null,
        string? finishReason = null,
        string? model = null,
        string? provider = null,
        List<string>? handoffPath = null,
        string? finalAgentName = null,
        AgentRunStatus? status = null,
        string? reasoning = null,
        List<string>? suggestions = null,
        List<TodoItemDto>? todos = null,
        List<AgentArtifactDto>? artifacts = null,
        string? clarificationQuestion = null,
        Guid? userMessageId = null,
        Guid? assistantMessageId = null)
    {
        return new AgentRunResult
        {
            Response = response ?? Response,
            RunId = runId ?? RunId,
            ThreadId = threadId ?? ThreadId,
            Usage = usage ?? Usage,
            Citations = citations ?? Citations,
            FinishReason = finishReason ?? FinishReason,
            Model = model ?? Model,
            Provider = provider ?? Provider,
            HandoffPath = handoffPath ?? HandoffPath,
            FinalAgentName = finalAgentName ?? FinalAgentName,
            Status = status ?? Status,
            Reasoning = reasoning ?? Reasoning,
            Suggestions = suggestions ?? Suggestions,
            Todos = todos ?? Todos,
            Artifacts = artifacts ?? Artifacts,
            ClarificationQuestion = clarificationQuestion ?? ClarificationQuestion,
            UserMessageId = userMessageId ?? UserMessageId,
            AssistantMessageId = assistantMessageId ?? AssistantMessageId
        };
    }
}
