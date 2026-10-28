using Mapster;
using MapsterMapper;
using Tnzi.Mapster;
using Tnzi.Storage.Controllers;

namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// 直传 <c>POST files/upload</c> 的生命周期由调用方表达：不传是临时上传（先传后绑），
/// 传 <c>isTemporary=false</c> 是正式记录（传上来就是为了留着）。
/// </summary>
/// <remarks>
/// <b>被保护的缺陷</b>：直传恒落临时记录，没有任何参数能要求正式记录。文件库（管理端文件管理器）
/// 用它上传 90 MB 以下的文件，这些记录没有任何 <c>[FileField]</c> 引用，开启后台清理后过了保留期
/// 就连同缩略图一起被孤儿回收删掉；同一页面上走分片的大文件却是正式记录、永久保留，
/// 一个文件活多久取决于它有多大。
/// </remarks>
public class UploadLifecycleTests : StorageIntegrationTestBase
{
    public UploadLifecycleTests()
    {
        // 控制器把记录映射成 DTO 返回，宿主里没有 Mapster 模块时要先给一个映射器
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
    }

    [Fact]
    public async Task Upload_WithoutALifecycle_StaysATemporaryUpload()
    {
        var record = await UploadAsync("avatar.txt", isTemporary: null);

        Assert.True(record.IsTemporary);
        Assert.Equal(0, record.ReferenceCount);
    }

    [Fact]
    public async Task Upload_AskingForAPermanentRecord_StoresOneHeldByTheUploadItself()
    {
        var record = await UploadAsync("contract.txt", isTemporary: false);

        Assert.False(record.IsTemporary);
        Assert.Equal(1, record.ReferenceCount);
    }

    /// <summary>
    /// 端到端：过了保留期，正式上传原样保留；同一轮里没人绑定的临时上传照常回收（正控制）。
    /// </summary>
    [Fact]
    public async Task APermanentUpload_SurvivesOrphanCleanupPastTheRetention()
    {
        var kept = await UploadAsync("contract.txt", isTemporary: false);
        var abandoned = await UploadAsync("draft.txt", isTemporary: null);
        await AgeAsync(kept.Id, abandoned.Id);

        var deleted = await CreateCleanupService().CleanupOrphanFilesAsync();

        Assert.Equal(1, deleted);
        Assert.NotNull(await DbContext.FileRecords.FindAsync(kept.Id));
        Assert.Null(await DbContext.FileRecords.FindAsync(abandoned.Id));
    }

    private async Task<FileRecordDto> UploadAsync(string name, bool? isTemporary)
    {
        // 每份内容各不相同：相同字节会被 MD5 去重合并成一条记录
        var content = System.Text.Encoding.UTF8.GetBytes($"{name}:{Guid.NewGuid()}");
        var controller = new DefaultStorageController(CreateStorageService());
        var file = new FormFile(new MemoryStream(content), 0, content.Length, "file", name);

        var result = await controller.Upload(file, isPublic: false, isTemporary: isTemporary);

        Assert.True(result.Succeeded, result.Message);
        DbContext.ChangeTracker.Clear();
        return result.Data!;
    }

    private async Task AgeAsync(params Guid[] ids)
    {
        var cutoff = DateTime.UtcNow.AddHours(-(StorageOptions.Cleanup.OrphanFileRetentionHours + 24));
        await DbContext.FileRecords
            .Where(f => ids.Contains(f.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.CreationTime, cutoff));
        DbContext.ChangeTracker.Clear();
    }
}
