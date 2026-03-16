namespace Tnzi.AI.Middleware;

/// <summary>
/// 中间件执行上下文。
/// 携带请求/响应数据和共享状态，中间件可以读写。
/// </summary>
public class AiMiddlewareContext
{
    /// <summary>当前运行请求</summary>
    public required AgentRunRequest Request { get; init; }

    /// <summary>已解析的 Agent（由 AgentRuntime 预加载）</summary>
    public required AgentResolution Agent { get; init; }

    /// <summary>对话消息（中间件可追加 system/context 消息）</summary>
    public List<ChatMessage> Messages { get; set; } = [];

    /// <summary>附加工具（中间件可注入工具）</summary>
    public List<AITool> AdditionalTools { get; set; } = [];

    /// <summary>引用来源（RAG/Memory 中间件填充）</summary>
    public List<CitationDto> Citations { get; set; } = [];

    /// <summary>共享属性包（中间件间传递数据）</summary>
    public Dictionary<string, object> Properties { get; } = [];

    /// <summary>Model 覆盖（由 SkillConstraintMiddleware 设置，AgentRuntime 在创建 ChatClient 时使用）</summary>
    public string? EffectiveModel { get; set; }

    /// <summary>Provider 覆盖（由 SkillConstraintMiddleware 设置）</summary>
    public string? EffectiveProvider { get; set; }

    /// <summary>当前 Run 实例（如果启用了 Run 追踪）</summary>
    public AgentRun? Run { get; set; }

    /// <summary>服务提供者</summary>
    public required IServiceProvider ServiceProvider { get; init; }
}
