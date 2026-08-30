namespace Tnzi.AI.Services;

/// <summary>
/// 核心 <see cref="IAiUtility"/> 默认实现所用的命名 <see cref="HttpClient"/> 键。
/// </summary>
/// <remarks>
/// 每个提供商一个命名客户端，使连接池与（消费方自行挂载的）熔断状态按提供商隔离 ——
/// 某个提供商的 429 不会波及其它提供商。
/// </remarks>
[ExperimentalApi(Reason = "AI abstractions are evolving")]
public static class AiUtilityHttpClientNames
{
    /// <summary>未指定提供商时使用的客户端名。</summary>
    public const string Fallback = "Tnzi.AiUtility";

    /// <summary>返回指定提供商的命名客户端键。</summary>
    public static string For(string? providerName)
        => string.IsNullOrWhiteSpace(providerName) ? Fallback : $"{Fallback}:{providerName}";
}
