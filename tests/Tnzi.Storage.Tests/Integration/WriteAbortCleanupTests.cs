using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Tnzi.EFCore;

namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// 对象已经交给 provider、记录却没落成（<c>InsertAsync</c> 抛了）：不收拾就是一个任何清理都看不见的
/// 孤儿对象 —— 孤儿回收按 <c>FileRecord</c> 枚举，而这里根本没有记录。<c>SaveAsync</c> 已经这么做了，
/// 这里守的是同一程序集里另外两条写路径：<c>CopyAsync</c> 与 <c>CompressAsync</c>。
/// </summary>
public class WriteAbortCleanupTests : StorageIntegrationTestBase
{
    private static StorageOptions Options() => new()
    {
        MaxFileSize = 50 * 1024 * 1024,
        AllowedExtensions = [".png", ".zip"],
        AutoGenerateThumbnail = false
    };

    private FileStorageService BuildWithThrowingInsert(RecordingFileStorage recorder)
        => new(
            new ThrowingInsertRepository(DbContext, ServiceProvider),
            new EFCoreRepository<StorageTestDbContext, FileReference, Guid>(DbContext, serviceProvider: ServiceProvider),
            recorder,
            new StaticOptionsMonitor<StorageOptions>(Options()),
            TestFileAccessAuthorizer.AllowAll(),
            TestPublicFileFieldResolver.Empty(),
            new TestFileUrlSigner(),
            ServiceProvider,
            CreateUploadGuard(Options()),
            new FileThumbnailGenerator(recorder, new StaticOptionsMonitor<StorageOptions>(Options())));

    [Fact]
    public async Task CopyAsync_InsertThrows_DeletesTheCopiedObject()
    {
        var source = await CreateStoredFileAsync("a.png", [1, 2, 3]);
        var recorder = new RecordingFileStorage(Storage);
        var service = BuildWithThrowingInsert(recorder);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CopyAsync(source.Id));

        var copied = Assert.Single(recorder.UploadedPaths);
        Assert.False(await Storage.ExistsAsync(copied));
        // 原件不能被殃及。
        Assert.True(await Storage.ExistsAsync(source.Path!));
    }

    [Fact]
    public async Task CompressAsync_InsertThrows_DeletesTheZipObject()
    {
        var a = await CreateStoredFileAsync("a.png", [1, 2, 3]);
        var b = await CreateStoredFileAsync("b.png", [4, 5, 6]);
        var recorder = new RecordingFileStorage(Storage);
        var service = BuildWithThrowingInsert(recorder);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompressAsync([a.Id, b.Id]));

        var zip = Assert.Single(recorder.UploadedPaths);
        Assert.False(await Storage.ExistsAsync(zip));
        Assert.True(await Storage.ExistsAsync(a.Path!));
        Assert.True(await Storage.ExistsAsync(b.Path!));
    }

    [Fact]
    public async Task GetOrCreateByMd5Async_InsertThrows_DeletesTheObjectAndItsThumbnail()
    {
        // 这条公开写路径此前既不收集上传的对象、也不收拾：正文与缩略图都成了孤儿回收看不见的对象。
        var options = Options();
        options.AutoGenerateThumbnail = true;
        var recorder = new RecordingFileStorage(Storage);
        var service = new FileStorageService(
            new ThrowingInsertRepository(DbContext, ServiceProvider),
            new EFCoreRepository<StorageTestDbContext, FileReference, Guid>(DbContext, serviceProvider: ServiceProvider),
            recorder,
            new StaticOptionsMonitor<StorageOptions>(options),
            TestFileAccessAuthorizer.AllowAll(),
            TestPublicFileFieldResolver.Empty(),
            new TestFileUrlSigner(),
            ServiceProvider,
            CreateUploadGuard(options),
            new FileThumbnailGenerator(recorder, new StaticOptionsMonitor<StorageOptions>(options)));
        var png = TinyPng();
        var md5 = Convert.ToHexString(MD5.HashData(png)).ToLowerInvariant();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetOrCreateByMd5Async(md5, "a.png", new MemoryStream(png)));

        Assert.Equal(2, recorder.UploadedPaths.Count); // 正文 + 缩略图
        foreach (var path in recorder.UploadedPaths)
            Assert.False(await Storage.ExistsAsync(path), path);
    }

    private static byte[] TinyPng()
    {
        using var image = new Image<Rgba32>(8, 8);
        using var buffer = new MemoryStream();
        image.SaveAsPng(buffer);
        return buffer.ToArray();
    }

    private sealed class ThrowingInsertRepository(StorageTestDbContext dbContext, IServiceProvider serviceProvider)
        : EFCoreRepository<StorageTestDbContext, FileRecord, Guid>(dbContext, serviceProvider: serviceProvider)
    {
        public override Task InsertAsync(FileRecord entity, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated database failure.");
    }
}
