namespace Tnzi.AI.Sandbox.Services;

/// <summary>
/// 命令语义映射 — 将非零退出码映射为语义化含义。
/// 借鉴 Claude Code 对 grep/diff/find 等命令的退出码语义理解。
/// </summary>
public static class CommandSemantics
{
    /// <summary>
    /// 已知命令的退出码语义映射
    /// </summary>
    private static readonly Dictionary<string, Dictionary<int, string>> ExitCodeMeanings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["grep"] = new() { [1] = "No matches found (not an error)", [2] = "Syntax error or inaccessible file" },
        ["rg"] = new() { [1] = "No matches found (not an error)", [2] = "Error during search" },
        ["diff"] = new() { [1] = "Files differ (not an error)", [2] = "Error" },
        ["cmp"] = new() { [1] = "Files differ (not an error)", [2] = "Error" },
        ["find"] = new() { [1] = "Some files could not be accessed" },
        ["test"] = new() { [1] = "Condition is false (not an error)" },
        ["["] = new() { [1] = "Condition is false (not an error)" },
    };

    /// <summary>
    /// 解释命令的退出码含义
    /// </summary>
    /// <param name="command">完整命令字符串</param>
    /// <param name="exitCode">退出码</param>
    /// <returns>语义说明，如果是已知的正常退出码；否则 null</returns>
    public static string? InterpretExitCode(string command, int exitCode)
    {
        if (exitCode == 0) return null;

        var baseCommand = ExtractBaseCommand(command);
        if (baseCommand == null) return null;

        if (ExitCodeMeanings.TryGetValue(baseCommand, out var meanings)
            && meanings.TryGetValue(exitCode, out var meaning))
        {
            return meaning;
        }

        return null;
    }

    /// <summary>
    /// 判断退出码是否表示正常结果（非错误）
    /// </summary>
    public static bool IsNonErrorExitCode(string command, int exitCode)
    {
        if (exitCode == 0) return true;

        var meaning = InterpretExitCode(command, exitCode);
        return meaning != null && meaning.Contains("not an error", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 从命令字符串中提取基础命令名
    /// </summary>
    public static string? ExtractBaseCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;

        var trimmed = command.TrimStart();

        // Handle pipes: take the LAST command (e.g., "cat file | grep pattern" → "grep")
        var pipeIndex = trimmed.LastIndexOf('|');
        if (pipeIndex >= 0 && pipeIndex < trimmed.Length - 1)
        {
            trimmed = trimmed[(pipeIndex + 1)..].TrimStart();
        }

        // Handle env vars, sudo prefix
        while (trimmed.StartsWith("sudo ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("env ", StringComparison.OrdinalIgnoreCase))
        {
            var spaceIdx = trimmed.IndexOf(' ');
            if (spaceIdx < 0) break;
            trimmed = trimmed[(spaceIdx + 1)..].TrimStart();
        }

        // Extract first word
        var endIdx = trimmed.IndexOfAny([' ', '\t', '\n', ';', '&', '|']);
        var cmd = endIdx > 0 ? trimmed[..endIdx] : trimmed;

        // Strip path (e.g., /usr/bin/grep → grep)
        var slashIdx = cmd.LastIndexOf('/');
        if (slashIdx >= 0) cmd = cmd[(slashIdx + 1)..];

        return cmd.Length > 0 ? cmd : null;
    }
}
