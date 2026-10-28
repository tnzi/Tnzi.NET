using Microsoft.EntityFrameworkCore.Diagnostics;
using Tnzi.Data;

namespace Tnzi.Storage.Workspace.Tests.Integration;

/// <summary>
/// 批量挪进目录要么整批生效，要么一条都不变：转正不能在归档失败之后留下来。
/// </summary>
/// <remarks>
/// 被保护的缺陷：<c>MoveFilesToFolderAsync</c> 逐条转正（条件写，立即生效），之后才整批保存 <c>FolderId</c>，
/// 两步不在同一个工作单元里。批量保存失败时已经转正的临时记录不随之回滚，留下「转正了、没挪动」的记录。
/// 这里注册真实的工作单元管理器（生产装配的形状），在整批保存那一步注入失败，然后回读每一条记录。
/// </remarks>
public class FileMoveAtomicityTests : WorkspaceIntegrationTestBase
{
    // 字段初始化先于基类构造函数执行，ConfigureDbContextOptions 被调用时它已经就位
    private readonly FailingFolderAssignment _failure = new();

    protected override void ConfigureDbContextOptions(DbContextOptionsBuilder options)
        => options.AddInterceptors(_failure);

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        var entityManager = new Mock<IEntityManager>();
        entityManager.Setup(m => m.GetAllDbContextTypes()).Returns([typeof(WorkspaceTestDbContext)]);
        services.AddSingleton(entityManager.Object);
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();
    }

    [Fact]
    public async Task A_failing_batch_save_leaves_every_file_temporary_and_where_it_was()
    {
        var first = await SaveTemporaryAsync("first.txt");
        var second = await SaveTemporaryAsync("second.txt");
        var folder = (await CreateFolderService().CreateAsync(new CreateFileFolderDto { Name = "contracts" })).Data!;
        DbContext.ChangeTracker.Clear();

        _failure.Armed = true;
        await Assert.ThrowsAnyAsync<DbUpdateException>(
            () => CreateFolderService().MoveFilesToFolderAsync([first.Id, second.Id], folder.Id));

        foreach (var id in new[] { first.Id, second.Id })
        {
            var record = await ReloadAsync(id);
            Assert.True(record.IsTemporary, "a promotion must roll back with the failed move");
            Assert.Equal(0, record.ReferenceCount);
            Assert.Null(record.FolderId);
        }
    }

    /// <summary>对照：同一套装配下没有故障时，整批转正并挪进目录。</summary>
    [Fact]
    public async Task Without_a_failure_the_whole_batch_is_promoted_and_moved()
    {
        var first = await SaveTemporaryAsync("first.txt");
        var second = await SaveTemporaryAsync("second.txt");
        var folder = (await CreateFolderService().CreateAsync(new CreateFileFolderDto { Name = "contracts" })).Data!;
        DbContext.ChangeTracker.Clear();

        var result = await CreateFolderService().MoveFilesToFolderAsync([first.Id, second.Id], folder.Id);

        Assert.True(result.Succeeded, result.Message);
        foreach (var id in new[] { first.Id, second.Id })
        {
            var record = await ReloadAsync(id);
            Assert.False(record.IsTemporary);
            Assert.Equal(1, record.ReferenceCount);
            Assert.Equal(folder.Id, record.FolderId);
        }
    }

    private async Task<FileRecord> SaveTemporaryAsync(string name)
    {
        // 每份内容各不相同：相同字节会被 MD5 去重合并成一条记录
        var content = Encoding.UTF8.GetBytes($"{name}:{Guid.NewGuid()}");
        var result = await CreateStorageService().SaveAsync(name, new MemoryStream(content), isTemporary: true);
        Assert.True(result.Succeeded, result.Message);
        Assert.True(result.Data!.IsTemporary);
        DbContext.ChangeTracker.Clear();
        return result.Data;
    }

    private async Task<FileRecord> ReloadAsync(Guid id)
    {
        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkspaceTestDbContext>();
        return await db.FileRecords.AsNoTracking().SingleAsync(f => f.Id == id);
    }

    /// <summary>保存一条被改了 <c>FolderId</c> 的文件记录时抛异常：模拟整批归档那一步写库失败。</summary>
    private sealed class FailingFolderAssignment : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var assigning = eventData.Context?.ChangeTracker.Entries<FileRecord>()
                .Any(e => e.State == EntityState.Modified && e.Property(f => f.FolderId).IsModified) == true;
            if (Armed && assigning)
            {
                Armed = false;
                throw new DbUpdateException("simulated failure while saving the moved files");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
