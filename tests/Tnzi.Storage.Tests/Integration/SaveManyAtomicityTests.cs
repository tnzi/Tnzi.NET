using Tnzi.Data;
using Tnzi.EFCore;

namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// <c>SaveManyAsync</c> 是一次意图：要么整批落库，要么一条记录、一个对象都不留。
/// </summary>
/// <remarks>
/// <para>
/// 此前它在 <c>ExecuteInUnitOfWorkAsync</c> 里逐个 <c>SaveAsync</c>，第一条失败就 <b>return Fail</b> ——
/// 而工作单元只在异常时回滚，正常返回的失败结果照样提交：<c>[a.png, b.png, evil.exe]</c> 回 400，
/// <c>a.png</c> / <c>b.png</c> 却以 <c>ReferenceCount = 1</c> 的正式记录留了下来，孤儿回收永远不收它们
/// （只收 <c>&lt;= 0</c>），错误响应里也没有它们的 id，调用方删都没法删；重试整批又经 MD5 去重把它们的计数再加一。
/// 镜像形态：provider 在第三条上抛异常，行是回滚了，前两条的字节却留在桶里、没有任何记录指向它们。
/// </para>
/// <para>
/// 这里注册的是<b>真实的</b> <c>UnitOfWorkManager</c>：没有它 <c>ExecuteInUnitOfWorkAsync</c> 会直接执行，
/// 回滚断言就算绿也是错的理由。
/// </para>
/// </remarks>
public class SaveManyAtomicityTests : StorageIntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        var entityManager = new Mock<IEntityManager>();
        entityManager.Setup(m => m.GetAllDbContextTypes()).Returns([typeof(StorageTestDbContext)]);
        services.AddSingleton(entityManager.Object);
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();
    }

    private static StorageOptions Options() => new()
    {
        MaxFileSize = 50 * 1024 * 1024,
        AllowedExtensions = [".png"],
        AutoGenerateThumbnail = false
    };

    private (RecordingFileStorage Recorder, FileStorageService Service) Build(IEnumerable<IUploadSanitizer>? sanitizers = null, IFileStorage? inner = null)
    {
        var recorder = new RecordingFileStorage(inner ?? Storage);
        return (recorder, CreateStorageService(recorder, Options(), sanitizers: sanitizers));
    }

    private async Task<int> CountRecordsAsync()
        => await DbContext.FileRecords.AsNoTracking().IgnoreQueryFilters().CountAsync();

    [Fact]
    public void TheFixtureHasARealUnitOfWorkManager()
    {
        Assert.IsType<UnitOfWorkManager>(ServiceProvider.GetRequiredService<IUnitOfWorkManager>());
    }

    [Fact]
    public async Task AllSucceed_CommitsEveryRecord()
    {
        var (_, service) = Build();

        var result = await service.SaveManyAsync(
        [
            ("a.png", new MemoryStream([1])),
            ("b.png", new MemoryStream([2])),
        ]);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(2, result.Data!.Count());
        Assert.All(result.Data!, r => Assert.NotEqual(Guid.Empty, r.Id));
        Assert.Equal(2, await CountRecordsAsync());
    }

    [Fact]
    public async Task LastFileRejectedByExtension_LeavesNoRecordsAndNoObjects()
    {
        var (recorder, service) = Build();

        var result = await service.SaveManyAsync(
        [
            ("a.png", new MemoryStream([1])),
            ("b.png", new MemoryStream([2])),
            ("evil.exe", new MemoryStream([3])),
        ]);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.Contains(".exe", result.Message);
        Assert.Equal(0, await CountRecordsAsync());
        foreach (var path in recorder.UploadedPaths)
        {
            Assert.False(await Storage.ExistsAsync(path), $"object {path} must not survive an aborted batch");
        }
    }

    [Fact]
    public async Task SanitizerRejectsTheThirdFile_RollsBackTheFirstTwoRecordsAndObjects()
    {
        // 扩展名闸门在写之前就能整批拒掉；净化器是在逐条上传的路上拒的，前两条已经交给 provider 并插了行。
        var (recorder, service) = Build(sanitizers: [new RejectByNameSanitizer("evil")]);

        var result = await service.SaveManyAsync(
        [
            ("a.png", new MemoryStream([1])),
            ("b.png", new MemoryStream([2])),
            ("evil.png", new MemoryStream([3])),
        ]);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.Equal(2, recorder.UploadedPaths.Count);
        Assert.Equal(0, await CountRecordsAsync());
        foreach (var path in recorder.UploadedPaths)
        {
            Assert.False(await Storage.ExistsAsync(path), $"object {path} must be deleted when the batch aborts");
        }
    }

    [Fact]
    public async Task ProviderThrowsMidBatch_DeletesAlreadyUploadedObjects_AndRollsBackRows()
    {
        var (recorder, service) = Build(inner: new ThrowOnNthUploadStorage(Storage, failOn: 3));

        await Assert.ThrowsAsync<IOException>(() => service.SaveManyAsync(
        [
            ("a.png", new MemoryStream([1])),
            ("b.png", new MemoryStream([2])),
            ("c.png", new MemoryStream([3])),
        ]));

        Assert.Equal(2, recorder.UploadedPaths.Count);
        Assert.Equal(0, await CountRecordsAsync());
        foreach (var path in recorder.UploadedPaths)
        {
            Assert.False(await Storage.ExistsAsync(path), $"object {path} must be deleted when the batch aborts");
        }
    }

    [Fact]
    public async Task ReusedRecordByMd5_KeepsItsObjectAndReferenceCount_WhenTheBatchAborts()
    {
        // 去重命中的是别人的既有记录：作废整批时只能把它的计数退回去，绝不能删它的对象。
        var existing = await CreateStoredFileAsync("shared.png", [9, 9, 9]);
        var (recorder, service) = Build(sanitizers: [new RejectByNameSanitizer("evil")]);

        var result = await service.SaveManyAsync(
        [
            ("same-bytes.png", new MemoryStream([9, 9, 9])),
            ("evil.png", new MemoryStream([3])),
        ]);

        Assert.False(result.Succeeded);
        DbContext.ChangeTracker.Clear();
        var survivor = await DbContext.FileRecords.AsNoTracking().SingleAsync(r => r.Id == existing.Id);
        Assert.Equal(1, survivor.ReferenceCount);
        Assert.True(await Storage.ExistsAsync(existing.Path!));
        Assert.Empty(recorder.DeletedPaths);
        Assert.Equal(1, await CountRecordsAsync());
    }

    [Fact]
    public async Task SaveAsync_InsertThrows_DeletesTheUploadedObject()
    {
        // 单文件路径的同款：对象已交给 provider、记录没落成 ⇒ 一个任何清理都看不见的孤儿对象。
        var recorder = new RecordingFileStorage(Storage);
        var service = new FileStorageService(
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

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync("a.png", new MemoryStream([1])));

        var path = Assert.Single(recorder.UploadedPaths);
        Assert.False(await Storage.ExistsAsync(path));
    }

    private sealed class RejectByNameSanitizer(string needle) : IUploadSanitizer
    {
        public int Order => 100;

        public Task<UploadSanitizationResult> SanitizeAsync(UploadSanitizationContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(context.FileName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                ? UploadSanitizationResult.Reject("Simulated malware detected.")
                : UploadSanitizationResult.Unchanged());
    }

    private sealed class ThrowOnNthUploadStorage(IFileStorage inner, int failOn) : IFileStorage
    {
        private int _uploads;

        public string ProviderName => inner.ProviderName;

        public Task<string> UploadAsync(string fileName, Stream stream, string? contentType = null)
        {
            if (Interlocked.Increment(ref _uploads) == failOn)
                throw new IOException("Simulated provider outage.");
            return inner.UploadAsync(fileName, stream, contentType);
        }

        public Task<Stream> DownloadAsync(string filePath) => inner.DownloadAsync(filePath);
        public Task<bool> DeleteAsync(string filePath) => inner.DeleteAsync(filePath);
        public Task<bool> ExistsAsync(string filePath) => inner.ExistsAsync(filePath);
        public Task<string> GetUrlAsync(string filePath, int? expiresIn = null) => inner.GetUrlAsync(filePath, expiresIn);
        public Task<long> GetFileSizeAsync(string filePath) => inner.GetFileSizeAsync(filePath);
        public Task<(Stream Stream, long Start, long End, long TotalLength)> DownloadRangeAsync(string filePath, long? rangeStart = null, long? rangeEnd = null)
            => inner.DownloadRangeAsync(filePath, rangeStart, rangeEnd);
        public Task<string?> CopyAsync(string sourcePath, string destFileName) => inner.CopyAsync(sourcePath, destFileName);
    }

    private sealed class ThrowingInsertRepository(StorageTestDbContext dbContext, IServiceProvider serviceProvider)
        : EFCoreRepository<StorageTestDbContext, FileRecord, Guid>(dbContext, serviceProvider: serviceProvider)
    {
        public override Task InsertAsync(FileRecord entity, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated database failure.");
    }
}
