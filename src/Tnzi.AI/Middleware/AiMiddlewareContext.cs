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

    /// <summary>
    /// 本轮要从模型可见工具列表里摘掉的工具名（不区分大小写）。对 Agent 自带的工具与
    /// <see cref="AdditionalTools"/> 一并生效，由 AgentRuntime 在合并工具之后应用。
    /// 由 SkillConstraintMiddleware 写入（已激活技能的组 / 白黑名单约束）。
    /// 这只是「不给模型看」；真正的拦截在工具执行管线（SkillConstraintToolMiddleware），
    /// 那一层不依赖本集合，自定义执行器不实现 <c>WithoutTools</c> 时约束仍然成立。
    /// </summary>
    public HashSet<string> ExcludedToolNames { get; } = new(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>
    /// 本次执行是否创建了新线程（用于首轮事件触发）
    /// </summary>
    public bool IsNewThread { get; set; }

    /// <summary>服务提供者</summary>
    public required IServiceProvider ServiceProvider { get; init; }

    /// <summary>
    /// 经 InputGuardrail 修改后的有效用户消息。
    /// InputGuardrailMiddleware 在 PII 脱敏或内容替换后写入此属性；
    /// AgentRuntime.Core 在构建 ChatMessage 时优先读取此值（如未设置则回退到 Request.UserMessage）。
    /// </summary>
    public string? EffectiveUserMessage { get; set; }

}
