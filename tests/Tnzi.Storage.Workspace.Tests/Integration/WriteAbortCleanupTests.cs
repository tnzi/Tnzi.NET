namespace Tnzi.Storage.Workspace.Tests.Integration;

/// <summary>
/// 对象已经交给 provider、记录却没落成（<c>InsertAsync</c> 抛了）：不收拾就是一个任何清理都看不见的
/// 孤儿对象 —— 孤儿回收按 <c>FileRecord</c> 枚举，而这里根本没有记录。父模块的 <c>SaveAsync</c> /
/// <c>CopyAsync</c> / <c>CompressAsync</c> 都这么做，这里守的是本模块的两条写路径：建新版本与分片上传完成。
/// </summary>
public class WriteAbortCleanupTests : WorkspaceIntegrationTestBase
{
    [Fact]
    public async Task CreateVersionAsync_InsertThrows_DeletesTheUploadedVersionObject()
    {
        var file = await CreateStoredFileAsync("versioned.txt", "v1"u8.ToArray());
        var recorder = new RecordingFileStorage(Storage);
        var service = new FileVersionService(
            new ThrowingVersionInsertRepository(DbContext, ServiceProvider),
            Repo<FileRecord>(),
            recorder,
            TestFileAccessAuthorizer.AllowAll(),
            ServiceProvider,
            Guard(StorageOptions));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateVersionAsync(file.Id, new MemoryStream("v2"u8.ToArray())));

        var uploaded = Assert.Single(recorder.UploadedPaths);
        Assert.False(await Storage.ExistsAsync(uploaded));
        // 当前内容不能被殃及，记录也不能指向一个没落成的版本。
        Assert.True(await Storage.ExistsAsync(file.Path!));
        DbContext.ChangeTracker.Clear();
        Assert.Equal(file.Path, (await DbContext.FileRecords.AsNoTracking().SingleAsync(f => f.Id == file.Id)).Path);
    }

    [Fact]
    public async Task CompleteChunkedUploadAsync_InsertThrows_DeletesTheMergedObjectAndKeepsTheSessionRetryable()
    {
        var recorder = new RecordingFileStorage(Storage);
        var healthy = CreateChunkUploadService();
        var session = (await healthy.InitiateChunkedUploadAsync("merged.txt", 6, 3)).Data!;
        await healthy.UploadChunkAsync(session.Id, 0, new MemoryStream("abc"u8.ToArray()));
        await healthy.UploadChunkAsync(session.Id, 1, new MemoryStream("def"u8.ToArray()));

        var failing = new FileChunkUploadService(
            Repo<FileUploadSession>(),
            Repo<FileChunk>(),
            new ThrowingFileInsertRepository(DbContext, ServiceProvider),
            recorder,
            new StaticOptionsMonitor<StorageOptions>(StorageOptions),
            ServiceProvider,
            Guard(StorageOptions));

        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.CompleteChunkedUploadAsync(session.Id));

        var merged = Assert.Single(recorder.UploadedPaths);
        Assert.False(await Storage.ExistsAsync(merged));
        // 分片与会话原样保留：客户端可以再试一次 complete。
        DbContext.ChangeTracker.Clear();
        Assert.Equal(2, await DbContext.FileChunks.CountAsync(c => c.UploadSessionId == session.Id));
        Assert.False((await DbContext.FileUploadSessions.AsNoTracking().SingleAsync(s => s.Id == session.Id)).IsCompleted);
    }

    private sealed class ThrowingVersionInsertRepository(WorkspaceTestDbContext dbContext, IServiceProvider serviceProvider)
        : EFCoreRepository<WorkspaceTestDbContext, FileVersion, Guid>(dbContext, serviceProvider: serviceProvider)
    {
        // v1 快照（上传之前）照常落库；抛的是上传之后那条新版本记录。
        public override Task InsertAsync(FileVersion entity, CancellationToken cancellationToken = default)
            => entity.Version >= 2
                ? throw new InvalidOperationException("Simulated database failure.")
                : base.InsertAsync(entity, cancellationToken);
    }

    private sealed class ThrowingFileInsertRepository(WorkspaceTestDbContext dbContext, IServiceProvider serviceProvider)
        : EFCoreRepository<WorkspaceTestDbContext, FileRecord, Guid>(dbContext, serviceProvider: serviceProvider)
    {
        public override Task InsertAsync(FileRecord entity, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated database failure.");
    }
}
