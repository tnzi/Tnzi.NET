namespace Tnzi.AI.Infrastructure.Streaming;

/// <summary>
/// 推理参数写进请求体的形态（由 <see cref="ThinkingRequestPolicy"/> 按它注入）。
/// </summary>
public enum ThinkingPayloadFormat
{
    /// <summary>不注入：推理常开或靠内联 &lt;think&gt; 标签的厂商（Kimi / GLM / MiniMax）</summary>
    None = 0,

    /// <summary>OpenAI Chat Completions：顶层字符串 <c>reasoning_effort</c></summary>
    OpenAI = 1,

    /// <summary>Gemini OpenAI 兼容端点：<c>extra_body.google.thinking_config</c></summary>
    Gemini = 2,

    /// <summary>通义千问 DashScope 兼容端点：顶层 <c>enable_thinking</c> + <c>thinking_budget</c></summary>
    Qwen = 3,

    /// <summary>DeepSeek：推理常开，不注入；但多轮工具调用要求每条助手消息都带 <c>reasoning_content</c></summary>
    DeepSeek = 4,

    /// <summary>Anthropic：由 <c>AnthropicThinkingChatClient</c> 经 <c>ChatOptions</c> 处理，请求体不动</summary>
    Anthropic = 5
}

/// <summary>
/// 按提供商条目（名字 / <c>ProviderType</c> / BaseUrl 主机）与模型名识别 <see cref="ThinkingPayloadFormat"/>。
/// </summary>
/// <remarks>
/// 部署里的条目多半叫 "gemini-prod"、"MyProxy" 这类名字并经 OpenAI 兼容端点接入，按条目名精确匹配几乎什么都认不出，
/// 认不出就落到 OpenAI 形态（与 <see cref="Providers.ChatMessageProcessorSelector"/> 同一思路）。
/// </remarks>
public static class ThinkingPayloadFormats
{
    /// <summary>识别格式；什么都认不出时按 OpenAI 兼容处理。</summary>
    public static ThinkingPayloadFormat Resolve(ProviderOptions? provider, string? model)
    {
        var name = provider?.Name;
        var type = provider?.ProviderType;
        var host = Uri.TryCreate(provider?.BaseUrl, UriKind.Absolute, out var uri) ? uri.Host : null;

        bool Named(params string[] fragments) => fragments.Any(f => Contains(type, f) || Contains(name, f));
        bool Hosted(params string[] fragments) => fragments.Any(f => Contains(host, f));
        bool Model(params string[] prefixes) => prefixes.Any(p => model?.StartsWith(p, StringComparison.OrdinalIgnoreCase) == true);

        if (Named("anthropic") || Hosted("anthropic.com"))
            return ThinkingPayloadFormat.Anthropic;
        if (Named("deepseek") || Hosted("deepseek") || Model("deepseek"))
            return ThinkingPayloadFormat.DeepSeek;
        if (Named("gemini", "google") || Hosted("googleapis") || Model("gemini"))
            return ThinkingPayloadFormat.Gemini;
        if (Named("qwen", "dashscope", "alibaba", "aliyun") || Hosted("dashscope", "aliyuncs") || Model("qwen", "qwq"))
            return ThinkingPayloadFormat.Qwen;
        if (Named("kimi", "moonshot", "glm", "zhipu", "minimax") || Hosted("moonshot", "bigmodel", "zhipu", "minimax")
            || Model("kimi", "glm", "minimax", "abab"))
            return ThinkingPayloadFormat.None;

        return ThinkingPayloadFormat.OpenAI;
    }

    private static bool Contains(string? value, string fragment)
        => !string.IsNullOrEmpty(value) && value.Contains(fragment, StringComparison.OrdinalIgnoreCase);
}
