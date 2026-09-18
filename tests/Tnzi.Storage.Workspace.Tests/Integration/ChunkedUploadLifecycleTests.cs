namespace Tnzi.Storage.Workspace.Tests.Integration;

/// <summary>
/// 分片合并出的记录必须与直传 <c>SaveAsync</c> 同一口径消费 <c>isTemporary</c>：
/// 正式文件 <c>IsTemporary = false</c>、临时文件 <c>IsTemporary = true</c>，引用计数按同一条表达式落库。
/// </summary>
/// <remarks>
/// 此前 <c>CompleteChunkedUploadAsync</c> 的 <c>isTemporary</c> 形参从未被读取：整条纵切
/// （前端 → DTO → 控制器 → 契约）都在传这个开关，落库时却一律 <c>IsTemporary = false</c>、
/// 代码里写死 <c>ReferenceCount = 0</c>。请求「临时」得到的是一份永远不会被临时清理回收的正式文件。
/// ★ 对照用例写成与 <c>SaveAsync</c> 的<b>对照</b>而不是钉死数字：两条路径必须对同一个
/// <c>isTemporary</c> 产出同样的生命周期标记，这正是缺陷的定义。数字本身另有一层：
/// <c>FileRecordConfiguration</c> 的 <c>HasDefaultValue(1)</c> 曾让 EF 把 CLR 默认值 0 当作「未设置」，
/// 插入时改用数据库默认 1 —— 于是「临时」记录照样以 1 落库、孤儿回收永远选不中它，对照用例
/// 有没有修复都会绿。父模块已给该列配了哨兵；<c>Temporary_*</c> 两条用例从数据库回读 0
/// 并让孤儿回收真的删掉它，退回哨兵即变红。
/// SQLite 内存库跑得动整条路径（合并 → 落库 → 清理查询），无数据库差异。
/// </remarks>
public class ChunkedUploadLifecycleTests : WorkspaceIntegrationTestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteChunkedUpload_ProducesSameLifecycleFlagsAsDirectUpload(bool isTemporary)
    {
        var direct = await SaveDirectlyAsync(isTemporary);
        var chunked = await CompleteChunkedAsync(isTemporary);

        Assert.Equal(isTemporary, chunked.IsTemporary);
        Assert.Equal(direct.IsTemporary, chunked.IsTemporary);
        Assert.Equal(direct.ReferenceCount, chunked.ReferenceCount);
    }

    [Fact]
    public async Task CompleteChunkedUpload_NotTemporary_IsNotAnOrphan()
    {
        var record = await CompleteChunkedAsync(isTemporary: false);

        Assert.False(record.IsTemporary);
        Assert.Equal(1, record.ReferenceCount);
    }

    [Fact]
    public async Task CompleteChunkedUpload_Temporary_PersistsZeroReferences()
    {
        var record = await CompleteChunkedAsync(isTemporary: true);

        Assert.True(record.IsTemporary);
        Assert.Equal(0, record.ReferenceCount);
    }

    [Fact]
    public async Task CompleteChunkedUpload_Temporary_IsReclaimedByOrphanCleanupOnceUnboundPastRetention()
    {
        // 这才是「临时」的含义：没人在保留期内把它绑到实体上，孤儿回收就把记录与对象一起收走。
        var record = await CompleteChunkedAsync(isTemporary: true);
        await BackdatePastRetentionAsync(record);

        var deleted = await CreateCleanupService(ZeroRetention()).CleanupOrphanFilesAsync();

        Assert.Equal(1, deleted);
        DbContext.ChangeTracker.Clear();
        Assert.Null(await DbContext.FileRecords.FindAsync(record.Id));
        Assert.False(await Storage.ExistsAsync(record.Path!));
    }

    [Fact]
    public async Task CompleteChunkedUpload_NotTemporary_SurvivesOrphanCleanup()
    {
        // 钉住结论而不是钉住机制：正式文件不能被孤儿回收选中。保留期归零 + 把创建时间推到昨天，
        // 让「幸存」不取决于时钟精度。
        var record = await CompleteChunkedAsync(isTemporary: false);
        await BackdatePastRetentionAsync(record);

        var deleted = await CreateCleanupService(ZeroRetention()).CleanupOrphanFilesAsync();

        Assert.Equal(0, deleted);
        Assert.NotNull(await DbContext.FileRecords.FindAsync(record.Id));
        Assert.True(await Storage.ExistsAsync(record.Path!));
    }

    private async Task<FileRecord> SaveDirectlyAsync(bool isTemporary)
    {
        var result = await CreateStorageService().SaveAsync("direct.txt", new MemoryStream("abcdef"u8.ToArray()), isTemporary: isTemporary);
        Assert.True(result.Succeeded, result.Message);
        return await ReloadAsync(result.Data!.Id);
    }

    private async Task<FileRecord> CompleteChunkedAsync(bool isTemporary)
    {
        var service = CreateChunkUploadService();
        var session = (await service.InitiateChunkedUploadAsync("big.txt", 6, 3)).Data!;
        await service.UploadChunkAsync(session.Id, 0, new MemoryStream("abc"u8.ToArray()));
        await service.UploadChunkAsync(session.Id, 1, new MemoryStream("def"u8.ToArray()));

        var result = await service.CompleteChunkedUploadAsync(session.Id, isTemporary);

        Assert.True(result.Succeeded, result.Message);
        return await ReloadAsync(result.Data!.Id);
    }

    /// <summary>回读落库的那一行而不是信任返回值：断言的是持久化状态。</summary>
    private async Task<FileRecord> ReloadAsync(Guid id)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.FileRecords.SingleAsync(r => r.Id == id);
    }

    private async Task BackdatePastRetentionAsync(FileRecord record)
    {
        await DbContext.FileRecords
            .Where(r => r.Id == record.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(r => r.CreationTime, DateTime.UtcNow.AddDays(-1)));
        DbContext.ChangeTracker.Clear();
    }

    private StorageOptions ZeroRetention() => new()
    {
        MaxFileSize = StorageOptions.MaxFileSize,
        AllowedExtensions = StorageOptions.AllowedExtensions,
        AutoGenerateThumbnail = false,
        Cleanup = new CleanupOptions { OrphanFileRetentionHours = 0, MaxFilesPerRun = 100 }
    };
}
