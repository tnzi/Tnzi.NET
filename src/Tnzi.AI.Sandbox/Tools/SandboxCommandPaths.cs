namespace Tnzi.AI.Sandbox.Tools;

/// <summary>
/// bash 工具命令里 <c>/mnt/*</c> 虚拟路径的定位、越界判定与改写。
/// </summary>
/// <remarks>
/// <para>
/// ★ 在<b>原始命令</b>上按 shell 分词规则定位 <c>/mnt/{workspace|uploads|outputs|skills}</c>，而不是先把虚拟根
/// 字面替换成物理路径、再在替换结果里按空格切词。后者在物理路径含空格时（macOS 默认的
/// <c>~/Library/Application Support/...</c>、带空格的 Windows 用户名）从根目录的第一个空格处把词切断，
/// 于是 <c>cat /mnt/workspace/a.txt</c> 被判越界；就算放行，shell 也会把没加引号的路径拆成几个参数。
/// </para>
/// <para>
/// 虚拟根本身不含空格，它的展开由这里整体接管：守卫看到的是「物理根 + 同一个 shell 词里的其余部分」
/// （去掉引号、处理转义之后的字面值），改写时只替换虚拟根那几个字符，并按它所在的引号上下文给物理根加引号。
/// 词里 agent 自己写的部分原样保留，glob 与变量照常由 shell 处理。
/// </para>
/// <para>
/// ★ 为什么守卫没有因此变弱：词的边界现在按 shell 自己的规则判定（未加引号的空白与控制符），
/// 引号里的空格和紧跟在闭合引号后面的 <c>/../..</c> 都算同一个词，守卫看到的是 shell 真正会用的那条路径 ——
/// 比按字符切词更完整。仍然只是字面判定：<c>$VAR</c>、命令替换这类运行期才成形的路径不在它能推理的范围，
/// Local provider 下它是加固层而不是监狱，这一点与此前相同。
/// </para>
/// </remarks>
internal static class SandboxCommandPaths
{
    private const string VirtualPrefix = "/mnt/";
    private static readonly string[] Mounts = ["workspace", "uploads", "outputs", "skills"];

    /// <summary>命令里的一次虚拟根出现。</summary>
    /// <param name="Index">虚拟根在原始命令里的起始下标。</param>
    /// <param name="Length">虚拟根本身的长度（<c>/mnt/workspace</c> 这几个字符）。</param>
    /// <param name="Mount">子目录名（<c>workspace</c> 等）。</param>
    /// <param name="LiteralSuffix">同一个 shell 词里紧随其后的部分，已去引号、已处理转义。</param>
    /// <param name="Context">虚拟根所处的引号上下文。</param>
    internal readonly record struct MountToken(int Index, int Length, string Mount, string LiteralSuffix, QuoteContext Context);

    internal enum QuoteContext
    {
        None,
        Single,
        Double
    }

    /// <summary>按出现顺序找出命令里的虚拟根。</summary>
    public static IReadOnlyList<MountToken> Find(string command, SandboxShellDialect dialect)
    {
        var tokens = new List<MountToken>();
        var state = QuoteContext.None;
        var i = 0;
        while (i < command.Length)
        {
            if (TryMatchMount(command, i, out var mount))
            {
                // 上下文取读后缀之前的状态：后缀里的引号切换属于 agent 自己写的那部分，与根无关。
                var context = state;
                var rootLength = VirtualPrefix.Length + mount.Length;
                var suffix = new StringBuilder();
                var end = ReadWordRemainder(command, i + rootLength, dialect, ref state, suffix);
                tokens.Add(new MountToken(i, rootLength, mount, suffix.ToString(), context));
                i = end;
                continue;
            }

            i = Advance(command, i, dialect, ref state);
        }

        return tokens;
    }

    /// <summary>
    /// 每个虚拟根展开后（物理根 + 同一个词里的其余部分）都必须落在线程目录之内；解析不了的一律按越界处理。
    /// </summary>
    public static bool AllWithin(IReadOnlyList<MountToken> tokens, string threadDirectory)
    {
        var normalizedThreadDir = Path.GetFullPath(threadDirectory);
        var comparison = HostPathComparison;

        foreach (var token in tokens)
        {
            string resolved;
            try
            {
                resolved = Path.GetFullPath(Path.Combine(threadDirectory, token.Mount) + token.LiteralSuffix);
            }
            catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
            {
                // 解析不了的路径可疑：失败关闭。
                return false;
            }

            if (!resolved.Equals(normalizedThreadDir, comparison)
                && !resolved.StartsWith(normalizedThreadDir + Path.DirectorySeparatorChar, comparison)
                && !resolved.StartsWith(normalizedThreadDir + Path.AltDirectorySeparatorChar, comparison))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 把每个虚拟根换成 <paramref name="mapRoot"/> 给出的沙箱侧路径，并按所在引号上下文加引号。只在 <see cref="AllWithin"/> 放行之后调用。
    /// </summary>
    public static string Rewrite(string command, IReadOnlyList<MountToken> tokens, SandboxShellDialect dialect, Func<string, string> mapRoot)
    {
        if (tokens.Count == 0) return command;

        var builder = new StringBuilder(command.Length + 64);
        var copied = 0;
        foreach (var token in tokens)
        {
            builder.Append(command, copied, token.Index - copied);
            builder.Append(Quote(mapRoot(token.Mount), token.Context, dialect));
            copied = token.Index + token.Length;
        }

        builder.Append(command, copied, command.Length - copied);
        return builder.ToString();
    }

    /// <summary>按引号上下文把一条路径变成 shell 里的同一个词。不需要引号的路径原样返回。</summary>
    internal static string Quote(string path, QuoteContext context, SandboxShellDialect dialect)
    {
        if (dialect == SandboxShellDialect.Cmd)
        {
            // cmd 只有双引号；Windows 路径里不可能出现 "。已经在双引号里的，原样放进去就是一个词。
            return context == QuoteContext.None && !path.All(IsCmdSafe) ? $"\"{path}\"" : path;
        }

        return context switch
        {
            QuoteContext.Single => path.Replace("'", "'\\''"),
            QuoteContext.Double => EscapeForPosixDoubleQuotes(path),
            _ => path.All(IsPosixSafe) ? path : $"'{path.Replace("'", "'\\''")}'"
        };
    }

    private static string EscapeForPosixDoubleQuotes(string path)
    {
        var builder = new StringBuilder(path.Length);
        foreach (var c in path)
        {
            if (c is '\\' or '"' or '$' or '`') builder.Append('\\');
            builder.Append(c);
        }

        return builder.ToString();
    }

    private static bool IsPosixSafe(char c) => char.IsLetterOrDigit(c) || c is '/' or '_' or '.' or '-' or '+' or ',' or ':' or '@' or '%' or '=';

    private static bool IsCmdSafe(char c) => char.IsLetterOrDigit(c) || c is '\\' or '/' or '_' or '.' or '-' or '+' or ':' or '@' or '#' or '$' or '~';

    private static bool TryMatchMount(string command, int index, out string mount)
    {
        mount = string.Empty;
        if (string.CompareOrdinal(command, index, VirtualPrefix, 0, VirtualPrefix.Length) != 0) return false;

        var afterPrefix = index + VirtualPrefix.Length;
        foreach (var candidate in Mounts)
        {
            if (string.CompareOrdinal(command, afterPrefix, candidate, 0, candidate.Length) == 0)
            {
                mount = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 从 <paramref name="start"/> 读到当前 shell 词结束（未加引号的空白或控制符），把字面值写进 <paramref name="literal"/>。
    /// 引号切换会更新 <paramref name="state"/>，返回词结束处的下标。
    /// </summary>
    private static int ReadWordRemainder(string command, int start, SandboxShellDialect dialect, ref QuoteContext state, StringBuilder literal)
    {
        var i = start;
        while (i < command.Length)
        {
            var c = command[i];
            switch (state)
            {
                case QuoteContext.None:
                    if (IsWordBoundary(c, dialect)) return i;
                    if (IsEscapeOutsideQuotes(c, dialect) && i + 1 < command.Length)
                    {
                        literal.Append(command[i + 1]);
                        i += 2;
                        continue;
                    }

                    if (c == '\'' && dialect == SandboxShellDialect.Posix) state = QuoteContext.Single;
                    else if (c == '"') state = QuoteContext.Double;
                    else literal.Append(c);
                    i++;
                    break;

                case QuoteContext.Single:
                    if (c == '\'') state = QuoteContext.None;
                    else literal.Append(c);
                    i++;
                    break;

                default:
                    if (c == '"')
                    {
                        state = QuoteContext.None;
                        i++;
                        break;
                    }

                    if (dialect == SandboxShellDialect.Posix && c == '\\' && i + 1 < command.Length && IsPosixDoubleQuoteEscapable(command[i + 1]))
                    {
                        literal.Append(command[i + 1]);
                        i += 2;
                        continue;
                    }

                    literal.Append(c);
                    i++;
                    break;
            }
        }

        return i;
    }

    /// <summary>越过一个（或一对转义）字符并更新引号状态。</summary>
    private static int Advance(string command, int i, SandboxShellDialect dialect, ref QuoteContext state)
    {
        var c = command[i];
        switch (state)
        {
            case QuoteContext.None:
                if (IsEscapeOutsideQuotes(c, dialect)) return Math.Min(i + 2, command.Length);
                if (c == '\'' && dialect == SandboxShellDialect.Posix) state = QuoteContext.Single;
                else if (c == '"') state = QuoteContext.Double;
                return i + 1;

            case QuoteContext.Single:
                if (c == '\'') state = QuoteContext.None;
                return i + 1;

            default:
                if (c == '"') state = QuoteContext.None;
                else if (dialect == SandboxShellDialect.Posix && c == '\\') return Math.Min(i + 2, command.Length);
                return i + 1;
        }
    }

    private static bool IsEscapeOutsideQuotes(char c, SandboxShellDialect dialect)
        => dialect == SandboxShellDialect.Posix ? c == '\\' : c == '^';

    private static bool IsPosixDoubleQuoteEscapable(char c) => c is '$' or '`' or '"' or '\\' or '\n';

    private static bool IsWordBoundary(char c, SandboxShellDialect dialect)
        => char.IsWhiteSpace(c) || c is '|' or '&' or ';' or '<' or '>' or '(' or ')'
           || (dialect == SandboxShellDialect.Posix && c == '`');

    private static StringComparison HostPathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
