namespace Tnzi.AI.Infrastructure.Providers;

/// <summary>
/// 按提供商形态选 <see cref="IChatMessageProcessor"/>：条目名 / <c>ProviderType</c> / BaseUrl 主机 / 模型名前缀，
/// 命中任一即选中；都不命中返回 null（OpenAI / Anthropic 等不需要处理器）。
/// </summary>
/// <remarks>
/// 处理器以 <see cref="IChatMessageProcessor.ProviderName"/>（"kimi" / "glm" / "minimax" / "deepseek" / "gemini"）自报身份，
/// 而部署里的提供商条目多半叫 "MyProxy" 这种名字、经 OpenAI 兼容端点接入，光比名字什么都选不到；
/// 所以每个处理器名下另配一组别名（厂商名、端点主机片段、模型前缀）。应用注册的自定义处理器只按名字精确匹配。
/// </remarks>
public static class ChatMessageProcessorSelector
{
    private sealed record Signature(string[] NameHints, string[] HostHints, string[] ModelPrefixes);

    private static readonly Dictionary<string, Signature> Signatures = new(StringComparer.OrdinalIgnoreCase)
    {
        ["kimi"] = new(["kimi", "moonshot"], ["moonshot"], ["kimi"]),
        ["glm"] = new(["glm", "zhipu", "chatglm"], ["bigmodel", "zhipu"], ["glm"]),
        ["minimax"] = new(["minimax"], ["minimax"], ["minimax", "abab"]),
        ["deepseek"] = new(["deepseek"], ["deepseek"], ["deepseek"]),
        ["gemini"] = new(["gemini", "google"], ["googleapis"], ["gemini"])
    };

    /// <summary>选出适用于该提供商 + 模型的处理器；没有则 null。</summary>
    public static IChatMessageProcessor? Select(IEnumerable<IChatMessageProcessor> processors, ProviderOptions options, string? model)
    {
        Check.NotNull(processors);
        Check.NotNull(options);

        var host = TryGetHost(options.BaseUrl);
        foreach (var processor in processors)
        {
            if (Matches(processor.ProviderName, options, host, model))
            {
                return processor;
            }
        }

        return null;
    }

    private static bool Matches(string processorName, ProviderOptions options, string? host, string? model)
    {
        if (ContainsIgnoreCase(options.ProviderType, processorName) || ContainsIgnoreCase(options.Name, processorName))
        {
            return true;
        }

        if (!Signatures.TryGetValue(processorName, out var signature))
        {
            return false;
        }

        return signature.NameHints.Any(h => ContainsIgnoreCase(options.ProviderType, h) || ContainsIgnoreCase(options.Name, h))
            || signature.HostHints.Any(h => ContainsIgnoreCase(host, h))
            || signature.ModelPrefixes.Any(p => model?.StartsWith(p, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static bool ContainsIgnoreCase(string? value, string fragment)
        => !string.IsNullOrEmpty(value) && value.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    private static string? TryGetHost(string? baseUrl)
        => Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ? uri.Host : null;
}
