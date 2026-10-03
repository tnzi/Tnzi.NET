namespace Tnzi.AI.Options;

/// <summary>
/// 单次 <see cref="Services.IAiUtility.ExecuteAsync"/> 调用自带的 OpenAI 兼容提供商 ——
/// 不经 <c>AI:Providers</c> 注册、不改任何全局配置。
/// </summary>
/// <remarks>
/// ★存在的理由：凭据不在 <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> 里的调用方。
/// 典型形态是密钥存在消费方自己的加密存储里、只在运行时解密得到；把它再抄一份进 <c>AI:Providers</c>
/// 会得到两份各自轮换、迟早不一致的密钥。
/// <para>
/// 生命周期只有这一次调用：实现不缓存、不记日志、不回写本对象的任何字段。
/// <see cref="ToString"/> 刻意不输出 <see cref="ApiKey"/>，把本对象整个写进日志模板也不会泄露。
/// </para>
/// <para>
/// ★框架不对 <see cref="BaseUrl"/> 跑 <see cref="Tnzi.Http.EgressGuard"/>（本地 Ollama / vLLM 正是
/// loopback 与私有网段）。地址来自最终用户可编辑的输入时，由调用方在构造本对象前自行
/// <c>EgressGuard.CheckAsync</c>。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "AI abstractions are evolving")]
public sealed class AiUtilityInlineProvider
{
    /// <summary>超时上限（秒），与 <c>AI:Providers:*:TimeoutSeconds</c> 的校验同口径。</summary>
    private const int MaxTimeoutSeconds = 600;

    private const string RedactedKey = "***";

    /// <summary>
    /// OpenAI 兼容端点的版本前缀（如 <c>https://api.deepseek.com/v1</c>），必须是绝对 http(s) 地址。
    /// </summary>
    /// <remarks>
    /// 与配置提供商不同，这里<b>没有</b>回退到 OpenAI 官方端点的默认值：自带提供商的调用方
    /// 漏填地址，更可能是读设置读空了，而不是真的想把这把密钥发给 OpenAI。
    /// </remarks>
    public required string BaseUrl { get; init; }

    /// <summary>Bearer 密钥。不能为空。</summary>
    public required string ApiKey { get; init; }

    /// <summary>
    /// 调用方未在 <see cref="AiUtilityCallOptions.Model"/> 指定模型时使用的模型名。
    /// </summary>
    /// <remarks>
    /// 两处都为空时本次调用记 Warning 并返回 <see langword="null"/>。刻意<b>不</b>回退
    /// <c>AI:Utility:Model</c> 与配置提供商的别名字典：那些名字是给另一家提供商定的。
    /// </remarks>
    public string? DefaultModel { get; init; }

    /// <summary>每次尝试的请求超时（秒，1–600），整个范围按原值生效。未设置时 100 秒。</summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary>
    /// 校验本对象；合法时返回 <see langword="null"/>，否则返回一条可直接写日志的原因。
    /// </summary>
    /// <remarks>
    /// 原因里<b>不回显</b>任何字段值：把密钥误填进 <see cref="BaseUrl"/> 是这类对象最现实的错误，
    /// 回显地址就等于把密钥写进日志。
    /// </remarks>
    public string? GetValidationError()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            return "BaseUrl is required";
        }

        if (!Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return "BaseUrl is not a valid absolute http(s) URL";
        }

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            return "ApiKey is required";
        }

        if (TimeoutSeconds is { } timeout && (timeout <= 0 || timeout > MaxTimeoutSeconds))
        {
            return $"TimeoutSeconds must be between 1 and {MaxTimeoutSeconds}";
        }

        return null;
    }

    /// <summary>
    /// 日志用的提供商标签：<c>inline:{host}</c>。只含主机名，不含路径、查询串与密钥。
    /// </summary>
    public string DescribeForLog()
        => Uri.TryCreate(BaseUrl?.Trim(), UriKind.Absolute, out var uri) ? "inline:" + uri.Host : "inline";

    /// <summary>
    /// 把 <paramref name="text"/> 里出现的 <see cref="ApiKey"/> 替换成 <c>***</c>。
    /// </summary>
    /// <remarks>
    /// 用在要写进日志的第三方文本上（错误响应体、异常消息）：有的提供商会在 401 响应里回显收到的密钥。
    /// </remarks>
    public string Redact(string text)
    {
        Check.NotNull(text);
        return string.IsNullOrEmpty(ApiKey) ? text : text.Replace(ApiKey, RedactedKey, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override string ToString() => $"{nameof(AiUtilityInlineProvider)} {{ {DescribeForLog()}, ApiKey = {RedactedKey} }}";
}
