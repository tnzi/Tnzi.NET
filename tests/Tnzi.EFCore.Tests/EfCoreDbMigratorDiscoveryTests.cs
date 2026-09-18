using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Tnzi.EFCore.Tests;

/// <summary>
/// <see cref="EfCoreDbMigrator"/> 对主 DbContext 的发现：它决定 <c>MigrateAsync</c> 与
/// <c>GetPendingMigrationsAsync</c> 到底看不看得见主库。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ 与 <c>UnitOfWorkManager</c> 同一个根因、同一批漏网：迁移器此前只经
/// <c>IEntityManager.GetAllDbContextTypes()</c> 发现上下文，而它刻意排除承载主上下文实体的 <c>object</c> 占位键
/// （实体配置的 <c>DbContextType</c> 默认为 null，参考消费方一处都没覆写）。于是注入 <c>IDbMigrator</c>
/// 的消费方得到「No DbContext types found. Skipping migration.」或只迁了模块自带的次要上下文；
/// <c>HasPendingMigrationsAsync()</c> 对主库答 false。工作单元那侧改读 <c>RegisteredDbContext</c> 时没有回头改这里。
/// </para>
/// <para>
/// 夹具：真实 <see cref="EntityManager"/>（不 Mock）+ 一个经 <c>AddTnziDbContext</c> 注册、实体没有 IEntityRegister
/// 的两参上下文 + 一条只属于它的迁移（<see cref="MigratorProbeMigration"/>，建一张表）。
/// 迁移「有没有被看见 / 有没有被应用」都可以从表存不存在直接观察，与「没有迁移可应用」区分得开。
/// </para>
/// </remarks>
public class EfCoreDbMigratorDiscoveryTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ServiceProvider _provider;

    public EfCoreDbMigratorDiscoveryTests()
    {
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentUser>(new MockCurrentUser());
        services.AddSingleton<IEntityManager, EntityManager>();
        services.AddScoped<IDbMigrator, EfCoreDbMigrator>();
        services.AddTnziDbContext<MigratorProbeDbContext>(options => options.UseSqlite(_connection), isPrimary: true);

        _provider = services.BuildServiceProvider();
        _provider.GetRequiredService<IEntityManager>().Initialize();
    }

    /// <summary>前提：这个夹具下 EntityManager 确实报不出主上下文，否则下面测的是别的东西。</summary>
    [Fact]
    public void Precondition_EntityManagerDoesNotReportThePrimaryContext()
    {
        Assert.DoesNotContain(typeof(MigratorProbeDbContext), _provider.GetRequiredService<IEntityManager>().GetAllDbContextTypes());
    }

    [Fact]
    public async Task GetPendingMigrations_ReachesTheRegisteredPrimaryContext()
    {
        using var scope = _provider.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<IDbMigrator>();

        var pending = await migrator.GetPendingMigrationsAsync();

        Assert.Contains(pending, m => m.Contains(MigratorProbeMigration.Id, StringComparison.Ordinal));
        Assert.True(await migrator.HasPendingMigrationsAsync());
    }

    /// <summary>修复前：记一条 Warning 后直接返回，表永远建不出来。</summary>
    [Fact]
    public async Task Migrate_AppliesTheRegisteredPrimaryContextsMigrations()
    {
        using var scope = _provider.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<IDbMigrator>();

        await migrator.MigrateAsync();

        Assert.True(await TableExistsAsync(MigratorProbeMigration.TableName));
        Assert.False(await migrator.HasPendingMigrationsAsync());
        Assert.Contains(await migrator.GetAppliedMigrationsAsync(), m => m.Contains(MigratorProbeMigration.Id, StringComparison.Ordinal));
    }

    /// <summary>
    /// 失败方向关闭：DbContext 绕过 <c>AddTnziDbContext</c>（裸 <c>AddDbContext</c>）时迁移器抛而不是
    /// 「No DbContext types found. Skipping migration.」—— 那一行 Warning 之后主库永远不迁而调用方以为迁过了。
    /// </summary>
    [Fact]
    public async Task ContextRegisteredOutsideTheFunnel_FailsClosed()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentUser>(new MockCurrentUser());
        services.AddSingleton<IEntityManager, EntityManager>();
        services.AddScoped<IDbMigrator, EfCoreDbMigrator>();
        services.AddDbContext<MigratorProbeDbContext>(options => options.UseSqlite(_connection));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<IDbMigrator>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => migrator.MigrateAsync());

        Assert.Contains(nameof(MigratorProbeDbContext), ex.Message);
        Assert.False(await TableExistsAsync(MigratorProbeMigration.TableName));
    }

    private async Task<bool> TableExistsAsync(string tableName)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>脚手架形状的两参上下文，实体没有 IEntityRegister（落在 EntityManager 的 object 占位键下）。</summary>
public class MigratorProbeDbContext : TnziDbContext<MigratorProbeDbContext>
{
    public MigratorProbeDbContext(DbContextOptions<MigratorProbeDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    public DbSet<MigratorProbeRow> Rows => Set<MigratorProbeRow>();
}

public class MigratorProbeRow : EntityBase<Guid>
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>只属于 <see cref="MigratorProbeDbContext"/> 的一条迁移：建一张可观察的表。</summary>
[DbContext(typeof(MigratorProbeDbContext))]
[Migration(Id)]
public class MigratorProbeMigration : Migration
{
    public const string Id = "20260912000000_MigratorProbe";
    public const string TableName = "MigratorProbeRow";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: TableName,
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                Name = table.Column<string>(nullable: false),
            },
            constraints: table => table.PrimaryKey($"PK_{TableName}", x => x.Id));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(TableName);
    }
}
