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
    /// Note: Anthropic has a 4-block cache limit - auto-disable when OAuth token detected.
    /// </remarks>
    public int CacheRecentUserMessages { get; set; }

    /// <summary>
    /// 当检测到 OAuth token 时自动禁用缓存（Anthropic 4-block cache limit）
    /// </summary>
    public bool DisableOnOAuthToken { get; set; } = true;

    /// <summary>
    /// 启用 static/dynamic 双断点边界优化（默认启用）
    /// <para>
    /// 第一断点：静态内容（Agent Instructions + Tool schemas）- 跨请求缓存命中率高
    /// 第二断点：动态内容（Memory + Context + Skills）- 会话内缓存命中率高
    /// </para>
    /// </summary>
    public bool CacheStaticDynamicBoundary { get; set; } = true;

    /// <summary>
    /// 是否缓存工具 schema 快照（默认启用）
    /// <para>
    /// 首次调用时快照工具列表，后续调用复用快照，避免配置变更导致缓存失效。
    /// 对话级别稳定性优化。
    /// </para>
    /// </summary>
    public bool SnapshotToolSchemas { get; set; } = true;
}
