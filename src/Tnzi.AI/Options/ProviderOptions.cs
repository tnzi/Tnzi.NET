namespace Tnzi.AI.Options;

/// <summary>
/// 提供商配置选项 —— 在核心的 <see cref="AiProviderOptions"/> 之上补充 Agent 层字段。
/// </summary>
/// <remarks>
/// 基础字段（Name / Enabled / ApiKey / BaseUrl / DefaultModel / TimeoutSeconds /
/// MaxTokens / Temperature / Models）定义在核心基类，本类只声明「需要 Agent 引擎才用得上」
/// 的那些。两者绑定同一个 <c>AI:Providers</c> 配置节：核心的
/// <c>AiProviderRegistryOptions</c> 只认基类字段，<c>AIOptions</c> 认全部。
/// </remarks>
public class ProviderOptions : AiProviderOptions
{
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
