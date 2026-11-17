namespace Tnzi.AI.Cli.Dispatch;

/// <summary>
/// 两次启动是不是同一种启动：一个已经起来的进程，能不能接着跑本来要另起一个进程的那一轮（§6.18）。
/// </summary>
/// <remarks>
/// 模型、权限档、工具清单、MCP 配置、环境都钉在启动参数与环境里，进程起来之后改不了：有一处不同，这一轮就要另起进程。
/// 续接参数（<c>--resume</c> / <c>--resume-session-at</c> / <c>--fork-session</c>）不比：接着用的就是那个会话本身。
/// 进程能在会话里换掉的开关（<c>changeable</c>，例如 provider 能换强度时的 <c>--effort</c>）在两次启动都给了时不比：
/// 接手的一方换成自己的值（<c>ICliTurnContinuation.ContinueAsync</c>）；只有一边给了仍算不同。
/// 两次启动都登记为按运行签发的环境变量（<see cref="CliLaunchEnvironment.RunScopedVariables"/>）比使用方给的标识、不比值：
/// 每条运行一份新签的令牌字节总是不同，但它登录的是同一份上游凭据时，起进程那条运行的令牌照样能用。
/// </remarks>
internal static class CliLaunchSpecs
{
    private static readonly HashSet<string> ResumeOptions = new(StringComparer.Ordinal) { "--resume", "--resume-session-at" };
    private const string ForkSessionFlag = "--fork-session";

    /// <param name="running">进程起来时的规格。</param>
    /// <param name="runningRunScoped">进程起来时启动环境里按运行签发的变量与标识。</param>
    /// <param name="wanted">这一轮本来要用的规格。</param>
    /// <param name="wantedRunScoped">这一轮的启动环境里按运行签发的变量与标识。</param>
    /// <param name="changeable">进程能在会话里换掉的带值开关；null = 没有。</param>
    public static bool SameLaunch(
        CliProcessSpec running, IReadOnlyDictionary<string, string?> runningRunScoped,
        CliProcessSpec wanted, IReadOnlyDictionary<string, string?> wantedRunScoped,
        IReadOnlyCollection<string>? changeable = null)
        => FindDifference(running, runningRunScoped, wanted, wantedRunScoped, changeable) is null;

    /// <summary>
    /// 两次启动哪里不同（写进日志，说明进程为什么没能接着用）；是同一种启动时为 null。
    /// </summary>
    /// <remarks>
    /// 只说出处，不带取值：参数里可能有设置 JSON，环境变量里有凭据。参数说第一处不同的开关名（不同的是取值时说它属于哪个开关），
    /// 环境说取值不同、只在一边有的变量名。
    /// </remarks>
    /// <inheritdoc cref="SameLaunch" path="/param"/>
    public static string? FindDifference(
        CliProcessSpec running, IReadOnlyDictionary<string, string?> runningRunScoped,
        CliProcessSpec wanted, IReadOnlyDictionary<string, string?> wantedRunScoped,
        IReadOnlyCollection<string>? changeable = null)
    {
        Check.NotNull(running);
        Check.NotNull(runningRunScoped);
        Check.NotNull(wanted);
        Check.NotNull(wantedRunScoped);

        if (!string.Equals(running.ExecutablePath, wanted.ExecutablePath, StringComparison.Ordinal))
        {
            return "executable";
        }

        if (!string.Equals(running.WorkingDirectory, wanted.WorkingDirectory, StringComparison.Ordinal))
        {
            return "working directory";
        }

        if (running.InheritAllHostEnvironment != wanted.InheritAllHostEnvironment)
        {
            return "host environment inheritance";
        }

        List<string> runningArgs = [.. WithoutResume(running.Arguments)];
        List<string> wantedArgs = [.. WithoutResume(wanted.Arguments)];
        foreach (var option in changeable ?? [])
        {
            if (runningArgs.Contains(option, StringComparer.Ordinal) && wantedArgs.Contains(option, StringComparer.Ordinal))
            {
                runningArgs = [.. WithoutOption(runningArgs, option)];
                wantedArgs = [.. WithoutOption(wantedArgs, option)];
            }
        }

        if (DescribeArgumentDifference(runningArgs, wantedArgs) is { } arguments)
        {
            return arguments;
        }

        if (!running.EnvironmentWhitelist.SequenceEqual(wanted.EnvironmentWhitelist, StringComparer.Ordinal))
        {
            return "environment whitelist";
        }

        var variables = DifferentVariables(running.Environment, runningRunScoped, wanted.Environment, wantedRunScoped);
        return variables.Count == 0 ? null : "environment variables " + string.Join(", ", variables);
    }

    private static string? DescribeArgumentDifference(IReadOnlyList<string> running, IReadOnlyList<string> wanted)
    {
        for (var i = 0; i < Math.Max(running.Count, wanted.Count); i++)
        {
            var left = i < running.Count ? running[i] : null;
            var right = i < wanted.Count ? wanted[i] : null;
            if (string.Equals(left, right, StringComparison.Ordinal))
            {
                continue;
            }

            if (IsOption(left) || IsOption(right))
            {
                return $"argument {(IsOption(right) ? right : left)}";
            }

            var owner = i > 0 && IsOption(running[i - 1]) ? running[i - 1] : null;
            return owner is null ? $"argument at position {i}" : $"value of argument {owner}";
        }

        return null;
    }

    private static bool IsOption(string? argument) => argument is not null && argument.StartsWith('-');

    /// <summary>去掉一个带值开关（连同它的取值）。</summary>
    private static IEnumerable<string> WithoutOption(IReadOnlyList<string> arguments, string option)
    {
        for (var i = 0; i < arguments.Count; i++)
        {
            if (string.Equals(arguments[i], option, StringComparison.Ordinal))
            {
                i++;
                continue;
            }

            if (!arguments[i].StartsWith(option + "=", StringComparison.Ordinal))
            {
                yield return arguments[i];
            }
        }
    }

    private static IEnumerable<string> WithoutResume(IReadOnlyList<string> arguments)
    {
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            if (ResumeOptions.Contains(argument))
            {
                i++; // 连同它的取值
                continue;
            }

            if (string.Equals(argument, ForkSessionFlag, StringComparison.Ordinal)
                || ResumeOptions.Any(option => argument.StartsWith(option + "=", StringComparison.Ordinal)))
            {
                continue;
            }

            yield return argument;
        }
    }

    /// <summary>取值不同（按运行签发的比标识）或只在一边有的变量名，按名字排序。</summary>
    private static List<string> DifferentVariables(
        IReadOnlyDictionary<string, string> running, IReadOnlyDictionary<string, string?> runningRunScoped,
        IReadOnlyDictionary<string, string> wanted, IReadOnlyDictionary<string, string?> wantedRunScoped)
    {
        var different = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in running)
        {
            var same = wanted.TryGetValue(name, out var wantedValue)
                       && (FindIdentity(runningRunScoped, name) is (true, var runningIdentity)
                           && FindIdentity(wantedRunScoped, name) is (true, var wantedIdentity)
                           ? string.Equals(runningIdentity, wantedIdentity, StringComparison.Ordinal)
                           : string.Equals(value, wantedValue, StringComparison.Ordinal));
            if (!same)
            {
                different.Add(name);
            }
        }

        foreach (var name in wanted.Keys)
        {
            if (!running.ContainsKey(name))
            {
                different.Add(name);
            }
        }

        return [.. different];
    }

    /// <summary>变量登记为按运行签发时它的标识。环境变量名不分大小写，使用方给的字典未必用同样的比较器。</summary>
    private static (bool Found, string? Identity) FindIdentity(IReadOnlyDictionary<string, string?> runScoped, string name)
    {
        if (runScoped.TryGetValue(name, out var identity))
        {
            return (true, identity);
        }

        foreach (var (key, value) in runScoped)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return (true, value);
            }
        }

        return (false, null);
    }
}
