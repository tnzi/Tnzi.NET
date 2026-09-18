namespace Tnzi.AI.Dtos;

/// <summary>
/// 建行时记下的请求快照，落在 <c>AgentRun.RequestSnapshot</c>（JSON），续跑按它重建请求。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>为什么要存</b>：行上只有 AgentId / ThreadId / InputSummary（截到 500 字），续跑靠这三个字段重建请求时，
/// 模板 spawn（AgentId 为 null）的运行会退化成一个没有任何工具的默认 agent，DB agent 的运行丢掉子 Agent 标记
/// （<c>ToolResolver</c> 的子 Agent 裁剪与 <c>IsSubAgentOnly</c> 规则整体失效，比原运行更宽），用户消息也只剩摘要。
/// 这些都没有别的地方可回填：trace 只记调用方给的事件数据，不含请求。
/// </para>
/// <para>
/// 刻意不存的：<c>ExistingRunId</c> / <c>EnableRunTracking</c>（续跑自己定）、<c>WorkflowId</c> / <c>WorkflowInputs</c>
/// （工作流运行走 <c>WorkflowDelegator.ResumeWorkflowRunAsync</c>，不经这里）、<c>ContentParts</c>（多模态内容体可能很大，
/// 且续跑的语境是「接着上一轮」，历史里已经有它）、<c>StreamMode</c>（续跑是非流式的）、<c>Ephemeral</c>（临时运行不建行）。
/// <c>TrustedToolSelection</c> 原样记下：HTTP 来的自选工具组续跑时仍要过 <c>AI:AdHocTools</c> 允许列表。
/// </para>
/// </remarks>
public sealed class AgentRunRequestSnapshot
{
    public string? OperationType { get; init; }
    public Guid? AgentId { get; init; }
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public string? UserMessage { get; init; }
    public List<string>? ToolGroups { get; init; }
    public List<string>? ToolNames { get; init; }
    public bool TrustedToolSelection { get; init; }
    public bool IsBackground { get; init; }
    public string? SubAgentName { get; init; }
    public int? AgentVersionNumber { get; init; }
    public ReasoningEffort? ReasoningEffort { get; init; }
    public List<FileAttachment>? Attachments { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
    public bool PlanMode { get; init; }

    public static AgentRunRequestSnapshot From(AgentRunRequest request)
    {
        Check.NotNull(request);

        return new AgentRunRequestSnapshot
        {
            OperationType = request.OperationType,
            AgentId = request.AgentId,
            Provider = request.Provider,
            Model = request.Model,
            UserMessage = request.UserMessage,
            ToolGroups = request.ToolGroups?.ToList(),
            ToolNames = request.ToolNames?.ToList(),
            TrustedToolSelection = request.TrustedToolSelection,
            IsBackground = request.IsBackground,
            SubAgentName = request.SubAgentName,
            AgentVersionNumber = request.AgentVersionNumber,
            ReasoningEffort = request.ReasoningEffort,
            Attachments = request.Attachments?.ToList(),
            Metadata = request.Metadata?.ToDictionary(kv => kv.Key, kv => kv.Value),
            PlanMode = request.PlanMode
        };
    }

    public string Serialize() => JsonSerializer.Serialize(this, TnziJsonDefaults.Options);

    /// <summary>
    /// 解析落库的快照。空 = 迁移前的旧行（没有快照）；有内容却解析不了不当成「没有」——那会静默续跑成另一个 agent。
    /// </summary>
    /// <exception cref="JsonException">快照内容损坏。</exception>
    public static AgentRunRequestSnapshot? Parse(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<AgentRunRequestSnapshot>(json, TnziJsonDefaults.Options);
}
