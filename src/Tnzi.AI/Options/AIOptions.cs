namespace Tnzi.AI.Options;

/// <summary>
/// AI 模块配置选项
/// </summary>
[ConfigSection("AI")]
[RuntimeSettingGroup(Key = "ai-general", Module = "AI", DisplayName = "AI General",
    I18nKey = "admin.modules.system.settings.groups.aiGeneral", Icon = "mdi:robot-outline", Order = 100)]
public class AIOptions
{
    /// <summary>
    /// 默认提供商名称
    /// </summary>
    [RuntimeSetting(Label = "Default Provider", I18n = "admin.modules.system.settings.fields.defaultProvider")]
    public string DefaultProvider { get; set; } = "OpenAI";

    /// <summary>
    /// 提供商配置字典
    /// </summary>
    public Dictionary<string, ProviderOptions> Providers { get; set; } = new();

    /// <summary>
    /// 是否启用 OpenTelemetry 可观测性
    /// </summary>
    public bool EnableObservability { get; set; } = false;

    /// <summary>
    /// Strip TextContent from assistant messages that contain tool calls before
    /// sending the continuation request. Some models (e.g. DeepSeek) produce token
    /// fracturing when receiving both content and tool_calls in the same message.
    /// </summary>
    public bool StripTextFromToolCallMessages { get; set; }

    /// <summary>
    /// 历史记录存储配置
    /// </summary>
    /// <remarks>
    /// 控制聊天历史存储和压缩策略
    /// </remarks>
    public HistoryOptions History { get; set; } = new();

    /// <summary>
    /// 上下文提供器配置
    /// </summary>
    /// <remarks>
    /// 控制 IContextProvider 的行为，包括 Memory、RAG、Skills 等
    /// </remarks>
    public ContextProvidersOptions ContextProviders { get; set; } = new();

    /// <summary>
    /// 工具审批配置
    /// </summary>
    /// <remarks>
    /// 控制工具调用的审批机制，支持 human-in-the-loop 场景
    /// </remarks>
    public ToolApprovalOptions ToolApproval { get; set; } = new();

    /// <summary>
    /// 工具权限规则配置
    /// </summary>
    public ToolPermissionOptions Permissions { get; set; } = new();

    /// <summary>
    /// MCP (Model Context Protocol) 配置
    /// </summary>
    /// <remarks>
    /// 用于连接到 MCP 服务器并使用其提供的工具
    /// </remarks>
    public McpOptions Mcp { get; set; } = new();

    /// <summary>
    /// 内置工具配置
    /// </summary>
    /// <remarks>
    /// 控制内置工具（日期时间、文本处理、Web 搜索）的注册。
    /// 默认关闭，因为现代 LLM 已能原生处理这些操作，注册它们会额外消耗 Token 空间。
    /// </remarks>
    public BuiltInToolsOptions BuiltInTools { get; set; } = new();

    /// <summary>
    /// 配额默认值配置
    /// </summary>
    public QuotaOptions Quota { get; set; } = new();

    /// <summary>
    /// 安全防护 (Guardrails) 配置
    /// </summary>
    /// <remarks>
    /// 控制输入验证、输出过滤、Prompt 注入检测等安全防护。默认关闭。
    /// </remarks>
    public GuardrailsOptions Guardrails { get; set; } = new();

    /// <summary>
    /// OpenAPI 工具自动生成配置
    /// </summary>
    /// <remarks>
    /// 从 OpenAPI 3.x 规范自动生成 AITool，使 AI Agent 能够调用外部 REST API。默认关闭。
    /// </remarks>
    public OpenApiToolsOptions OpenApiTools { get; set; } = new();

    /// <summary>
    /// 成本追踪配置
    /// </summary>
    /// <remarks>
    /// 配置各模型的 Token 成本率（美元/百万Token），启用后 UsageLog 自动计算 EstimatedCostUsd。
    /// </remarks>
    public CostTrackingOptions CostTracking { get; set; } = new();

    /// <summary>
    /// USD 成本预算管控配置
    /// </summary>
    /// <remarks>
    /// 启用后在 QuotaMiddleware 中检查月度 USD 预算上限，支持按 Agent 覆盖预算。
    /// 需同时启用 CostTracking 以确保 UsageLog 有成本数据。
    /// </remarks>
    public BudgetOptions Budget { get; set; } = new();

    /// <summary>
    /// 重试与熔断配置
    /// </summary>
    /// <remarks>
    /// 控制 AI API 调用的自动重试和熔断保护策略。
    /// 对 HTTP 429/5xx 和连接超时自动重试，对 4xx（除 429）和业务异常不重试。
    /// </remarks>
    public RetryOptions Retry { get; set; } = new();

    /// <summary>
    /// YAML Agent 定义文件配置
    /// </summary>
    /// <remarks>
    /// 启用后可从文件系统读取 YAML 格式的 Agent 定义并同步到数据库。
    /// 支持文件监视热重载。
    /// </remarks>
    public AgentDefinitionOptions AgentDefinitions { get; set; } = new();

    /// <summary>
    /// Workspace-based agent discovery configuration
    /// </summary>
    public WorkspaceOptions Workspace { get; set; } = new();

    /// <summary>
    /// 工具结果预算配置
    /// </summary>
    public ToolResultBudgetOptions ToolResultBudget { get; set; } = new();

    /// <summary>
    /// 对话摘要配置
    /// </summary>
    /// <remarks>
    /// 控制长对话的自动摘要压缩，在消息 token 超过阈值时触发 LLM 摘要替换旧消息。
    /// </remarks>
    public SummarizationOptions Summarization { get; set; } = new();
}
