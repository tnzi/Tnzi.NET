namespace Tnzi.AI.Dtos;

/// <summary>
/// AI 运行结果
/// </summary>
public class AgentRunResult
{
    /// <summary>响应内容</summary>
    public required string Response { get; init; }

    /// <summary>
    /// 交付物：最后一次工具调用之后的文本，剔除过程叙述（「我先看一下日志」这类）。
    /// </summary>
    /// <remarks>
    /// <b>为 null 表示「交付物就是 <see cref="Response"/> 本身」</b>，这是常态：非流式执行的
    /// <see cref="Response"/> 取的是最后一次模型响应的文本，工具调用轮次之间的叙述根本不在里面。
    /// 只有流式执行会把整轮的文本增量累积成一条完整回复，过程叙述与最终答案因此混在一起 ——
    /// 那时本字段才会被填上。
    /// <para>
    /// 消费方一律用 <see cref="EffectiveDeliverable"/>，不要自己写 <c>?? Response</c>。
    /// 对外出站（chat 回复、渠道推送）该用交付物；审计留痕、要展示推理过程的场景该用
    /// <see cref="Response"/> 全文。
    /// </para>
    /// <para>
    /// 判定边界只有工具调用一处 —— 模型把叙述和答案发成同一种文本增量，没有别的信号可用。
    /// 因此「最后一次工具调用之后没有任何文本」时回落到上一个非空文本块，而不是给出空串：
    /// 一个空的交付物会让出站消息变成空白，那比多一句过程话术糟得多。
    /// </para>
    /// <para>
    /// ⚠️ <b>改写 <see cref="Response"/> 会作废本字段</b>（<see cref="CloneWith"/> 自动置 null）：
    /// 交付物是从<i>旧</i>全文里切出来的。输出防护脱敏或替换全文之后若还留着旧交付物，
    /// 读 <see cref="EffectiveDeliverable"/> 的消费方就会拿到刚被拦下的原文，
    /// 而防护看起来仍在正常工作。要在改写全文的同时保留交付物语义，必须显式传入新的交付物。
    /// </para>
    /// </remarks>
    public string? Deliverable { get; init; }

    /// <summary>
    /// 实际应当发给用户的文本：有交付物用交付物，否则用全文。
    /// </summary>
    public string EffectiveDeliverable => Deliverable ?? Response;

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
        Guid? assistantMessageId = null,
        string? deliverable = null)
    {
        return new AgentRunResult
        {
            Response = response ?? Response,

            // Rewriting the response invalidates the deliverable, because the deliverable was cut
            // out of the *old* response. Carrying it over would let a caller reading
            // EffectiveDeliverable receive text the output guardrails just redacted or replaced -
            // the guardrail would still look like it was working. Dropping it falls back to the
            // new response, which is the safe direction.
            Deliverable = deliverable ?? (response is null ? Deliverable : null),
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
