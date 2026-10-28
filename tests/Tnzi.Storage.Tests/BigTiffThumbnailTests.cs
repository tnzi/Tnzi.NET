using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Tiff.Constants;
using SixLabors.ImageSharp.PixelFormats;

namespace Tnzi.Storage.Tests;

/// <summary>
/// 上传后自动生成缩略图，是 ImageSharp BigTIFF 死循环（GHSA-wmxv-xphr-5c9g）在本仓里最短的可达路径：
/// 任何能上传的人传一个 24 字节的文件，扩展名随便起，就能占住一个线程几十秒。
/// </summary>
/// <remarks>
/// 生成器经 <c>ImageDecodeGuard</c> 解码，拒绝发生在那里；这里守的是「缩略图这条路确实走到了那道判定」：
/// 没有缩略图、不抛、记一条带着那个拒绝的 Warning，而且在时限内返回。
/// </remarks>
public class BigTiffThumbnailTests
{
    /// <summary>拒绝是毫秒级的；未缓解时一次解码要空转几十秒。</summary>
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(false, ".jpg")]
    [InlineData(true, ".jpg")]
    [InlineData(false, ".tif")]
    [InlineData(true, ".tiff")]
    public async Task ABigTiffUpload_GetsNoThumbnail_WithoutHoldingTheThread(bool bigEndian, string extension)
    {
        var logger = new Mock<ILogger<FileThumbnailGenerator>>();
        var (generator, path, _) = await StoreAsync(BigTiffBomb(bigEndian), extension, logger.Object);

        var thumbnail = await RunWithinBudget(() => generator.GenerateAsync(path, extension, size: 24));

        Assert.Null(thumbnail);
        logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.Is<Exception?>(ex => ex is UnknownImageFormatException && ex.Message.Contains("BigTIFF")),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    /// <summary>
    /// 正控制：同一条路径上，经典 TIFF（扫描仪的默认格式）照常出图。这证明上面的「没有缩略图」来自那道判定，
    /// 而不是这个扩展名本来就不出图。
    /// </summary>
    [Theory]
    [InlineData(".tif")]
    [InlineData(".jpg")]
    public async Task AClassicTiffUpload_StillGetsAThumbnail(string extension)
    {
        var (generator, path, _) = await StoreAsync(ClassicTiff(), extension);

        var thumbnail = await RunWithinBudget(() => generator.GenerateAsync(path, extension, size: 0));

        Assert.False(string.IsNullOrEmpty(thumbnail));
    }

    private static async Task<(FileThumbnailGenerator Generator, string Path, string Key)> StoreAsync(
        byte[] content, string extension, ILogger<FileThumbnailGenerator>? logger = null)
    {
        var storage = new InMemoryFileStorage();
        var options = new StaticOptionsMonitor<StorageOptions>(new StorageOptions());
        var generator = new FileThumbnailGenerator(storage, options, logger);

        var key = $"uploads/scan{extension}";
        var path = await storage.UploadAsync(key, new MemoryStream(content));
        return (generator, path, key);
    }

    private static async Task<string?> RunWithinBudget(Func<Task<string?>> call)
    {
        // 放到线程池上：未缓解时卡住的是同步的解析循环，在测试线程上直接调用的话计时器没有机会开始
        var running = Task.Run(call);
        var finished = await Task.WhenAny(running, Task.Delay(Budget));

        Assert.True(ReferenceEquals(finished, running),
            $"Thumbnail generation did not return within {Budget.TotalSeconds:0}s; the decoder is stuck in the BigTIFF entry loop.");

        return await running;
    }

    /// <summary>
    /// 公开披露的样本：24 字节的 BigTIFF，首个 IFD 声明 50 亿个条目，其后没有任何条目字节。
    /// </summary>
    private static byte[] BigTiffBomb(bool bigEndian) => bigEndian
        ?
        [
            0x4D, 0x4D, 0x00, 0x2B,                             // "MM"，魔数 43
            0x00, 0x08, 0x00, 0x00,                             // 偏移量字节数 8，保留 0
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10,     // 首个 IFD 在偏移 16
            0x00, 0x00, 0x00, 0x01, 0x2A, 0x05, 0xF2, 0x00,     // 条目数 5,000,000,000
        ]
        :
        [
            0x49, 0x49, 0x2B, 0x00,
            0x08, 0x00, 0x00, 0x00,
            0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0xF2, 0x05, 0x2A, 0x01, 0x00, 0x00, 0x00,
        ];

    private static byte[] ClassicTiff()
    {
        using var image = new Image<Rgba32>(40, 30, new Rgba32(30, 120, 200));
        using var stream = new MemoryStream();
        // 显式不压缩：CCITT 编码器正是被压制的两条告警所在，测试也不去碰它
        image.SaveAsTiff(stream, new TiffEncoder { Compression = TiffCompression.None });
        return stream.ToArray();
    }
}
