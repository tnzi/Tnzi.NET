using Tnzi.EFCore.Dapper;
using Tnzi.EFCore.Dapper.Providers;

namespace Tnzi.EFCore.Tests.Dapper;

/// <summary>
/// <see cref="DapperBulkOperations.BulkInsertAsync{T}"/> 必须写入主键，并按参数总数封顶分批。
/// </summary>
/// <remarks>
/// <para>
/// 框架实体的 Id 全部在应用侧生成（Sequential GUID / Snowflake），库里没有默认值。此前 BulkInsert
/// 恒排除主键列：Guid 键整批 NOT NULL 违例，long 键落库 Id 与内存对不上且零报错。
/// 这条公开 API 此前零调用方零测试，从未被证明工作过。
/// </para>
/// <para>
/// 参数上限那一条 SQLite 测不出真实失败（SQL Server 每命令 2100 个参数），故分两层守：
/// 纯函数断言每批参数数不越界，再用一个上限很小的测试方言证明封顶真的接进了插入循环。
/// </para>
/// </remarks>
public class DapperBulkInsertTests : EFCoreTestBase
{
    private DapperService CreateService(IDatabaseProvider? provider = null) => new(DbContext, provider ?? new SqliteTestProvider());

    [Fact]
    public async Task BulkInsertAsync_GuidKeyedEntity_PersistsRowsWithCallerIds()
    {
        var products = new[]
        {
            new TestProduct { Id = Guid.NewGuid(), Name = "bulk-1", Price = 1m, Stock = 1 },
            new TestProduct { Id = Guid.NewGuid(), Name = "bulk-2", Price = 2m, Stock = 2 },
            new TestProduct { Id = Guid.NewGuid(), Name = "bulk-3", Price = 3m, Stock = 3 },
        };

        var inserted = await CreateService().BulkInsertAsync(products);

        Assert.Equal(3, inserted);
        foreach (var product in products)
        {
            var reloaded = await DbContext.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == product.Id);
            Assert.NotNull(reloaded);
            Assert.Equal(product.Name, reloaded!.Name);
        }
    }

    [Fact]
    public async Task BulkInsertAsync_GuidEntityWithDefaultId_GeneratesIdBeforeInsert()
    {
        var product = new TestProduct { Name = "generated-guid", Price = 1m, Stock = 1 };

        await CreateService().BulkInsertAsync([product]);

        Assert.NotEqual(Guid.Empty, product.Id);
        Assert.NotNull(await DbContext.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == product.Id));
    }

    [Fact]
    public async Task BulkInsertAsync_LongEntityWithDefaultId_GeneratesSnowflakeIdBeforeInsert()
    {
        var entity = new TestEntityWithLongId { Name = "generated-long" };

        await CreateService().BulkInsertAsync([entity]);

        Assert.NotEqual(0L, entity.Id);
        var reloaded = await DbContext.TestEntitiesWithLongId.AsNoTracking().FirstOrDefaultAsync(e => e.Id == entity.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("generated-long", reloaded!.Name);
    }

    /// <summary>
    /// 失败关闭：框架不生成 int 键，键为默认值时必须拒绝，而不是让数据库另发一套 Id。
    /// </summary>
    [Fact]
    public async Task BulkInsertAsync_IntEntityWithDefaultId_Throws()
    {
        var entity = new TestEntityWithIntId { Name = "no-id" };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService().BulkInsertAsync([entity]));

        Assert.Contains(nameof(TestEntityWithIntId), ex.Message);
        Assert.Equal(0, await DbContext.TestEntitiesWithIntId.CountAsync());
    }

    /// <summary>数据库自增键是显式选择：includeKey:false 时不写主键列，由库分配。</summary>
    [Fact]
    public async Task BulkInsertAsync_IncludeKeyFalse_LetsDatabaseGenerateKey()
    {
        var entities = new[]
        {
            new TestEntityWithIntId { Name = "db-1" },
            new TestEntityWithIntId { Name = "db-2" },
        };

        var connection = DbContext.Database.GetDbConnection();
        var inserted = await DapperBulkOperations.BulkInsertAsync(
            connection, new SqliteTestProvider(), DbContext, entities, includeKey: false);

        Assert.Equal(2, inserted);
        var names = await DbContext.TestEntitiesWithIntId.AsNoTracking().OrderBy(e => e.Id).Select(e => e.Name).ToListAsync();
        Assert.Equal(["db-1", "db-2"], names);
    }

    /// <summary>
    /// 封顶真的接进了插入循环：上限 8 个参数、每行 4 列 ⇒ 每批 2 行，5 条要分 3 批且全部落库。
    /// 同时把 SQLite 自己的变量上限也压到 8（<c>sqlite3_limit</c>），不分批的 20 参数命令会真的被拒绝，
    /// 这条用例才能在封顶没接线时变红。
    /// </summary>
    [Fact]
    public async Task BulkInsertAsync_ParameterLimit_SplitsBatchesAndPersistsEverything()
    {
        var products = Enumerable.Range(1, 5)
            .Select(i => new TestProduct { Name = $"capped-{i}", Price = i, Stock = i })
            .ToList();
        var provider = new SqliteTestProvider { MaxParametersPerCommand = 8 };
        var handle = ((SqliteConnection)DbContext.Database.GetDbConnection()).Handle!;
        SQLitePCL.raw.sqlite3_limit(handle, SQLitePCL.raw.SQLITE_LIMIT_VARIABLE_NUMBER, 8);

        var inserted = await CreateService(provider).BulkInsertAsync(products);

        Assert.Equal(5, inserted);
        Assert.Equal(5, await DbContext.Products.CountAsync(p => p.Name.StartsWith("capped-")));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(50)]
    [InlineData(2098)]
    public void CalculateRowsPerBatch_SqlServer_NeverExceeds2100Parameters(int parametersPerRow)
    {
        var limit = new SqlServerProvider().MaxParametersPerCommand;

        var rows = DapperBulkOperations.CalculateRowsPerBatch(1000, parametersPerRow, limit);

        Assert.True(rows >= 1);
        Assert.True(rows * parametersPerRow <= 2100, $"{rows} rows x {parametersPerRow} parameters exceeds 2100");
    }

    [Fact]
    public void CalculateRowsPerBatch_RequestedBatchSmallerThanLimit_KeepsRequested()
    {
        Assert.Equal(10, DapperBulkOperations.CalculateRowsPerBatch(10, 4, 2098));
    }

    [Fact]
    public void CalculateRowsPerBatch_RowWiderThanLimit_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => DapperBulkOperations.CalculateRowsPerBatch(1000, 3000, 2098));
    }
}
