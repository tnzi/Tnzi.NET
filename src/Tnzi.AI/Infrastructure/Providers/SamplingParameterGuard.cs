namespace Tnzi.AI.Infrastructure.Providers;

/// <summary>
/// 发请求前按目标型号去掉它不收的采样参数（<see cref="ChatOptions.Temperature"/> / <see cref="ChatOptions.TopP"/> / <see cref="ChatOptions.TopK"/>）。
/// </summary>
/// <remarks>
/// <para>
/// 温度可能来自很多地方：Agent 自己的设置、<c>AI:Utility:Temperature</c>、单次调用的覆盖、子模块写死的值。
/// 逐个来源去判断「这个型号收不收」会漏，所以判断放在两个提供商装饰器上（每个 Anthropic 与 OpenAI 兼容客户端都经过它们），
/// 那里知道这次调用真正发往哪个型号（<see cref="ChatOptions.ModelId"/> 优先于客户端绑定的型号）。
/// </para>
/// <para>
/// 三个参数整组去掉：不收温度的型号同样不收 <c>top_p</c> / <c>top_k</c> 的替代写法，只去一个会让调用方换个参数照样 400。
/// 型号收的时候原样不动，显式配置照常生效。永远克隆、不改调用方的 options。
/// </para>
/// </remarks>
internal static class SamplingParameterGuard
{
    /// <summary>型号不收采样参数且 options 里带着它们时，返回去掉它们的副本；否则原样返回。</summary>
    public static ChatOptions? Apply(ChatOptions? options, string? model)
    {
        if (options is null || !HasSamplingParameters(options))
        {
            return options;
        }

        return ModelSamplingSupport.AcceptsSamplingParameters(options.ModelId ?? model)
            ? options
            : Without(options);
    }

    /// <summary>无条件去掉采样参数（返回副本；没有可去的时原样返回）。</summary>
    public static ChatOptions? Without(ChatOptions? options)
    {
        if (options is null || !HasSamplingParameters(options))
        {
            return options;
        }

        var stripped = options.Clone();
        stripped.Temperature = null;
        stripped.TopP = null;
        stripped.TopK = null;
        return stripped;
    }

    private static bool HasSamplingParameters(ChatOptions options)
        => options.Temperature.HasValue || options.TopP.HasValue || options.TopK.HasValue;
}
