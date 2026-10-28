using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Tnzi.Data;
using Tnzi.EFCore;

namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// 单条删除与批量删除一样，在一个工作单元里完成：计数 -1 与条件删除记录落在同一个事务里。
/// </summary>
/// <remarks>
/// <para>
/// <b>被保护的缺陷</b>：<c>DeleteAsync</c> 此前不开事务，原子 -1 当场提交。之后、删对象之前落下的一次 +1
/// （同一个人的 MD5 复用、一次实体绑定）让计数回到 1：对象照删，随后的条件删除因计数不为 0 而留下了记录，
/// 刚拿到这条记录的人手里是一个指向已删除对象的文件（完整性校验报 Missing，下载 404 / 500）。
/// 在一个事务里，-1 的行锁持有到提交，并发的 +1 等着它，提交之后找不到这一行。
/// </para>
/// <para>
/// SQLite 测试库里只有一个连接，「另一个事务被行锁挡住」排不出来；这里验的是那个前提：两条写落在同一个事务上。
/// 注册的是<b>真实的</b> <c>UnitOfWorkManager</c>，没有它 <c>ExecuteInUnitOfWorkAsync</c> 会直接执行。
/// </para>
/// </remarks>
public class SingleDeleteAtomicityTests : StorageIntegrationTestBase
{
    private readonly WriteRecorder _recorder = new();

    protected override void ConfigureDbContextOptions(DbContextOptionsBuilder options)
        => options.AddInterceptors(_recorder);

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        var entityManager = new Mock<IEntityManager>();
        entityManager.Setup(m => m.GetAllDbContextTypes()).Returns([typeof(StorageTestDbContext)]);
        services.AddSingleton(entityManager.Object);
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();
    }

    private string RecordTable => DbContext.Model.FindEntityType(typeof(FileRecord))!.GetTableName()!;

    [Fact]
    public void TheFixtureHasARealUnitOfWorkManager()
    {
        Assert.IsType<UnitOfWorkManager>(ServiceProvider.GetRequiredService<IUnitOfWorkManager>());
    }

    [Fact]
    public async Task ReleasingTheLastHoldAndDeletingTheRecord_RunInOneTransaction()
    {
        var file = await CreateStoredFileAsync("solo.txt", "solo"u8.ToArray());
        DbContext.ChangeTracker.Clear();
        _recorder.Start(RecordTable);

        var result = await CreateStorageService().DeleteAsync(file.Id);

        Assert.True(result.Succeeded, result.Message);
        Assert.False(await DbContext.FileRecords.AsNoTracking().AnyAsync(f => f.Id == file.Id));
        Assert.False(await Storage.ExistsAsync(file.Path!));

        var release = Assert.Single(_recorder.Writes, w => w.Verb == "UPDATE");
        var delete = Assert.Single(_recorder.Writes, w => w.Verb == "DELETE");
        Assert.NotNull(release.Transaction);
        Assert.Same(release.Transaction, delete.Transaction);
    }

    /// <summary>对照：仍有别的持有者时只减计数，事务照常提交（不因为开了工作单元而丢掉这次 -1）。</summary>
    [Fact]
    public async Task ReleasingOneOfTwoHolds_CommitsTheDecrement()
    {
        var file = await CreateStoredFileAsync("shared.txt", "shared"u8.ToArray());
        await DbContext.FileRecords.Where(f => f.Id == file.Id).ExecuteUpdateAsync(s => s.SetProperty(f => f.ReferenceCount, 2));
        DbContext.ChangeTracker.Clear();

        var result = await CreateStorageService().DeleteAsync(file.Id);

        Assert.True(result.Succeeded, result.Message);
        DbContext.ChangeTracker.Clear();
        Assert.Equal(1, await DbContext.FileRecords.AsNoTracking().Where(f => f.Id == file.Id).Select(f => f.ReferenceCount).SingleAsync());
        Assert.True(await Storage.ExistsAsync(file.Path!));
    }

    /// <summary>记下对某张表的每一条 UPDATE / DELETE 落在哪个事务上（没有事务为 null）。</summary>
    private sealed class WriteRecorder : DbCommandInterceptor
    {
        private string? _table;

        public List<(string Verb, DbTransaction? Transaction)> Writes { get; } = [];

        public void Start(string table)
        {
            _table = table;
            Writes.Clear();
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        private void Record(DbCommand command)
        {
            if (_table == null || !command.CommandText.Contains($"\"{_table}\"", StringComparison.Ordinal))
                return;

            var text = command.CommandText.TrimStart();
            foreach (var verb in new[] { "UPDATE", "DELETE" })
            {
                if (text.StartsWith(verb, StringComparison.OrdinalIgnoreCase))
                    Writes.Add((verb, command.Transaction));
            }
        }
    }
}
