using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.Documents.Exceptions;
using Tnzi.Documents.Models;
using Tnzi.Documents.Services;
using Tnzi.Geometry;
using Tnzi.Signing.Metadata;
using Tnzi.Signing.Services.Internal;
using UglyToad.PdfPig;
using Xunit.Abstractions;

namespace Tnzi.Signing.Tests;

/// <summary>
/// Composed 排版用<b>真实字体</b>画出来之后，每一个字形都落在正文区之内。
/// </summary>
/// <remarks>
/// <para>
/// ★ 排版决策测试（<see cref="ComposedDocumentRendererTests"/>）用假 stamper，断言的是「拆成了几行」；
/// 那证明不了拆出来的每一行<b>画出来</b>放得下。<see cref="IPdfStamper"/> 不换行也不裁剪，
/// 一行估窄了，后半截就画到 A4 的 595 pt 之外：PDF 打开看不见、密封后的哈希也是这份缺字的版本，
/// 而整条链路不报任何错。全大写的免责条款与超过一行的标题是两种最常见的形态。
/// </para>
/// <para>
/// 这里读回成品里每个字母的位置（PdfPig），直接问「最右边的字形在哪」。画字要系统字体，
/// 没有字体的环境（精简 CI 镜像）跳过而不是让整个套件变红，与 Tnzi.Documents 自己的测试同一口径。
/// </para>
/// </remarks>
public class ComposedDocumentPageEdgeTests
{
    // 与 ComposedDocumentRenderer 的版式常数一致：A4，左右边距各 9%。
    private const double PageWidthPoints = 595d;
    private const double RightMarginEdgePoints = PageWidthPoints * (1d - 0.09d);

    /// <summary>字形外框的浮点容差（pt）。</summary>
    private const double Tolerance = 0.5d;

    private readonly ITestOutputHelper _output;
    private readonly IPdfStamper _stamper = new PdfSharpPdfStamper(NullLogger<PdfSharpPdfStamper>.Instance);

    public ComposedDocumentPageEdgeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // 法律文书里常见的全大写条款：大写字母比小写宽约三成，按小写平均字宽估出来的行一定越界。
    private const string AllCapsClause =
        "TO THE MAXIMUM EXTENT PERMITTED BY APPLICABLE LAW, IN NO EVENT SHALL EITHER PARTY BE LIABLE FOR ANY " +
        "INDIRECT, INCIDENTAL, SPECIAL, CONSEQUENTIAL OR PUNITIVE DAMAGES, OR ANY LOSS OF PROFITS OR REVENUES, " +
        "WHETHER INCURRED DIRECTLY OR INDIRECTLY, OR ANY LOSS OF DATA, USE, GOODWILL, OR OTHER INTANGIBLE LOSSES.";

    // 粗体 16 pt 的标题，比一行长：此前标题根本不换行。
    private const string LongTitle = "Engagement Letter and Fee Agreement for Litigation Services (Plaintiff v. Defendant)";
    private const string AllCapsTitle = "MASTER SERVICES AGREEMENT AND STATEMENT OF WORK FOR WAREHOUSE OPERATIONS AND MAINTENANCE";

    // 8 pt 的字段标签，比一行长（标签上限 200 字）。
    private const string LongLabel =
        "CLIENT SIGNATURE OF THE AUTHORISED SIGNATORY FOR AND ON BEHALF OF THE CLIENT ENTITY NAMED ABOVE, " +
        "WHO CONFIRMS THE AUTHORITY TO BIND THE CLIENT TO THIS AGREEMENT";

    /// <summary>
    /// 每一种会越界的形态单独一例，这样任何一处退回去按字符数估都会让它对应的那一例变红。
    /// </summary>
    [Theory]
    [InlineData("title")]
    [InlineData("all-caps-title")]
    [InlineData("all-caps-body")]
    [InlineData("unbreakable-wide-word")]
    [InlineData("field-label")]
    public void Render_EveryGlyphStaysInsideTheRightMargin(string shape)
    {
        if (!FontAvailableOrSkip())
            return;

        var (title, body, label, expected) = shape switch
        {
            "title" => (LongTitle, "First clause.", "Signature", LongTitle),
            "all-caps-title" => (AllCapsTitle, "First clause.", "Signature", AllCapsTitle),
            "all-caps-body" => ("Agreement", AllCapsClause, "Signature", AllCapsClause),
            "unbreakable-wide-word" => ("Agreement", new string('W', 160), "Signature", new string('W', 160)),
            "field-label" => ("Agreement", "First clause.", LongLabel, LongLabel),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };

        var renderer = new ComposedDocumentRenderer(_stamper);
        var result = renderer.Render(
            title,
            body + "\n\n[[client_signature]]",
            new Dictionary<string, object?>(),
            [Field("client_signature", label)]);

        using var document = PdfDocument.Open(result.Pdf);
        var letters = document.GetPages().SelectMany(page => page.Letters).ToList();
        letters.ShouldNotBeEmpty();

        var rightmost = letters.Max(letter => Math.Max(letter.BoundingBox.Right, letter.EndBaseLine.X));
        _output.WriteLine($"Rightmost glyph edge: {rightmost:F1} pt (right margin at {RightMarginEdgePoints:F1} pt, page edge at {PageWidthPoints:F1} pt).");

        rightmost.ShouldBeLessThanOrEqualTo(RightMarginEdgePoints + Tolerance);

        // 文字一个都不能少：拆行只换行不丢字。
        var drawn = string.Concat(letters.Select(letter => letter.Value)).Replace(" ", string.Empty);
        drawn.ShouldContain(expected.Replace(" ", string.Empty));
    }

    private bool FontAvailableOrSkip()
    {
        try
        {
            _stamper.Create(new PdfStampRequest
            {
                AppendPages = [new PdfPageSpec()],
                Stamps = [new PdfTextStamp { PageNumber = 1, Rect = new NormalizedRect(0.1d, 0.1d, 0.5d, 0.05d), Text = "probe" }],
            });
            return true;
        }
        catch (PdfDocumentException)
        {
            _output.WriteLine("Skipped: no system sans font is available on this machine.");
            return false;
        }
    }

    private static SnapshotField Field(string key, string label)
        => new()
        {
            Key = key,
            Label = label,
            Type = SigningFieldType.Signature,
            RecipientRole = "Client",
            Required = true,
            PlacementMode = FieldPlacementMode.Anchor,
            AnchorText = "somewhere",
        };
}
