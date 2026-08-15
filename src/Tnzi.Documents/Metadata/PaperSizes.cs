namespace Tnzi.Documents.Metadata;

/// <summary>
/// 具名纸张尺寸（PDF 点，1pt = 1/72 英寸）。
/// </summary>
/// <remarks>
/// <para>
/// 存在的理由是让配置写 <c>"Letter"</c> 而不是 <c>612 x 792</c>：纸张是有名字的东西，
/// 让人去记数字必然写错，而写错的症状（内容被缩放或截断）在肉眼看来只是「排版有点怪」。
/// </para>
/// <para>
/// 名字大小写不敏感。找不到时**不回退默认值** —— 配置里写了 <c>"A4 "</c> 的笔误应当在启动期
/// 被验证器拦下，而不是悄悄按 Letter 出图。
/// </para>
/// </remarks>
public static class PaperSizes
{
    /// <summary>US Letter 的宽（点）。<c>IPdfStamper.Create</c> 无尺寸时也回退到这个尺寸。</summary>
    public const double LetterWidthPt = 612d;

    /// <summary>US Letter 的高（点）。</summary>
    public const double LetterHeightPt = 792d;

    private static readonly IReadOnlyDictionary<string, (double Width, double Height)> Sizes =
        new Dictionary<string, (double, double)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Letter"] = (LetterWidthPt, LetterHeightPt),
            ["Legal"] = (612d, 1008d),
            ["Tabloid"] = (792d, 1224d),
            ["Ledger"] = (1224d, 792d),
            ["Executive"] = (522d, 756d),
            ["A0"] = (2384d, 3370d),
            ["A1"] = (1684d, 2384d),
            ["A2"] = (1191d, 1684d),
            ["A3"] = (842d, 1191d),
            ["A4"] = (595d, 842d),
            ["A5"] = (420d, 595d),
            ["A6"] = (298d, 420d)
        };

    /// <summary>全部已知的纸张名（用于错误消息与验证）。</summary>
    public static IReadOnlyCollection<string> Names => (IReadOnlyCollection<string>)Sizes.Keys;

    /// <summary>按名字取尺寸（点）；名字未知返回 false。</summary>
    /// <param name="name">纸张名，大小写不敏感。</param>
    /// <param name="size">命中时的宽高（点）。</param>
    public static bool TryGet(string? name, out (double WidthPt, double HeightPt) size)
    {
        size = default;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (!Sizes.TryGetValue(name.Trim(), out var found))
            return false;

        size = (found.Width, found.Height);
        return true;
    }
}
