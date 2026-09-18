using Tnzi.EventBus;
using Tnzi.Storage.Events;

namespace Tnzi.Storage.Workspace.Tests.Integration;

/// <summary>
/// 建版本 / 还原版本换掉记录指向的字节之后，旧缩略图对象的物理删除必须走模块自己的约定：
/// 发 <see cref="FileDeleteRequestedEvent"/>（事务感知，提交之后才处理），而不是在行更新落库之前就同步删掉。
/// </summary>
/// <remarks>
/// 此前 <c>ResetThumbnailAsync</c> 先删对象再更新行：更新或提交失败时行仍指向一个已删除的对象，
/// <c>/thumbnail</c> 从此答 500（provider 抛 <c>FileNotFoundException</c>）而不是文档写的 404。
/// </remarks>
public class ThumbnailResetOrderingTests : WorkspaceIntegrationTestBase
{
    private readonly List<FileDeleteRequestedEvent> _deleteRequests = [];

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        var bus = new Mock<IEventBus> { DefaultValue = DefaultValue.Empty };
        bus.Setup(b => b.PublishAsync(It.IsAny<FileDeleteRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<FileDeleteRequestedEvent, CancellationToken>((e, _) => _deleteRequests.Add(e))
            .Returns(Task.CompletedTask);
        services.AddSingleton(bus.Object);
    }

    private async Task<(FileRecord File, string ThumbnailPath)> SeedFileWithThumbnailAsync()
    {
        var file = await CreateStoredFileAsync("photo.png", "v1"u8.ToArray());
        var thumbnailPath = await Storage.UploadAsync("thumb-of-v1.jpg", new MemoryStream("thumb"u8.ToArray()), "image/jpeg");
        file.ThumbnailPath = thumbnailPath;
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return (file, thumbnailPath);
    }

    [Fact]
    public async Task CreateVersionAsync_RequestsTheStaleThumbnailDeletionThroughTheEvent_NotInline()
    {
        var (file, thumbnailPath) = await SeedFileWithThumbnailAsync();
        var service = CreateVersionService();

        var result = await service.CreateVersionAsync(file.Id, new MemoryStream("v2"u8.ToArray()), "second");

        Assert.True(result.Succeeded, result.Message);
        DbContext.ChangeTracker.Clear();
        Assert.Null(DbContext.FileRecords.Single(f => f.Id == file.Id).ThumbnailPath);
        var request = Assert.Single(_deleteRequests);
        Assert.Equal(file.Id, request.FileId);
        Assert.Equal(thumbnailPath, request.ThumbnailPath);
        Assert.Null(request.FilePath);
        // 物理删除交给事件处理器（提交之后），这里不该已经删掉。
        Assert.True(await Storage.ExistsAsync(thumbnailPath));
    }

    [Fact]
    public async Task RestoreVersionAsync_RequestsTheStaleThumbnailDeletionThroughTheEvent()
    {
        var file = await CreateStoredFileAsync("photo.png", "v1"u8.ToArray());
        var service = CreateVersionService();
        Assert.True((await service.CreateVersionAsync(file.Id, new MemoryStream("v2"u8.ToArray()), "second")).Succeeded);
        _deleteRequests.Clear();
        var thumbnailPath = await Storage.UploadAsync("thumb-of-v2.jpg", new MemoryStream("thumb"u8.ToArray()), "image/jpeg");
        DbContext.ChangeTracker.Clear();
        var tracked = DbContext.FileRecords.Single(f => f.Id == file.Id);
        tracked.ThumbnailPath = thumbnailPath;
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var result = await service.RestoreVersionAsync(file.Id, 1);

        Assert.True(result.Succeeded, result.Message);
        var request = Assert.Single(_deleteRequests);
        Assert.Equal(thumbnailPath, request.ThumbnailPath);
        Assert.True(await Storage.ExistsAsync(thumbnailPath));
    }

    [Fact]
    public async Task CreateVersionAsync_UpdateThrows_LeavesTheThumbnailObjectAndRequestsNothing()
    {
        // 行更新失败：记录仍指向旧缩略图，对象必须还在，也不能有一条删除请求在提交后把它删掉。
        var (file, thumbnailPath) = await SeedFileWithThumbnailAsync();
        var service = new FileVersionService(
            Repo<FileVersion>(),
            new ThrowingUpdateRepository(DbContext, ServiceProvider),
            Storage,
            TestFileAccessAuthorizer.AllowAll(),
            ServiceProvider,
            Guard(StorageOptions));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateVersionAsync(file.Id, new MemoryStream("v2"u8.ToArray()), "second"));

        Assert.True(await Storage.ExistsAsync(thumbnailPath));
        Assert.Empty(_deleteRequests);
        DbContext.ChangeTracker.Clear();
        Assert.Equal(thumbnailPath, DbContext.FileRecords.Single(f => f.Id == file.Id).ThumbnailPath);
    }

    private sealed class ThrowingUpdateRepository(WorkspaceTestDbContext dbContext, IServiceProvider serviceProvider)
        : EFCoreRepository<WorkspaceTestDbContext, FileRecord, Guid>(dbContext, serviceProvider: serviceProvider)
    {
        public override Task UpdateAsync(FileRecord entity, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated database failure.");
    }
}
