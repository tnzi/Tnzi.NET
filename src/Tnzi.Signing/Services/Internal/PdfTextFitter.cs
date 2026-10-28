namespace Tnzi.Signing.Services.Internal;

/// <summary>
/// 按<b>实测宽度</b>把一段文本装进给定宽度：换成多行（<see cref="Wrap"/>）。
/// </summary>
/// <remarks>
/// <para>
/// ★ <see cref="IPdfStamper"/> 不换行也不裁剪：放不下的部分照样画出去，越过页边的字形落在 MediaBox 之外，
/// 打开 PDF 看不见，而压平、哈希、归档的正是这份缺字的版本。所以宽度必须由画字的同一个实现量
/// （<see cref="IPdfStamper.MeasureTextWidth"/>）。曾经按「字号 × 0.52 em × 字符数」估：那是小写拉丁字母的平均字宽，
/// 全大写的条款、W/M 这类宽字母、非拉丁文字都会估窄，而估窄的代价是密封文档里少一截作者写下的条款。
/// </para>
/// <para>
/// 宽度按整行重新量而不是逐词累加：两者在不做字距调整时相等，但整行量不依赖这个前提。
/// 硬切按<b>文本元素</b>（<see cref="StringInfo"/>）切，不会把代理对或组合字符从中间劈开。
/// </para>
/// </remarks>
internal sealed class PdfTextFitter
{
    private readonly IPdfStamper _stamper;
    private readonly double _fontSize;
    private readonly bool _bold;
    private readonly bool _italic;

    /// <param name="stamper">之后负责画这些文字的盖章实现（量与画必须是同一个）。</param>
    /// <param name="fontSize">字号（pt）。</param>
    /// <param name="bold">是否粗体。</param>
    /// <param name="italic">是否斜体。</param>
    public PdfTextFitter(IPdfStamper stamper, double fontSize, bool bold = false, bool italic = false)
    {
        _stamper = Check.NotNull(stamper);
        _fontSize = fontSize;
        _bold = bold;
        _italic = italic;
    }

    /// <summary>这一行按本字体画出来是否不超过 <paramref name="maxWidthPoints"/>。</summary>
    public bool Fits(string text, double maxWidthPoints)
        => _stamper.MeasureTextWidth(text, _fontSize, _bold, _italic) <= maxWidthPoints;

    /// <summary>
    /// 按空格断词换行，每一行画出来都不超过 <paramref name="maxWidthPoints"/>；单个放不下的词按文本元素硬切。
    /// </summary>
    /// <remarks>整段放得下时原样返回（保留行首缩进）；需要换行时连续空格合并为一个。</remarks>
    public IReadOnlyList<string> Wrap(string text, double maxWidthPoints)
    {
        Check.NotNull(text);

        if (text.Length == 0 || Fits(text, maxWidthPoints))
            return [text];

        var lines = new List<string>();
        var line = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (Fits(candidate, maxWidthPoints))
            {
                line = candidate;
                continue;
            }

            if (line.Length > 0)
                lines.Add(line);

            // 一个词自己就放不下（长网址、哈希、没有空格的文字）：宁可切开，也不要溢出到页外看不见。
            line = word;
            while (!Fits(line, maxWidthPoints))
            {
                var cut = LongestFittingPrefixLength(line, string.Empty, maxWidthPoints);
                lines.Add(line[..cut]);
                line = line[cut..];
            }
        }

        if (line.Length > 0)
            lines.Add(line);

        return lines;
    }

    /// <summary>
    /// 最长的、加上 <paramref name="suffix"/> 之后仍放得下的前缀长度（UTF-16 下标，落在文本元素边界上）。
    /// </summary>
    /// <remarks>
    /// 二分查找：宽度随前缀单调不减。<paramref name="suffix"/> 为空时（硬切）至少返回一个文本元素：
    /// 一个字形就比整行还宽时也要前进，否则调用方会原地打转。
    /// </remarks>
    private int LongestFittingPrefixLength(string text, string suffix, double maxWidthPoints)
    {
        var starts = StringInfo.ParseCombiningCharacters(text);
        var boundaries = new int[starts.Length + 1];
        starts.CopyTo(boundaries, 0);
        boundaries[^1] = text.Length;

        // 在 [low, high] 里找最大的元素个数 k，使 text[..boundaries[k]] + suffix 放得下。
        var low = suffix.Length == 0 ? 1 : 0;
        var high = starts.Length;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (Fits(text[..boundaries[mid]] + suffix, maxWidthPoints))
                low = mid;
            else
                high = mid - 1;
        }

        return boundaries[low];
    }
}
