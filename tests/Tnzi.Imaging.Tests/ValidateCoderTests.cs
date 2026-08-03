
namespace Tnzi.Imaging.Tests;

public class ValidateCoderTests
{
    private readonly ValidateCoder _coder;

    public ValidateCoderTests()
    {
        _coder = new ValidateCoder();
    }

    // ==================== GetCode Tests ====================

    [Fact]
    public void GetCode_Number_ReturnsCorrectLength()
    {
        var code = _coder.GetCode(6, ValidateCodeType.Number);
        code.Length.ShouldBe(6);
    }

    [Fact]
    public void GetCode_Number_ContainsOnlyDigits()
    {
        var code = _coder.GetCode(10, ValidateCodeType.Number);
        code.ShouldAllBe(c => char.IsDigit(c));
    }

    [Fact]
    public void GetCode_NumberAndLetter_ReturnsCorrectLength()
    {
        var code = _coder.GetCode(8, ValidateCodeType.NumberAndLetter);
        code.Length.ShouldBe(8);
    }

    [Fact]
    public void GetCode_NumberAndLetter_ContainsAlphanumeric()
    {
        var code = _coder.GetCode(8, ValidateCodeType.NumberAndLetter);
        code.ShouldAllBe(c => char.IsLetterOrDigit(c));
    }

    [Fact]
    public void GetCode_NumberAndLetter_ExcludesConfusingChars()
    {
        // Generate many codes to test exclusion
        var confusingChars = new[] { '0', '1', 'I', 'O', 'l' };
        for (int i = 0; i < 100; i++)
        {
            var code = _coder.GetCode(20, ValidateCodeType.NumberAndLetter);
            code.ShouldNotContain("I");
            code.ShouldNotContain("O");
            code.ShouldNotContain("l");
        }
    }

    [Fact]
    public void GetCode_Hanzi_ReturnsCorrectLength()
    {
        var code = _coder.GetCode(4, ValidateCodeType.Hanzi);
        code.Length.ShouldBe(4);
    }

    [Fact]
    public void GetCode_Hanzi_ContainsChinese()
    {
        var code = _coder.GetCode(4, ValidateCodeType.Hanzi);
        code.ShouldAllBe(c => c >= '\u4e00' && c <= '\u9fa5');
    }

    [Fact]
    public void GetCode_ZeroLength_Throws()
    {
        Should.Throw<ArgumentException>(() => _coder.GetCode(0));
    }

    // ==================== CreateImageBytes Tests ====================

    [Fact]
    public void CreateImageBytes_ReturnsNonEmptyBytes()
    {
        var bytes = _coder.CreateImageBytes("1234", ValidateCodeType.Number);
        bytes.ShouldNotBeEmpty();
    }

    [Fact]
    public void CreateImageBytes_ReturnsPngFormat()
    {
        var bytes = _coder.CreateImageBytes("AB", ValidateCodeType.NumberAndLetter);
        // PNG magic bytes: 0x89 0x50 0x4E 0x47
        bytes[0].ShouldBe((byte)0x89);
        bytes[1].ShouldBe((byte)0x50);
        bytes[2].ShouldBe((byte)0x4E);
        bytes[3].ShouldBe((byte)0x47);
    }

    [Fact]
    public void CreateImageBytes_WithLength_OutputsCode()
    {
        var bytes = _coder.CreateImageBytes(4, out var code);
        bytes.ShouldNotBeEmpty();
        code.Length.ShouldBe(4);
    }

    [Fact]
    public void CreateImageBytes_EmptyCode_Throws()
    {
        Should.Throw<ArgumentException>(() => _coder.CreateImageBytes("", ValidateCodeType.Number));
    }

    [Fact]
    public void CreateImage_CentersGlyphsVertically_NotClippedAtBottom()
    {
        // Regression: the glyph top used to be pinned at y = FontSize, pushing a
        // FontSize-tall glyph past the (1.5 * FontSize) image height and clipping
        // its bottom half. Glyphs must now be vertically centred and fully inside.
        using var image = _coder.CreateImage("8888", ValidateCodeType.Number);
        int h = image.Height;
        int w = image.Width;
        var bg = _coder.BgColor.ToPixel<Rgba32>();
        int bgSum = bg.R + bg.G + bg.B;

        long inkTotal = 0;
        double weightedY = 0;
        long bottomRowInk = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < w; x++)
                {
                    var p = row[x];
                    // "ink" = noticeably darker than the light background.
                    if (p.R + p.G + p.B < bgSum - 150)
                    {
                        inkTotal++;
                        weightedY += y;
                        if (y == h - 1) bottomRowInk++;
                    }
                }
            }
        });

        inkTotal.ShouldBeGreaterThan(0);                 // the text was actually drawn
        (weightedY / inkTotal).ShouldBeInRange(h * 0.25, h * 0.75); // centred, not sunk to the bottom
        bottomRowInk.ShouldBe(0);                        // not clipped against the bottom edge
    }

    // ==================== Default Property Values Tests ====================

    [Fact]
    public void DefaultProperties_AreCorrect()
    {
        var coder = new ValidateCoder();
        coder.FontSize.ShouldBe(20);
        coder.FontWidth.ShouldBe(20);
        coder.HasBorder.ShouldBeFalse();
        coder.RandomPosition.ShouldBeFalse();
        coder.RandomColor.ShouldBeFalse();
        coder.RandomItalic.ShouldBeFalse();
        coder.RandomPointPercent.ShouldBe(0);
        coder.RandomLineCount.ShouldBe(0);
    }

    [Fact]
    public void InitProperties_CanBeSet()
    {
        var coder = new ValidateCoder
        {
            FontSize = 30,
            FontWidth = 25,
            Height = 50,
            HasBorder = true,
            RandomColor = true,
            RandomItalic = true,
            RandomPointPercent = 5,
            RandomLineCount = 3
        };

        coder.FontSize.ShouldBe(30);
        coder.FontWidth.ShouldBe(25);
        coder.Height.ShouldBe(50);
        coder.HasBorder.ShouldBeTrue();
        coder.RandomColor.ShouldBeTrue();
        coder.RandomItalic.ShouldBeTrue();
        coder.RandomPointPercent.ShouldBe(5);
        coder.RandomLineCount.ShouldBe(3);
    }

    // ==================== CreateImage Tests ====================

    [Fact]
    public void CreateImage_ReturnsImageWithCorrectDimensions()
    {
        var coder = new ValidateCoder { FontWidth = 20, Height = 40 };
        using var image = coder.CreateImage("ABCD", ValidateCodeType.NumberAndLetter);
        image.Width.ShouldBe(20 * 4 + 20); // FontWidth * code.Length + FontWidth
        image.Height.ShouldBe(40);
    }

    [Fact]
    public void CreateImage_AutoHeightCalculation()
    {
        var coder = new ValidateCoder { FontSize = 24, FontWidth = 24 };
        using var image = coder.CreateImage("AB", ValidateCodeType.NumberAndLetter);
        // Height = FontSize + FontSize / 2 = 24 + 12 = 36
        image.Height.ShouldBe(36);
    }

    [Fact]
    public void CreateImage_WithFeatures_DoesNotThrow()
    {
        var coder = new ValidateCoder
        {
            HasBorder = true,
            RandomColor = true,
            RandomItalic = true,
            RandomPointPercent = 5,
            RandomLineCount = 3
        };

        using var image = coder.CreateImage("1234", ValidateCodeType.Number);
        image.ShouldNotBeNull();
    }
}
