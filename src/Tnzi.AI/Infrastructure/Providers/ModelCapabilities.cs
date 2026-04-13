namespace Tnzi.AI.Infrastructure.Providers;

/// <summary>
/// 模型推理能力检测。基于模型名称前缀判断是否支持推理、是否 always-on、是否支持强度控制。
/// </summary>
public static class ModelCapabilities
{
    /// <summary>是否支持推理模式</summary>
    public static bool SupportsReasoning(string? model)
    {
        if (string.IsNullOrEmpty(model)) return false;

        return StartsWithAny(model, "o1", "o3", "o4")
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

    private static bool SupportsClaudeThinking(string model)
        => StartsWithAny(model,
            "claude-opus-4-1",
            "claude-opus-4",
            "claude-sonnet-4",
            "claude-3-7-sonnet");
}
