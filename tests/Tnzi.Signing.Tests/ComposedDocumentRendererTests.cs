using Tnzi.Documents.Models;
using Tnzi.Documents.Services;
using Tnzi.Signing.Metadata;
using Tnzi.Signing.Services.Internal;

namespace Tnzi.Signing.Tests;

/// <summary>
/// Composed 模板排版。
/// </summary>
/// <remarks>
/// 用一个记录调用的假 stamper：这些断言要问的是**排版决策**（字段落在第几页的哪里、
/// 合并变量替没替、超长正文翻不翻页），不是 PDFsharp 画得对不对 —— 后者是
/// Tnzi.Documents 自己的测试，而且真画字要求机器上装了字体。
/// </remarks>
public class ComposedDocumentRendererTests
{
    /// <summary>只记下收到的请求，返回一段可辨认的假字节。</summary>
    private sealed class RecordingStamper : IPdfStamper
    {
        public PdfStampRequest? LastCreate { get; private set; }

        public byte[] Stamp(byte[] pdf, PdfStampRequest request) => pdf;

        public byte[] Create(PdfStampRequest request)
        {
            LastCreate = request;
            return [0x25, 0x50, 0x44, 0x46];
        }

        public double MeasureTextWidth(string text, double fontSize, bool bold = false, bool italic = false)
            => TestTextMetrics.Width(text, fontSize, bold);
    }

    private static SnapshotField Field(string key, SigningFieldType type = SigningFieldType.Text, string? label = null)
        => new()
        {
            Key = key,
            Label = label ?? key,
            Type = type,
            RecipientRole = "Client",
            Required = true,
            PlacementMode = FieldPlacementMode.Anchor,
            AnchorText = "somewhere",
        };

    private static (ComposedDocumentRenderer Renderer, RecordingStamper Stamper) Create()
    {
        var stamper = new RecordingStamper();
        return (new ComposedDocumentRenderer(stamper), stamper);
    }

    [Fact]
    public void Render_CapturesFieldPlacement_WhereItActuallyLaidItOut()
    {
        var (renderer, _) = Create();

        var result = renderer.Render(
            "Retainer Agreement",
            "First clause.\n\n[[client_signature]]\n",
            new Dictionary<string, object?>(),
            [Field("client_signature", SigningFieldType.Signature)]);

        // ★ 这是 Composed 存在的全部理由：落点在排版当时就已知，不必事后去搜。
        result.Placements.ShouldContainKey("client_signature");
        var placement = result.Placements["client_signature"];
        placement.Page.ShouldBe(1);
        placement.Y.ShouldBeGreaterThan(0m);
        placement.H.ShouldBeGreaterThan(0m);
        (placement.X + placement.W).ShouldBeLessThanOrEqualTo(1m);
        (placement.Y + placement.H).ShouldBeLessThanOrEqualTo(1m);
    }

    [Fact]
    public void Render_SubstitutesMergeValues()
    {
        var (renderer, stamper) = Create();

        renderer.Render(
            "Agreement",
            "This agreement is with {{ClientName}}.",
            new Dictionary<string, object?> { ["ClientName"] = "Acme Corp" },
            []);

        var texts = stamper.LastCreate!.Stamps.OfType<PdfTextStamp>().Select(s => s.Text).ToList();
        texts.ShouldContain(t => t.Contains("Acme Corp"));
        texts.ShouldNotContain(t => t.Contains("{{ClientName}}"));
    }

    [Fact]
    public void Render_LeavesUnresolvedTokensVisible_RatherThanBlanking()
    {
        var (renderer, stamper) = Create();

        renderer.Render(
            "Agreement",
            "Address on file: {{ClientAddress}}",
            new Dictionary<string, object?>(),
            []);

        // ★ 一处空白读起来像"这里本来就没有内容";留着 {{ClientAddress}} 是一句刺眼的
        //   "这份文档没合并完" —— 它会在有人签字之前被发现。
        var texts = stamper.LastCreate!.Stamps.OfType<PdfTextStamp>().Select(s => s.Text).ToList();
        texts.ShouldContain(t => t.Contains("{{ClientAddress}}"));
    }

    [Fact]
    public void Render_FlowsOntoASecondPage_WhenTheBodyIsLong()
    {
        var (renderer, stamper) = Create();

        var body = string.Join("\n", Enumerable.Range(0, 120).Select(i => $"Clause {i}: the parties agree."));
        var result = renderer.Render("Long", body, new Dictionary<string, object?>(), []);

        result.PageCount.ShouldBeGreaterThan(1);
        // 造出来的页数必须与内容用到的页数一致 —— 少造一页，最后那些盖章会因页码越界而抛。
        stamper.LastCreate!.AppendPages.Count.ShouldBe(result.PageCount);
        stamper.LastCreate.Stamps.Max(s => s.PageNumber).ShouldBeLessThanOrEqualTo(result.PageCount);
    }

    [Fact]
    public void Render_CarriesAFieldOntoTheNextPage_AndRecordsThatPage()
    {
        var (renderer, _) = Create();

        var body = string.Join("\n", Enumerable.Range(0, 120).Select(i => $"Clause {i}."))
                   + "\n[[witness_signature]]\n";
        var result = renderer.Render("Long", body, new Dictionary<string, object?>(),
            [Field("witness_signature", SigningFieldType.Signature)]);

        // 落点记的是它真正被排到的那一页，不是模板上写的 1。
        result.Placements["witness_signature"].Page.ShouldBe(result.PageCount);
        result.Placements["witness_signature"].Page.ShouldBeGreaterThan(1);
    }

    [Fact]
    public void Render_WrapsLongLines_SoNothingRunsOffTheRightEdge()
    {
        var (renderer, stamper) = Create();

        var longLine = string.Join(" ", Enumerable.Repeat("word", 400));
        renderer.Render("Wrap", longLine, new Dictionary<string, object?>(), []);

        // IPdfStamper 不换行,所以换行必须在排版这一侧发生;不换的话文字会越过页边、右半截看不见。
        var body = stamper.LastCreate!.Stamps.OfType<PdfTextStamp>()
            .Where(s => s.FontSize < 12d)
            .ToList();
        body.Count.ShouldBeGreaterThan(1);
        body.ShouldAllBe(s => MeasuredWidth(s) <= ContentWidthPoints);
        string.Join(" ", body.Select(s => s.Text)).ShouldBe(longLine);
    }

    /// <summary>
    /// 全大写的条款：大写字母比小写宽约四成。按「小写平均字宽 × 字符数」估出来的一行，量出来越过了正文区。
    /// </summary>
    [Fact]
    public void Render_AllCapsParagraph_EveryLineFitsByMeasuredWidth()
    {
        var (renderer, stamper) = Create();

        var clause = string.Join(" ", Enumerable.Repeat("IN NO EVENT SHALL EITHER PARTY BE LIABLE FOR INDIRECT DAMAGES", 8));
        renderer.Render("Agreement", clause, new Dictionary<string, object?>(), []);

        var body = BodyStamps(stamper);
        body.Count.ShouldBeGreaterThan(1);
        body.ShouldAllBe(s => MeasuredWidth(s) <= ContentWidthPoints);
        string.Join(" ", body.Select(s => s.Text)).ShouldBe(clause);
    }

    /// <summary>
    /// 标题此前根本不换行：16 pt 粗体一行只放得下几十个字符，而标题可以长到 200 字。
    /// </summary>
    [Theory]
    [InlineData("Engagement Letter and Fee Agreement for Litigation Services in the Matter of Plaintiff v. Defendant")]
    [InlineData("MASTER SERVICES AGREEMENT AND STATEMENT OF WORK FOR WAREHOUSE OPERATIONS AND MAINTENANCE")]
    public void Render_WrapsALongTitle_AndStartsTheBodyBelowItsLastLine(string title)
    {
        var (renderer, stamper) = Create();

        renderer.Render(title, "First clause.", new Dictionary<string, object?>(), []);

        var titleLines = stamper.LastCreate!.Stamps.OfType<PdfTextStamp>().Where(s => s.FontSize >= 16d).ToList();
        titleLines.Count.ShouldBeGreaterThan(1);
        titleLines.ShouldAllBe(s => s.Bold && MeasuredWidth(s) <= ContentWidthPoints);
        string.Join(" ", titleLines.Select(s => s.Text)).ShouldBe(title);

        // 换行之后正文必须从标题最后一行之下开始，否则两行文字叠在一起。
        var firstBody = BodyStamps(stamper).First();
        firstBody.Rect.Y.ShouldBeGreaterThanOrEqualTo(titleLines.Max(s => s.Rect.Y + s.Rect.Height));
    }

    /// <summary>字段标签同样要换行，而且字段框排在标签最后一行之下。</summary>
    [Fact]
    public void Render_WrapsALongFieldLabel_AndPlacesTheFieldBelowIt()
    {
        var (renderer, stamper) = Create();

        var label = string.Join(" ", Enumerable.Repeat("AUTHORISED SIGNATORY FOR AND ON BEHALF OF THE CLIENT", 4));
        var result = renderer.Render("Agreement", "[[client_signature]]", new Dictionary<string, object?>(),
            [Field("client_signature", SigningFieldType.Signature, label)]);

        var labelLines = stamper.LastCreate!.Stamps.OfType<PdfTextStamp>().Where(s => s.FontSize <= 8d).ToList();
        labelLines.Count.ShouldBeGreaterThan(1);
        labelLines.ShouldAllBe(s => MeasuredWidth(s) <= ContentWidthPoints);
        string.Join(" ", labelLines.Select(s => s.Text)).ShouldBe(label + " *");

        var placement = result.Placements["client_signature"];
        ((double)placement.Y).ShouldBeGreaterThanOrEqualTo(labelLines.Max(s => s.Rect.Y + s.Rect.Height) - 1e-9);
    }

    /// <summary>
    /// 一个比整行还宽的「词」（长网址、哈希、合并进来的无空格文本）按文本元素硬切：
    /// 每段都放得下、拼回去一字不差，而且不会把代理对劈成两半（劈开的半个代理对画出来是乱码）。
    /// </summary>
    [Fact]
    public void Render_HardSplitsAWordWiderThanALine_WithoutSplittingSurrogatePairs()
    {
        var (renderer, stamper) = Create();

        // 一个字母打头再接一串代理对：按 UTF-16 码元二分时，行尾恰好剩下放得下半个代理对的宽度。
        var word = "A" + string.Concat(Enumerable.Repeat("\U0001F600", 100));
        renderer.Render("Agreement", word, new Dictionary<string, object?>(), []);

        var body = BodyStamps(stamper);
        body.Count.ShouldBeGreaterThan(1);
        body.ShouldAllBe(s => MeasuredWidth(s) <= ContentWidthPoints);
        body.ShouldAllBe(s => !char.IsHighSurrogate(s.Text[s.Text.Length - 1]) && !char.IsLowSurrogate(s.Text[0]));
        string.Concat(body.Select(s => s.Text)).ShouldBe(word);
    }

    /// <summary>与 <c>ComposedDocumentRenderer</c> 的版式一致：A4 宽 595 pt，左右边距各 9%。</summary>
    private const double ContentWidthPoints = 595d * (1d - (0.09d * 2));

    private static double MeasuredWidth(PdfTextStamp stamp) => TestTextMetrics.Width(stamp.Text, stamp.FontSize, stamp.Bold);

    private static List<PdfTextStamp> BodyStamps(RecordingStamper stamper)
        => stamper.LastCreate!.Stamps.OfType<PdfTextStamp>().Where(s => s.FontSize is > 8d and < 12d).ToList();

    [Fact]
    public void Render_IgnoresAFieldTokenThatIsNotOnTheTemplate()
    {
        var (renderer, _) = Create();

        // 正文里写了一个模板上不存在的字段键:排版照常(留一个框),但不会凭空造出
        // 一个可填字段 —— 快照里没有它,密封时也就不会盖任何东西。
        var result = renderer.Render("X", "[[ghost]]", new Dictionary<string, object?>(), []);

        result.Placements.ShouldContainKey("ghost");
        result.PageCount.ShouldBe(1);
    }
}
