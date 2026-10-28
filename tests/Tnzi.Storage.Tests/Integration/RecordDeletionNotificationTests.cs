using Tnzi.EFCore;
using Tnzi.EventBus;
using Tnzi.Storage.Events;

namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// 每一条删掉文件记录的路径都要发 <see cref="FileRecordDeletedEvent"/>（别的程序集据此收掉挂在这个文件上的历史版本与分享），
/// 也都要把记录名下的对象一并删掉或请求删除。
/// </summary>
/// <remarks>
/// 手动清理临时文件的那条路径（<c>FileReferenceService.CleanupTemporaryFilesAsync</c>，管理端 <c>cleanup-temporary</c>）
/// 此前只删行：原件与缩略图留在存储里，而孤儿回收按记录枚举，永远找不到它们。
/// </remarks>
public class RecordDeletionNotificationTests : StorageIntegrationTestBase
{
    private readonly List<object> _published = [];

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        var bus = new Mock<IEventBus> { DefaultValue = DefaultValue.Empty };
        bus.Setup(b => b.PublishAsync(It.IsAny<FileRecordDeletedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<FileRecordDeletedEvent, CancellationToken>((e, _) => _published.Add(e))
            .Returns(Task.CompletedTask);
        bus.Setup(b => b.PublishAsync(It.IsAny<FileDeleteRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<FileDeleteRequestedEvent, CancellationToken>((e, _) => _published.Add(e))
            .Returns(Task.CompletedTask);
        services.AddSingleton(bus.Object);
    }

    private FileReferenceService CreateReferenceService()
        => new(
            new EFCoreRepository<StorageTestDbContext, FileRecord, Guid>(DbContext, serviceProvider: ServiceProvider),
            new EFCoreRepository<StorageTestDbContext, FileReference, Guid>(DbContext, serviceProvider: ServiceProvider),
            ServiceProvider);

    private async Task<FileRecord> UploadTemporaryAsync(string name)
    {
        var result = await CreateStorageService().SaveAsync(name, new MemoryStream(System.Text.Encoding.UTF8.GetBytes(name)), isTemporary: true);
        Assert.True(result.Succeeded, result.Message);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return result.Data!;
    }

    [Fact]
    public async Task DeleteAsync_AnnouncesTheDeletedRecord()
    {
        var file = await UploadTemporaryAsync("draft.txt");

        var deleted = await CreateStorageService().DeleteAsync(file.Id);

        Assert.True(deleted.Succeeded, deleted.Message);
        Assert.Equal(file.Id, Assert.Single(_published.OfType<FileRecordDeletedEvent>()).FileId);
    }

    [Fact]
    public async Task OrphanCleanup_AnnouncesEveryDeletedRecord()
    {
        var first = await UploadTemporaryAsync("a.txt");
        var second = await UploadTemporaryAsync("b.txt");
        await DbContext.FileRecords.ExecuteUpdateAsync(s => s.SetProperty(f => f.CreationTime, DateTime.UtcNow.AddHours(-500)));
        DbContext.ChangeTracker.Clear();

        var removed = await CreateCleanupService().CleanupOrphanFilesAsync();

        Assert.Equal(2, removed);
        Assert.Equal(
            new[] { first.Id, second.Id }.OrderBy(id => id),
            _published.OfType<FileRecordDeletedEvent>().Select(e => e.FileId).OrderBy(id => id));
    }

    /// <summary>
    /// ★★ The maintenance page lists and cleans temporary uploads, which carry no reference row at all.
    /// Both used to walk temporary reference rows only, so the list was always empty and the cleanup always reported 0.
    /// </summary>
    [Fact]
    public async Task ManualTemporaryCleanup_FindsAndRemovesAnAbandonedUploadWithoutAReferenceRow()
    {
        var abandoned = await UploadTemporaryAsync("abandoned-upload.txt");
        var fresh = await UploadTemporaryAsync("fresh-upload.txt");
        await DbContext.FileRecords.Where(f => f.Id == abandoned.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.CreationTime, DateTime.UtcNow.AddHours(-30)));
        DbContext.ChangeTracker.Clear();

        var listed = await CreateReferenceService().GetTemporaryFilesAsync(TimeSpan.FromHours(24));
        Assert.True(listed.Succeeded, listed.Message);
        Assert.Equal(abandoned.Id, Assert.Single(listed.Data!).Id);

        var result = await CreateReferenceService().CleanupTemporaryFilesAsync(TimeSpan.FromHours(24));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(1, result.Data);
        Assert.Null(await DbContext.FileRecords.AsNoTracking().SingleOrDefaultAsync(f => f.Id == abandoned.Id));
        Assert.NotNull(await DbContext.FileRecords.AsNoTracking().SingleOrDefaultAsync(f => f.Id == fresh.Id));
        Assert.Equal(abandoned.Path, Assert.Single(_published.OfType<FileDeleteRequestedEvent>()).FilePath);
    }

    /// <summary>
    /// A zero or negative age puts the cutoff at now or later, which would take uploads whose form is still being filled in.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task ManualTemporaryCleanup_RefusesAnAgeUnderOneHour(int hours)
    {
        var fresh = await UploadTemporaryAsync("in-progress.txt");

        var result = await CreateReferenceService().CleanupTemporaryFilesAsync(TimeSpan.FromHours(hours));

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.NotNull(await DbContext.FileRecords.AsNoTracking().SingleOrDefaultAsync(f => f.Id == fresh.Id));
    }

    [Fact]
    public async Task ManualTemporaryCleanup_RequestsTheObjectsDeletion_AndAnnouncesTheRecord()
    {
        var file = await UploadTemporaryAsync("abandoned.txt");
        DbContext.FileReferences.Add(new FileReference
        {
            FileId = file.Id,
            EntityType = "Draft",
            EntityId = Guid.NewGuid(),
            FieldName = "Attachment",
            IsTemporary = true
        });
        await DbContext.SaveChangesAsync();
        await DbContext.FileReferences.ExecuteUpdateAsync(s => s.SetProperty(r => r.CreationTime, DateTime.UtcNow.AddDays(-3)));
        DbContext.ChangeTracker.Clear();

        var result = await CreateReferenceService().CleanupTemporaryFilesAsync();

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(1, result.Data);
        Assert.Null(await DbContext.FileRecords.AsNoTracking().SingleOrDefaultAsync(f => f.Id == file.Id));
        var objectDeletion = Assert.Single(_published.OfType<FileDeleteRequestedEvent>());
        Assert.Equal(file.Id, objectDeletion.FileId);
        Assert.Equal(file.Path, objectDeletion.FilePath);
        Assert.Equal(file.Id, Assert.Single(_published.OfType<FileRecordDeletedEvent>()).FileId);
    }
}
