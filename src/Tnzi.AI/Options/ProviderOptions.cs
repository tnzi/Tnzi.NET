namespace Tnzi.AI.Options;

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

    /// <summary>
    /// Model context window size (tokens). Used by SummarizationMiddleware for fraction-based triggers.
    /// If null, falls back to SummarizationOptions.ModelContextWindow (default 128K).
    /// </summary>
    /// <remarks>
    /// Common values: GPT-4.1 (1M), Claude Sonnet 4 (200K), GPT-4o (128K), DeepSeek-R1 (64K), GPT-4.1-mini (128K).
    /// </remarks>
    public int? ContextWindowSize { get; set; }
}
