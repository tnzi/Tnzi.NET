namespace Tnzi.Documents.Signing.Services.Internal;

/// <summary>渲染结果：PDF 字节、实际页数，以及排版过程中就地捕获的字段落点。</summary>
/// <param name="Pdf">渲染出的 PDF 字节。</param>
/// <param name="PageCount">实际页数（随内容长度变化，不是模板上记的那个）。</param>
/// <param name="Placements">字段键 → (页码, 归一化框)。只含正文里真的出现过的字段。</param>
public sealed record ComposedRenderResult(
    byte[] Pdf,
    int PageCount,
    IReadOnlyDictionary<string, (int Page, decimal X, decimal Y, decimal W, decimal H)> Placements);

/// <summary>
/// 把 <see cref="TemplateSource.Composed"/> 模板的正文排成 PDF，并<b>在排版过程中就地捕获</b>
/// 每个字段的落点。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>为什么坐标必须在这里产生，而不是事后去搜。</b>Composed 模板的正文长度随合并变量
/// 变化（一个地址两行还是三行，条款列表几条），分页因此每份文档都可能不同。事后按锚文本
/// 反搜是上传文档才需要的补救手段 —— 对自己排的版还去搜一遍，等于先把已知的东西丢掉、
/// 再花力气猜回来，而且猜错时是把签名盖到别处。
/// </para>
/// <para>
/// 版式刻意极简：标题 + 段落 + 字段行。它要能在没有中文字体的精简容器里画得出来，
/// 而每多一种版式元素就多一分画不出来的可能。需要复杂版式的走 <c>Uploaded</c>。
/// </para>
/// <para>
/// 正文语法两种占位：<c>{{变量}}</c> 由合并值替换；<c>[[字段键]]</c> 独占一行，排到哪里、
/// 那里就是这个字段的框。
/// </para>
/// </remarks>
public sealed partial class ComposedDocumentRenderer
{
    private const double PageWidthPoints = 595d;   // A4
    private const double PageHeightPoints = 842d;

    private const double MarginX = 0.09d;
    private const double ContentWidth = 1d - (MarginX * 2);
    private const double TopY = 0.08d;
    private const double BottomY = 0.92d;

    private const double BodyFontSize = 10.5d;
    private const double TitleFontSize = 16d;
    private const double BodyLineHeight = 0.0182d;
    private const double ParagraphGap = 0.008d;

    /// <summary>字段框的高度（归一化）。签名框比文本框高，因为落进去的是一张图。</summary>
    private const double FieldHeight = 0.020d;
    private const double SignatureFieldHeight = 0.045d;

    /// <summary>
    /// 一行大约放得下多少个字符。<see cref="IPdfStamper"/> 不换行，所以换行得在这里算；
    /// 没有字体度量可用（那要求容器装了字体），用字号推一个保守值 —— 宁可一行短一点，
    /// 也不要让文字被裁掉右半。
    /// </summary>
    private static int CharsPerLine(double fontSize)
        => Math.Max(20, (int)(PageWidthPoints * ContentWidth / (fontSize * 0.52d)));

    [GeneratedRegex(@"\{\{\s*([^}]+?)\s*\}\}")]
    private static partial Regex MergeTokenRegex();

    [GeneratedRegex(@"^\s*\[\[\s*([^\]]+?)\s*\]\]\s*$")]
    private static partial Regex FieldTokenRegex();

    private readonly IPdfStamper _stamper;

    public ComposedDocumentRenderer(IPdfStamper stamper)
    {
        _stamper = Check.NotNull(stamper);
    }

    /// <summary>
    /// 排版并渲染。
    /// </summary>
    /// <param name="title">标题（第一页顶部）。</param>
    /// <param name="bodyTemplate">正文模板（含 <c>{{变量}}</c> 与 <c>[[字段键]]</c>）。</param>
    /// <param name="mergeValues">合并变量取值。缺的键**原样保留占位符**——见下方说明。</param>
    /// <param name="fields">模板字段（用来知道每个字段该多高、标签写什么）。</param>
    public ComposedRenderResult Render(
        string title,
        string bodyTemplate,
        IReadOnlyDictionary<string, object?> mergeValues,
        IReadOnlyList<SnapshotField> fields)
    {
        Check.NotNull(mergeValues);
        Check.NotNull(fields);

        var fieldByKey = fields.ToDictionary(f => f.Key, StringComparer.OrdinalIgnoreCase);
        var placements = new Dictionary<string, (int, decimal, decimal, decimal, decimal)>(StringComparer.OrdinalIgnoreCase);
        var stamps = new List<PdfStamp>();

        var page = 1;
        var y = TopY;

        if (!string.IsNullOrWhiteSpace(title))
        {
            stamps.Add(new PdfTextStamp
            {
                PageNumber = page,
                Rect = new NormalizedRect(MarginX, y, ContentWidth, BodyLineHeight * 1.8),
                Text = title.Trim(),
                FontSize = TitleFontSize,
                Bold = true,
                Alignment = PdfStampAlignment.CenterLeft,
            });
            y += BodyLineHeight * 2.4;
        }

        foreach (var rawLine in SplitLines(bodyTemplate))
        {
            var fieldMatch = FieldTokenRegex().Match(rawLine);
            if (fieldMatch.Success)
            {
                var key = fieldMatch.Groups[1].Value;
                var height = fieldByKey.TryGetValue(key, out var field) && field.IsSignatureLike
                    ? SignatureFieldHeight
                    : FieldHeight;

                (page, y) = EnsureRoom(page, y, height + BodyLineHeight);

                // 标签画在框上方一行；框本身不画边线 —— 签好之后那条线就是多余的墨，
                // 而没签的空白正文本来就该看得出是空白。
                if (fieldByKey.TryGetValue(key, out var described) && !string.IsNullOrWhiteSpace(described.Label))
                {
                    stamps.Add(new PdfTextStamp
                    {
                        PageNumber = page,
                        Rect = new NormalizedRect(MarginX, y, ContentWidth, BodyLineHeight),
                        Text = described.Required ? $"{described.Label} *" : described.Label,
                        FontSize = 8d,
                        Color = "#666666",
                        Alignment = PdfStampAlignment.CenterLeft,
                    });
                    y += BodyLineHeight;
                }

                // ★ 就地记下落点：这一刻我们**确知**它在第几页的哪里。
                placements[key] = (page, (decimal)MarginX, (decimal)y, (decimal)(ContentWidth * 0.55), (decimal)height);
                y += height + ParagraphGap;
                continue;
            }

            var text = ApplyMerge(rawLine, mergeValues);
            if (string.IsNullOrWhiteSpace(text))
            {
                y += ParagraphGap;
                continue;
            }

            foreach (var wrapped in Wrap(text, CharsPerLine(BodyFontSize)))
            {
                (page, y) = EnsureRoom(page, y, BodyLineHeight);
                stamps.Add(new PdfTextStamp
                {
                    PageNumber = page,
                    Rect = new NormalizedRect(MarginX, y, ContentWidth, BodyLineHeight),
                    Text = wrapped,
                    FontSize = BodyFontSize,
                    Alignment = PdfStampAlignment.CenterLeft,
                });
                y += BodyLineHeight;
            }
            y += ParagraphGap;
        }

        var pageCount = Math.Max(page, 1);
        var pdf = _stamper.Create(new PdfStampRequest
        {
            AppendPages = Enumerable.Range(0, pageCount)
                .Select(_ => new PdfPageSpec { WidthPoints = PageWidthPoints, HeightPoints = PageHeightPoints })
                .ToList(),
            Stamps = stamps,
        });

        return new ComposedRenderResult(pdf, pageCount, placements);
    }

    /// <summary>放不下就翻页。返回翻页之后的 (页码, y)。</summary>
    private static (int Page, double Y) EnsureRoom(int page, double y, double needed)
        => y + needed > BottomY ? (page + 1, TopY) : (page, y);

    /// <summary>
    /// 替换 <c>{{变量}}</c>。
    /// </summary>
    /// <remarks>
    /// ★ <b>解析不出的键原样留着占位符，不换成空串。</b>一处空白读起来像"这里本来就没有内容"，
    /// 而 <c>{{ClientAddress}}</c> 留在纸面上是一句刺眼的"这份文档没合并完" —— 它会在有人
    /// 签字之前被发现。这与 <see cref="IMergeSourceProvider.ResolveAsync"/> 那条
    /// "解析不出就不要返回键"是同一条原则的两端。
    /// </remarks>
    private static string ApplyMerge(string line, IReadOnlyDictionary<string, object?> values)
        => MergeTokenRegex().Replace(line, match =>
        {
            var key = match.Groups[1].Value;
            if (!values.TryGetValue(key, out var value) || value is null)
                return match.Value;
            return value switch
            {
                DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                decimal dec => dec.ToString("0.##", CultureInfo.InvariantCulture),
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? match.Value,
            };
        });

    private static IEnumerable<string> SplitLines(string? body)
        => string.IsNullOrEmpty(body)
            ? []
            : body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    /// <summary>按空白断词换行；单个超长词直接硬切（宁可切开，也不要溢出到页外看不见）。</summary>
    private static IEnumerable<string> Wrap(string text, int maxChars)
    {
        if (text.Length <= maxChars)
        {
            yield return text;
            yield break;
        }

        var builder = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var piece = word;
            while (piece.Length > maxChars)
            {
                if (builder.Length > 0)
                {
                    yield return builder.ToString();
                    builder.Clear();
                }
                yield return piece[..maxChars];
                piece = piece[maxChars..];
            }

            if (builder.Length == 0)
            {
                builder.Append(piece);
            }
            else if (builder.Length + 1 + piece.Length <= maxChars)
            {
                builder.Append(' ').Append(piece);
            }
            else
            {
                yield return builder.ToString();
                builder.Clear().Append(piece);
            }
        }

        if (builder.Length > 0)
            yield return builder.ToString();
    }
}
