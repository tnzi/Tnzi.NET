using Microsoft.EntityFrameworkCore.Infrastructure;
using Tnzi.EFCore.Dapper.Providers;

namespace Tnzi.EFCore.Tests.Dapper;

/// <summary>
/// Dapper 的 SQL 方言跟着上下文实际使用的 EF provider 走，而不是重新读一遍配置。
/// </summary>
/// <remarks>
/// ★★ 配置里省略 <c>Provider</c>（文档示范的「按连接字符串自动识别」）时，绑定出来的枚举是默认值 SqlServer。
/// 启动期的自动识别只写在注册路径自己那份绑定实例上，Dapper 重新绑定配置拿到的仍是 SqlServer，
/// 于是 PostgreSQL 应用的批量写入生成 <c>[方括号]</c> 标识符与 <c>SET tbl.col =</c>，当场语法错误。
/// <c>Database.ProviderName</c> 是上下文真正用的那个 provider，精确且不需要猜。
/// <para>
/// 测试项目只引用 SQLite 驱动，所以用一个覆写 <see cref="DbContext.Database"/> 的上下文
/// 报出别的 provider 名（<see cref="DatabaseFacade.ProviderName"/> 是 virtual 的）。
/// </para>
/// </remarks>
public class DapperDatabaseProviderFactoryTests
{
    private const string NpgsqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";

    [Fact]
    public void ProviderOmittedInConfiguration_UsesTheContextsActualProvider()
    {
        using var context = new ProviderNamedDbContext(NpgsqlProviderName);
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Database:DbContexts:0:Name"] = "Default",
            ["Database:DbContexts:0:DbContextType"] = typeof(ProviderNamedDbContext).AssemblyQualifiedName,
            ["Database:DbContexts:0:ConnectionString"] = "Host=localhost;Database=app;Username=app"
        });

        var provider = DapperDatabaseProviderFactory.CreateFromDbContext(context, configuration);

        Assert.IsType<PostgreSQLProvider>(provider);
    }

    [Fact]
    public void ConfigurationNotAvailable_UsesTheContextsActualProvider()
    {
        using var context = new ProviderNamedDbContext(NpgsqlProviderName);

        var provider = DapperDatabaseProviderFactory.CreateFromDbContext(context);

        Assert.IsType<PostgreSQLProvider>(provider);
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", typeof(SqlServerProvider))]
    [InlineData("Pomelo.EntityFrameworkCore.MySql", typeof(MySqlProvider))]
    [InlineData("MySql.EntityFrameworkCore", typeof(MySqlProvider))]
    public void KnownProviderNames_MapToTheirDialect(string providerName, Type expected)
    {
        using var context = new ProviderNamedDbContext(providerName);

        var provider = DapperDatabaseProviderFactory.CreateFromDbContext(context);

        Assert.IsType(expected, provider);
    }

    /// <summary>
    /// 对照组：SQLite 没有 Dapper 方言，构造 Dapper 服务（执行普通查询）不能因此失败：
    /// 仍走配置 / 连接字符串的旧路径，只有批量写入才会用到方言。
    /// </summary>
    [Fact]
    public void Sqlite_DoesNotThrowWhenNothingElseSaysSqlite()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        using var context = new DbContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);

        var provider = DapperDatabaseProviderFactory.CreateFromDbContext(context);

        Assert.NotNull(provider);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    /// <summary>底层是 SQLite，但对外报出指定的 provider 名。</summary>
    public sealed class ProviderNamedDbContext(string providerName)
        : DbContext(new DbContextOptionsBuilder().UseSqlite("Data Source=:memory:").Options)
    {
        private DatabaseFacade? _database;

        public override DatabaseFacade Database => _database ??= new FixedNameDatabaseFacade(this, providerName);
    }

    private sealed class FixedNameDatabaseFacade(DbContext context, string providerName) : DatabaseFacade(context)
    {
        public override string? ProviderName => providerName;
    }
}
