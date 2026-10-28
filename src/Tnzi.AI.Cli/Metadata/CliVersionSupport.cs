namespace Tnzi.AI.Cli.Metadata;

/// <summary>
/// 一个 CLI 版本在不在部署为这家 provider 声明的支持范围内（<see cref="CliProviderDescriptor.MinimumVersion"/> /
/// <see cref="CliProviderDescriptor.MaximumVersion"/>）。
/// </summary>
/// <remarks>
/// <para>
/// 这是「跑不跑」的门，不是「怎么跑」的分支：范围内的版本走同一条协议路径。没声明范围时任何版本都放行（与没有这道门时相同）。
/// </para>
/// <para>
/// 版本取 CLI 报出的字符串里第一段「数字.数字…」（<c>2.1.310 (Claude Code)</c> → <c>2.1.310</c>），逐段按数字比较。
/// 声明了范围却认不出版本时拒绝：不知道是哪个版本，就说不出它在不在范围里。
/// </para>
/// </remarks>
public static partial class CliVersionSupport
{
    /// <summary>
    /// 这个版本不被接受的原因（给使用者看的英文）；接受时返回 null。
    /// </summary>
    /// <param name="provider">provider 描述（带部署声明的范围）。</param>
    /// <param name="version">运行时登记的版本（CLI 报出的原样字符串）。</param>
    /// <param name="location">这个 CLI 在哪里（执行目标的标识），写进原因里。</param>
    public static string? GetRejection(CliProviderDescriptor provider, string? version, string location)
    {
        Check.NotNull(provider);

        var minimum = Parse(provider.MinimumVersion);
        var maximum = Parse(provider.MaximumVersion);
        if (minimum is null && maximum is null)
        {
            return null;
        }

        var range = Describe(provider);
        if (Parse(version) is not { } actual)
        {
            return $"The version of {provider.DisplayName} on '{location}' is unknown, and this deployment only runs {range}.";
        }

        if ((minimum is not null && Compare(actual, minimum, minimum.Length) < 0)
            || (maximum is not null && Compare(actual, maximum, maximum.Length, truncate: true) > 0))
        {
            return $"{provider.DisplayName} {string.Join('.', actual)} on '{location}' is not supported here; this deployment runs {range}. "
                   + "Update the CLI on that host.";
        }

        return null;
    }

    /// <summary>取出版本号的数字段；认不出时为 null。</summary>
    public static int[]? Parse(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var match = VersionPattern().Match(version);
        if (!match.Success)
        {
            return null;
        }

        var segments = match.Value.Split('.');
        var parsed = new int[segments.Length];
        for (var i = 0; i < segments.Length; i++)
        {
            if (!int.TryParse(segments[i], out parsed[i]))
            {
                return null;
            }
        }

        return parsed;
    }

    /// <summary>
    /// 逐段比较。<paramref name="truncate"/> = true 时只比 <paramref name="segments"/> 段（上限 <c>2.1</c> 涵盖 2.1.x），
    /// 否则缺的段按 0 补齐。
    /// </summary>
    private static int Compare(int[] actual, int[] bound, int segments, bool truncate = false)
    {
        var length = truncate ? segments : Math.Max(actual.Length, bound.Length);
        for (var i = 0; i < length; i++)
        {
            var left = i < actual.Length ? actual[i] : 0;
            var right = i < bound.Length ? bound[i] : 0;
            if (left != right)
            {
                return left.CompareTo(right);
            }
        }

        return 0;
    }

    private static string Describe(CliProviderDescriptor provider)
        => (provider.MinimumVersion, provider.MaximumVersion) switch
        {
            ({ } min, { } max) when !string.IsNullOrWhiteSpace(min) && !string.IsNullOrWhiteSpace(max)
                => $"{provider.DisplayName} {min} up to {max}.x",
            ({ } min, _) when !string.IsNullOrWhiteSpace(min) => $"{provider.DisplayName} {min} or later",
            (_, { } max) => $"{provider.DisplayName} up to {max}.x",
            _ => provider.DisplayName
        };

    [GeneratedRegex(@"\d+(\.\d+)*")]
    private static partial Regex VersionPattern();
}
