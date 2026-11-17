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

    /// <summary>
    /// 这次调用新建线程时（不给 threadId）写进线程的来源（<c>AgentThreadDto.Origin</c>），例如 <c>Schedule</c>；续接已有线程时不用。
    /// 应用自己的值，只校验形状。
    /// </summary>
    public string? ThreadOrigin { get; init; }

    /// <summary>
    /// 这次调用新建线程时（不给 threadId）它在不在主人的列表里（<c>AgentThreadDto.Listing</c>）；续接已有线程时不用。
    /// 默认 <see cref="ThreadListing.Listed"/>；无人值守的运行给 <see cref="ThreadListing.UntilUserMessage"/>。
    /// </summary>
    public ThreadListing ThreadListing { get; init; }

    /// <summary>用户 ID（用于配额等）</summary>
    public Guid? UserId { get; init; }

    /// <summary>流式粒度模式</summary>
    public StreamMode StreamMode { get; init; } = StreamMode.Messages;
}
