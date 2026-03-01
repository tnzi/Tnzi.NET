namespace Tnzi.AI.Coder.Security;

/// <summary>
/// .gitignore 模式匹配器 — 解析并匹配 .gitignore 规则
/// </summary>
public class IgnorePatternMatcher
{
    private readonly List<IgnoreRule> _rules;

    private IgnorePatternMatcher(List<IgnoreRule> rules)
    {
        _rules = rules;
    }

    /// <summary>
    /// 从目录加载 .gitignore 规则（向上遍历到项目根目录）
    /// </summary>
    /// <param name="directory">起始目录</param>
    /// <param name="projectRoot">项目根目录（为 null 时遍历到文件系统根）</param>
    public static IgnorePatternMatcher LoadFromDirectory(string directory, string? projectRoot = null)
    {
        Check.NotNullOrWhiteSpace(directory);

        var rules = new List<IgnoreRule>();
        var resolvedDir = Path.GetFullPath(directory);
        var resolvedRoot = projectRoot != null ? Path.GetFullPath(projectRoot) : null;

        // 从当前目录向上收集 .gitignore 规则
        var current = resolvedDir;
        while (current != null)
        {
            var gitignorePath = Path.Combine(current, ".gitignore");
            if (File.Exists(gitignorePath))
            {
                var basePath = current;
                var lines = File.ReadAllLines(gitignorePath);
                foreach (var line in lines)
                {
                    var rule = ParseLine(line);
                    if (rule != null)
                    {
                        rules.Add(rule);
                    }
                }
            }

            // 到达项目根目录则停止
            if (resolvedRoot != null &&
                string.Equals(current, resolvedRoot, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (parent == null || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = parent;
        }

        return new IgnorePatternMatcher(rules);
    }

    /// <summary>
    /// 检查相对路径是否应被忽略
    /// </summary>
    /// <param name="relativePath">相对路径（使用 / 分隔符）</param>
    public bool IsIgnored(string relativePath)
    {
        Check.NotNullOrWhiteSpace(relativePath);

        // 规范化路径分隔符
        var normalizedPath = relativePath.Replace('\\', '/').TrimStart('/');

        // 逐条规则匹配，后定义的规则优先
        var ignored = false;
        foreach (var rule in _rules)
        {
            if (rule.Regex.IsMatch(normalizedPath))
            {
                ignored = !rule.IsNegation;
            }
            // 目录规则也匹配路径中的目录组件
            else if (rule.IsDirectoryOnly && MatchesDirectoryComponent(normalizedPath, rule.Regex))
            {
                ignored = !rule.IsNegation;
            }
        }

        return ignored;
    }

    private static readonly HashSet<string> DefaultIgnoredDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", "bin", "obj", ".vs", ".idea",
        "__pycache__", "dist", "build", ".next", ".nuxt",
        "coverage", ".tnzi"
    };

    /// <summary>
    /// 获取默认忽略目录集合（无 .gitignore 时使用）
    /// </summary>
    public static HashSet<string> GetDefaultIgnoredDirectories() => DefaultIgnoredDirs;

    /// <summary>
    /// 检查路径中的目录组件是否匹配
    /// </summary>
    private static bool MatchesDirectoryComponent(string normalizedPath, Regex regex)
    {
        // 检查路径中的每个目录段
        var parts = normalizedPath.Split('/');
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (regex.IsMatch(parts[i]) || regex.IsMatch(parts[i] + "/"))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 解析 .gitignore 文件中的一行规则
    /// </summary>
    private static IgnoreRule? ParseLine(string line)
    {
        var trimmed = line.Trim();

        // 跳过空行和注释
        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
        {
            return null;
        }

        var isNegation = false;
        if (trimmed.StartsWith('!'))
        {
            isNegation = true;
            trimmed = trimmed[1..];
        }

        // 目录模式标记
        var isDirectoryOnly = trimmed.EndsWith('/');
        if (isDirectoryOnly)
        {
            trimmed = trimmed[..^1];
        }

        // 根路径锚定
        var isRooted = trimmed.StartsWith('/');
        if (isRooted)
        {
            trimmed = trimmed[1..];
        }

        // 将 gitignore 模式转换为正则表达式
        var regexPattern = ConvertPatternToRegex(trimmed, isRooted);

        try
        {
            var regex = new Regex(regexPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(5));
            return new IgnoreRule
            {
                Regex = regex,
                IsNegation = isNegation,
                IsDirectoryOnly = isDirectoryOnly
            };
        }
        catch (RegexParseException)
        {
            // 无效模式，跳过
            return null;
        }
    }

    /// <summary>
    /// 将 .gitignore 模式转换为正则表达式
    /// </summary>
    private static string ConvertPatternToRegex(string pattern, bool isRooted)
    {
        var sb = new StringBuilder();

        // 非锚定且不含路径分隔符的模式可匹配任何目录级别
        var containsSlash = pattern.Contains('/');

        if (isRooted || containsSlash)
        {
            sb.Append('^');
        }
        else
        {
            // 匹配任意前缀目录
            sb.Append("(^|/)");
        }

        var i = 0;
        while (i < pattern.Length)
        {
            var c = pattern[i];

            if (c == '*')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                {
                    // ** 模式
                    if (i + 2 < pattern.Length && pattern[i + 2] == '/')
                    {
                        // **/ 匹配零个或多个目录
                        sb.Append("(.*/)?");
                        i += 3;
                        continue;
                    }

                    // 尾部 ** 匹配所有子路径
                    sb.Append(".*");
                    i += 2;
                    continue;
                }

                // 单个 * 匹配除 / 外的任意字符
                sb.Append("[^/]*");
            }
            else if (c == '?')
            {
                sb.Append("[^/]");
            }
            else if (c == '[')
            {
                // 字符集 — 直接传递到正则
                var end = pattern.IndexOf(']', i + 1);
                if (end > i)
                {
                    sb.Append(pattern[i..(end + 1)]);
                    i = end;
                }
                else
                {
                    sb.Append(Regex.Escape(c.ToString()));
                }
            }
            else if (c == '/')
            {
                sb.Append('/');
            }
            else
            {
                sb.Append(Regex.Escape(c.ToString()));
            }

            i++;
        }

        sb.Append("(/.*)?$");

        return sb.ToString();
    }

    /// <summary>
    /// 单条忽略规则
    /// </summary>
    private class IgnoreRule
    {
        public required Regex Regex { get; init; }
        public bool IsNegation { get; init; }
        public bool IsDirectoryOnly { get; init; }
    }
}
