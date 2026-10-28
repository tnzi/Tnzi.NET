namespace Tnzi.AI.Infrastructure.Providers;

/// <summary>
/// 模型推理能力检测。基于模型名称前缀判断是否支持推理、是否 always-on、是否支持强度控制。
/// </summary>
/// <remarks>
/// 这是一张按名字认模型的表，新模型发布后要跟着补：表里没有的模型，配置的推理强度会被跳过
/// （<see cref="ThinkingMiddleware"/> 每个提供商 + 模型记一条 Information）。
/// Claude 例外，按型号里的版本号判定（见 <see cref="UsesClaudeAdaptiveThinking"/>），新的 Claude 型号不必逐个补。
/// </remarks>
public static partial class ModelCapabilities
{
    /// <summary>是否支持推理模式</summary>
    public static bool SupportsReasoning(string? model)
    {
        if (string.IsNullOrEmpty(model)) return false;

        return StartsWithAny(model, "o1", "o3", "o4")
            || IsOpenAIGpt5Reasoning(model)
            || model.StartsWith("gpt-oss", StringComparison.OrdinalIgnoreCase)
            || StartsWithAny(model, "deepseek-reasoner", "deepseek-r1")
            || StartsWithAny(model, "gemini-2.5", "gemini-3")
            || model.StartsWith("qwq", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("qwen3", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("grok-3-mini", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("kimi-k1", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("glm-z1", StringComparison.OrdinalIgnoreCase)
            || SupportsClaudeThinking(model);
    }

    /// <summary>推理是否 always-on（无法关闭，如 DeepSeek R1）</summary>
    public static bool IsAlwaysOnReasoning(string? model)
    {
        if (string.IsNullOrEmpty(model)) return false;

        return StartsWithAny(model, "deepseek-reasoner", "deepseek-r1")
            || model.StartsWith("qwq", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("kimi-k1", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("glm-z1", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>是否支持推理强度控制（effort level）</summary>
    public static bool SupportsEffortControl(string? model)
    {
        if (string.IsNullOrEmpty(model)) return false;

        return StartsWithAny(model, "o1", "o3", "o4")
            || IsOpenAIGpt5Reasoning(model)
            || model.StartsWith("gpt-oss", StringComparison.OrdinalIgnoreCase)
            || StartsWithAny(model, "gemini-2.5", "gemini-3")
            || model.StartsWith("qwen3", StringComparison.OrdinalIgnoreCase)
            || SupportsClaudeThinking(model);
    }

    private static bool StartsWithAny(string model, params string[] prefixes)
    {
        foreach (var prefix in prefixes)
        {
            if (model.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// GPT-5 家族（gpt-5、gpt-5-mini、gpt-5.1 ...）收 <c>reasoning_effort</c>；
    /// 其中的 chat 型号（gpt-5-chat-latest 等）是不带推理的对话模型，不收这个参数。
    /// </summary>
    private static bool IsOpenAIGpt5Reasoning(string model)
        => model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase)
            && !model.Contains("-chat", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Claude 型号是否按 adaptive 形态请求推理（<c>thinking.type=adaptive</c> + <c>output_config.effort</c>）。
    /// </summary>
    /// <remarks>
    /// 两种形态各自只被一部分型号接受：4.7 起（含 5 系列、Fable、Mythos）拒收 <c>budget_tokens</c>，只能走 adaptive；
    /// 3.7 Sonnet 与 4 / 4.1 / 4.5 只认 <c>thinking.type=enabled</c> + <c>budget_tokens</c>。
    /// 4.6 两种都收，这里取 extended：它的 adaptive effort 只有 low / medium / high / max，框架的 Max 档经 SDK 落成
    /// <c>xhigh</c>（4.7 才有）会被拒；extended 下 Max 是 32768 的预算，显式 <c>BudgetTokens</c> 也照样生效。
    /// 非 Claude 型号恒为 false。
    /// </remarks>
    public static bool UsesClaudeAdaptiveThinking(string? model)
    {
        if (string.IsNullOrEmpty(model)) return false;

        if (StartsWithAny(model, "claude-fable-", "claude-mythos-")) return true;

        return ClaudeVersion(model) is var (major, minor) && (major > 4 || (major == 4 && minor >= 7));
    }

    private static bool SupportsClaudeThinking(string model)
        => model.StartsWith("claude-3-7-sonnet", StringComparison.OrdinalIgnoreCase)
            || UsesClaudeAdaptiveThinking(model)
            || ClaudeVersion(model) is ( >= 4, _);

    /// <summary>
    /// 从 <c>claude-{opus|sonnet|haiku}-{major}[-{minor}]</c> 里取版本号。次版本号是一到两位数字，
    /// 紧跟其后的日期戳（<c>claude-opus-4-20250514</c> 的 8 位数字）不算次版本号。
    /// </summary>
    private static (int Major, int Minor)? ClaudeVersion(string model)
    {
        var match = ClaudeVersionedName().Match(model);
        if (!match.Success) return null;

        var major = int.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture);
        var minor = match.Groups["minor"].Success
            ? int.Parse(match.Groups["minor"].Value, CultureInfo.InvariantCulture)
            : 0;
        return (major, minor);
    }

    [GeneratedRegex(@"^claude-(?:opus|sonnet|haiku)-(?<major>\d+)(?:-(?<minor>\d{1,2})(?!\d))?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClaudeVersionedName();
}
