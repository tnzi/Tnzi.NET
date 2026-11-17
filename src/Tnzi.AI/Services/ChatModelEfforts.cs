namespace Tnzi.AI.Services;

/// <summary>
/// 一个模型可选的推理强度：校验、规整与按值查找。值是运行时原生的（各家 CLI 词表不同），只比较、不解释。
/// </summary>
public static class ChatModelEfforts
{
    /// <summary>一个强度值的最大长度（与绑定上的 <c>ThinkingLevel</c> 同宽）。</summary>
    public const int MaxLength = 64;

    /// <summary>
    /// 一组强度与默认档合不合规：每一项非空、不超过 <see cref="MaxLength"/>、不含空白、不重复（不分大小写）；默认档必须是其中之一。
    /// 合规返回 null，否则返回原因。
    /// </summary>
    /// <param name="efforts">可选的强度；null 或空 = 不声明。</param>
    /// <param name="defaultEffort">默认档；null = 不预先选。</param>
    public static string? Validate(IReadOnlyCollection<string>? efforts, string? defaultEffort)
    {
        var list = efforts ?? [];
        if (list.Any(e => string.IsNullOrWhiteSpace(e) || e.Trim().Length > MaxLength || e.Trim().Any(char.IsWhiteSpace)))
        {
            return $"every reasoning effort must be a non-empty value of at most {MaxLength} characters without spaces.";
        }

        var duplicates = list.GroupBy(e => e.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
        {
            return $"the reasoning effort '{duplicates[0]}' is listed more than once.";
        }

        if (!string.IsNullOrWhiteSpace(defaultEffort) && Find(list, defaultEffort) is null)
        {
            return $"the default reasoning effort '{defaultEffort.Trim()}' is not among the efforts listed ({string.Join(", ", list)}).";
        }

        return null;
    }

    /// <summary>去掉首尾空白、空项与重复项（保留先出现的写法与次序）。</summary>
    public static List<string> Normalize(IEnumerable<string>? efforts)
        => (efforts ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>在 <paramref name="efforts"/> 里找 <paramref name="value"/>（不分大小写），返回清单里的写法；找不到为 null。</summary>
    public static string? Find(IEnumerable<string>? efforts, string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : (efforts ?? []).FirstOrDefault(e => string.Equals(e.Trim(), value.Trim(), StringComparison.OrdinalIgnoreCase))?.Trim();

    /// <summary>
    /// 内建 Agent 上这个强度值对应的 <see cref="ReasoningEffort"/>：按成员名读（不分大小写，none / low / medium / high / max），读不出时为 null。
    /// </summary>
    /// <remarks>内建路径的提供商载荷按枚举生成，所以只认这五个名字；其余原生值（<c>xhigh</c>、<c>on</c>）只对外部 CLI 运行时有意义。</remarks>
    public static ReasoningEffort? ToBuiltIn(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && !value.Trim().All(char.IsDigit)
           && Enum.TryParse<ReasoningEffort>(value.Trim(), ignoreCase: true, out var parsed)
           && Enum.IsDefined(parsed)
            ? parsed
            : null;
}
