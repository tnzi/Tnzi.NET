using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tnzi.AI.Tools.Sql;
using Tnzi.Exceptions;
using Tnzi.Modules;

namespace Tnzi.AI.Tests.Tools.Sql;

/// <summary>
/// SQL 工具套件的连接工厂回退：可选能力不得让「不用它的应用」付出启动成本。
/// </summary>
/// <remarks>
/// 背景：<c>IReadOnlySqlExecutor</c> / <c>ISchemaInspector</c> 是无条件注册的，而它们的构造函数
/// 要一个只能由应用提供的 <c>Func&lt;string?, DbConnection&gt;</c>。回退出现之前，任何加载
/// <c>Tnzi.AI</c> 却不打算跑 SQL 的应用都会在 <c>ValidateOnBuild</c>（Development 下的 .NET 默认值）
/// 阶段启动失败——而消费方消除该失败的最省事办法是关掉<b>宿主级</b>作用域校验，
/// 连带压掉自己代码里的全部同类错误。
/// </remarks>
public class SqlConnectionFactoryFallbackTests
{
    private static async Task<ServiceProvider> BuildAsync(Action<IServiceCollection>? applicationRegistrations = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder().AddInMemoryCollection([]).Build();
        var context = new ServiceConfigurationContext(services, configuration);
        var module = new AIModule();

        await module.PreConfigureServicesAsync(context);
        await module.ConfigureServicesAsync(context);

        // 应用自己的注册发生在 Configure 阶段（业务模块的常规位置），早于 AIModule 的 Post 阶段回退。
        applicationRegistrations?.Invoke(services);

        await module.PostConfigureServicesAsync(context);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = false, // 这里只关心 SQL 套件本身；整图校验由架构门禁负责
        });
    }

    /// <summary>没有注册任何东西的应用，也能把 SQL 套件解析出来（= 不会在启动时炸）。</summary>
    [Fact]
    public async Task WithoutApplicationFactory_SqlToolSuiteStillResolves()
    {
        await using var provider = await BuildAsync();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IReadOnlySqlExecutor>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ISchemaInspector>());
    }

    /// <summary>
    /// 真的去用它时，拿到的是一条指名道姓的配置错误，而不是空引用。
    /// </summary>
    [Fact]
    public async Task WithoutApplicationFactory_UsingItThrowsActionableError()
    {
        await using var provider = await BuildAsync();
        using var scope = provider.CreateScope();

        var factory = scope.ServiceProvider.GetRequiredService<Func<string?, DbConnection>>();

        var ex = Assert.Throws<ConfigurationException>(() => factory("reporting"));

        Assert.Equal(UnconfiguredSqlConnectionFactory.ConfigurationKey, ex.ConfigurationKey);
        // 错误要能直接指导下一步：缺什么、注册什么、去哪看。
        Assert.Contains("reporting", ex.Message, StringComparison.Ordinal);
        Assert.Contains("has not", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AddScoped", ex.Message, StringComparison.Ordinal);
        Assert.Contains("ai-tools.md", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 想用这套工具的应用照旧：注册自己的工厂即胜出，回退不得抢占。
    /// </summary>
    /// <remarks>
    /// 这条守的是回退的<b>注册时机</b>。若把 <c>TryAdd</c> 回退挪到 <c>ConfigureServices</c> 阶段，
    /// 它会先于业务模块落地，应用同样用 <c>TryAdd</c> 注册真实工厂时会被静默跳过——
    /// 表现为「明明接了数据库，一跑就报没接数据库」。
    /// </remarks>
    [Fact]
    public async Task WithApplicationFactory_ApplicationRegistrationWins()
    {
        await using var provider = await BuildAsync(services =>
            services.TryAddScopedFactory(_ => new SqliteConnection("Data Source=:memory:")));

        using var scope = provider.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<Func<string?, DbConnection>>();

        using var connection = factory(null);
        Assert.IsType<SqliteConnection>(connection);
    }
}

internal static class SqlFactoryTestExtensions
{
    /// <summary>模拟应用侧「用 TryAdd 注册连接工厂」的常见写法。</summary>
    public static IServiceCollection TryAddScopedFactory(
        this IServiceCollection services,
        Func<string?, DbConnection> factory)
    {
        services.TryAddScoped<Func<string?, DbConnection>>(_ => factory);
        return services;
    }
}
