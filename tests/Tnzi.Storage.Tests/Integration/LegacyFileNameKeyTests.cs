using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Tnzi.Storage.Helpers;

namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// 存量记录的 <see cref="FileRecord.FileName"/> 可能是调用方给的名字（2026-09-04 之前分片完成、解压、压缩三条写路径
/// 都把它直接写了进去），同名的记录在不同用户之间随处可见。从它派生出的任何存储键都会在记录之间相撞。
/// </summary>
/// <remarks>
/// <para>
/// 缩略图回填按 <c>thumb_{FileName}</c> 写对象：两条都叫 <c>image1.png</c> 的记录先后回填，第二张图覆盖第一张，
/// 两条记录登记的是同一个对象，A 的列表缩略图从此显示 B 的图片。对象存储上键没有日期前缀，本地磁盘上同一天回填也撞。
/// </para>
/// <para>
/// 同一形状的另一处：去重命中「记录在、对象丢了」时按原键重传。原键若是存量的调用方名字，重传就写进了
/// 另一条同名记录正在用的对象。
/// </para>
/// </remarks>
public class LegacyFileNameKeyTests : StorageIntegrationTestBase
{
    private static readonly StorageOptions Options = new()
    {
        MaxFileSize = 50 * 1024 * 1024,
        AllowedExtensions = [".png", ".txt"],
        AutoGenerateThumbnail = false,
    };

    private static byte[] SolidPng(Rgba32 colour)
    {
        using var image = new Image<Rgba32>(40, 40, colour);
        using var buffer = new MemoryStream();
        image.SaveAsPng(buffer);
        return buffer.ToArray();
    }

    private static string Md5Of(byte[] content)
        => Convert.ToHexString(MD5.HashData(content)).ToLowerInvariant();

    /// <summary>一条存量形态的记录：对象存在自己的键下，<c>FileName</c> 却是一个与别人相同的调用方名字。</summary>
    private async Task<FileRecord> InsertLegacyRecordAsync(string legacyFileName, byte[] content, string? objectKey)
    {
        string path;
        if (objectKey == null)
        {
            path = $"2020/01/01/{legacyFileName}";
        }
        else
        {
            path = await Storage.UploadAsync(objectKey, new MemoryStream(content), "image/png");
        }

        var record = new FileRecord
        {
            Id = Guid.NewGuid(),
            FileName = legacyFileName,
            OriginalName = legacyFileName,
            Extension = Path.GetExtension(legacyFileName),
            Size = content.LongLength,
            Path = path,
            Md5Hash = Md5Of(content),
            Provider = Storage.ProviderName,
            ContentType = "image/png",
            ReferenceCount = 1,
        };
        DbContext.FileRecords.Add(record);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return record;
    }

    private async Task<Rgba32> ThumbnailCentreAsync(FileStorageService service, Guid id)
    {
        var thumbnail = await service.GetThumbnailAsync(id);
        Assert.True(thumbnail.Succeeded, thumbnail.Message);
        await using var stream = thumbnail.Data!;
        using var image = await Image.LoadAsync<Rgba32>(stream);
        return image[image.Width / 2, image.Height / 2];
    }

    [Fact]
    public async Task Backfill_TwoRecordsWithTheSameLegacyName_EachKeepTheirOwnThumbnail()
    {
        var red = await InsertLegacyRecordAsync("image1.png", SolidPng(new Rgba32(220, 20, 20)), "legacy-a.png");
        var blue = await InsertLegacyRecordAsync("image1.png", SolidPng(new Rgba32(20, 20, 220)), "legacy-b.png");
        var service = CreateStorageService(Options);

        var result = await service.BackfillThumbnailsAsync();

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(2, result.Data!.Generated);

        DbContext.ChangeTracker.Clear();
        var redPath = DbContext.FileRecords.Single(f => f.Id == red.Id).ThumbnailPath;
        var bluePath = DbContext.FileRecords.Single(f => f.Id == blue.Id).ThumbnailPath;
        Assert.NotEqual(redPath, bluePath);

        var redCentre = await ThumbnailCentreAsync(service, red.Id);
        var blueCentre = await ThumbnailCentreAsync(service, blue.Id);
        Assert.True(redCentre.R > 150 && redCentre.B < 100, $"A's thumbnail must show A's image, got {redCentre}");
        Assert.True(blueCentre.B > 150 && blueCentre.R < 100, $"B's thumbnail must show B's image, got {blueCentre}");
    }

    [Fact]
    public async Task EveryThumbnail_GetsAFreshKey_EvenForTheSameRecord()
    {
        // 同一条记录重画一次（建版本后清掉旧图、再回填）也不能落回旧键：旧键上的删除请求是异步的，
        // 晚到的那一次会把新画好的图删掉，记录从此指向一个不存在的对象。
        var generator = new FileThumbnailGenerator(Storage, new StaticOptionsMonitor<StorageOptions>(Options));
        var original = await Storage.UploadAsync(StorageKeyHelper.NewKey(".png"), new MemoryStream(SolidPng(new Rgba32(1, 2, 3))), "image/png");

        var first = await generator.GenerateAsync(original, ".png", 0);
        var second = await generator.GenerateAsync(original, ".png", 0);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first, second);
        Assert.True(StorageKeyHelper.IsGenerated(Path.GetFileName(first)));
        Assert.True(StorageKeyHelper.IsGenerated(Path.GetFileName(second)));
    }

    [Fact]
    public async Task ReuploadOfAMissingObject_DoesNotWriteUnderALegacyCallerName()
    {
        // B 的对象就躺在调用方名字 image1.png 这个键下（存量形态）。
        var bBytes = "bob's bytes"u8.ToArray();
        var bob = await InsertLegacyRecordAsync("image1.png", bBytes, "image1.png");

        // A 的记录同名，对象丢了；A 再正式上传一次同样的字节，去重命中它并重传。
        var aBytes = "alice's bytes"u8.ToArray();
        var alice = await InsertLegacyRecordAsync("image1.png", aBytes, objectKey: null);

        var saved = await CreateStorageService(Options).SaveAsync("again.txt", new MemoryStream(aBytes), isTemporary: false);
        Assert.True(saved.Succeeded, saved.Message);
        Assert.Equal(alice.Id, saved.Data!.Id);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        // B 的字节原封不动。
        await using (var stream = await Storage.DownloadAsync(bob.Path!))
        using (var copy = new MemoryStream())
        {
            await stream.CopyToAsync(copy);
            Assert.Equal(bBytes, copy.ToArray());
        }

        // A 的记录换上了服务端生成的键，重传的对象就在那个键下。
        var reloaded = DbContext.FileRecords.Single(f => f.Id == alice.Id);
        Assert.True(StorageKeyHelper.IsGenerated(reloaded.FileName), reloaded.FileName);
        Assert.NotEqual(bob.Path, reloaded.Path);
        await using (var stream = await Storage.DownloadAsync(reloaded.Path!))
        using (var copy = new MemoryStream())
        {
            await stream.CopyToAsync(copy);
            Assert.Equal(aBytes, copy.ToArray());
        }
    }
}
