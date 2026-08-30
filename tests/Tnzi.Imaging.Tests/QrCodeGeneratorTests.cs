namespace Tnzi.Imaging.Tests;

/// <summary>
/// <see cref="QrCodeGenerator"/> 的行为。
/// </summary>
/// <remarks>
/// 重点在两条几乎不会以异常形式暴露的性质：<b>版本没有被悄悄放大</b>，
/// 以及<b>每模块像素数是整数</b>。两者出错的症状都不是报错，
/// 而是几个月后发现有一批印出去的纸读不回来。
/// </remarks>
public class QrCodeGeneratorTests
{
    private static readonly QrCodeGenerator Generator = new();

    private static QrCodeImage Generate(string payload, Action<QrCodeRequest>? configure = null)
    {
        var request = new QrCodeRequest { Payload = payload };
        configure?.Invoke(request);
        return Generator.Generate(request);
    }

    /// <summary>PNG 的 IHDR 恒在字节 16-23（大端），拿真实像素宽高对账声明的几何。</summary>
    private static (int Width, int Height) PngSize(byte[] png) =>
        (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)),
         System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));

    [Fact]
    public void Generate_ProducesAPngWhoseRealPixelsMatchTheDeclaredGeometry()
    {
        var code = Generate("TNZI-CORR-000123", r => r.PixelsPerModule = 6);

        var (width, height) = PngSize(code.Content);

        width.ShouldBe(code.Width);
        height.ShouldBe(code.Height);
        code.Width.ShouldBe((code.ModuleCount + (2 * code.QuietZoneModules)) * code.PixelsPerModule);
        code.ContentType.ShouldBe("image/png");
    }

    [Theory]
    [InlineData("A")]
    [InlineData("TNZI-CORR-000123")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123456789")]
    public void Generate_ReportsAVersionThatMatchesThePixelsItActuallyEmitted(string payload)
    {
        // 版本是调用方用来在版面上留方框的唯一依据。
        // ★ 断言必须落到**真实像素**上：`ModuleCount` 是由 `Version` 算出来的
        // （4 × 版本 + 17），拿它去对 `Version` 是一句恒真的废话。
        // 报出来的版本要是和编码器实际用的那个不是同一个，只有出图的宽度会暴露。
        const int pixelsPerModule = 5;
        var code = Generate(payload, r => r.PixelsPerModule = pixelsPerModule);

        code.Version.ShouldBeInRange(1, 40);

        var expectedWidth = ((4 * code.Version) + 17 + (2 * code.QuietZoneModules)) * pixelsPerModule;
        PngSize(code.Content).ShouldBe((expectedWidth, expectedWidth));
    }

    [Fact]
    public void Generate_PinnedToOneVersion_KeepsTheModuleCountStableAcrossPayloadLengths()
    {
        // 这正是 MinVersion/MaxVersion 存在的理由：版面上那个方框的尺寸可以写死。
        var shortCode = Generate("A", Pin1);
        var longCode = Generate("TNZI-CORR-000123", Pin1);

        shortCode.Version.ShouldBe(1);
        longCode.Version.ShouldBe(1);
        shortCode.ModuleCount.ShouldBe(longCode.ModuleCount);
        shortCode.Width.ShouldBe(longCode.Width);

        static void Pin1(QrCodeRequest r)
        {
            r.MinVersion = 1;
            r.MaxVersion = 1;
            r.ErrorCorrection = QrErrorCorrection.Quartile;
        }
    }

    [Fact]
    public void Generate_WhenThePayloadOverflowsTheVersionCeiling_ThrowsInsteadOfGrowingTheCode()
    {
        // ★ 本测试守的是「不静默升版本」。升版本会让模块变小，而排版早就按某个模块尺寸定好了。
        var tooLong = new string('X', 40);

        var ex = Should.Throw<ArgumentException>(() => Generate(tooLong, r =>
        {
            r.MaxVersion = 1;
            r.ErrorCorrection = QrErrorCorrection.Quartile;
        }));

        ex.Message.ShouldContain("version 1");
    }

    [Fact]
    public void Generate_WithoutAVersionCeiling_GrowsTheVersionForTheSamePayload()
    {
        // 上一条断言的是「拒绝」，这条证明拒绝是个**选择**而不是这个载荷本来就编不出来。
        var tooLongForVersion1 = new string('X', 40);

        var code = Generate(tooLongForVersion1, r => r.ErrorCorrection = QrErrorCorrection.Quartile);

        code.Version.ShouldBeGreaterThan(1);
    }

    // ★ 取值刻意覆盖「余数过半」的情形（305 / 29 = 10.52、320 / 29 = 11.03）：
    // 只用 300（10.34）的话，向下取整与四舍五入结果相同，这条测试就分辨不出两者 ——
    // 而四舍五入正是会让产物**超出**目标尺寸的那个写法。
    [Theory]
    [InlineData(87)]
    [InlineData(200)]
    [InlineData(300)]
    [InlineData(305)]
    [InlineData(320)]
    public void Generate_FromATargetSize_SnapsDownToAWholeNumberOfPixelsPerModule(int targetSize)
    {
        // 非整数倍会让模块宽度在 n 和 n+1 像素之间参差，而扫描端靠模块边界的规则性定位。
        var code = Generate("TNZI-CORR-000123", r =>
        {
            r.TargetSize = targetSize;
            r.MinVersion = 1;
            r.MaxVersion = 1;
            r.ErrorCorrection = QrErrorCorrection.Quartile;
        });

        var totalModules = code.ModuleCount + (2 * code.QuietZoneModules);

        code.Width.ShouldBeLessThanOrEqualTo(targetSize, "TargetSize 是上界，不是等号");
        (code.Width % totalModules).ShouldBe(0, "每个模块必须占整数个像素");
        // 而且是「装得下的最大」：再加一个像素每模块就会超出目标尺寸。
        ((code.PixelsPerModule + 1) * totalModules).ShouldBeGreaterThan(targetSize);
    }

    [Fact]
    public void Generate_PixelsPerModule_WinsOverTargetSize()
    {
        var code = Generate("A", r =>
        {
            r.PixelsPerModule = 11;
            r.TargetSize = 50;
        });

        code.PixelsPerModule.ShouldBe(11);
    }

    [Fact]
    public void Generate_BoostsTheErrorCorrectionWithoutGrowingTheVersion()
    {
        // 提级不改变模块数，所以在物理尺寸不变的前提下只多不少 ——
        // 与「手动把等级提到 H」是两回事，后者会顶高版本。
        var boosted = Generate("ABC123", r =>
        {
            r.ErrorCorrection = QrErrorCorrection.Low;
            r.BoostErrorCorrection = true;
        });
        var plain = Generate("ABC123", r =>
        {
            r.ErrorCorrection = QrErrorCorrection.Low;
            r.BoostErrorCorrection = false;
        });

        boosted.Version.ShouldBe(plain.Version);
        ((int)boosted.ErrorCorrection).ShouldBeGreaterThan((int)plain.ErrorCorrection);
        plain.ErrorCorrection.ShouldBe(QrErrorCorrection.Low);
    }

    [Fact]
    public void Generate_ManuallyRaisingToHigh_CanCostAVersion()
    {
        // 记录实测里那条反直觉的结论：更高的纠错等级换不回采样精度，
        // 因为它可能把载荷顶到下一个版本、模块随之变小。
        const string payload = "TNZI-CORR-0001";

        var quartile = Generate(payload, r =>
        {
            r.ErrorCorrection = QrErrorCorrection.Quartile;
            r.BoostErrorCorrection = false;
        });
        var high = Generate(payload, r =>
        {
            r.ErrorCorrection = QrErrorCorrection.High;
            r.BoostErrorCorrection = false;
        });

        quartile.Version.ShouldBe(1);
        high.Version.ShouldBeGreaterThan(quartile.Version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Generate_WithoutAPayload_Throws(string? payload)
        => Should.Throw<ArgumentException>(() => Generator.Generate(new QrCodeRequest { Payload = payload! }));

    [Fact]
    public void Generate_WithoutARequest_Throws()
        => Should.Throw<ArgumentNullException>(() => Generator.Generate(null!));

    [Theory]
    [InlineData(-1, 0, 0, 0)]
    [InlineData(99, 0, 0, 0)]
    [InlineData(4, -1, 0, 0)]
    [InlineData(4, 999, 0, 0)]
    [InlineData(4, 0, 41, 0)]
    [InlineData(4, 0, 0, 41)]
    [InlineData(4, 0, 5, 2)]
    public void Generate_WithAnOutOfRangeRequest_Throws(int quietZone, int pixelsPerModule, int minVersion, int maxVersion)
        => Should.Throw<ArgumentOutOfRangeException>(() => Generator.Generate(new QrCodeRequest
        {
            Payload = "A",
            QuietZoneModules = quietZone,
            PixelsPerModule = pixelsPerModule,
            MinVersion = minVersion,
            MaxVersion = maxVersion
        }));
}
