namespace Tnzi.Imaging.Tests;

/// <summary>
/// 会让 ImageSharp 3.1 越界写的 CCITT TIFF 在交给它之前就被拒绝；条带 Group 4 与其余 TIFF 照常可解。
/// </summary>
/// <remarks>
/// <para>
/// <b>被缓解的缺陷</b>（GHSA-v76p-62qx-wwq2，ImageSharp 3.0.0 至 3.2.0 之前、4.0.0 至 4.1.1 之前）：分块 TIFF 的
/// CCITT 解压器按整幅图宽逐行写进按块宽分配的缓冲区，写入器不查边界，越界长度由文件决定（堆越界写，确定性地放倒进程）。
/// 同一次修复还给条带上的 Modified Huffman 与 T4 补了「游程装得进本行」的检查：3.1 里一个超长游程同样写出缓冲区。
/// 修复版带构建期授权校验，框架不升级，改为在每个解码入口前按 IFD 拒绝这些形态。
/// </para>
/// <para>
/// 这里的攻击样本不带能触发越界的像素数据：拒绝发生在解码之前，数据根本不会被读到。
/// 断言看的是拒绝消息，而不只是异常类型，否则 ImageSharp 自己对坏数据的报错也会让测试变绿。
/// </para>
/// </remarks>
public class CcittTiffRejectionTests
{
    private static readonly ImageEditor Editor = new();
    private static readonly QrCodeReader Reader = new();

    /// <summary>本模块把外来字节交给 ImageSharp 解码的每一个公开入口（<see cref="QrCodeReader"/> 单独测）。</summary>
    private static readonly Dictionary<string, Func<byte[], Task>> DecodeEntryPoints = new()
    {
        ["ImageDecodeGuard.Load(byte[])"] = bytes => Dispose(ImageDecodeGuard.Load(bytes)),
        ["ImageDecodeGuard.Load(Stream)"] = bytes => Dispose(ImageDecodeGuard.Load(new MemoryStream(bytes))),
        ["ImageDecodeGuard.Load(non-seekable Stream)"] = bytes => Dispose(ImageDecodeGuard.Load(new NonSeekableStream(bytes))),
        ["ImageDecodeGuard.LoadAsync(byte[])"] = async bytes => (await ImageDecodeGuard.LoadAsync(bytes)).Dispose(),
        ["ImageDecodeGuard.LoadAsync(non-seekable Stream)"] = async bytes => (await ImageDecodeGuard.LoadAsync(new NonSeekableStream(bytes))).Dispose(),
        ["ImageDecodeGuard.LoadFirstFrameAsync(byte[])"] = async bytes => (await ImageDecodeGuard.LoadFirstFrameAsync(bytes)).Dispose(),
        ["BitmapExtensions.LoadFromBytes"] = bytes => Dispose(BitmapExtensions.LoadFromBytes(bytes)),
        ["BitmapExtensions.LoadFromStreamAsync"] = async bytes => (await BitmapExtensions.LoadFromStreamAsync(new MemoryStream(bytes))).Dispose(),
        // 扩展名只决定 CanEdit 与输出格式，解码时 ImageSharp 照样按内容嗅探
        ["ImageEditor.EditAsync(.jpg)"] = bytes => Editor.EditAsync(bytes, "photo.jpg", new ImageEditRequest()),
    };

    public static TheoryData<string, ushort, bool, bool> RejectedShapes()
    {
        var data = new TheoryData<string, ushort, bool, bool>();
        foreach (var entry in DecodeEntryPoints.Keys)
        {
            foreach (var bigEndian in new[] { false, true })
            {
                // 公告本身：分块 + 三种 CCITT 压缩
                data.Add(entry, TiffSamples.CompressionModifiedHuffman, true, bigEndian);
                data.Add(entry, TiffSamples.CompressionCcittT4, true, bigEndian);
                data.Add(entry, TiffSamples.CompressionCcittT6, true, bigEndian);

                // 同一次修复补上的条带缺口：Modified Huffman 与 T4 的超长游程
                data.Add(entry, TiffSamples.CompressionModifiedHuffman, false, bigEndian);
                data.Add(entry, TiffSamples.CompressionCcittT4, false, bigEndian);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RejectedShapes))]
    public async Task AnUnsafeCcittTiff_IsRejectedBeforeDecoding(string entry, ushort compression, bool tiled, bool bigEndian)
    {
        var tiff = TiffSamples.Pages(bigEndian, TiffSamples.Ccitt(compression, tiled));

        var ex = await Should.ThrowAsync<UnknownImageFormatException>(() => DecodeEntryPoints[entry](tiff));

        ex.Message.ShouldContain("CCITT", Case.Sensitive);
    }

    [Fact]
    public async Task TheCompressionTag_IsReadWhetherItIsAShortOrALong()
    {
        var tiff = TiffSamples.Pages(bigEndian: false, TiffSamples.Ccitt(TiffSamples.CompressionCcittT6, tiled: true, compressionAsLong: true));

        (await Should.ThrowAsync<UnknownImageFormatException>(() => ImageDecodeGuard.LoadAsync(tiff))).Message.ShouldContain("CCITT", Case.Sensitive);
    }

    [Fact]
    public void AnUnsafePageAfterASafeOne_IsStillRejected_EvenWhenOnlyTheFirstFrameIsWanted()
    {
        // 每一页都查：识别阶段会读全部 IFD，而解码全部帧的入口会把后面那一页也解出来
        var tiff = TiffSamples.Pages(bigEndian: false, TiffSamples.Uncompressed(4, 4), TiffSamples.Ccitt(TiffSamples.CompressionCcittT4, tiled: false));

        Should.Throw<UnknownImageFormatException>(() => ImageDecodeGuard.Load(tiff)).Message.ShouldContain("CCITT", Case.Sensitive);
        Should.Throw<UnknownImageFormatException>(() => ImageDecodeGuard.LoadFirstFrameAsync(tiff).GetAwaiter().GetResult()).Message.ShouldContain("CCITT", Case.Sensitive);
    }

    [Fact]
    public async Task AnUnsafeTiffThatDoesNotStartTheStream_IsStillRejected()
    {
        // ImageSharp 从流的当前位置认文件头：探测也必须从那里读，而不是从流开头
        using var stream = Prefixed(TiffSamples.Pages(bigEndian: false, TiffSamples.Ccitt(TiffSamples.CompressionCcittT4, tiled: false)));

        (await Should.ThrowAsync<UnknownImageFormatException>(() => ImageDecodeGuard.LoadAsync(stream))).Message.ShouldContain("CCITT", Case.Sensitive);
    }

    [Fact]
    public async Task TheProbe_PutsTheStreamBackWhereItFoundIt()
    {
        using var stream = Prefixed(TiffSamples.Pages(bigEndian: false, TiffSamples.Uncompressed(4, 4)));

        ImageDecodeGuard.ThrowIfUnsafeFaxTiff(stream);
        stream.Position.ShouldBe(3);

        await ImageDecodeGuard.ThrowIfUnsafeFaxTiffAsync(stream, CancellationToken.None);
        stream.Position.ShouldBe(3);
    }

    [Fact]
    public void ACyclicPageChain_EndsTheProbe()
    {
        // 末页的「下一页」指回首页：探测按访问过的偏移停下，不在环里打转
        var tiff = TiffSamples.Pages(bigEndian: false, TiffSamples.Uncompressed(4, 4));
        var nextOffsetAt = 8 + 2 + (BitConverter.ToUInt16(tiff, 8) * 12);
        BitConverter.GetBytes(8u).CopyTo(tiff, nextOffsetAt);

        Should.NotThrow(() => ImageDecodeGuard.ThrowIfUnsafeFaxTiff(new MemoryStream(tiff)));
    }

    private static MemoryStream Prefixed(byte[] tiff)
    {
        var bytes = new byte[3 + tiff.Length];
        tiff.CopyTo(bytes, 3);
        return new MemoryStream(bytes) { Position = 3 };
    }

    #region 正控制

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AStripGroup4Tiff_StillDecodes(bool bigEndian)
    {
        // 扫描件与传真件最常见的形态：拒绝它就等于关掉纸质回路
        var tiff = TiffSamples.Pages(bigEndian, TiffSamples.WhiteGroup4Strip(width: 16, height: 3));

        foreach (var (entry, decode) in DecodeEntryPoints)
        {
            await Should.NotThrowAsync(() => decode(tiff), entry);
        }

        using var image = ImageDecodeGuard.Load(tiff);
        image.Width.ShouldBe(16);
        image.Height.ShouldBe(3);
        image[15, 2].R.ShouldBe((byte)255);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ATiledTiffWithoutCcitt_StillDecodes(bool bigEndian)
    {
        // 分块本身不是问题：只有 CCITT 解压器忽略块宽
        var tiff = TiffSamples.Pages(bigEndian, TiffSamples.Uncompressed(16, 16, tiled: true));

        using var image = await ImageDecodeGuard.LoadAsync(tiff);

        image.Width.ShouldBe(16);
        image.Height.ShouldBe(16);
    }

    [Fact]
    public async Task QrCodeReader_RejectsAnUnsafeCcittTiff_AsNotAReadableImage()
    {
        var tiff = TiffSamples.Pages(bigEndian: false, TiffSamples.Ccitt(TiffSamples.CompressionCcittT6, tiled: true));

        var ex = await Should.ThrowAsync<ArgumentException>(() => Reader.ReadAsync(tiff));

        ex.InnerException.ShouldBeOfType<UnknownImageFormatException>().Message.ShouldContain("CCITT", Case.Sensitive);
    }

    [Fact]
    public async Task QrCodeReader_StillReadsAStripGroup4Tiff()
    {
        var tiff = TiffSamples.Pages(bigEndian: false, TiffSamples.WhiteGroup4Strip(width: 32, height: 32));

        var result = await Reader.ReadAsync(tiff);

        // 一张空白页里没有码：返回「没找到」而不是抛
        result.Found.ShouldBeFalse();
    }

    #endregion

    private static Task Dispose(IDisposable image)
    {
        image.Dispose();
        return Task.CompletedTask;
    }
}
