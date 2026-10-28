namespace Tnzi.EFCore.Tests.Services;

/// <summary>
/// 运行期注册在<b>展开占位符之后</b>的连接字符串上识别 provider，与设计期工厂同口径。
/// </summary>
/// <remarks>
/// ★★ 配置写 <c>"ConnectionString": "${PG_CONN}"</c> 且省略 <c>Provider</c>：此前运行期在未展开的串上识别，
/// 什么都认不出、留在默认的 SqlServer，于是 <c>UseSqlServer</c> 拿到一条 PostgreSQL 连接串，首次连库失败；
/// 而 <c>dotnet ef</c> 走设计期工厂，在展开后的串上识别为 PostgreSQL，迁移照常生成。
/// 两条路径对同一份配置给出不同的 provider。
/// </remarks>
public class DbContextProviderDetectionTests
{
    private const string Variable = "TNZI_TEST_PROVIDER_DETECTION_CONN";

    [Fact]
    public void PlaceholderConnectionString_IsDetectedAfterExpansion()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [Variable] = "Host=localhost;Port=5432;Database=app;Username=app;Password=secret"
            })
            .Build();
        var config = new DbContextConfiguration
        {
            Name = "Default",
            DbContextType = typeof(DefaultDbContext).AssemblyQualifiedName!,
            ConnectionString = "${" + Variable + "}"
        };
        var item = new DbContextDiscoveryItem { Configuration = config, DbContextType = typeof(DefaultDbContext) };

        new DbContextRegistrar().RegisterDbContext(new ServiceCollection(), item, isPrimary: true, configuration);

        Assert.Equal(DatabaseProvider.PostgreSQL, config.Provider);
    }

    /// <summary>对照组：显式写了别的 provider 时尊重配置，不被识别结果改写。</summary>
    [Fact]
    public void ExplicitProvider_IsKept()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [Variable] = "Host=localhost;Port=5432;Database=app;Username=app;Password=secret"
            })
            .Build();
        var config = new DbContextConfiguration
        {
            Name = "Default",
            DbContextType = typeof(DefaultDbContext).AssemblyQualifiedName!,
            ConnectionString = "${" + Variable + "}",
            Provider = DatabaseProvider.MySql
        };
        var item = new DbContextDiscoveryItem { Configuration = config, DbContextType = typeof(DefaultDbContext) };

        new DbContextRegistrar().RegisterDbContext(new ServiceCollection(), item, isPrimary: true, configuration);

        Assert.Equal(DatabaseProvider.MySql, config.Provider);
    }
}
