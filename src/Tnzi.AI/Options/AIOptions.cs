namespace Tnzi.AI.Options;

/// <summary>
/// AI 模块配置选项
/// </summary>
public class AIOptions
{
    /// <summary>
    /// 默认提供商名称
    /// </summary>
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
    /// 控制内置工具（日期时间、数学计算、文本处理）的注册。
    /// 默认关闭，因为现代 LLM 已能原生处理这些操作，注册它们会额外消耗 Token 空间。
    /// </remarks>
    public BuiltInToolsOptions BuiltInTools { get; set; } = new();
}

/// <summary>
/// 提供商配置选项
/// </summary>
public class ProviderOptions
{
    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// API Key
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// 基础 URL
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// 默认模型名称
    /// </summary>
    public string? DefaultModel { get; set; }

    /// <summary>
    /// 超时时间（秒）
    /// </summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>
    /// 最大 Token 数
    /// </summary>
    public int? MaxTokens { get; set; }

    /// <summary>
    /// 温度参数（0-2）
    /// </summary>
    public double? Temperature { get; set; }
}
