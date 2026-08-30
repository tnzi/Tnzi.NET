namespace Tnzi.Imaging.Tests;

/// <summary>
/// 服务端图像编辑。
/// </summary>
/// <remarks>
/// 这个能力存在的前提是「原件不许离开受控环境」，因此这里的用例大多不在验证画得好不好看，
/// 而在验证<b>该没的东西真的没了</b>，以及<b>坐标搞错时会响</b>。
/// </remarks>
public class ImageEditorTests
{
    private readonly ImageEditor _editor = new();

    /// <summary>造一张左半红、右半蓝的图，好按颜色断言哪块被动过。</summary>
    private static byte[] CreateTwoToneImage(int width = 100, int height = 100)
    {
        using var image = new Image<Rgba32>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < width; x++)
                {
                    row[x] = x < width / 2 ? new Rgba32(255, 0, 0) : new Rgba32(0, 0, 255);
                }
            }
        });

        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    private static Rgba32 PixelAt(byte[] png, int x, int y)
    {
        using var image = Image.Load<Rgba32>(png);
        return image[x, y];
    }

    [Theory]
    [InlineData("photo.jpg", true)]
    [InlineData("scan.PNG", true)]
    [InlineData("clip.webp", true)]
    [InlineData("report.pdf", false)]
    [InlineData("clip.mp4", false)]
    [InlineData("", false)]
    public void CanEdit_AnswersByExtension(string fileName, bool expected)
    {
        // 界面要在展示「编辑」入口之前就知道答案，而不是等用户点下去拿到一个失败。
        Assert.Equal(expected, _editor.CanEdit(fileName));
    }

    [Fact]
    public async Task EditAsync_RedactionOverwritesPixels()
    {
        var source = CreateTwoToneImage();

        var result = await _editor.EditAsync(source, "a.png", new ImageEditRequest
        {
            // 左上四分之一：整块落在红色区域内。
            Redactions = [new NormalizedRect(0, 0, 0.25, 0.25)],
            OutputFormat = ImageOutputFormat.Png
        });

        // 遮挡块内是填充色。
        Assert.Equal(new Rgba32(0, 0, 0), PixelAt(result.Content, 5, 5));
        // 块外一个字节没动 —— 遮挡不该顺手改别处。
        Assert.Equal(new Rgba32(255, 0, 0), PixelAt(result.Content, 40, 40));
        Assert.Equal(new Rgba32(0, 0, 255), PixelAt(result.Content, 80, 80));
    }

    [Fact]
    public async Task EditAsync_RedactionColorIsConfigurable()
    {
        var source = CreateTwoToneImage();

        var result = await _editor.EditAsync(source, "a.png", new ImageEditRequest
        {
            Redactions = [new NormalizedRect(0, 0, 0.25, 0.25)],
            RedactionColor = "#00FF00",
            OutputFormat = ImageOutputFormat.Png
        });

        Assert.Equal(new Rgba32(0, 255, 0), PixelAt(result.Content, 5, 5));
    }

    [Fact]
    public async Task EditAsync_RejectsAnInvalidRedactionColor()
    {
        var source = CreateTwoToneImage();

        await Assert.ThrowsAsync<ArgumentException>(() => _editor.EditAsync(source, "a.png", new ImageEditRequest
        {
            Redactions = [new NormalizedRect(0, 0, 0.25, 0.25)],
            RedactionColor = "not-a-color"
        }));
    }

    [Fact]
    public async Task EditAsync_CropUsesSourceRelativeCoordinates()
    {
        var source = CreateTwoToneImage(100, 100);

        var result = await _editor.EditAsync(source, "a.png", new ImageEditRequest
        {
            Crop = new NormalizedRect(0.5, 0, 0.5, 1),
            OutputFormat = ImageOutputFormat.Png
        });

        Assert.Equal(50, result.Width);
        Assert.Equal(100, result.Height);
        // 裁到的是右半边，整块蓝。
        Assert.Equal(new Rgba32(0, 0, 255), PixelAt(result.Content, 10, 10));
    }

    [Fact]
    public async Task EditAsync_RedactionCoordinatesStayRelativeToTheSourceEvenWhenCropping()
    {
        // ★ 这条钉住的是本接口最容易写错的地方：调用方在<b>源图</b>上框选，
        //   不该为了配合裁切自己换算一遍坐标。
        var source = CreateTwoToneImage(100, 100);

        var result = await _editor.EditAsync(source, "a.png", new ImageEditRequest
        {
            // 遮挡右上角（蓝色区域内），同时只保留右半边。
            Redactions = [new NormalizedRect(0.6, 0, 0.2, 0.2)],
            Crop = new NormalizedRect(0.5, 0, 0.5, 1),
            OutputFormat = ImageOutputFormat.Png
        });

        // 裁后坐标 (15, 5) 对应源图 (65, 5)，正落在遮挡块内。
        Assert.Equal(new Rgba32(0, 0, 0), PixelAt(result.Content, 15, 5));
        // 裁后 (45, 5) 对应源图 (95, 5)，在遮挡块外，仍是蓝色。
        Assert.Equal(new Rgba32(0, 0, 255), PixelAt(result.Content, 45, 5));
    }

    [Fact]
    public async Task EditAsync_ResizeOnlyShrinks()
    {
        var source = CreateTwoToneImage(100, 100);

        var shrunk = await _editor.EditAsync(source, "a.png", new ImageEditRequest { MaxWidth = 40, MaxHeight = 40 });
        Assert.Equal(40, shrunk.Width);

        // 上界大于原图时不放大：放大只会得到一张更大的糊图。
        var untouched = await _editor.EditAsync(source, "a.png", new ImageEditRequest { MaxWidth = 500, MaxHeight = 500 });
        Assert.Equal(100, untouched.Width);
    }

    [Fact]
    public async Task EditAsync_BlurChangesPixelsWithoutChangingSize()
    {
        var source = CreateTwoToneImage();

        var result = await _editor.EditAsync(source, "a.png", new ImageEditRequest
        {
            // 跨过红蓝分界，模糊后交界处必然混色。
            BlurRegions = [new NormalizedRect(0.4, 0.4, 0.2, 0.2)],
            OutputFormat = ImageOutputFormat.Png
        });

        Assert.Equal(100, result.Width);

        var blurred = PixelAt(result.Content, 50, 50);
        Assert.NotEqual(new Rgba32(255, 0, 0), blurred);
        Assert.NotEqual(new Rgba32(0, 0, 255), blurred);
    }

    [Fact]
    public async Task EditAsync_BlursTinyRegionsThatCannotHoldAKernel()
    {
        // ★ 卷积核不能比选区大，否则 ImageSharp 当场抛越界。而「选区太小就什么都不做」
        //   是最坏的结果：调用方以为糊上了，实际原样。小选区走均色平涂。
        var source = CreateTwoToneImage(100, 100);

        var result = await _editor.EditAsync(source, "a.png", new ImageEditRequest
        {
            // 3×3 像素，跨在红蓝分界上。
            BlurRegions = [new NormalizedRect(0.49, 0.49, 0.03, 0.03)],
            BlurSigma = 12f,
            OutputFormat = ImageOutputFormat.Png
        });

        var pixel = PixelAt(result.Content, 50, 50);
        Assert.NotEqual(new Rgba32(255, 0, 0), pixel);
        Assert.NotEqual(new Rgba32(0, 0, 255), pixel);
    }

    [Theory]
    [InlineData(-0.1, 0, 0.2, 0.2)]
    [InlineData(0, 0, 1.5, 0.2)]
    [InlineData(0.9, 0.9, 0.2, 0.2)]
    [InlineData(0, 0, 0, 0.2)]
    public async Task EditAsync_RejectsOutOfRangeCoordinates(double x, double y, double w, double h)
    {
        // ★ 越界几乎总是坐标系搞错了（拿像素当归一化）。安静地裁到边界，
        //   表现就是「遮挡画在了图外」—— 一张没遮干净的图比一个异常危险得多。
        var source = CreateTwoToneImage();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _editor.EditAsync(source, "a.png", new ImageEditRequest
        {
            Redactions = [new NormalizedRect(x, y, w, h)]
        }));
    }

    [Fact]
    public async Task EditAsync_RejectsUnsupportedFormats()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => _editor.EditAsync([1, 2, 3], "clip.mp4", new ImageEditRequest()));
    }

    [Fact]
    public async Task EditAsync_KeepsTheSourceFormatUnlessAskedOtherwise()
    {
        var source = CreateTwoToneImage();

        var kept = await _editor.EditAsync(source, "a.png", new ImageEditRequest { MaxWidth = 50 });
        Assert.Equal(ImageOutputFormat.Png, kept.Format);
        Assert.Equal("image/png", kept.ContentType);

        var converted = await _editor.EditAsync(source, "a.png", new ImageEditRequest
        {
            MaxWidth = 50,
            OutputFormat = ImageOutputFormat.Jpeg
        });
        Assert.Equal(ImageOutputFormat.Jpeg, converted.Format);
        Assert.Equal(".jpg", converted.Extension);
    }

    [Fact]
    public async Task EditAsync_DoesNotTouchTheSourceBytes()
    {
        // 产物往哪存是调用方的决定，源图必须原样可用 —— 原件不动是这条链路的前提。
        var source = CreateTwoToneImage();
        var before = (byte[])source.Clone();

        await _editor.EditAsync(source, "a.png", new ImageEditRequest
        {
            Redactions = [new NormalizedRect(0, 0, 0.5, 0.5)]
        });

        Assert.Equal(before, source);
    }
}
