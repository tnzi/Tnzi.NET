namespace Tnzi.AI.Dtos;

/// <summary>
/// ITnziAiClient 调用选项
/// </summary>
public class AiClientOptions
{
    /// <summary>指定 Agent ID</summary>
    public Guid? AgentId { get; init; }

    /// <summary>指定 Provider（无需预定义 Agent）</summary>
    public string? Provider { get; init; }

    /// <summary>指定 Model</summary>
    public string? Model { get; init; }

    /// <summary>附加工具组</summary>
    public List<string>? ToolGroups { get; init; }

    /// <summary>是否启用 Run 追踪</summary>
    public bool EnableRunTracking { get; init; }

    /// <summary>用户 ID（用于配额等）</summary>
    public Guid? UserId { get; init; }

    /// <summary>流式粒度模式</summary>
    public StreamMode StreamMode { get; init; } = StreamMode.Messages;
}
