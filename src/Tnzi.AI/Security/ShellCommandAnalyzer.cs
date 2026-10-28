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

    /// <summary>
    /// 这条命令会执行（或可能执行）的全部命令片段：顶层片段之外，还包括命令替换 / 进程替换里的命令、
    /// <c>bash -c</c> / <c>eval</c> 的脚本里的命令，以及包装命令（<c>sudo</c>、<c>xargs</c>、<c>timeout</c> 等）
    /// 每个可能的被执行命令起点。权限规则与沙箱黑名单逐个片段判断，这里宁多勿漏。
    /// </summary>
    public IReadOnlyList<ShellCommandSegment> Segments { get; init; } = Array.Empty<ShellCommandSegment>();

    public bool HasPipeline { get; init; }

    public bool HasCommandSeparator { get; init; }

    public bool HasRedirection { get; init; }

    /// <summary>
    /// 是否按破坏性命令对待：某个片段的命令是破坏性命令，或命令里有分析器无法拆解的结构
    /// （命令名来自变量 / 通配 / 命令替换、函数定义、未闭合的替换、编码过的脚本等），后者一律失败关闭。
    /// </summary>
    public bool IsDestructiveCandidate { get; init; }
}

/// <summary>
/// Shell 命令片段
/// </summary>
public sealed class ShellCommandSegment
{
    public string CommandText { get; init; } = string.Empty;

    /// <summary>
    /// 片段实际执行的命令名：跳过前置的变量赋值、重定向与 <c>!</c> / <c>then</c> / <c>do</c> 等保留字，
    /// 去掉引号、转义、路径（<c>/bin/rm</c> → <c>rm</c>）与 <c>.exe</c> 后缀，小写。
    /// </summary>
    public string CommandPrefix { get; init; } = string.Empty;
}

/// <summary>
/// 轻量 shell 命令分析器。
/// 目标不是构造完整 shell AST，而是为权限规则提供稳定的片段和命令前缀信息。
/// </summary>
/// <remarks>
/// <para>
/// 它是两道防线的输入：权限评估器「破坏性工具无显式 allow 即拒绝」只看这里给出的破坏性判断，
/// 沙箱的命令前缀黑名单逐片段比对这里给出的命令名。漏拆一个片段就等于放行一条命令，
/// 所以拿不准的地方一律往多拆、往破坏性判（失败关闭），代价只是偶尔多拦。
/// </para>
/// <para>
/// 拆分覆盖 bash 会当作命令分隔的全部写法：<c>;</c>、换行、<c>&amp;&amp;</c>、<c>||</c>、<c>|</c>、<c>|&amp;</c>、
/// 单个 <c>&amp;</c>（后台执行，后面是另一条命令；<c>2&gt;&amp;1</c>、<c>&amp;&gt;</c> 这类重定向除外）。
/// 引号与反斜杠转义按 bash 规则处理，<c>\"</c> 不会把后面的分隔符藏进一个假的引号里；反斜杠 + 换行是续行，整个删掉。
/// here-document 的正文不当命令拆分（引号定界符的正文不展开，非引号定界符的正文里的命令替换照样分析）。
/// </para>
/// </remarks>
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

    /// <summary>
    /// 把自己的参数当命令执行的包装命令。参数里哪个是被执行的命令没有通用解析办法
    /// （<c>sudo -u root rm</c>、<c>timeout 5 rm</c>、<c>nice -n 10 rm</c>），每个不是选项的参数位置都当作一个可能的命令起点。
    /// </summary>
    private static readonly HashSet<string> Wrappers = new(StringComparer.Ordinal)
    {
        "sudo", "doas", "su", "runuser", "env", "nohup", "time", "timeout", "nice", "ionice", "chrt", "taskset",
        "stdbuf", "unbuffer", "setsid", "flock", "watch", "strace", "ltrace", "xargs", "parallel",
        "exec", "command", "builtin", "chroot", "busybox"
    };

    /// <summary>以内联脚本方式执行参数的命令（<c>bash -c</c>、<c>eval</c>、<c>cmd /c</c>、<c>pwsh -c</c>）。</summary>
    private static readonly HashSet<string> ScriptRunners = new(StringComparer.Ordinal)
    {
        "sh", "bash", "zsh", "dash", "ksh", "ash", "fish", "su", "eval", "cmd", "powershell", "pwsh"
    };

    /// <summary>位于命令名之前、本身不是命令的保留字。</summary>
    private static readonly HashSet<string> LeadingKeywords = new(StringComparer.Ordinal)
    {
        "!", "{", "(", "if", "then", "else", "elif", "do", "while", "until", "coproc"
    };

    /// <summary>分析器拆不开的结构：出现即按破坏性对待。</summary>
    private static readonly HashSet<string> OpaqueKeywords = new(StringComparer.Ordinal)
    {
        "function", "case", "select", "alias"
    };

    private static readonly string[] FindExecFlags = ["-exec", "-execdir", "-ok", "-okdir"];

    /// <summary>嵌套分析的最大深度（命令替换、<c>bash -c</c>、包装命令逐层计数），超过即失败关闭。</summary>
    private const int MaxDepth = 8;

    /// <summary>单条命令最多产出的片段数（包装命令的候选起点会放大），超过即失败关闭。</summary>
    private const int MaxSegments = 256;

    public ShellCommandAnalysis Analyze(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return new ShellCommandAnalysis
            {
                OriginalCommand = command ?? string.Empty
            };
        }

        var acc = new Accumulator();
        Collect(command, 0, acc);

        return new ShellCommandAnalysis
        {
            OriginalCommand = command,
            Segments = acc.Segments,
            HasPipeline = acc.HasPipeline,
            HasCommandSeparator = acc.HasSeparator,
            HasRedirection = acc.HasRedirection,
            IsDestructiveCandidate = acc.Opaque || acc.Destructive || acc.Segments.Any(x => IsDestructivePrefix(x.CommandPrefix))
        };
    }

    /// <summary>分析过程中的累积结果（一次 Analyze 调用私有，不跨调用共享）。</summary>
    private sealed class Accumulator
    {
        public List<ShellCommandSegment> Segments { get; } = [];
        public bool HasPipeline { get; set; }
        public bool HasSeparator { get; set; }
        public bool HasRedirection { get; set; }
        public bool Opaque { get; set; }

        /// <summary>命令本身不在破坏性前缀表里、但这次调用是删除（<c>find -delete</c>）。</summary>
        public bool Destructive { get; set; }
    }

    private readonly record struct Heredoc(string Delimiter, bool StripTabs, bool Quoted);

    private static void Collect(string script, int depth, Accumulator acc)
    {
        if (depth > MaxDepth)
        {
            acc.Opaque = true;
            return;
        }

        var nested = new List<string>();
        foreach (var part in SplitSegments(script, acc, nested))
        {
            var text = part.Trim();
            if (text.Length > 0)
            {
                AddSegment(text, Tokenize(text, acc), depth, acc);
            }
        }

        foreach (var inner in nested)
        {
            Collect(inner, depth + 1, acc);
        }
    }

    private static void AddSegment(string text, List<string> tokens, int depth, Accumulator acc)
    {
        if (acc.Segments.Count >= MaxSegments)
        {
            acc.Opaque = true;
            return;
        }

        var start = SkipLeadingTokens(tokens);
        var name = start < tokens.Count ? CanonicalName(tokens[start], acc) : string.Empty;
        acc.Segments.Add(new ShellCommandSegment { CommandText = text, CommandPrefix = name });

        if (name.Length == 0)
        {
            return;
        }

        var definesFunction = tokens[start].Contains("()", StringComparison.Ordinal)
            || (start + 1 < tokens.Count && tokens[start + 1].StartsWith("()", StringComparison.Ordinal));
        if (OpaqueKeywords.Contains(name) || definesFunction)
        {
            // 函数定义 / case 分支 / 别名：真正执行的命令藏在分析器不拆的结构里
            acc.Opaque = true;
            return;
        }

        if (ScriptRunners.Contains(name))
        {
            var script = FindInlineScript(name, tokens, start + 1, acc);
            if (script != null)
            {
                Collect(script, depth + 1, acc);
            }
        }

        if (name == "find")
        {
            AddFindActions(tokens, start + 1, depth, acc);
            return;
        }

        if (Wrappers.Contains(name))
        {
            if (depth + 1 > MaxDepth)
            {
                acc.Opaque = true;
                return;
            }

            for (var j = start + 1; j < tokens.Count; j++)
            {
                if (!tokens[j].StartsWith('-'))
                {
                    var rest = tokens.GetRange(j, tokens.Count - j);
                    AddSegment(string.Join(' ', rest), rest, depth + 1, acc);
                }
            }
        }
    }

    /// <summary><c>find -exec rm {} \;</c> 执行的是 <c>-exec</c> 之后的命令；<c>find -delete</c> 本身就是删除。</summary>
    private static void AddFindActions(List<string> tokens, int from, int depth, Accumulator acc)
    {
        for (var j = from; j < tokens.Count; j++)
        {
            if (tokens[j] == "-delete")
            {
                acc.Destructive = true;
                continue;
            }

            if (FindExecFlags.Contains(tokens[j]) && j + 1 < tokens.Count)
            {
                var end = j + 1;
                while (end < tokens.Count && tokens[end] != ";" && tokens[end] != "+")
                {
                    end++;
                }

                var action = tokens.GetRange(j + 1, end - j - 1);
                if (action.Count > 0)
                {
                    AddSegment(string.Join(' ', action), action, depth + 1, acc);
                }
                j = end;
            }
        }
    }

    /// <summary>
    /// 找出以内联脚本方式执行的参数：<c>bash -c 'script'</c>（含 <c>-lc</c> 等组合选项）、<c>eval args</c>、
    /// <c>cmd /c ...</c>、<c>pwsh -c / -Command ...</c>。没有内联脚本（运行脚本文件 / 交互式）返回 null；
    /// 有标志却拿不到脚本、或脚本经过编码（<c>-EncodedCommand</c>）时失败关闭。
    /// </summary>
    private static string? FindInlineScript(string runner, List<string> tokens, int from, Accumulator acc)
    {
        switch (runner)
        {
            case "eval":
                return from < tokens.Count ? string.Join(' ', tokens.Skip(from)) : null;

            case "cmd":
                for (var j = from; j < tokens.Count; j++)
                {
                    if (tokens[j].Equals("/c", StringComparison.OrdinalIgnoreCase) || tokens[j].Equals("/k", StringComparison.OrdinalIgnoreCase))
                    {
                        return JoinRestOrOpaque(tokens, j + 1, acc);
                    }
                }
                return null;

            case "powershell":
            case "pwsh":
                for (var j = from; j < tokens.Count; j++)
                {
                    var option = tokens[j].ToLowerInvariant();
                    if (option.StartsWith("-e", StringComparison.Ordinal) && "-encodedcommand".StartsWith(option, StringComparison.Ordinal))
                    {
                        acc.Opaque = true;
                        return null;
                    }

                    if (option == "-c" || (option.Length > 2 && "-command".StartsWith(option, StringComparison.Ordinal)))
                    {
                        return JoinRestOrOpaque(tokens, j + 1, acc);
                    }
                }
                return null;

            default:
                // sh 系：-c 可以与其它单字母选项合写（-lc、-xc）；脚本是紧跟其后的那一个参数，之后的是 $0 $1 ...
                for (var j = from; j < tokens.Count; j++)
                {
                    var option = tokens[j];
                    if (option.StartsWith("--command=", StringComparison.Ordinal))
                    {
                        return option["--command=".Length..];
                    }

                    var isShortCommandFlag = option.Length > 1 && option[0] == '-' && option[1] != '-' && option.Contains('c');
                    if (isShortCommandFlag || option == "--command")
                    {
                        if (j + 1 < tokens.Count)
                        {
                            return tokens[j + 1];
                        }

                        acc.Opaque = true;
                        return null;
                    }
                }

                // 没有内联脚本也没有脚本文件参数：shell 从标准输入读命令（echo ... | sh、curl ... | bash、
                // bash <<EOF、bash -s < file、sudo bash），执行的是分析器看不到的脚本，失败关闭。
                // 运行脚本文件（bash build.sh）照旧不判：文件内容同样看不到，但那是显式交给文件的，与 python script.py 同一口径
                if (!HasScriptOperand(tokens, from))
                {
                    acc.Opaque = true;
                }
                return null;
        }
    }

    /// <summary>
    /// shell 命令在第一个重定向之前有没有不是选项的参数（脚本文件）。重定向之后的词是重定向目标或 here-document 正文，
    /// 不是参数；单独的 <c>-</c> 表示从标准输入读，也不算。
    /// </summary>
    private static bool HasScriptOperand(List<string> tokens, int from)
    {
        for (var j = from; j < tokens.Count; j++)
        {
            var token = tokens[j];
            if (IsRedirectionOperator(token) || StartsWithRedirection(token))
            {
                return false;
            }

            if (!token.StartsWith('-'))
            {
                return true;
            }
        }

        return false;
    }

    private static string? JoinRestOrOpaque(List<string> tokens, int from, Accumulator acc)
    {
        if (from < tokens.Count)
        {
            return string.Join(' ', tokens.Skip(from));
        }

        acc.Opaque = true;
        return null;
    }

    /// <summary>跳过命令名之前的变量赋值、重定向与保留字，返回命令名所在下标（没有则为 tokens.Count）。</summary>
    private static int SkipLeadingTokens(List<string> tokens)
    {
        var i = 0;
        while (i < tokens.Count)
        {
            var token = tokens[i];
            if (LeadingKeywords.Contains(token) || IsAssignment(token))
            {
                i++;
                continue;
            }

            if (IsRedirectionOperator(token))
            {
                // 「> file rm x」：操作符与它的目标都不是命令
                i += 2;
                continue;
            }

            if (StartsWithRedirection(token))
            {
                i++;
                continue;
            }

            break;
        }

        return i;
    }

    private static bool IsAssignment(string token)
    {
        var eq = token.IndexOf('=');
        if (eq <= 0)
        {
            return false;
        }

        var name = token.AsSpan(0, eq).TrimEnd('+');
        if (name.Length == 0 || !(char.IsLetter(name[0]) || name[0] == '_'))
        {
            return false;
        }

        foreach (var c in name)
        {
            if (!(char.IsLetterOrDigit(c) || c == '_'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsRedirectionOperator(string token)
    {
        var body = token.TrimStart('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        return body is ">" or ">>" or "<" or "<<" or "<<<" or "&>" or "&>>" or ">|" or "<>" or ">&" or "<&";
    }

    private static bool StartsWithRedirection(string token)
    {
        var body = token.TrimStart('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        return body.Length > 0 && (body[0] == '>' || body[0] == '<' || body.StartsWith("&>", StringComparison.Ordinal));
    }

    /// <summary>
    /// 命令名的规范形：去掉子 shell 的左括号、路径与 <c>.exe</c>，小写。命令名在运行时才确定（变量、通配、
    /// 花括号展开、命令替换）时，分析器不知道会执行什么，失败关闭。
    /// </summary>
    private static string CanonicalName(string token, Accumulator acc)
    {
        var name = token.TrimStart('(');
        if (name.Length == 0)
        {
            return string.Empty;
        }

        if (name is "[" or "[[")
        {
            return name;
        }

        // $VAR / `cmd` / 通配 / 花括号展开 / cmd 的 %VAR%：执行哪个命令要到运行时才知道
        if (name.IndexOfAny(['$', '`', '*', '?', '[', '%']) >= 0 || (name.Contains('{') && name.Contains(',')))
        {
            acc.Opaque = true;
        }

        // cmd 的转义符 ^ 在执行前被去掉：d^el 就是 del
        name = name.Replace("^", string.Empty, StringComparison.Ordinal);

        var slash = Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
        if (slash >= 0)
        {
            name = name[(slash + 1)..];
        }

        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return name.TrimEnd(')').ToLowerInvariant();
    }

    /// <summary>
    /// 按 bash 的引号与转义规则把一个片段拆成词，并去掉引号与转义（<c>'rm'</c>、<c>\rm</c>、<c>"r"m</c> 都是 <c>rm</c>）。
    /// </summary>
    private static List<string> Tokenize(string segment, Accumulator acc)
    {
        var tokens = new List<string>();
        var word = new StringBuilder();
        var inWord = false;
        var inSingle = false;
        var inDouble = false;

        for (var i = 0; i < segment.Length; i++)
        {
            var ch = segment[i];

            if (inSingle)
            {
                if (ch == '\'')
                {
                    inSingle = false;
                }
                else
                {
                    word.Append(ch);
                }
                continue;
            }

            if (inDouble)
            {
                if (ch == '"')
                {
                    inDouble = false;
                }
                else if (ch == '\\' && i + 1 < segment.Length && "\"\\$`\n".Contains(segment[i + 1]))
                {
                    word.Append(segment[++i]);
                }
                else
                {
                    word.Append(ch);
                }
                continue;
            }

            switch (ch)
            {
                case '\'':
                    inSingle = true;
                    inWord = true;
                    break;
                case '"':
                    inDouble = true;
                    inWord = true;
                    break;
                case '\\' when i + 1 < segment.Length:
                    word.Append(segment[++i]);
                    inWord = true;
                    break;
                default:
                    if (char.IsWhiteSpace(ch))
                    {
                        if (inWord)
                        {
                            tokens.Add(word.ToString());
                            word.Clear();
                            inWord = false;
                        }
                    }
                    else
                    {
                        word.Append(ch);
                        inWord = true;
                    }
                    break;
            }
        }

        if (inSingle || inDouble)
        {
            // 未闭合的引号：bash 会报语法错误不执行，但这里无从判断后面的内容，失败关闭
            acc.Opaque = true;
        }

        if (inWord)
        {
            tokens.Add(word.ToString());
        }

        return tokens;
    }

    /// <summary>
    /// 按命令分隔符拆分，命令替换 / 进程替换的内层脚本放进 <paramref name="nested"/> 另行分析。
    /// </summary>
    private static List<string> SplitSegments(string command, Accumulator acc, List<string> nested)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var pendingHeredocs = new List<Heredoc>();
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var i = 0; i < command.Length; i++)
        {
            var ch = command[i];
            var next = i + 1 < command.Length ? command[i + 1] : '\0';

            if (inSingleQuote)
            {
                current.Append(ch);
                if (ch == '\'')
                {
                    inSingleQuote = false;
                }
                continue;
            }

            // 反斜杠 + 换行是续行（单引号外，含双引号内）：bash 把这两个字符整个删掉，r\<换行>m 就是 rm。
            // 保留下来的话换行进了词里，命令名对不上任何前缀，破坏性判断与沙箱黑名单一并落空。
            // \<CR><LF> 同样按续行处理：宁可把两行并成一个片段多拦，也不能让 Windows 换行把命令名拆开
            var continuation = LineContinuationLength(command, i);
            if (continuation > 0)
            {
                i += continuation - 1;
                continue;
            }

            // 反斜杠转义下一个字符（单引号外，含双引号内）：\" 不开引号、\; 不分隔
            if (ch == '\\' && i + 1 < command.Length)
            {
                current.Append(ch).Append(next);
                i++;
                continue;
            }

            // 命令替换在双引号里同样执行
            if (ch == '`' || (ch == '$' && next == '('))
            {
                var innerStart = ch == '`' ? i + 1 : i + 2;
                var close = ch == '`' ? FindClosingBacktick(command, innerStart) : FindClosingParen(command, innerStart);
                if (close < 0)
                {
                    acc.Opaque = true;
                    current.Append(command, i, command.Length - i);
                    break;
                }

                nested.Add(command[innerStart..close]);
                current.Append(command, i, close - i + 1);
                i = close;
                continue;
            }

            if (inDoubleQuote)
            {
                current.Append(ch);
                if (ch == '"')
                {
                    inDoubleQuote = false;
                }
                continue;
            }

            if (ch == '\'')
            {
                inSingleQuote = true;
                current.Append(ch);
                continue;
            }

            if (ch == '"')
            {
                inDoubleQuote = true;
                current.Append(ch);
                continue;
            }

            // 进程替换 <( ... ) / >( ... )
            if ((ch == '<' || ch == '>') && next == '(')
            {
                var close = FindClosingParen(command, i + 2);
                if (close < 0)
                {
                    acc.Opaque = true;
                    current.Append(command, i, command.Length - i);
                    break;
                }

                nested.Add(command[(i + 2)..close]);
                current.Append(command, i, close - i + 1);
                i = close;
                continue;
            }

            if (ch == '\n' || ch == '\r')
            {
                if (pendingHeredocs.Count > 0)
                {
                    i = ConsumeHeredocBodies(command, i, pendingHeredocs, current, nested, acc);
                    pendingHeredocs.Clear();
                }

                acc.HasSeparator = true;
                FlushCurrent(result, current);
                continue;
            }

            if (ch == '|')
            {
                if (next == '|')
                {
                    acc.HasSeparator = true;
                    FlushCurrent(result, current);
                    i++;
                    continue;
                }

                acc.HasPipeline = true;
                FlushCurrent(result, current);
                if (next == '&')
                {
                    i++;
                }
                continue;
            }

            if (ch == ';')
            {
                acc.HasSeparator = true;
                FlushCurrent(result, current);
                continue;
            }

            if (ch == '&')
            {
                if (next == '&')
                {
                    acc.HasSeparator = true;
                    FlushCurrent(result, current);
                    i++;
                    continue;
                }

                // &> / &>> / 2>&1 / >& / <& 是重定向，不是分隔符
                var previous = current.Length > 0 ? current[^1] : '\0';
                if (next == '>' || previous == '>' || previous == '<')
                {
                    acc.HasRedirection = true;
                    current.Append(ch);
                    continue;
                }

                // 单个 &：前一条命令转后台，后面是另一条命令
                acc.HasSeparator = true;
                FlushCurrent(result, current);
                continue;
            }

            if (ch == '<' && next == '<')
            {
                acc.HasRedirection = true;
                if (i + 2 < command.Length && command[i + 2] == '<')
                {
                    // here-string <<<
                    current.Append("<<<");
                    i += 2;
                    continue;
                }

                var j = i + 2;
                var stripTabs = false;
                if (j < command.Length && command[j] == '-')
                {
                    stripTabs = true;
                    j++;
                }
                while (j < command.Length && (command[j] == ' ' || command[j] == '\t'))
                {
                    j++;
                }

                var (delimiter, quoted, end) = ReadHeredocDelimiter(command, j);
                if (delimiter.Length > 0)
                {
                    pendingHeredocs.Add(new Heredoc(delimiter, stripTabs, quoted));
                }

                current.Append(command, i, end - i);
                i = end - 1;
                continue;
            }

            if (ch is '>' or '<')
            {
                acc.HasRedirection = true;
                current.Append(ch);
                continue;
            }

            current.Append(ch);
        }

        FlushCurrent(result, current);
        return result;
    }

    /// <summary>here-document 定界词：到空白或操作符为止；带引号即「引号定界符」（正文不做展开）。</summary>
    private static (string Delimiter, bool Quoted, int End) ReadHeredocDelimiter(string command, int start)
    {
        var word = new StringBuilder();
        var quoted = false;
        var i = start;
        char quote = '\0';
        for (; i < command.Length; i++)
        {
            var c = command[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
                else
                {
                    word.Append(c);
                }
                continue;
            }

            if (c is '\'' or '"')
            {
                quote = c;
                quoted = true;
                continue;
            }

            if (c == '\\' && i + 1 < command.Length)
            {
                quoted = true;
                word.Append(command[++i]);
                continue;
            }

            if (char.IsWhiteSpace(c) || c is ';' or '|' or '&' or '<' or '>' or '(' or ')')
            {
                break;
            }

            word.Append(c);
        }

        return (word.ToString(), quoted, i);
    }

    /// <summary>
    /// 从命令行结尾的换行处开始，依次吞掉挂起的 here-document 正文（直到各自的定界行），
    /// 正文并入当前片段而不是拆成命令；非引号定界符的正文里的命令替换照样分析。返回最后一个被吞掉的字符的下标。
    /// </summary>
    private static int ConsumeHeredocBodies(string command, int newlineIndex, List<Heredoc> heredocs, StringBuilder current, List<string> nested, Accumulator acc)
    {
        var pos = newlineIndex;
        if (command[pos] == '\r' && pos + 1 < command.Length && command[pos + 1] == '\n')
        {
            pos++;
        }

        foreach (var heredoc in heredocs)
        {
            while (true)
            {
                var lineStart = pos + 1;
                if (lineStart >= command.Length)
                {
                    return command.Length - 1;
                }

                var lineEnd = command.IndexOf('\n', lineStart);
                if (lineEnd < 0)
                {
                    lineEnd = command.Length;
                }

                var line = command[lineStart..lineEnd].TrimEnd('\r');
                current.Append('\n').Append(line);
                pos = lineEnd;

                var candidate = heredoc.StripTabs ? line.TrimStart('\t') : line;
                if (candidate == heredoc.Delimiter)
                {
                    break;
                }

                if (!heredoc.Quoted)
                {
                    CollectSubstitutions(line, nested, acc);
                }

                if (lineEnd >= command.Length)
                {
                    return command.Length - 1;
                }
            }
        }

        return Math.Min(pos, command.Length - 1);
    }

    /// <summary>here-document 正文（非引号定界符）里的命令替换会被执行：只按转义找 <c>$(</c> 与反引号。</summary>
    private static void CollectSubstitutions(string text, List<string> nested, Accumulator acc)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '\\')
            {
                i++;
                continue;
            }

            if (ch == '`' || (ch == '$' && i + 1 < text.Length && text[i + 1] == '('))
            {
                var innerStart = ch == '`' ? i + 1 : i + 2;
                var close = ch == '`' ? FindClosingBacktick(text, innerStart) : FindClosingParen(text, innerStart);
                if (close < 0)
                {
                    acc.Opaque = true;
                    return;
                }

                nested.Add(text[innerStart..close]);
                i = close;
            }
        }
    }

    private static int FindClosingParen(string text, int start)
    {
        var depth = 1;
        var inSingle = false;
        var inDouble = false;
        for (var j = start; j < text.Length; j++)
        {
            var c = text[j];
            if (inSingle)
            {
                if (c == '\'')
                {
                    inSingle = false;
                }
                continue;
            }

            if (c == '\\')
            {
                j++;
                continue;
            }

            if (inDouble)
            {
                if (c == '"')
                {
                    inDouble = false;
                }
                continue;
            }

            switch (c)
            {
                case '\'':
                    inSingle = true;
                    break;
                case '"':
                    inDouble = true;
                    break;
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    if (depth == 0)
                    {
                        return j;
                    }
                    break;
            }
        }

        return -1;
    }

    private static int FindClosingBacktick(string text, int start)
    {
        for (var j = start; j < text.Length; j++)
        {
            if (text[j] == '\\')
            {
                j++;
                continue;
            }

            if (text[j] == '`')
            {
                return j;
            }
        }

        return -1;
    }

    /// <summary>
    /// <paramref name="index"/> 处是不是续行（<c>\</c> 后紧跟 LF，或 CR LF）：是则返回续行占的字符数（2 或 3），否则 0。
    /// </summary>
    private static int LineContinuationLength(string text, int index)
    {
        if (text[index] != '\\' || index + 1 >= text.Length)
        {
            return 0;
        }

        if (text[index + 1] == '\n')
        {
            return 2;
        }

        return text[index + 1] == '\r' && index + 2 < text.Length && text[index + 2] == '\n' ? 3 : 0;
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

    private static bool IsDestructivePrefix(string prefix)
        => DestructivePrefixes.Contains(prefix, StringComparer.OrdinalIgnoreCase);
}
