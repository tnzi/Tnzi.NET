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

    /// <summary>
    /// 创建副本并覆盖指定字段。用于中间件修改结果时保留所有原始字段。
    /// </summary>
    public AgentRunResult CloneWith(
        string? response = null,
        Guid? threadId = null,
        string? finishReason = null,
        AgentRunStatus? status = null)
    {
        return new AgentRunResult
        {
            Response = response ?? Response,
            RunId = RunId,
            ThreadId = threadId ?? ThreadId,
            Usage = Usage,
            Citations = Citations,
            FinishReason = finishReason ?? FinishReason,
            HandoffPath = HandoffPath,
            FinalAgentName = FinalAgentName,
            Status = status ?? Status,
            Reasoning = Reasoning,
            Suggestions = Suggestions,
            Todos = Todos,
            Artifacts = Artifacts,
            ClarificationQuestion = ClarificationQuestion
        };
    }
}
