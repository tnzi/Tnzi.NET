namespace Tnzi.Documents.Models;

/// <summary>
/// PDF 内一次正则命中的定位结果。
/// </summary>
/// <remarks>
/// 一个标签在 PDF 里可能被排版**折行**，从而落在不止一个行框上，因此同时给出
/// <see cref="LineBoxes"/>（全部行框）与 <see cref="Box"/>（面积最大的那个，作为主定位框）。
/// 单行命中时两者一致。坐标语义见 <see cref="NormalizedRect"/>。
/// </remarks>
[ExperimentalApi(Reason = "文档原语包的首个版本，字段可能随消费方需求增补")]
public sealed class PdfTextMatch
{
    /// <summary>初始化一个 <see cref="PdfTextMatch"/> 实例。</summary>
    /// <param name="pageNumber">命中所在页码（从 1 开始）。</param>
    /// <param name="text">命中的完整文本。</param>
    /// <param name="lineBoxes">命中覆盖的全部行框（按阅读顺序，至少一个）。</param>
    /// <param name="groups">正则捕获组的值，索引 0 为完整命中。</param>
    public PdfTextMatch(int pageNumber, string text, IReadOnlyList<NormalizedRect> lineBoxes, IReadOnlyList<string> groups)
    {
        Check.NotNull(text);
        Check.NotNullOrEmpty(lineBoxes);
        Check.NotNull(groups);

        PageNumber = pageNumber;
        Text = text;
        LineBoxes = lineBoxes;
        Groups = groups;

        var primary = lineBoxes[0];
        foreach (var box in lineBoxes)
        {
            if (box.Area > primary.Area)
                primary = box;
        }
        Box = primary;
    }

    /// <summary>命中所在页码（从 1 开始）。</summary>
    public int PageNumber { get; }

    /// <summary>命中的完整文本（等价于 <c>Groups[0]</c>）。</summary>
    public string Text { get; }

    /// <summary>主定位框：<see cref="LineBoxes"/> 中面积最大的一个。</summary>
    public NormalizedRect Box { get; }

    /// <summary>命中覆盖的全部行框；标签未折行时只有一个。</summary>
    public IReadOnlyList<NormalizedRect> LineBoxes { get; }

    /// <summary>正则捕获组的值，索引 0 为完整命中；未参与匹配的组为空串。</summary>
    /// <remarks>随命中一起返回，调用方不必为了取组值再跑一遍正则。</remarks>
    public IReadOnlyList<string> Groups { get; }
}
