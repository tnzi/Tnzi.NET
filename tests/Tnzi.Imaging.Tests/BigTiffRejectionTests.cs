using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Tiff.Constants;

namespace Tnzi.Imaging.Tests;

/// <summary>
/// BigTIFF 在交给 ImageSharp 之前就被拒绝；经典 TIFF 照常可解。
/// </summary>
/// <remarks>
/// <para>
/// <b>被缓解的缺陷</b>（GHSA-wmxv-xphr-5c9g，ImageSharp 2.0.0 至 4.1.1）：BigTIFF 的 IFD 条目数是 64 位，
/// 解码器对每个声明的条目循环一次，而剩余字节不足一个条目时那次读取不前进流。24 字节的输入
/// 就能让一个线程空转几十秒。ImageSharp 按内容而不是扩展名嗅探格式，所以起名 <c>photo.jpg</c>
/// 的上传照样走到这里。修复版带构建期授权校验，框架不升级，改为在每个解码入口前按文件头拒绝。
/// </para>
/// <para>
/// ★ 每条断言都带时限：未缓解时调用<b>不是失败而是不返回</b>。只断言异常类型的话，去掉拒绝逻辑后
/// 测试会先挂几十秒再报出另一个异常；而 <see cref="QrCodeReader"/> 恰好把那个异常也映射成
/// <see cref="ArgumentException"/>，单看类型分不出来。
/// </para>
/// </remarks>
public class BigTiffRejectionTests
{
    /// <summary>
    /// 拒绝只看前四个字节，是毫秒级的；未缓解时一次调用要空转几十秒。五秒给足慢机器的余量，又远在后者之下。
    /// </summary>
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    private static readonly ImageEditor Editor = new();
    private static readonly QrCodeReader Reader = new();

    /// <summary>
    /// 本模块把外来字节交给 ImageSharp 解码的每一个公开入口（<see cref="QrCodeReader"/> 的错误语义不同，单独测）。
    /// </summary>
    private static readonly Dictionary<string, Func<byte[], Task>> DecodeEntryPoints = new()
    {
        ["ImageDecodeGuard.Load(byte[])"] = bytes => Dispose(ImageDecodeGuard.Load(bytes)),
        ["ImageDecodeGuard.Load(Stream)"] = bytes => Dispose(ImageDecodeGuard.Load(new MemoryStream(bytes))),
        ["ImageDecodeGuard.Load(non-seekable Stream)"] = bytes => Dispose(ImageDecodeGuard.Load(new NonSeekableStream(bytes))),
        ["ImageDecodeGuard.LoadAsync(byte[])"] = async bytes => (await ImageDecodeGuard.LoadAsync(bytes)).Dispose(),
        ["ImageDecodeGuard.LoadAsync(Stream)"] = async bytes => (await ImageDecodeGuard.LoadAsync(new MemoryStream(bytes))).Dispose(),
        ["ImageDecodeGuard.LoadAsync(non-seekable Stream)"] = async bytes => (await ImageDecodeGuard.LoadAsync(new NonSeekableStream(bytes))).Dispose(),
        ["BitmapExtensions.LoadFromBytes"] = bytes => Dispose(BitmapExtensions.LoadFromBytes(bytes)),
        ["BitmapExtensions.LoadFromStream"] = bytes => Dispose(BitmapExtensions.LoadFromStream(new MemoryStream(bytes))),
        ["BitmapExtensions.LoadFromBytesAsync"] = async bytes => (await BitmapExtensions.LoadFromBytesAsync(bytes)).Dispose(),
        ["BitmapExtensions.LoadFromStreamAsync"] = async bytes => (await BitmapExtensions.LoadFromStreamAsync(new MemoryStream(bytes))).Dispose(),
        // 扩展名只决定 CanEdit 与输出格式，解码时 ImageSharp 照样按内容嗅探
        ["ImageEditor.EditAsync(.tif)"] = bytes => Editor.EditAsync(bytes, "scan.tif", new ImageEditRequest()),
        ["ImageEditor.EditAsync(.jpg)"] = bytes => Editor.EditAsync(bytes, "photo.jpg", new ImageEditRequest()),
    };

    public static TheoryData<string, bool> EntryPointsByByteOrder()
    {
        var data = new TheoryData<string, bool>();
        foreach (var entry in DecodeEntryPoints.Keys)
        {
            data.Add(entry, false);
            data.Add(entry, true);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EntryPointsByByteOrder))]
    public async Task ABigTiff_IsRejectedAsAnUnknownFormat_WithoutHoldingTheThread(string entry, bool bigEndian)
    {
        var bomb = TiffSamples.BigTiffBomb(bigEndian);

        var call = await RunWithinBudget(() => DecodeEntryPoints[entry](bomb), entry);

        var ex = await Should.ThrowAsync<UnknownImageFormatException>(call);
        ex.Message.ShouldContain("BigTIFF");
    }

    /// <summary>
    /// 正控制：判定只认 BigTIFF 的魔数，经典 TIFF（扫描件、传真件）的每个入口都照常解得开。
    /// </summary>
    [Theory]
    [MemberData(nameof(EntryPointsByByteOrder))]
    public async Task AClassicTiff_StillDecodes(string entry, bool bigEndian)
    {
        var tiff = TiffSamples.ClassicGrayscale(bigEndian, width: 3, height: 2);

        var call = await RunWithinBudget(() => DecodeEntryPoints[entry](tiff), entry);

        await call;
    }

    /// <summary>
    /// 正控制的像素级版本：大端那一份如果被读反了字节序，尺寸和像素都会是错的。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AClassicTiff_DecodesToTheExpectedPixels(bool bigEndian)
    {
        using var image = ImageDecodeGuard.Load(TiffSamples.ClassicGrayscale(bigEndian, width: 3, height: 2));

        image.Width.ShouldBe(3);
        image.Height.ShouldBe(2);
        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 3; x++)
            {
                image[x, y].R.ShouldBe(TiffSamples.PixelValue(x, y, 3), $"pixel ({x}, {y})");
            }
        }
    }

    /// <summary>
    /// 字节流开头不足四个字节时判定不越界，交给 ImageSharp 按原来的语义报「不认识」。
    /// </summary>
    [Theory]
    [InlineData(new byte[] { 0x49 })]
    [InlineData(new byte[] { 0x49, 0x49, 0x2B })]
    [InlineData(new byte[] { 0x4D, 0x4D, 0x00 })]
    public void AStreamShorterThanTheSignature_FailsAsBefore(byte[] bytes)
    {
        Should.Throw<UnknownImageFormatException>(() => ImageDecodeGuard.Load(bytes));
    }

    /// <summary>
    /// <see cref="QrCodeReader"/> 不经过 <see cref="ImageDecodeGuard"/> 的像素闸门（它按边长自判、解成 L8），
    /// 所以它要自己在交给 ImageSharp 之前拒绝 BigTIFF，并沿用「这不是一张可读的图片」的既有语义。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QrCodeReader_RejectsABigTiff_AsNotAReadableImage(bool bigEndian)
    {
        var bomb = TiffSamples.BigTiffBomb(bigEndian);

        var call = await RunWithinBudget(() => Reader.ReadAsync(bomb), "QrCodeReader.ReadAsync");

        var ex = await Should.ThrowAsync<ArgumentException>(call);
        var inner = ex.InnerException.ShouldBeOfType<UnknownImageFormatException>();
        inner.Message.ShouldContain("BigTIFF");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QrCodeReader_DoesNotRejectAClassicTiff(bool bigEndian)
    {
        var tiff = TiffSamples.ClassicGrayscale(bigEndian, width: 3, height: 2);

        QrCodeScanResult result = default;
        var call = await RunWithinBudget(async () => result = await Reader.ReadAsync(tiff), "QrCodeReader.ReadAsync");

        await call;
        // 一张没有码的图是正常输入：返回「没找到」而不是抛
        result.Found.ShouldBeFalse();
    }

    /// <summary>
    /// 纸质回路的真实形态：传真网关与扫描仪交回来的就是 TIFF，码要照常读得出来。
    /// </summary>
    [Fact]
    public async Task QrCodeReader_StillReadsACodeFromAClassicTiff()
    {
        const string payload = "TNZI-CORR-000123";
        var png = new QrCodeGenerator().Generate(new QrCodeRequest { Payload = payload, PixelsPerModule = 8 });

        using var code = Image.Load<L8>(png.Content);
        using var tiff = new MemoryStream();
        // 显式不压缩：CCITT 编码器正是被压制的两条告警所在，测试也不去碰它
        code.SaveAsTiff(tiff, new TiffEncoder { BitsPerPixel = TiffBitsPerPixel.Bit8, Compression = TiffCompression.None });

        var result = await Reader.ReadAsync(tiff.ToArray());

        result.Payload.ShouldBe(payload);
    }

    private static Task Dispose(IDisposable image)
    {
        image.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 在线程池上跑，超时即判失败。返回那个调用本身，由调用方断言它的结局。
    /// </summary>
    /// <remarks>
    /// 必须放到另一个线程上：未缓解时卡住的是同步的解析循环，连 <c>async</c> 入口也是在
    /// 第一次 <c>await</c> 之前就陷进去，在测试线程上直接调用的话计时器根本没有机会开始。
    /// </remarks>
    private static async Task<Task> RunWithinBudget(Func<Task> call, string what)
    {
        // Task.Run 对返回 Task 的委托会解包：外层任务等到内层的异步调用真正结束才完成
        var running = Task.Run(call);
        var finished = await Task.WhenAny(running, Task.Delay(Budget));

        finished.ShouldBeSameAs(running, $"{what} did not return within {Budget.TotalSeconds:0}s; the decoder is stuck in the BigTIFF entry loop.");

        return running;
    }
}
