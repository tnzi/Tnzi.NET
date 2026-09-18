using Tnzi.Data;
using Tnzi.EFCore;

namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// 没有 <c>IUnitOfWorkManager</c> 的宿主上（本夹具默认就是这样跑的），<c>SaveManyAsync</c> 里每条
/// <c>InsertAsync</c> 都是立即提交的：失败发生时前面那些记录**已经在库里**，不是「待回滚」。
/// 作废这一批时不能把它们的对象也删掉 —— 那会把「孤儿但自洽的记录」变成「记录在、字节没了」，
/// 下载直接 500。只删失败的那一条交给 provider 的对象。
/// </summary>
public class SaveManyWithoutUnitOfWorkTests : StorageIntegrationTestBase
{
    private static StorageOptions Options() => new()
    {
        MaxFileSize = 50 * 1024 * 1024,
        AllowedExtensions = [".png"],
        AutoGenerateThumbnail = false
    };

    [Fact]
    public void TheFixtureHasNoUnitOfWorkManager()
    {
        Assert.Null(ServiceProvider.GetService<IUnitOfWorkManager>());
    }

    [Fact]
    public async Task SaveManyAsync_InsertThrowsOnTheSecondFile_KeepsTheCommittedFirstFileIntact()
    {
        var recorder = new RecordingFileStorage(Storage);
        var service = new FileStorageService(
            new ThrowOnNthInsertRepository(DbContext, ServiceProvider, failOn: 2),
            new EFCoreRepository<StorageTestDbContext, FileReference, Guid>(DbContext, serviceProvider: ServiceProvider),
            recorder,
            new StaticOptionsMonitor<StorageOptions>(Options()),
            TestFileAccessAuthorizer.AllowAll(),
            TestPublicFileFieldResolver.Empty(),
            new TestFileUrlSigner(),
            ServiceProvider,
            CreateUploadGuard(Options()),
            new FileThumbnailGenerator(recorder, new StaticOptionsMonitor<StorageOptions>(Options())));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveManyAsync(
        [
            ("a.png", new MemoryStream([1, 2, 3])),
            ("b.png", new MemoryStream([4, 5, 6]))
        ]));

        Assert.Equal(2, recorder.UploadedPaths.Count);
        DbContext.ChangeTracker.Clear();
        var committed = await DbContext.FileRecords.AsNoTracking().SingleAsync();
        Assert.Equal("a.png", committed.OriginalName);
        // 已提交记录的字节必须还在；只有没落成记录的第二个对象被删。
        Assert.True(await Storage.ExistsAsync(committed.Path!));
        Assert.Equal(recorder.UploadedPaths[0], committed.Path);
        Assert.False(await Storage.ExistsAsync(recorder.UploadedPaths[1]));
    }

    private sealed class ThrowOnNthInsertRepository(StorageTestDbContext dbContext, IServiceProvider serviceProvider, int failOn)
        : EFCoreRepository<StorageTestDbContext, FileRecord, Guid>(dbContext, serviceProvider: serviceProvider)
    {
        private int _inserts;

        public override Task InsertAsync(FileRecord entity, CancellationToken cancellationToken = default)
            => Interlocked.Increment(ref _inserts) == failOn
                ? throw new InvalidOperationException("Simulated database failure.")
                : base.InsertAsync(entity, cancellationToken);
    }
}
