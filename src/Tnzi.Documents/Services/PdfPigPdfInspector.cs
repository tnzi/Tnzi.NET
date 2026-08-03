using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Exceptions;

namespace Tnzi.Documents.Services;

/// <summary>
/// 默认的 PDF 读取实现（PdfPig，纯托管、Apache-2.0）。
/// </summary>
/// <remarks>
/// <para><b>两条算法约束是实测出来的，改动前先读完这段。</b></para>
/// <list type="number">
/// <item><b>字母级扫描，不用 <c>page.GetWords()</c></b>。分词器会把
/// <c>{{Key;type=date;role=Client}}</c> 这类标签按空白/间距切碎，切完就再也匹配不上。
/// 做法是把 <c>page.Letters</c> 按顺序拼成一整条字符串跑正则，再把命中区间映射回 letters 取包围盒。</item>
/// <item><b>分行按 <see cref="Letter.StartBaseLine"/> 的 Y 分组，不能按包围盒底边</b>。
/// 带下降部的字形（<c>{</c>、<c>y</c>、<c>p</c>）底边比同行邻居低，按底边分组会把一行炸成 6-8 段。</item>
/// </list>
/// <para>另：<c>Letter.GlyphRectangle</c> 已标记 obsolete，一律用 <see cref="Letter.BoundingBox"/>。</para>
/// </remarks>
public sealed class PdfPigPdfInspector : IPdfInspector
{
    /// <summary>正则执行超时：模式来自调用方，给个上限防病态回溯拖垮请求。</summary>
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    /// <summary>同一行的基线 Y 容差（point）。同一行的基线本该完全相等，留一点浮点余量。</summary>
    private const double BaselineTolerance = 1.0d;

    /// <inheritdoc />
    public PdfInfo GetInfo(byte[] pdf)
    {
        Check.NotNull(pdf);

        using var document = Open(pdf);
        var pages = new List<PdfPageInfo>();
        foreach (var page in document.GetPages())
            pages.Add(new PdfPageInfo(page.Number, page.Width, page.Height));

        return new PdfInfo(pages);
    }

    /// <inheritdoc />
    public IReadOnlyList<PdfTextMatch> FindTags(byte[] pdf, string pattern)
    {
        Check.NotNull(pdf);
        Check.NotNullOrWhiteSpace(pattern);

        Regex regex;
        try
        {
            regex = new Regex(pattern, RegexOptions.None, RegexTimeout);
        }
        catch (ArgumentException ex)
        {
            throw new PdfDocumentException($"Invalid tag pattern '{pattern}': {ex.Message}", ex);
        }

        var matches = new List<PdfTextMatch>();

        using var document = Open(pdf);
        foreach (var page in document.GetPages())
            CollectMatches(page, regex, matches);

        return matches;
    }

    private static void CollectMatches(Page page, Regex regex, List<PdfTextMatch> matches)
    {
        var letters = page.Letters;
        if (letters.Count == 0)
            return;

        // 拼出与 letters 逐字符对齐的文本：letterOfChar[i] = 第 i 个字符属于哪个 letter。
        // Letter.Value 可能是多字符（连字），所以不能假设一 letter 一字符。
        var builder = new StringBuilder();
        var letterOfChar = new List<int>();
        for (var index = 0; index < letters.Count; index++)
        {
            var value = letters[index].Value;
            if (string.IsNullOrEmpty(value))
                continue;

            builder.Append(value);
            for (var offset = 0; offset < value.Length; offset++)
                letterOfChar.Add(index);
        }

        if (letterOfChar.Count == 0)
            return;

        var text = builder.ToString();
        Match match;
        try
        {
            match = regex.Match(text);
        }
        catch (RegexMatchTimeoutException ex)
        {
            throw new PdfDocumentException(
                $"Tag pattern '{regex}' timed out on page {page.Number}; simplify the pattern.", ex);
        }

        while (match.Success)
        {
            if (match.Length > 0)
            {
                var first = letterOfChar[match.Index];
                var last = letterOfChar[match.Index + match.Length - 1];
                var lineBoxes = BuildLineBoxes(page, letters, first, last);
                if (lineBoxes.Count > 0)
                    matches.Add(new PdfTextMatch(page.Number, match.Value, lineBoxes, CaptureGroups(match)));
            }

            match = match.NextMatch();
        }
    }

    /// <summary>
    /// 把命中覆盖的 letters 按基线分行，每行给一个归一化包围盒。
    /// </summary>
    private static IReadOnlyList<NormalizedRect> BuildLineBoxes(Page page, IReadOnlyList<Letter> letters, int first, int last)
    {
        // 分行按 StartBaseLine.Y —— 按包围盒底边会被下降部字形（{ y p）打散，见类注释。
        var lines = new List<LineAccumulator>();

        for (var index = first; index <= last; index++)
        {
            var letter = letters[index];
            var baseline = letter.StartBaseLine.Y;

            LineAccumulator? line = null;
            foreach (var candidate in lines)
            {
                if (Math.Abs(candidate.Baseline - baseline) <= BaselineTolerance)
                {
                    line = candidate;
                    break;
                }
            }

            if (line == null)
            {
                line = new LineAccumulator(baseline);
                lines.Add(line);
            }

            line.Add(letter.BoundingBox);
        }

        var boxes = new List<NormalizedRect>(lines.Count);
        foreach (var line in lines)
            boxes.Add(NormalizedCoordinates.FromPdfBox(line.MinX, line.MinY, line.MaxX, line.MaxY, page.Width, page.Height));

        return boxes;
    }

    private static IReadOnlyList<string> CaptureGroups(Match match)
    {
        var groups = new List<string>(match.Groups.Count);
        for (var index = 0; index < match.Groups.Count; index++)
            groups.Add(match.Groups[index].Success ? match.Groups[index].Value : string.Empty);

        return groups;
    }

    private static PdfDocument Open(byte[] pdf)
    {
        if (pdf.Length == 0)
            throw new PdfDocumentException("The PDF byte array is empty.");

        try
        {
            return PdfDocument.Open(pdf);
        }
        catch (PdfDocumentEncryptedException ex)
        {
            throw new PdfDocumentException("The PDF is password protected and cannot be inspected.", ex);
        }
        catch (Exception ex) when (ex is not TnziException and not OperationCanceledException)
        {
            // 边界包装：字节来自上传，任何底层解析异常都要变成可读的框架异常并保留 inner
            throw new PdfDocumentException($"The byte array is not a readable PDF: {ex.Message}", ex);
        }
    }

    /// <summary>一行的包围盒累加器（避免为每行分配中间集合）。</summary>
    private sealed class LineAccumulator
    {
        public LineAccumulator(double baseline)
        {
            Baseline = baseline;
            MinX = double.MaxValue;
            MinY = double.MaxValue;
            MaxX = double.MinValue;
            MaxY = double.MinValue;
        }

        public double Baseline { get; }

        public double MinX { get; private set; }

        public double MinY { get; private set; }

        public double MaxX { get; private set; }

        public double MaxY { get; private set; }

        public void Add(PdfRectangle box)
        {
            MinX = Math.Min(MinX, box.Left);
            MinY = Math.Min(MinY, box.Bottom);
            MaxX = Math.Max(MaxX, box.Right);
            MaxY = Math.Max(MaxY, box.Top);
        }
    }
}
