using Tnzi.EventBus;
using Tnzi.Storage.Events;
using Tnzi.Storage.Workspace.Events.Handlers;

namespace Tnzi.Storage.Workspace.Tests.Integration;

/// <summary>
/// 文件删除之后，它的历史版本（行与对象）与分享链接不能留下。
/// </summary>
/// <remarks>
/// 父模块删记录时只删记录名下的原件与缩略图；建过新版本之后，旧版本的字节只由版本行指向，
/// 而读取与删除版本的端点都先查记录、一律 404。于是用户删掉的文件，每个旧版本都完整地永久留在存储里，
/// 再也没有途径删除或读取，管理端的版本审计页却仍列着这些行。
/// </remarks>
public class DeletedFileHistoryTests : WorkspaceIntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // 一个把 FileRecordDeletedEvent 交给真实处理器的总线，其余事件照旧什么都不做。
        var bus = new Mock<IEventBus> { DefaultValue = DefaultValue.Empty };
        bus.Setup(b => b.PublishAsync(It.IsAny<FileRecordDeletedEvent>(), It.IsAny<CancellationToken>()))
            .Returns<FileRecordDeletedEvent, CancellationToken>((e, ct) => new FileRecordDeletedEventHandler(CreateCleaner()).HandleAsync(e, ct));
        services.AddSingleton(bus.Object);
    }

    private DeletedFileWorkspaceCleaner CreateCleaner(IFileStorage? storage = null)
        => new(Repo<FileRecord>(), Repo<FileVersion>(), Repo<FileShare>(), Repo<RecycleBinEntry>(), storage ?? Storage, NullLogger<DeletedFileWorkspaceCleaner>.Instance);

    private DeletedFileWorkspaceCleanupContributor CreateContributor()
        => new(Repo<FileRecord>(), Repo<FileVersion>(), Repo<FileShare>(), Repo<RecycleBinEntry>(), CreateCleaner(), NullLogger<DeletedFileWorkspaceCleanupContributor>.Instance);

    /// <summary>一个建过两个新版本、发过一条分享链接的文件。返回它与三个版本各自的对象路径。</summary>
    private async Task<(FileRecord File, List<string> VersionPaths)> CreateVersionedSharedFileAsync(string name)
    {
        var file = await CreateStoredFileAsync(name, Encoding.UTF8.GetBytes($"{name} v1"));
        var versions = CreateVersionService();
        Assert.True((await versions.CreateVersionAsync(file.Id, new MemoryStream(Encoding.UTF8.GetBytes($"{name} v2")))).Succeeded);
        Assert.True((await versions.CreateVersionAsync(file.Id, new MemoryStream(Encoding.UTF8.GetBytes($"{name} v3")))).Succeeded);
        Assert.True((await CreateShareService().CreateShareAsync(file.Id)).Succeeded);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var paths = await DbContext.FileVersions.AsNoTracking()
            .Where(v => v.FileId == file.Id)
            .Select(v => v.Path)
            .ToListAsync();
        Assert.Equal(3, paths.Count);
        foreach (var path in paths)
            Assert.True(await Storage.ExistsAsync(path), path);

        return (file, paths);
    }

    private async Task AssertNothingLeftAsync(Guid fileId, IEnumerable<string> versionPaths)
    {
        DbContext.ChangeTracker.Clear();
        Assert.Empty(await DbContext.FileVersions.AsNoTracking().Where(v => v.FileId == fileId).ToListAsync());
        Assert.Empty(await DbContext.FileShares.AsNoTracking().Where(s => s.FileId == fileId).ToListAsync());
        foreach (var path in versionPaths)
            Assert.False(await Storage.ExistsAsync(path), $"version object {path} should be gone");
    }

    [Fact]
    public async Task DeletingAVersionedFile_TakesEveryVersionAndShareWithIt()
    {
        var (file, versionPaths) = await CreateVersionedSharedFileAsync("contract.txt");

        var deleted = await CreateStorageService().DeleteAsync(file.Id);
        Assert.True(deleted.Succeeded, deleted.Message);

        Assert.Null(await DbContext.FileRecords.AsNoTracking().SingleOrDefaultAsync(f => f.Id == file.Id));
        await AssertNothingLeftAsync(file.Id, versionPaths);
    }

    [Fact]
    public async Task TheCleanupContributor_SweepsWhatAnEarlierDeletionLeftBehind()
    {
        // 这条清理出现之前删掉的文件：记录没了，版本与分享还在。另一个仍在的文件是正控制。
        var (gone, gonePaths) = await CreateVersionedSharedFileAsync("old.txt");
        var (kept, keptPaths) = await CreateVersionedSharedFileAsync("kept.txt");
        await DbContext.FileRecords.Where(f => f.Id == gone.Id).ExecuteDeleteAsync();
        DbContext.ChangeTracker.Clear();

        var removed = await CreateContributor().CleanupAsync(maxItems: 100);

        Assert.Equal(4, removed); // 三个版本 + 一条分享
        await AssertNothingLeftAsync(gone.Id, gonePaths);

        Assert.Equal(3, await DbContext.FileVersions.AsNoTracking().CountAsync(v => v.FileId == kept.Id));
        Assert.Equal(1, await DbContext.FileShares.AsNoTracking().CountAsync(s => s.FileId == kept.Id));
        foreach (var path in keptPaths)
            Assert.True(await Storage.ExistsAsync(path), path);
    }

    [Fact]
    public async Task TheCleaner_NeverTouchesAFileThatStillExists()
    {
        var (file, paths) = await CreateVersionedSharedFileAsync("alive.txt");

        var removed = await CreateCleaner().PurgeAsync(file.Id);

        Assert.Equal(0, removed);
        Assert.Equal(3, await DbContext.FileVersions.AsNoTracking().CountAsync(v => v.FileId == file.Id));
        foreach (var path in paths)
            Assert.True(await Storage.ExistsAsync(path), path);
    }

    [Fact]
    public async Task AVersionObjectThatCannotBeDeleted_KeepsTheRowsForTheNextAttempt()
    {
        var (file, _) = await CreateVersionedSharedFileAsync("stuck.txt");
        await DbContext.FileRecords.Where(f => f.Id == file.Id).ExecuteDeleteAsync();
        DbContext.ChangeTracker.Clear();
        var failing = new Mock<IFileStorage>();
        failing.Setup(s => s.DeleteAsync(It.IsAny<string>())).ThrowsAsync(new IOException("provider unavailable"));

        await Assert.ThrowsAsync<AggregateException>(() => CreateCleaner(failing.Object).PurgeAsync(file.Id));

        // 行还在：下一次（事件重试、下一趟后台清理）还找得到这些对象。
        Assert.Equal(3, await DbContext.FileVersions.AsNoTracking().CountAsync(v => v.FileId == file.Id));
        Assert.Equal(1, await DbContext.FileShares.AsNoTracking().CountAsync(s => s.FileId == file.Id));
    }

    /// <summary>
    /// ★★ 内置 provider 的故障形态不是抛异常，而是吞掉它报 <c>false</c>。此前清理只看有没有抛：
    /// 报 <c>false</c> 的对象被当成删掉了，行随即删除，对象从此没有任何途径找回。
    /// </summary>
    [Fact]
    public async Task AVersionObjectTheProviderReportsAsNotDeleted_KeepsTheRows()
    {
        var (file, paths) = await CreateVersionedSharedFileAsync("refused.txt");
        await DbContext.FileRecords.Where(f => f.Id == file.Id).ExecuteDeleteAsync();
        DbContext.ChangeTracker.Clear();
        var refusing = new Mock<IFileStorage>();
        refusing.Setup(s => s.DeleteAsync(It.IsAny<string>())).ReturnsAsync(false);
        refusing.Setup(s => s.ExistsAsync(It.IsAny<string>())).Returns<string>(path => Storage.ExistsAsync(path));

        await Assert.ThrowsAsync<AggregateException>(() => CreateCleaner(refusing.Object).PurgeAsync(file.Id));

        Assert.Equal(3, await DbContext.FileVersions.AsNoTracking().CountAsync(v => v.FileId == file.Id));
        foreach (var path in paths)
            Assert.True(await Storage.ExistsAsync(path), path);
    }

    /// <summary>对照：报 <c>false</c> 而对象本来就不在（上一次已经删掉）是成功，行照常收掉。</summary>
    [Fact]
    public async Task VersionObjectsThatAreAlreadyGone_StillLetTheRowsGo()
    {
        var (file, paths) = await CreateVersionedSharedFileAsync("gone.txt");
        await DbContext.FileRecords.Where(f => f.Id == file.Id).ExecuteDeleteAsync();
        DbContext.ChangeTracker.Clear();
        foreach (var path in paths)
            await Storage.DeleteAsync(path);

        var removed = await CreateCleaner().PurgeAsync(file.Id);

        Assert.Equal(4, removed);
        await AssertNothingLeftAsync(file.Id, paths);
    }
}
