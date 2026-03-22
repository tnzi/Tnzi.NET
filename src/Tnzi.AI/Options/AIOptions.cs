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
}

/// <summary>
/// 配额默认值配置选项
/// </summary>
public class QuotaOptions
{
    /// <summary>
    /// 新用户默认每日 Token 限额（默认 100 万）
    /// </summary>
    public long DefaultDailyTokenLimit { get; set; } = 1_000_000;

    /// <summary>
    /// 新用户默认每月 Token 限额（默认 2000 万）
    /// </summary>
    public long DefaultMonthlyTokenLimit { get; set; } = 20_000_000;
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

    /// <summary>
    /// 降级提供商列表（按优先级排序）
    /// </summary>
    /// <remarks>
    /// 当主提供商请求失败时，按顺序尝试降级提供商。
    /// 每个条目格式为 "ProviderName" 或 "ProviderName:ModelName"。
    /// </remarks>
    public List<string>? FallbackProviders { get; set; }

    /// <summary>
    /// Thinking/reasoning 配置
    /// </summary>
    /// <remarks>
    /// 控制是否请求 LLM 的思考/推理内容。不同提供商格式不同：
    /// DeepSeek R1 / Qwen QwQ / OpenAI o-series 自动返回 reasoning_content；
    /// Gemini 2.5 需要在请求中注入 extra_body.google.thinking_config。
    /// </remarks>
    public ThinkingOptions? Thinking { get; set; }

    /// <summary>
    /// 模型别名字典，如 { "think": "o4-mini", "fast": "gpt-4.1-mini" }
    /// </summary>
    /// <remarks>
    /// 通过别名引用模型：ChatRequestDto.Model = "think" → 自动解析为 "o4-mini"。
    /// ThinkingMiddleware 在启用推理时自动查找 "think" 别名切换到推理模型。
    /// </remarks>
    public Dictionary<string, string>? Models { get; set; }

    /// <summary>
    /// Prompt Caching 配置（减少重复 system prompt 和工具定义的 Token 成本）
    /// </summary>
    /// <remarks>
    /// Anthropic: 自动注入 cache_control 断点（系统提示 + 工具定义）
    /// OpenAI: 服务端自动缓存，无需客户端操作
    /// Gemini: 通过 context caching API 缓存
    /// </remarks>
    public PromptCachingOptions? PromptCaching { get; set; }
}

/// <summary>
/// Prompt Caching 配置选项
/// </summary>
public class PromptCachingOptions
{
    /// <summary>
    /// 是否启用 Prompt Caching（默认关闭）
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 是否缓存系统提示（默认启用）
    /// </summary>
    public bool CacheSystemPrompt { get; set; } = true;

    /// <summary>
    /// 是否缓存工具定义（默认启用）
    /// </summary>
    public bool CacheToolDefinitions { get; set; } = true;

    /// <summary>
    /// 缓存前 N 条历史消息（默认 0 = 不缓存历史）
    /// </summary>
    public int CacheFirstNMessages { get; set; }
}

/// <summary>
/// 推理强度等级
/// </summary>
public enum ReasoningEffort
{
    /// <summary>不启用推理</summary>
    None = 0,

    /// <summary>低强度推理</summary>
    Low = 1,

    /// <summary>中等强度推理</summary>
    Medium = 2,

    /// <summary>高强度推理（深度推理）</summary>
    High = 3,

    /// <summary>最高强度（Anthropic Opus / OpenAI xhigh）</summary>
    Max = 4
}

/// <summary>
/// Thinking/reasoning 配置选项
/// </summary>
public class ThinkingOptions
{
    /// <summary>
    /// 推理强度等级。None = 不启用推理。
    /// </summary>
    public ReasoningEffort Effort { get; set; } = ReasoningEffort.None;

    /// <summary>
    /// 最大推理 Token 预算（可选，Provider 特定）
    /// </summary>
    public int? BudgetTokens { get; set; }
}

/// <summary>
/// 安全防护 (Guardrails) 配置选项
/// </summary>
public class GuardrailsOptions
{
    /// <summary>
    /// 是否启用 Guardrails（默认关闭）
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 是否启用输入长度限制
    /// </summary>
    public bool EnableMaxLength { get; set; } = true;

    /// <summary>
    /// 最大输入长度（字符数，默认 50000）
    /// </summary>
    public int MaxInputLength { get; set; } = 50_000;

    /// <summary>
    /// 是否启用 Prompt 注入检测
    /// </summary>
    public bool EnablePromptInjectionDetection { get; set; } = true;

    /// <summary>
    /// 是否启用 PII 检测
    /// </summary>
    public bool EnablePiiDetection { get; set; }

    /// <summary>
    /// 是否启用输出内容过滤
    /// </summary>
    public bool EnableContentFilter { get; set; }

    /// <summary>
    /// 输出内容过滤的屏蔽关键词列表
    /// </summary>
    public List<string> BlockedOutputKeywords { get; set; } = [];

    /// <summary>
    /// Guardrail 执行模式（默认顺序执行，支持并行执行 + Tripwire 立即中止）
    /// </summary>
    public GuardrailExecutionMode ExecutionMode { get; set; } = GuardrailExecutionMode.Sequential;

    /// <summary>
    /// 流式输出 Guardrail 缓冲区大小（字符数，默认 500）
    /// </summary>
    /// <remarks>
    /// 流式场景下，每累积此数量的字符后执行一次输出 Guardrail 检查，
    /// 通过后才将缓冲的 chunk 释放给客户端。设为 0 禁用缓冲（退化为后验检查）。
    /// </remarks>
    public int StreamingBufferSize { get; set; } = 500;

    /// <summary>
    /// 流式输出防护滑动窗口重叠大小（Token 数），用于检测跨窗口边界的违规关键词。
    /// 默认 50。
    /// </summary>
    public int StreamingOverlapSize { get; set; } = 50;

    /// <summary>
    /// LLM-as-Judge guardrail 配置
    /// </summary>
    public LlmJudgeOptions LlmJudge { get; set; } = new();
}

/// <summary>
/// Guardrail 执行模式
/// </summary>
public enum GuardrailExecutionMode
{
    /// <summary>
    /// 顺序执行，遇到第一个拒绝即停止
    /// </summary>
    Sequential,

    /// <summary>
    /// 并行执行所有 guardrail，支持 tripwire 立即中止
    /// </summary>
    Parallel
}
