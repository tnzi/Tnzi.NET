using Microsoft.Extensions.Logging.Abstractions;

namespace Tnzi.EFCore.Tests;

/// <summary>
/// 没有任何迁移文件的上下文，启动时的数据库初始化必须把表建出来。
/// </summary>
/// <remarks>
/// ★ 此前「库存不存在」靠 <c>CanConnect</c> 判断，而 SQLite 打开连接即创建空文件：只要启动早些时候有任何一次数据库访问，
/// 文件就已经在了，建表分支从此走不到；随后对一个没有迁移的上下文调 <c>MigrateAsync</c> 一张业务表都不建，
/// 最多留下一张空的历史表，接着的 <c>EnsureCreated</c> 又因为「库里已经有表」什么都不做。
/// 结果是新库启动成功，第一条查询报 no such table。
/// </remarks>
public sealed class EfCoreDbInitializerTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), "tnzi-dbinit-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly ServiceProvider _provider;

    public EfCoreDbInitializerTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentUser>(new MockCurrentUser());
        services.AddDbContext<TestDbContext>(o => o.UseSqlite($"Data Source={_databasePath}"));
        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(_databasePath);
        }
        catch (IOException)
        {
            // 临时目录里的残留文件不影响结论
        }
    }

    private async Task InitializeAsync()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await new EFCoreDbInitializer<TestDbContext>(context, scope.ServiceProvider, NullLogger<EFCoreDbInitializer<TestDbContext>>.Instance)
            .MigrateAsync();
    }

    private async Task<int> CountProductsAsync()
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TestDbContext>().Products.CountAsync();
    }

    [Fact]
    public async Task NoMigrations_NewSqliteFile_TablesAreCreated()
    {
        await InitializeAsync();

        Assert.Equal(0, await CountProductsAsync());
    }

    /// <summary>启动早些时候已经打开过一次连接（文件已被建出来、一张表都没有）：照样建表。</summary>
    [Fact]
    public async Task NoMigrations_SqliteFileAlreadyOpenedOnce_TablesAreStillCreated()
    {
        await using (var connection = new SqliteConnection($"Data Source={_databasePath}"))
        {
            await connection.OpenAsync();
        }

        Assert.True(File.Exists(_databasePath));

        await InitializeAsync();

        Assert.Equal(0, await CountProductsAsync());
    }

    /// <summary>已经建好表的库：再次初始化不动它，数据还在。</summary>
    [Fact]
    public async Task NoMigrations_ExistingSchema_IsLeftAlone()
    {
        await InitializeAsync();
        using (var scope = _provider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            context.Products.Add(new TestProduct { Id = Guid.NewGuid(), Name = "kept", Price = 1m, Stock = 1 });
            await context.SaveChangesAsync();
        }

        await InitializeAsync();

        Assert.Equal(1, await CountProductsAsync());
    }
}
