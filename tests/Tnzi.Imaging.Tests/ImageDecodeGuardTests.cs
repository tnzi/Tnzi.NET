using SixLabors.ImageSharp.Formats.Png;

namespace Tnzi.Imaging.Tests;

/// <summary>
/// 解码闸门：一张<b>声称</b>自己很大的图片必须在解码前被拒绝。
/// </summary>
/// <remarks>
/// <para>
/// <b>被保护的缺陷</b>：五个解码入口（含 <c>Tnzi.Storage</c> 的缩略图生成）都直接
/// <c>Image.Load</c>，没有任何像素上限。<b>压缩字节数与解码后的内存没有关系</b>：
/// 下面这张 PNG 只有几十字节，却声称 50000×50000 —— 真去解它要 10 TB。
/// 上传大小限制对这种输入毫无作用，一个上传口就能放倒一个进程。
/// </para>
/// <para>
/// ★ 这条测试的存在方式本身就是证据：如果实现是"先解码再检查尺寸"，
/// 它不会失败，而是会把测试进程一起带走。
/// </para>
/// </remarks>
public class ImageDecodeGuardTests
{
    [Fact]
    public void AnImageDeclaringAnAbsurdSize_IsRejectedBeforeDecoding()
    {
        var bomb = PngHeaderOnly(50_000, 50_000);

        var ex = Should.Throw<ImageDecodeLimitExceededException>(() => ImageDecodeGuard.Load(bomb));

        ex.Pixels.ShouldBe(2_500_000_000L);
        ex.Limit.ShouldBe(ImageDecodeGuard.DefaultMaxDecodePixels);
    }

    [Fact]
    public async Task TheAsyncEntryPoint_RejectsItToo()
    {
        var bomb = PngHeaderOnly(50_000, 50_000);

        await Should.ThrowAsync<ImageDecodeLimitExceededException>(() => ImageDecodeGuard.LoadAsync(bomb));
    }

    /// <summary>
    /// 不可定位的流（对象存储的响应流就是这样）同样受闸门保护。
    /// </summary>
    [Fact]
    public async Task ANonSeekableStream_IsGuardedAsWell()
    {
        using var stream = new NonSeekableStream(PngHeaderOnly(50_000, 50_000));

        await Should.ThrowAsync<ImageDecodeLimitExceededException>(() => ImageDecodeGuard.LoadAsync(stream));
    }

    [Fact]
    public void AnOrdinaryImage_LoadsNormally()
    {
        using var image = ImageDecodeGuard.Load(SmallPng(40, 30));

        image.Width.ShouldBe(40);
        image.Height.ShouldBe(30);
    }

    [Fact]
    public async Task ANonSeekableStreamOfAnOrdinaryImage_LoadsNormally()
    {
        using var stream = new NonSeekableStream(SmallPng(21, 12));

        using var image = await ImageDecodeGuard.LoadAsync(stream);

        image.Width.ShouldBe(21);
        image.Height.ShouldBe(12);
    }

    /// <summary>
    /// 上限设为 0 = 不限制，这条留给完全可信的内部管道。
    /// </summary>
    [Fact]
    public void WithTheLimitDisabled_TheHeaderCheckDoesNotReject()
    {
        // 只验证"闸门放行"，因此仍用一张真的小图：如果这里放一颗炸弹，
        // 通过测试的代价就是真的去解一张 50000×50000
        using var image = ImageDecodeGuard.Load(SmallPng(8, 8), maxPixels: 0);

        image.Width.ShouldBe(8);
    }

    [Fact]
    public void TheLimitIsExclusiveAtTheBoundary()
    {
        var bytes = SmallPng(10, 10);

        Should.NotThrow(() => ImageDecodeGuard.Load(bytes, maxPixels: 100).Dispose());
        Should.Throw<ImageDecodeLimitExceededException>(() => ImageDecodeGuard.Load(bytes, maxPixels: 99));
    }

    private static byte[] SmallPng(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        return ms.ToArray();
    }

    /// <summary>
    /// 一张只有签名 + IHDR 的 PNG：足够 <c>Image.Identify</c> 读出尺寸，
    /// 后面没有像素数据 —— 真去解码只会失败，所以它同时证明了"闸门在解码之前生效"。
    /// </summary>
    private static byte[] PngHeaderOnly(int width, int height)
    {
        var png = new List<byte> { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A };

        var ihdr = new List<byte> { (byte)'I', (byte)'H', (byte)'D', (byte)'R' };
        ihdr.AddRange(BigEndian(width));
        ihdr.AddRange(BigEndian(height));
        ihdr.Add(8);    // bit depth
        ihdr.Add(6);    // colour type: RGBA
        ihdr.Add(0);    // compression
        ihdr.Add(0);    // filter
        ihdr.Add(0);    // interlace

        png.AddRange(BigEndian(ihdr.Count - 4));   // chunk length excludes the type
        png.AddRange(ihdr);
        png.AddRange(BigEndian(unchecked((int)Crc32(ihdr))));

        return [.. png];
    }

    private static byte[] BigEndian(int value) =>
    [
        (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value
    ];

    private static uint Crc32(IEnumerable<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private sealed class NonSeekableStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes, writable: false);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
