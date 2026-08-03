namespace Tnzi.AI.Cli.Adapters;

/// <summary>
/// 过滤用户自定义参数中的协议契约参数。
/// </summary>
/// <remarks>
/// <para>
/// 刻意<b>只</b>拦截会破坏「框架 ↔ CLI 通信协议」的参数，不做「危险参数」黑名单：
/// 能配置 agent 的人本来就能给它任意工作目录和任意命令，再拦几个参数并不增加安全性，
/// 只会让合理用法（换模型别名、加 provider 私有开关）无谓受阻。
/// </para>
/// <para>
/// 顺带剥掉一层 shell 引号：用户习惯按 shell 语法填 <c>--deny-tool='write'</c>，
/// 而框架直接起进程不经 shell，引号会原样传给子进程并被它当成非法值拒掉。
/// </para>
/// </remarks>
public static class CliArgumentFilter
{
    /// <summary>
    /// 移除被禁参数（连带其取值），返回可安全追加的参数序列。
    /// </summary>
    /// <param name="arguments">用户提供的参数。</param>
    /// <param name="blocked">被禁参数表。</param>
    /// <param name="logger">命中时记录警告，让配置者能看见自己写的参数被丢弃了。</param>
    /// <param name="providerKey">日志用的 provider 键。</param>
    public static List<string> Filter(
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, BlockedArgMode> blocked,
        ILogger logger,
        string providerKey)
    {
        Check.NotNull(arguments);
        Check.NotNull(blocked);

        var result = new List<string>(arguments.Count);
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = Unquote(arguments[i]);
            var flag = argument;
            var hasInlineValue = false;

            var equalsIndex = argument.IndexOf('=');
            if (equalsIndex > 0)
            {
                flag = argument[..equalsIndex];
                hasInlineValue = true;
            }

            if (!blocked.TryGetValue(flag, out var mode))
            {
                result.Add(argument);
                continue;
            }

            logger.LogWarning(
                "[{Provider}] Dropping protocol-critical argument '{Flag}' from custom args; it is owned by the framework",
                providerKey, flag);

            // 带值参数的取值在下一个 token 上，必须一并吞掉 —— 否则它会以裸字符串
            // 的形式漏给 CLI，被当成位置参数（例如把模型名当成提示词文件路径）。
            if (mode == BlockedArgMode.WithValue && !hasInlineValue && i + 1 < arguments.Count)
            {
                i++;
            }
        }

        return result;
    }

    /// <summary>
    /// 剥掉一层成对的 shell 引号。
    /// </summary>
    /// <remarks>
    /// 只处理两种形态：<c>--flag='value'</c>（只剥值）与 <c>'standalone'</c>。
    /// 不做转义处理 —— 那需要一个真正的 shell 词法器，而这里的目的只是补救最常见的手误。
    /// </remarks>
    private static string Unquote(string argument)
    {
        if (argument.StartsWith('-'))
        {
            var equalsIndex = argument.IndexOf('=');
            if (equalsIndex > 0)
            {
                var value = argument[(equalsIndex + 1)..];
                return TryStripQuotes(value, out var unquotedValue)
                    ? string.Concat(argument.AsSpan(0, equalsIndex + 1), unquotedValue)
                    : argument;
            }
        }

        return TryStripQuotes(argument, out var unquoted) ? unquoted : argument;
    }

    private static bool TryStripQuotes(string value, out string result)
    {
        if (value.Length >= 2
            && ((value[0] == '\'' && value[^1] == '\'') || (value[0] == '"' && value[^1] == '"')))
        {
            result = value[1..^1];
            return true;
        }

        result = value;
        return false;
    }
}
