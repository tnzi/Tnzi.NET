namespace Tnzi.AI.Infrastructure;

/// <summary>
/// Agent 工厂接口
/// </summary>
public interface IAgentFactory
{
    /// <summary>
    /// 异步创建 AgentExecutor（支持 MCP 工具拉取与合并；当 AI:Mcp:Enabled 时无论 toolGroups 是否为空都会合并 MCP 工具）
    /// </summary>
    /// <param name="providerName">提供商名称（为空则使用默认提供商）</param>
    /// <param name="model">模型名称（为空则使用提供商默认模型）</param>
    /// <param name="instructions">系统指令</param>
    /// <param name="name">Agent 名称</param>
    /// <param name="toolGroups">工具组列表（为空时若启用 MCP 则 Agent 仅带 MCP 工具）</param>
    /// <param name="temperature">温度参数</param>
    /// <param name="maxTokens">最大 Token 数</param>
    /// <param name="options">自定义 AgentExecutorOptions（可选，用于注入 HistoryReducer/ContextProvider）；不会原地修改，内部使用副本合并参数</param>
    /// <param name="userPermissions">用户权限列表（为空时不过滤工具权限）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>AgentExecutor 实例</returns>
    Task<AgentExecutor> CreateAgentAsync(
        string? providerName = null,
        string? model = null,
        string? instructions = null,
        string? name = null,
        IEnumerable<string>? toolGroups = null,
        double? temperature = null,
        int? maxTokens = null,
        AgentExecutorOptions? options = null,
        IEnumerable<string>? userPermissions = null,
        CancellationToken ct = default);
}

/// <summary>
/// ChatClient 提供商接口 — 可插拔的 AI 提供商抽象
/// </summary>
/// <remarks>
/// 每个提供商（OpenAI、Azure、Anthropic 等）实现此接口。
/// ChatClientFactory 通过 DI 收集所有注册的 Provider 并按名称分发。
/// </remarks>
public interface IChatClientProvider
{
    /// <summary>
    /// 提供商名称（与配置中的 Providers 字典键匹配）
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// 创建 IChatClient 实例
    /// </summary>
    /// <param name="options">提供商配置选项</param>
    /// <param name="model">模型名称</param>
    /// <returns>MEAI IChatClient 实例</returns>
    IChatClient CreateChatClient(ProviderOptions options, string model);

    /// <summary>
    /// 创建 IEmbeddingGenerator 实例（可选，部分提供商不支持）
    /// </summary>
    /// <param name="options">提供商配置选项</param>
    /// <param name="model">模型名称</param>
    /// <returns>嵌入生成器，不支持时返回 null</returns>
    IEmbeddingGenerator<string, Embedding<float>>? CreateEmbeddingGenerator(ProviderOptions options, string model);

    /// <summary>
    /// 创建底层原生 SDK 客户端（可选，用于需要原生 SDK 功能的场景）
    /// </summary>
    /// <param name="options">提供商配置选项</param>
    /// <returns>原生客户端对象，不支持时返回 null</returns>
    object? CreateNativeClient(ProviderOptions options) => null;
}

/// <summary>
/// ChatClient 工厂接口 — 返回 MEAI 抽象类型
/// </summary>
/// <remarks>
/// 接口只暴露 MEAI 抽象方法。需要 OpenAI SDK 类型时，
/// 使用 <see cref="ChatClientFactoryExtensions"/> 中的扩展方法。
/// </remarks>
public interface IChatClientFactory
{
    /// <summary>
    /// 获取 IChatClient（MEAI 抽象）
    /// </summary>
    IChatClient GetChatClient(string? providerName = null, string? model = null);

    /// <summary>
    /// 获取 IEmbeddingGenerator（MEAI 抽象）
    /// </summary>
    IEmbeddingGenerator<string, Embedding<float>>? GetEmbeddingGenerator(string? providerName = null, string? model = null);

    /// <summary>
    /// 获取所有已配置且启用的提供商名称列表
    /// </summary>
    IReadOnlyList<string> GetAvailableProviders() => [];

    /// <summary>
    /// 获取指定提供商的默认模型名称
    /// </summary>
    string? GetDefaultModel(string? providerName = null) => null;
}

/// <summary>
/// AI 对话执行管道接口 — ChatService 和 AgentService 共享的核心执行逻辑
/// </summary>
public interface IChatExecutionPipeline
{
    /// <summary>
    /// 解析 Agent：根据 agentId / provider / model / toolGroups 创建 AgentExecutor
    /// </summary>
    Task<AgentResolution> ResolveAgentAsync(Guid? agentId, string? provider, string? model, List<string>? toolGroups, CancellationToken ct);

    /// <summary>
    /// 获取或创建 ConversationContext
    /// </summary>
    Task<(ConversationContext? context, Guid? resolvedThreadId)> PrepareThreadAsync(Guid? threadId, Guid agentId, CancellationToken ct);

    /// <summary>
    /// 执行非流式对话：构建消息列表，调用 AgentExecutor，返回响应
    /// </summary>
    Task<AgentResponse> ExecuteAsync(AgentExecutor agent, ChatMessage userMessage, ConversationContext? context, Guid? threadId, CancellationToken ct);

    /// <summary>
    /// 执行流式对话：构建消息列表，调用 AgentExecutor 流式接口
    /// </summary>
    IAsyncEnumerable<AgentStreamChunk> ExecuteStreamingAsync(AgentExecutor agent, ChatMessage userMessage, ConversationContext? context, Guid? threadId, CancellationToken ct);

    /// <summary>
    /// 持久化消息（对话结束后保存用户消息和助手回复）
    /// </summary>
    Task PersistAfterRunAsync(Guid? threadId, ConversationContext? context, ChatMessage userMessage, string assistantMessage, CancellationToken ct);

    /// <summary>
    /// 记录使用日志并上报 OTel 指标
    /// </summary>
    Task LogUsageAsync(string operationType, string provider, string model, int inputTokens, int outputTokens, long durationMs, bool isSuccess, string? errorMessage = null, Guid? agentId = null, Guid? threadId = null, CancellationToken ct = default);

    /// <summary>
    /// 原子配额预留（流式版本）：检查并扣减预估 Token，失败时抛出异常
    /// </summary>
    Task<QuotaReservation?> ReserveQuotaOrThrowAsync(Guid? userId, string message, CancellationToken ct);

    /// <summary>
    /// 原子配额预留（Result 版本）：检查并扣减预估 Token
    /// </summary>
    Task<(QuotaReservation? reservation, Result<T>? error)> ReserveQuotaAsync<T>(Guid? userId, string message, CancellationToken ct);

    /// <summary>
    /// 配额结算：根据实际使用量调整预留差值
    /// </summary>
    Task SettleQuotaAsync(Guid? userId, QuotaReservation? reservation, int actualTokens, CancellationToken ct);

    /// <summary>
    /// 构建用户消息：支持纯文本和多模态内容（图片、文件）
    /// </summary>
    /// <returns>包含正确 AIContent 类型的 ChatMessage（文本返回 TextContent，图片返回 DataContent）</returns>
    Task<ChatMessage> BuildChatMessageAsync(string? message, List<ContentPartDto>? content, CancellationToken ct);
}

/// <summary>
/// 工作流构建器工厂接口 — 从工作流定义构建 AgentExecutor 列表
/// </summary>
public interface IWorkflowBuilderFactory
{
    /// <summary>
    /// 从工作流定义构建 AgentExecutor 列表和执行模式（Sequential/Parallel 模式）
    /// </summary>
    Task<(IReadOnlyList<AgentExecutor> Agents, WorkflowExecutionMode ExecutionMode)> BuildWorkflowAsync(
        WorkflowDefinition workflowDef,
        CancellationToken ct = default);

    /// <summary>
    /// 从工作流定义构建 DAG 步骤列表（DAG 模式：含步骤 ID、依赖关系和条件）
    /// </summary>
    Task<IReadOnlyList<DagStep>> BuildDagStepsAsync(
        WorkflowDefinition workflowDef,
        CancellationToken ct = default);
}

/// <summary>
/// 使用日志服务接口
/// </summary>
public interface IUsageLogService
{
    /// <summary>
    /// 记录使用日志
    /// </summary>
    Task LogUsageAsync(
        string operationType,
        string provider,
        string model,
        int inputTokens,
        int outputTokens,
        long durationMs,
        bool isSuccess,
        string? errorMessage = null,
        Guid? agentId = null,
        Guid? threadId = null,
        CancellationToken ct = default);
}
