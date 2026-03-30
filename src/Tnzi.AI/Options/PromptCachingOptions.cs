namespace Tnzi.AI.Options;

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

    /// <summary>
    /// 缓存最近 N 条用户消息（默认 0 = 不缓存）
    /// </summary>
    /// <remarks>
    /// Anthropic 3-tier caching: system messages + recent user messages + tool definitions.
    /// Note: Anthropic has a 4-block cache limit — auto-disable when OAuth token detected.
    /// </remarks>
    public int CacheRecentUserMessages { get; set; }

    /// <summary>
    /// 当检测到 OAuth token 时自动禁用缓存（Anthropic 4-block cache limit）
    /// </summary>
    public bool DisableOnOAuthToken { get; set; } = true;
}
