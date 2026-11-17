namespace Tnzi.AI.Services;

/// <summary>
/// 一次 <see cref="IAiUtility"/> 调用的完整结果：文本之外还有模型为什么停、用了多少 token、实际是谁答的。
/// </summary>
/// <remarks>
/// 存在的理由是两类只看文本判断不了的事：
/// <list type="bullet">
/// <item>输出被 <c>MaxTokens</c> 截断（<see cref="AiUtilityFinishReason.Length"/>）时文本照样非空，
/// 对标题、分类这类「要么完整要么不要」的结果，半截文本比没有更糟；</item>
/// <item>调用方要按用户计量这次调用时需要 token 数与实际的提供商 / 模型。</item>
/// </list>
/// 实现给不出的字段留 <see langword="null"/>（或 <see cref="AiUtilityFinishReason.Unknown"/>），调用方按「不知道」处理。
/// </remarks>
[ExperimentalApi(Reason = "AI abstractions are evolving")]
public sealed record AiUtilityResponse
{
    /// <summary>回复文本（已去掉首尾空白）；模型没给文本时为空串。</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>模型为什么停下。</summary>
    public AiUtilityFinishReason FinishReason { get; init; }

    /// <summary>实际处理请求的提供商（配置名）；不知道时为 null。</summary>
    public string? Provider { get; init; }

    /// <summary>实际回答的模型；不知道时为 null。</summary>
    public string? Model { get; init; }

    /// <summary>输入 token 数（含缓存读取）；提供商没报时为 null。</summary>
    public int? InputTokens { get; init; }

    /// <summary>输出 token 数；提供商没报时为 null。</summary>
    public int? OutputTokens { get; init; }

    /// <summary>输入中命中缓存的 token 数；提供商没报时为 null。</summary>
    public int? CachedInputTokens { get; init; }
}

/// <summary>
/// <see cref="AiUtilityResponse.FinishReason"/> 的取值。
/// </summary>
public enum AiUtilityFinishReason
{
    /// <summary>实现没有报告停止原因。</summary>
    Unknown = 0,

    /// <summary>模型自然说完。</summary>
    Stop = 1,

    /// <summary>撞上输出上限被截断：文本不完整。</summary>
    Length = 2,

    /// <summary>模型要调用工具而不是回答（utility 调用不带工具，出现即说明它没按要求回答）。</summary>
    ToolCalls = 3,

    /// <summary>被提供商的内容过滤拦下。</summary>
    ContentFilter = 4
}
