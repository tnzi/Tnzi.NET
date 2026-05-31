namespace Tnzi.AI.Security;

/// <summary>
/// Shell 命令分析器接口
/// </summary>
public interface IShellCommandAnalyzer
{
    /// <summary>
    /// 解析 shell 命令，输出片段与风险特征
    /// </summary>
    ShellCommandAnalysis Analyze(string command);
}

/// <summary>
/// Shell 命令分析结果
/// </summary>
public sealed class ShellCommandAnalysis
{
    public string OriginalCommand { get; init; } = string.Empty;

    public IReadOnlyList<ShellCommandSegment> Segments { get; init; } = Array.Empty<ShellCommandSegment>();

    public bool HasPipeline { get; init; }

    public bool HasCommandSeparator { get; init; }

    public bool HasRedirection { get; init; }

    public bool IsDestructiveCandidate { get; init; }
}

/// <summary>
/// Shell 命令片段
/// </summary>
public sealed class ShellCommandSegment
{
    public string CommandText { get; init; } = string.Empty;

    public string CommandPrefix { get; init; } = string.Empty;
}

/// <summary>
/// 轻量 shell 命令分析器。
/// 目标不是构造完整 shell AST，而是为权限规则提供稳定的片段和命令前缀信息。
/// </summary>
public sealed class ShellCommandAnalyzer : IShellCommandAnalyzer
{
    private static readonly string[] DestructivePrefixes =
    [
        "rm",
        "mv",
        "chmod",
        "chown",
        "sed",
        "del",
        "rd",
        "rmdir",
        "remove-item",
        "move-item",
        "rename-item",
        "clear-content",
        "set-content"
    ];

    public ShellCommandAnalysis Analyze(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return new ShellCommandAnalysis
            {
                OriginalCommand = command ?? string.Empty
            };
        }

        var hasPipeline = false;
        var hasSeparator = false;
        var hasRedirection = false;

        var parts = SplitSegments(command, ref hasPipeline, ref hasSeparator, ref hasRedirection);
        var segments = parts
            .Select(part => part.Trim())
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => new ShellCommandSegment
            {
                CommandText = part,
                CommandPrefix = ExtractPrefix(part)
            })
            .ToList();

        return new ShellCommandAnalysis
        {
            OriginalCommand = command,
            Segments = segments,
            HasPipeline = hasPipeline,
            HasCommandSeparator = hasSeparator,
            HasRedirection = hasRedirection,
            IsDestructiveCandidate = segments.Any(x => IsDestructivePrefix(x.CommandPrefix))
        };
    }

    private static List<string> SplitSegments(
        string command,
        ref bool hasPipeline,
        ref bool hasSeparator,
        ref bool hasRedirection)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var i = 0; i < command.Length; i++)
        {
            var ch = command[i];
            var next = i + 1 < command.Length ? command[i + 1] : '\0';

            if (ch == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
                current.Append(ch);
                continue;
            }

            if (ch == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                current.Append(ch);
                continue;
            }

            if (inSingleQuote || inDoubleQuote)
            {
                current.Append(ch);
                continue;
            }

            if (ch == '|' && next != '|')
            {
                hasPipeline = true;
                FlushCurrent(result, current);
                continue;
            }

            if (ch == ';')
            {
                hasSeparator = true;
                FlushCurrent(result, current);
                continue;
            }

            if ((ch == '&' && next == '&') || (ch == '|' && next == '|'))
            {
                hasSeparator = true;
                FlushCurrent(result, current);
                i++;
                continue;
            }

            if (ch is '>' or '<')
            {
                hasRedirection = true;
                current.Append(ch);
                continue;
            }

            current.Append(ch);
        }

        FlushCurrent(result, current);
        return result;
    }

    private static void FlushCurrent(List<string> result, StringBuilder current)
    {
        var text = current.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(text))
        {
            result.Add(text);
        }

        current.Clear();
    }

    private static string ExtractPrefix(string segment)
    {
        var trimmed = segment.TrimStart();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var firstToken = trimmed
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        return firstToken?.Trim().ToLowerInvariant() ?? string.Empty;
    }

    private static bool IsDestructivePrefix(string prefix)
        => DestructivePrefixes.Contains(prefix, StringComparer.OrdinalIgnoreCase);
}
