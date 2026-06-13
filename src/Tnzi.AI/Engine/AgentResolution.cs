namespace Tnzi.AI.Engine;

/// <summary>
/// Agent 解析结果
/// </summary>
public class AgentResolution
{
    /// <summary>创建的 AgentExecutor 实例（以接口暴露，使核心层不依赖具体引擎实现）</summary>
    public IAgentExecutor? Agent { get; init; }

    /// <summary>提供商名称</summary>
    public string Provider { get; init; } = string.Empty;

    /// <summary>模型名称</summary>
    public string? Model { get; init; }

    /// <summary>Agent ID（当通过已定义 Agent 创建时非 null）</summary>
    public Guid? AgentId { get; init; }

    /// <summary>执行模式</summary>
    public AgentExecutionMode ExecutionMode { get; init; } = AgentExecutionMode.Single;

    /// <summary>额外配置 JSON（当通过已定义 Agent 创建时传递）</summary>
    public string? AgentConfiguration { get; init; }

    /// <summary>
    /// Persona FK from the resolved Agent entity. When set, ContextInjectionMiddleware
    /// loads the persona content via IAgentPersonaService and injects a &lt;soul&gt; block
    /// into the system prompt. Independent of AgentConfiguration JSON so DB-loaded agents
    /// don't need to round-trip personaId through the execution config blob.
    /// </summary>
    public Guid? PersonaId { get; init; }

    /// <summary>
    /// Inline persona content already loaded from an out-of-band source
    /// (e.g. workspace PERSONA.md). Takes precedence over PersonaId — when set,
    /// ContextInjectionMiddleware injects this content directly without hitting
    /// IAgentPersonaService. Enables persona for agents that have no DB row.
    /// </summary>
    public string? PersonaContent { get; init; }

    /// <summary>
    /// 该 Agent 分配的知识库 ID 列表（来自实体 KnowledgeBaseIds）。
    /// ContextInjectionMiddleware 传给 TextSearchProvider，使 RAG 检索仅限这些知识库。
    /// </summary>
    public IReadOnlyList<Guid>? KnowledgeBaseIds { get; init; }

    /// <summary>
    /// 该 Agent 分配的技能 slug 列表（来自实体 SkillSlugs）。
    /// 非空时 SkillContextProvider 仅暴露这些技能。
    /// </summary>
    public IReadOnlyList<string>? SkillSlugs { get; init; }

    /// <summary>错误码（仅失败时非 null）</summary>
    public string? ErrorCode { get; init; }

    /// <summary>是否解析成功</summary>
    public bool IsSuccess => Agent != null;

    /// <summary>
    /// 创建 AgentExecutor 时使用的原始参数 — 供 SkillConstraintMiddleware 触发模型/Provider 覆盖时重建 Executor 使用
    /// </summary>
    public AgentCreationParameters? CreationParameters { get; init; }

    /// <summary>
    /// 创建成功结果
    /// </summary>
    public static AgentResolution Success(IAgentExecutor agent, string provider, string? model, Guid? agentId, string? agentConfiguration = null, AgentExecutionMode executionMode = AgentExecutionMode.Single, AgentCreationParameters? creationParameters = null, Guid? personaId = null, string? personaContent = null, IReadOnlyList<Guid>? knowledgeBaseIds = null, IReadOnlyList<string>? skillSlugs = null)
    {
        return new AgentResolution { Agent = agent, Provider = provider, Model = model, AgentId = agentId, AgentConfiguration = agentConfiguration, ExecutionMode = executionMode, CreationParameters = creationParameters, PersonaId = personaId, PersonaContent = personaContent, KnowledgeBaseIds = knowledgeBaseIds, SkillSlugs = skillSlugs };
    }

    /// <summary>
    /// 创建失败结果
    /// </summary>
    public static AgentResolution Failure(string provider, string? model, Guid? agentId, string errorCode)
    {
        return new AgentResolution { Provider = provider, Model = model, AgentId = agentId, ErrorCode = errorCode };
    }
}

/// <summary>
/// AgentExecutor 创建时的原始参数快照 — 用于在模型/Provider 覆盖时重建执行器。
/// <c>ToolNames</c> 携带 per-tool 授权，使 SkillConstraintMiddleware 重建时保留单工具授权。
/// </summary>
public record AgentCreationParameters(
    string? Instructions,
    string? Name,
    IEnumerable<string>? ToolGroups,
    double? Temperature,
    int? MaxTokens,
    IEnumerable<string>? UserPermissions,
    IEnumerable<string>? ToolNames = null);
