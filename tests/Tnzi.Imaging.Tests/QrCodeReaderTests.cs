using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using ZXing;

namespace Tnzi.Imaging.Tests;

/// <summary>
/// <see cref="QrCodeReader"/> 的行为。
/// </summary>
/// <remarks>
/// <para>
/// 这里的形变不是想象出来的，是纸质回路的常态：整页扫描、几度歪斜、倒着放进扫描仪、
/// 传真那点可怜的采样率。每一条对应一种真实的失效方式。
/// </para>
/// <para>
/// ★ 断言用的图一律是<b>当场生成再劣化</b>的，不放二进制夹具：
/// 夹具会把「当初那一版编码器的输出」冻起来，而这些测试要守的是往返闭合本身。
/// </para>
/// </remarks>
public class QrCodeReaderTests
{
    private const string Payload = "TNZI-CORR-000123";

    private static readonly QrCodeGenerator Generator = new();
    private static readonly QrCodeReader Reader = new();

    private static Image<L8> Code(int pixelsPerModule = 10)
    {
        var image = Generator.Generate(new QrCodeRequest
        {
            Payload = Payload,
            ErrorCorrection = QrErrorCorrection.Quartile,
            MinVersion = 1,
            MaxVersion = 1,
            PixelsPerModule = pixelsPerModule
        });

        return Image.Load<L8>(image.Content);
    }

    /// <summary>
    /// 存成<b>真正的 8 位灰度</b> PNG。
    /// </summary>
    /// <remarks>
    /// ★ 必须显式指定编码参数：ImageSharp 的 PNG 编码器会按内容自动挑颜色类型，
    /// 一张 57×57 的带光照渐变灰度图会被它压成 343 字节的调色板图 ——
    /// 灰阶被量化掉之后，本来能救回来的码就再也读不出来了。
    /// 这不影响生产代码（生成侧用编码库自带的 1 位 PNG 写出，光栅化侧用 PDFium 的编码器），
    /// 但会让这里的夹具悄悄变成一张不是我们以为的那张图。
    /// </remarks>
    private static byte[] ToPng(Image<L8> image)
    {
        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder { ColorType = PngColorType.Grayscale, BitDepth = PngBitDepth.Bit8 });
        return stream.ToArray();
    }

    /// <summary>旋转后把透明的边角压到白底上：不这么做边角会变成黑色，凭空造出一圈墨。</summary>
    private static Image<L8> Rotate(Image<L8> source, float degrees)
    {
        using var rgba = source.CloneAs<Rgba32>();
        rgba.Mutate(x => x.Rotate(degrees).BackgroundColor(Color.White));
        return rgba.CloneAs<L8>();
    }

    [Fact]
    public async Task ReadAsync_RoundTripsAFreshlyGeneratedCode()
    {
        using var code = Code();

        var result = await Reader.ReadAsync(ToPng(code));

        result.Found.ShouldBeTrue();
        result.Payload.ShouldBe(Payload);
    }

    [Fact]
    public async Task ReadAsync_ReadsACodeThatWentInUpsideDown()
    {
        using var code = Code();
        using var upsideDown = Rotate(code, 180f);

        var result = await Reader.ReadAsync(ToPng(upsideDown));

        result.Payload.ShouldBe(Payload);
    }

    [Theory]
    [InlineData(2f)]
    [InlineData(-2.5f)]
    public async Task ReadAsync_ReadsACodeSkewedByACoupleOfDegrees(float degrees)
    {
        using var code = Code();
        using var skewed = Rotate(code, degrees);

        var result = await Reader.ReadAsync(ToPng(skewed));

        result.Payload.ShouldBe(Payload);
    }

    [Theory]
    [InlineData(2.39)]
    [InlineData(2.93)]
    [InlineData(3.33)]
    public async Task ReadAsync_ReadsACodeSampledAtTwoOrThreePixelsPerModule(double pixelsPerModule)
    {
        // 走一遍简化的传真链路：印大 -> 歪一点 -> 模糊 -> 按目标采样率缩小 -> 二值化。
        using var printed = Code(pixelsPerModule: 12);
        using var skewed = Rotate(printed, 1.5f);

        var totalModules = skewed.Width / 12;
        var scanned = (int)Math.Round(totalModules * pixelsPerModule);

        using var faxed = skewed.Clone(x => x
            .GaussianBlur(1.2f)
            .Resize(scanned, scanned, KnownResamplers.Box)
            .BinaryThreshold(0.5f));

        var result = await Reader.ReadAsync(ToPng(faxed));

        result.Payload.ShouldBe(Payload, $"每模块 {pixelsPerModule} 个扫描像素是传真回传的常态");
    }

    [Fact]
    public async Task ReadAsync_FindsACodeSittingInTheCornerOfAWholePage()
    {
        // 真实输入是一整页，不是裁好的码。
        using var code = Code(pixelsPerModule: 8);
        using var page = new Image<L8>(1700, 2200, new L8(255));

        using (var stamped = code.CloneAs<Rgba32>())
        {
            page.Mutate(x => x.DrawImage(stamped.CloneAs<L8>(), new Point(1400, 120), 1f));
        }

        var result = await Reader.ReadAsync(ToPng(page));

        result.Payload.ShouldBe(Payload);
    }

    [Fact]
    public async Task ReadAsync_OnAPageWithNoCode_ReportsNotFoundInsteadOfThrowing()
    {
        // ★ 一张没有码的纸是正常输入（回错了页、码被折住了），不是故障。
        using var blank = new Image<L8>(600, 800, new L8(255));

        var result = await Reader.ReadAsync(ToPng(blank));

        result.Found.ShouldBeFalse();
        result.Payload.ShouldBeNull();
        result.ShouldBe(QrCodeScanResult.NotFound);
    }

    [Fact]
    public async Task ReadAsync_OnBytesThatAreNotAnImage_Throws()
    {
        // 与「没找到」刻意区分开：这一条是调用方给错了东西。
        await Should.ThrowAsync<ArgumentException>(
            () => Reader.ReadAsync("this is not an image"u8.ToArray()));
    }

    [Fact]
    public async Task ReadAsync_WithoutBytes_Throws()
        => await Should.ThrowAsync<ArgumentException>(() => Reader.ReadAsync([]));

    [Fact]
    public async Task ReadAsync_OnAnOversizedImage_RefusesBeforeDecodingIt()
    {
        // ★ 输入是外来的（对方扫描或拍照回传的），而尺寸写在文件头里、与文件大小无关：
        // 一张几百字节的 PNG 可以声称自己 60000×60000。这里用一张真的超宽但压得极小的图，
        // 断言它在解成像素之前就被拒掉。
        using var oversized = new Image<L8>(10_001, 8, new L8(255));
        var bytes = ToPng(oversized);

        bytes.Length.ShouldBeLessThan(5_000, "这张图本身很小 —— 危险的是它声明的尺寸，不是它的体积");

        var ex = await Should.ThrowAsync<ArgumentException>(() => Reader.ReadAsync(bytes));
        ex.Message.ShouldContain("10000");
    }

    [Fact]
    public void PureBarcodeHint_IsNeverSet()
    {
        // ★★★ ZXing 用 ContainsKey 读这个字典、**从不看值**，所以只要这个键在里面，
        // 无论值是 true 还是 false，纯码模式都是开的。
        // 这条断言的是「这个键不在里面」，不是「它的值是 false」——
        // 下一条证明这个区别是有后果的。
        var reader = QrCodeReader.CreateReader();

        reader.Options.Hints.ShouldNotContainKey(DecodeHintType.PURE_BARCODE);
    }

    [Fact]
    public async Task PureBarcodeHintWithAFalseValue_BreaksSkewedCodes()
    {
        // ★ 这条测的是 ZXing 的行为而不是本框架的代码，存在的理由是：
        // 上一条守的不变量只有在这个陷阱确实存在时才值钱。哪天 ZXing 改了语义，
        // 这里会红，那时上一条的注释也该跟着改 —— 而不是让一条没有理由的断言留在仓库里。
        using var code = Code();
        using var skewed = Rotate(code, 2f);

        var luminances = new byte[skewed.Width * skewed.Height];
        skewed.CopyPixelDataTo(luminances);

        var trapped = QrCodeReader.CreateReader();
        trapped.Options.Hints[DecodeHintType.PURE_BARCODE] = false;

        trapped.Decode(new BitmapLuminanceSource(luminances, skewed.Width, skewed.Height))
            .ShouldBeNull("值是 false，纯码模式却是开的 —— 这正是那个陷阱");

        // 同一张图，本框架的读取器读得出来。
        (await Reader.ReadAsync(ToPng(skewed))).Payload.ShouldBe(Payload);
    }

    [Fact]
    public void Reader_IsConfiguredForRotatedAndInvertedCodes()
    {
        // 倒置与反相不靠单独的重试级，靠这两个开关 —— 关掉了不会有测试以外的地方报错。
        var reader = QrCodeReader.CreateReader();

        reader.AutoRotate.ShouldBeTrue();
        reader.Options.TryInverted.ShouldBeTrue();
        reader.Options.TryHarder.ShouldBeTrue();
        reader.Options.PossibleFormats.ShouldBe([BarcodeFormat.QR_CODE]);
    }

    [Fact]
    public async Task Upscaling_RescuesACodeThatASinglePassMisses()
    {
        // ★ 这条同时守两件事：放大那一级**在**，而且它**在救东西**。
        // 只断言「整体读得出来」区分不了「有这一级」和「第一级本来就够」——
        // 那样删掉它测试照样绿，而它就成了没人敢动也没人知道还需不需要的死代码。
        // 参数取自实测：整页拍照（光照渐变）+ 1.5 度歪斜 + 模糊 + 每模块 1.9 个采样像素。
        using var hard = PhotographedAtLowResolution();

        QrCodeReader.DecodeSinglePass(hard).ShouldBeNull(
            "这个夹具必须是第一级读不出来的，否则本测试退化成恒真");

        var result = await Reader.ReadAsync(ToPng(hard));

        result.Payload.ShouldBe(Payload);
    }

    /// <summary>造一张「用手机拍下来的整页」：光照渐变 + 歪斜 + 模糊 + 粗采样。</summary>
    /// <remarks>光照渐变而非二值化，正是 <c>GlobalHistogramBinarizer</c> 顶不住的那一类。</remarks>
    private static Image<L8> PhotographedAtLowResolution()
    {
        const int printedPixelsPerModule = 12;
        const double scanPixelsPerModule = 1.9;

        using var printed = Code(printedPixelsPerModule);
        using var skewed = Rotate(printed, 1.5f);

        var scanned = (int)Math.Round(skewed.Width * scanPixelsPerModule / printedPixelsPerModule);
        var photo = skewed.Clone(x => x
            .GaussianBlur(1.5f)
            .Resize(scanned, scanned, KnownResamplers.Box));

        photo.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var falloff = 0.35 + (0.65 * (1.0 - ((double)(x + y) / (rows.Height * 2))));
                    row[x] = new L8((byte)Math.Clamp(row[x].PackedValue * falloff, 0, 255));
                }
            }
        });

        return photo;
    }
}
