using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Data.SqlClient;

namespace Tnzi.EFCore.Tests;

/// <summary>
/// 框架实体的单列 long 主键由框架生成 Snowflake 值，不再是数据库生成列。
/// </summary>
/// <remarks>
/// <para>
/// ★★ EF 的约定把 long 主键配成数据库生成（SQL Server 上是 IDENTITY），而 <c>SaveChanges</c> 的自动赋 Id 总会填一个 Snowflake 值。
/// SQL Server 拒绝向 IDENTITY 列显式插值（IDENTITY_INSERT 为 OFF），这类实体（功能用量记录、MCP 工具调用记录）
/// 在 SQL Server 部署上一行都写不进去，后台发送器记一条错误后整批丢弃。SQLite / PostgreSQL 照收显式值，所以别处的测试全绿。
/// </para>
/// <para>
/// 下面在 SQL Server 的模型与建表脚本上断言（不连库）；真实 SQL Server 上的插入由环境变量
/// <see cref="SqlServerFactAttribute.ConnectionStringVariable"/> 给出连接串时才跑。
/// </para>
/// </remarks>
public class SnowflakeLongKeyTests
{
    private static TestDbContext SqlServerContext()
        => new(new DbContextOptionsBuilder<TestDbContext>()
                .UseSqlServer("Server=(localdb)\\unused;Database=ModelOnly;Trusted_Connection=True")
                .Options,
            new MockCurrentUser());

    private static IProperty LongKey(DbContext context)
        => context.Model.FindEntityType(typeof(TestEntityWithLongId))!.FindPrimaryKey()!.Properties.Single();

    [Fact]
    public void SqlServerModel_LongKeyIsNotAnIdentityColumn()
    {
        using var context = SqlServerContext();
        var key = LongKey(context);

        Assert.Equal(ValueGenerated.Never, key.ValueGenerated);
        Assert.Equal(SqlServerValueGenerationStrategy.None, SqlServerPropertyExtensions.GetValueGenerationStrategy(key));
        Assert.DoesNotContain(key.GetAnnotations(), a => a.Name == "SqlServer:Identity");
    }

    [Fact]
    public void SqlServerCreateScript_DoesNotDeclareTheLongKeyAsIdentity()
    {
        using var context = SqlServerContext();

        var script = context.Database.GenerateCreateScript();
        var table = script[script.IndexOf("[TestEntitiesWithLongId]", StringComparison.Ordinal)..];
        table = table[..table.IndexOf(");", StringComparison.Ordinal)];

        Assert.Contains("[Id] bigint NOT NULL", table);
        Assert.DoesNotContain("IDENTITY", table, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Guid 主键的处理不变：仍是客户端生成、换成框架的生成器。</summary>
    [Fact]
    public void SqlServerModel_GuidKeyStillUsesTheFrameworkGenerator()
    {
        using var context = SqlServerContext();
        var key = context.Model.FindEntityType(typeof(TestEntityWithGuidId))!.FindPrimaryKey()!.Properties.Single();

        Assert.Equal(ValueGenerated.OnAdd, key.ValueGenerated);
        Assert.NotNull(key.GetValueGeneratorFactory());
    }

    /// <summary>使用方显式选了数据库生成（<c>UseIdentityColumn()</c>）的 long 主键，约定不覆盖。</summary>
    [Fact]
    public void ExplicitIdentityColumn_IsLeftAlone()
    {
        using var context = new ExplicitIdentityContext(new DbContextOptionsBuilder<ExplicitIdentityContext>()
            .UseSqlServer("Server=(localdb)\\unused;Database=ModelOnly;Trusted_Connection=True")
            .Options);
        var key = LongKey(context);

        Assert.Equal(SqlServerValueGenerationStrategy.IdentityColumn, SqlServerPropertyExtensions.GetValueGenerationStrategy(key));
    }

    /// <summary>
    /// Id 在 <c>Add</c> 时就由框架生成：批量新增的多个实体不会都以 0 为键在变更跟踪器里相撞，落库后各不相同。
    /// </summary>
    [Fact]
    public async Task AddRange_AssignsDistinctSnowflakeKeysAtAdd_AndPersistsThem()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new TestDbContext(
            new DbContextOptionsBuilder<TestDbContext>().UseSqlite(connection).Options, new MockCurrentUser());
        await context.Database.EnsureCreatedAsync();

        var records = new[] { new TestEntityWithLongId { Name = "a" }, new TestEntityWithLongId { Name = "b" } };
        context.TestEntitiesWithLongId.AddRange(records);

        Assert.All(records, r => Assert.NotEqual(0L, r.Id));
        Assert.NotEqual(records[0].Id, records[1].Id);

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Equal(2, await context.TestEntitiesWithLongId.CountAsync(e => e.Id == records[0].Id || e.Id == records[1].Id));
    }

    /// <summary>真实 SQL Server：此前每一次插入都以「Cannot insert explicit value for identity column」失败。</summary>
    [SqlServerFact]
    public async Task SqlServer_InsertsLongKeyRecords()
    {
        var builder = new SqlConnectionStringBuilder(SqlServerFactAttribute.ConnectionString)
        {
            InitialCatalog = "TnziSnowflakeKey_" + Guid.NewGuid().ToString("N")
        };
        await using var context = new TestDbContext(
            new DbContextOptionsBuilder<TestDbContext>().UseSqlServer(builder.ConnectionString).Options, new MockCurrentUser());

        await context.Database.EnsureCreatedAsync();
        try
        {
            var records = new[] { new TestEntityWithLongId { Name = "a" }, new TestEntityWithLongId { Name = "b" } };
            context.TestEntitiesWithLongId.AddRange(records);
            await context.SaveChangesAsync();

            context.ChangeTracker.Clear();
            var stored = await context.TestEntitiesWithLongId.Select(e => e.Id).ToListAsync();
            Assert.Equal(records.Select(r => r.Id).OrderBy(id => id), stored.OrderBy(id => id));
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }

    private sealed class ExplicitIdentityContext(DbContextOptions<ExplicitIdentityContext> options)
        : TnziDbContext<ExplicitIdentityContext>(options, new MockCurrentUser())
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TestEntityWithLongId>(entity =>
            {
                entity.ToTable("TestEntitiesWithLongId");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).UseIdentityColumn();
            });

            base.OnModelCreating(modelBuilder);
        }
    }
}

/// <summary>
/// 只在环境变量 <see cref="ConnectionStringVariable"/> 给出 SQL Server 连接串时运行的测试（库名由测试自己取、跑完删掉）。
/// </summary>
/// <remarks>xUnit 2.x 没有运行期跳过，在发现阶段按环境变量决定。</remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SqlServerFactAttribute : FactAttribute
{
    /// <summary>给出 SQL Server 连接串的环境变量，例如 <c>Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True</c>。</summary>
    public const string ConnectionStringVariable = "TNZI_TEST_SQLSERVER";

    /// <summary>环境变量里的连接串；没有时为 null。</summary>
    public static string? ConnectionString => Environment.GetEnvironmentVariable(ConnectionStringVariable);

    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            Skip = $"Set {ConnectionStringVariable} to a SQL Server connection string to run this test.";
        }
    }
}
