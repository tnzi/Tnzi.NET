using Xunit.Abstractions;

namespace Tnzi.Documents.Tests;

/// <summary>
/// <see cref="PdfSharpPdfStamper"/> 的追加页、图片盖章与文本落位。
/// </summary>
public class PdfSharpPdfStamperTests
{
    private readonly ITestOutputHelper _output;
    private readonly IPdfStamper _stamper = new PdfSharpPdfStamper(NullLogger<PdfSharpPdfStamper>.Instance);
    private readonly IPdfInspector _inspector = new PdfPigPdfInspector();

    public PdfSharpPdfStamperTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Stamp_AppendedPage_InheritsTheLastPageSizeByDefault()
    {
        var pdf = TestPdfBuilder.Letter(new TestPdfBuilder.TextRun("hello", 72d, 700d));

        var stamped = _stamper.Stamp(pdf, new PdfStampRequest { AppendPages = [new PdfPageSpec()] });

        var info = _inspector.GetInfo(stamped);
        info.PageCount.ShouldBe(2);
        info.Pages[1].Width.ShouldBe(TestPdfBuilder.LetterWidth, 0.01d);
        info.Pages[1].Height.ShouldBe(TestPdfBuilder.LetterHeight, 0.01d);
    }

    [Fact]
    public void Stamp_AppendedPage_HonoursAnExplicitSize()
    {
        var pdf = TestPdfBuilder.Letter(new TestPdfBuilder.TextRun("hello", 72d, 700d));

        var stamped = _stamper.Stamp(pdf, new PdfStampRequest
        {
            AppendPages = [new PdfPageSpec { WidthPoints = 595d, HeightPoints = 842d }]
        });

        var info = _inspector.GetInfo(stamped);
        info.Pages[1].Width.ShouldBe(595d, 0.01d);
        info.Pages[1].Height.ShouldBe(842d, 0.01d);
    }

    [Fact]
    public void Stamp_DrawsAnImage_WithoutNeedingASystemFont()
    {
        // 图片盖章不碰字体解析器：精简运行时镜像（无系统字体）里签名图仍然能盖上
        var pdf = TestPdfBuilder.Letter(new TestPdfBuilder.TextRun("sign here", 72d, 700d));

        var stamped = _stamper.Stamp(pdf, new PdfStampRequest
        {
            Stamps =
            [
                new PdfImageStamp
                {
                    PageNumber = 1,
                    Rect = new NormalizedRect(0.1d, 0.8d, 0.2d, 0.05d),
                    Content = TestImages.Png(16, 8)
                }
            ]
        });

        stamped.Length.ShouldBeGreaterThan(pdf.Length);
        _inspector.GetInfo(stamped).PageCount.ShouldBe(1);
    }

    [Fact]
    public void Stamp_AcceptsAnImageGivenAsADataUrl()
    {
        var pdf = TestPdfBuilder.Letter(new TestPdfBuilder.TextRun("sign here", 72d, 700d));
        var dataUrl = "data:image/png;base64," + Convert.ToBase64String(TestImages.Png(16, 8));

        var stamped = _stamper.Stamp(pdf, new PdfStampRequest
        {
            Stamps =
            [
                new PdfImageStamp
                {
                    PageNumber = 1,
                    Rect = new NormalizedRect(0.1d, 0.8d, 0.2d, 0.05d),
                    DataUrl = dataUrl
                }
            ]
        });

        _inspector.GetInfo(stamped).PageCount.ShouldBe(1);
    }

    [Fact]
    public void Stamp_ImageWithoutAnyPayload_Throws()
    {
        var pdf = TestPdfBuilder.Letter(new TestPdfBuilder.TextRun("sign here", 72d, 700d));

        Should.Throw<PdfDocumentException>(() => _stamper.Stamp(pdf, new PdfStampRequest
        {
            Stamps = [new PdfImageStamp { PageNumber = 1, Rect = new NormalizedRect(0.1d, 0.8d, 0.2d, 0.05d) }]
        }));
    }

    [Fact]
    public void Stamp_PageNumberBeyondTheDocument_Throws()
    {
        var pdf = TestPdfBuilder.Letter(new TestPdfBuilder.TextRun("hello", 72d, 700d));

        Should.Throw<ArgumentOutOfRangeException>(() => _stamper.Stamp(pdf, new PdfStampRequest
        {
            Stamps = [new PdfTextStamp { PageNumber = 7, Text = "nope", Rect = new NormalizedRect(0.1d, 0.1d, 0.2d, 0.05d) }]
        }));
    }

    [Fact]
    public void Stamp_LeavesTheInputBytesUntouched()
    {
        var pdf = TestPdfBuilder.Letter(new TestPdfBuilder.TextRun("hello", 72d, 700d));
        var original = pdf.ToArray();

        _stamper.Stamp(pdf, new PdfStampRequest { AppendPages = [new PdfPageSpec()] });

        pdf.ShouldBe(original);
    }

    [Fact]
    public void Stamp_NotAPdf_Throws()
    {
        Should.Throw<PdfDocumentException>(() => _stamper.Stamp([1, 2, 3], new PdfStampRequest()));
    }

    [Fact]
    public void Stamp_TextLandsWhereTheNormalizedRectSaysItShould()
    {
        // 全链路验证「写不翻 Y」：盖上去 -> 读回来 -> 归一化坐标应当对得上。
        // 画文字要系统字体，无字体的环境（精简 CI 镜像）跳过而不是让整个套件变红。
        if (!DocumentFontResolver.EnsureReady())
        {
            _output.WriteLine("Skipped: no system sans font is available on this machine.");
            return;
        }

        var pdf = TestPdfBuilder.Letter(new TestPdfBuilder.TextRun("existing", 72d, 700d));
        var target = new NormalizedRect(0.2d, 0.30d, 0.4d, 0.04d);

        var stamped = _stamper.Stamp(pdf, new PdfStampRequest
        {
            Stamps =
            [
                new PdfTextStamp
                {
                    PageNumber = 1,
                    Rect = target,
                    Text = "STAMPED",
                    FontSize = 12d,
                    Alignment = PdfStampAlignment.TopLeft
                }
            ]
        });

        var match = _inspector.FindTags(stamped, "STAMPED").ShouldHaveSingleItem();

        match.Box.X.ShouldBe(target.X, 0.01d);
        // 一行文字画在框顶：容差覆盖字形上伸与行距，但绝不该翻到页面另一头（那会是 ~0.66）
        match.Box.Y.ShouldBe(target.Y, 0.04d);
    }

    [Fact]
    public void Stamp_TextAndImageOnAnAppendedPage_Works()
    {
        if (!DocumentFontResolver.EnsureReady())
        {
            _output.WriteLine("Skipped: no system sans font is available on this machine.");
            return;
        }

        // 「追加一页完成证书并往上写字」= 一次调用：追加页先发生，页码接在原有页之后
        var pdf = TestPdfBuilder.Letter(new TestPdfBuilder.TextRun("contract", 72d, 700d));

        var stamped = _stamper.Stamp(pdf, new PdfStampRequest
        {
            AppendPages = [new PdfPageSpec()],
            Stamps =
            [
                new PdfTextStamp
                {
                    PageNumber = 2,
                    Rect = new NormalizedRect(0.1d, 0.1d, 0.8d, 0.05d),
                    Text = "CERTIFICATE OF COMPLETION",
                    FontSize = 16d,
                    Bold = true,
                    Alignment = PdfStampAlignment.TopLeft
                },
                new PdfImageStamp
                {
                    PageNumber = 2,
                    Rect = new NormalizedRect(0.1d, 0.2d, 0.2d, 0.06d),
                    Content = TestImages.Png(24, 12)
                }
            ]
        });

        var info = _inspector.GetInfo(stamped);
        info.PageCount.ShouldBe(2);

        var match = _inspector.FindTags(stamped, "CERTIFICATE OF COMPLETION").ShouldHaveSingleItem();
        match.PageNumber.ShouldBe(2);
    }

    // ---------- Create（从零新建） ----------

    [Fact]
    public void Create_MakesADocumentWithTheRequestedPages()
    {
        // 完成证书、回执、封面这些页面不依附任何原件；没有 Create 就只能先造一个
        // 假原件再往上盖，那是把「从零新建」伪装成「盖章」。
        var pdf = _stamper.Create(new PdfStampRequest
        {
            AppendPages =
            [
                new PdfPageSpec { WidthPoints = 595d, HeightPoints = 842d },
                new PdfPageSpec { WidthPoints = 595d, HeightPoints = 842d },
            ],
        });

        var info = _inspector.GetInfo(pdf);
        info.PageCount.ShouldBe(2);
        info.Pages[0].Width.ShouldBe(595d, 0.01d);
        info.Pages[0].Height.ShouldBe(842d, 0.01d);
    }

    [Fact]
    public void Create_DefaultsToLetter_WhenNoSizeIsGiven()
    {
        // 没有"上一页"可参照时的回退，与 Stamp 那条 fallback 是同一段代码。
        var pdf = _stamper.Create(new PdfStampRequest { AppendPages = [new PdfPageSpec()] });

        var info = _inspector.GetInfo(pdf);
        info.PageCount.ShouldBe(1);
        info.Pages[0].Width.ShouldBe(612d, 0.01d);
        info.Pages[0].Height.ShouldBe(792d, 0.01d);
    }

    [Fact]
    public void Create_WithNoPages_Throws()
    {
        // 零页 PDF 不是一份文档;安静地返回一个打不开的文件比抛异常糟得多。
        Should.Throw<ArgumentException>(() => _stamper.Create(new PdfStampRequest()));
    }

    [Fact]
    public void Create_TextLandsWhereTheNormalizedRectSaysItShould()
    {
        if (!DocumentFontResolver.EnsureReady())
        {
            _output.WriteLine("Skipped: no system sans font is available on this machine.");
            return;
        }

        var target = new NormalizedRect(0.15d, 0.25d, 0.5d, 0.04d);
        var pdf = _stamper.Create(new PdfStampRequest
        {
            AppendPages = [new PdfPageSpec { WidthPoints = 595d, HeightPoints = 842d }],
            Stamps = [new PdfTextStamp { PageNumber = 1, Rect = target, Text = "CERTIFICATE", FontSize = 12d }],
        });

        var hit = _inspector.FindTags(pdf, "CERTIFICATE").FirstOrDefault();
        hit.ShouldNotBeNull();
        hit!.PageNumber.ShouldBe(1);
        // 「读要翻 Y、写不翻 Y」在新建这条路径上同样成立。
        hit.Box.Y.ShouldBe(target.Y, 0.03d);
        hit.Box.X.ShouldBe(target.X, 0.03d);
    }
}
