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

        // IPdfStamper 不换行,所以换行必须在排版这一侧发生;不换的话文字会被裁掉右半。
        var body = stamper.LastCreate!.Stamps.OfType<PdfTextStamp>()
            .Where(s => s.FontSize < 12d)
            .ToList();
        body.Count.ShouldBeGreaterThan(1);
        body.ShouldAllBe(s => s.Text.Length <= 220);
    }

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
