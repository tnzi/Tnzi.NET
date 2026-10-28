namespace Tnzi.Signing.Services.Internal;

/// <summary>
/// 模板的 <see cref="EnvelopeTemplate.HostEntityTypes"/>（逗号分隔的宿主类型清单，空 = 不限）的读写口径。
/// </summary>
/// <remarks>
/// <para>
/// 清单按<b>整项</b>比较、不分大小写：宿主类型名与 <see cref="MergeSourceRegistry"/> 同一口径。
/// 此前筛选是对整串做子串匹配，按 <c>Matter</c> 筛会把只限 <c>MatterNote</c> 的模板也列出来；
/// 发起请求时则根本不看这份清单，一份只限 <c>Matter</c> 的模板可以绑到 <c>Employee</c> 上，
/// 它的合并变量去另一个 provider 那里解析，必填的在发出时被拦下，不必填的留白密封。
/// </para>
/// <para>
/// 存储形态是规范化后的 <c>A,B</c>（去空白、去空项、不分大小写去重），好让数据库端的筛选能按
/// <c>,A,</c> 整项匹配（见 <see cref="MatchesExpression"/>）。规范化之前存下的行可能带空格，筛选时一并去掉。
/// </para>
/// </remarks>
internal static class HostEntityTypeList
{
    private const char Separator = ',';

    /// <summary>拆成各项（去空白、去空项、不分大小写去重，保留首次出现的写法与顺序）。</summary>
    public static IReadOnlyList<string> Parse(string? list)
        => string.IsNullOrWhiteSpace(list)
            ? []
            : list.Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    /// <summary>规范化成存储形态；没有任何项时为 <see langword="null"/>（= 不限）。</summary>
    public static string? Normalize(string? list)
    {
        var items = Parse(list);
        return items.Count == 0 ? null : string.Join(Separator, items);
    }

    /// <summary>
    /// 这份清单允不允许绑到 <paramref name="hostEntityType"/> 上：清单为空 = 不限；
    /// 清单不空时宿主类型必须是其中一项，不绑定宿主（独立文档）也不行：模板的合并变量正是为那几类记录写的。
    /// </summary>
    public static bool Allows(string? list, string? hostEntityType)
    {
        var items = Parse(list);
        if (items.Count == 0)
            return true;

        return !string.IsNullOrWhiteSpace(hostEntityType)
               && items.Contains(hostEntityType.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 数据库端的筛选：模板不限宿主类型，或清单里有 <paramref name="hostEntityType"/> 这一项（整项、不分大小写）。
    /// </summary>
    public static Expression<Func<EnvelopeTemplate, bool>> MatchesExpression(string hostEntityType)
    {
        Check.NotNullOrWhiteSpace(hostEntityType);

        // 两侧都去掉空格、包上分隔符再比：「,matter,」只命中整项，不会命中「,matternote,」。
        var needle = Separator + hostEntityType.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant() + Separator;
        return t => t.HostEntityTypes == null
                    || t.HostEntityTypes == string.Empty
                    || ("," + t.HostEntityTypes.Replace(" ", "") + ",").ToLower().Contains(needle);
    }
}
