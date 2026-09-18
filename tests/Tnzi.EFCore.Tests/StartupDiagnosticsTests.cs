using Tnzi.Modules;

namespace Tnzi.EFCore.Tests;

/// <summary>
/// DbContext 发现 / 注册路径上的启动期诊断必须真的打印出来。
/// </summary>
/// <remarks>
/// <para>
/// <c>ConfigureServicesAsync</c> 阶段拿不到 DI 的 <c>ILogger</c>，此前两处生产调用点都传 <c>logger: null</c>：
/// 2026-07-07 专为「重试策略 × UoW 手动事务互斥」加的告警、<c>[PRIMARY]</c> 信息、连接串 <c>${VAR}</c> 未解析告警
/// 全部被吞，护栏从落地那天起就是装饰品。现在诊断先进缓冲，<c>OnApplicationInitializationAsync</c> 拿到真正的
/// <c>ILoggerFactory</c> 后回放；未解析的占位符则在启动期直接拒绝（带字面量占位符的连接串没有任何正确用途）。
/// </para>
/// </remarks>
public class StartupDiagnosticsTests
{
    private static IConfiguration DatabaseConfiguration(bool enableRetry, string connectionString = "Data Source=:memory:") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:DbContexts:0:Name"] = "Diagnostics",
                ["Database:DbContexts:0:DbContextType"] = typeof(TestDbContext).AssemblyQualifiedName,
                ["Database:DbContexts:0:ConnectionString"] = connectionString,
                ["Database:DbContexts:0:Provider"] = "Sqlite",
                ["Database:DbContexts:0:EnableRetryOnFailure"] = enableRetry.ToString(),
            })
            .Build();

    private static async Task<CapturingLoggerProvider> RunModuleAsync(IConfiguration configuration)
    {
        var capture = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(capture));
        services.AddSingleton<ICurrentUser>(new MockCurrentUser());

        var module = new EFCoreModule();
        var context = new ServiceConfigurationContext(services, configuration);
        await module.PreConfigureServicesAsync(context);
        await module.ConfigureServicesAsync(context);

        using var provider = services.BuildServiceProvider();
        await module.OnApplicationInitializationAsync(new ApplicationInitializationContext(provider));
        return capture;
    }

    [Fact]
    public async Task EFCoreModule_RetryOnFailureConfigured_LogsUnitOfWorkConflictAtStartup()
    {
        var capture = await RunModuleAsync(DatabaseConfiguration(enableRetry: true));

        Assert.Contains(capture.Entries, e =>
            e.Level == LogLevel.Warning && e.Message.Contains("EnableRetryOnFailure=true") && e.Message.Contains("Diagnostics"));
    }

    [Fact]
    public async Task EFCoreModule_RegisteredPrimaryContext_LogsPrimaryMarker()
    {
        var capture = await RunModuleAsync(DatabaseConfiguration(enableRetry: false));

        Assert.Contains(capture.Entries, e =>
            e.Level == LogLevel.Information && e.Message.Contains("Registered DbContext 'Diagnostics'") && e.Message.Contains("[PRIMARY]"));
        // 发现阶段的诊断也回放
        Assert.Contains(capture.Entries, e =>
            e.Level == LogLevel.Information && e.Message.Contains("DbContext discovery completed"));
    }

    /// <summary>对照：没开重试时不响。</summary>
    [Fact]
    public async Task EFCoreModule_RetryNotConfigured_DoesNotWarn()
    {
        var capture = await RunModuleAsync(DatabaseConfiguration(enableRetry: false));

        Assert.DoesNotContain(capture.Entries, e => e.Message.Contains("EnableRetryOnFailure=true"));
    }

    /// <summary>失败关闭：占位符解析不出来时启动期拒绝并指名变量，而不是把字面量 ${DB_PASSWORD} 送去连库。</summary>
    [Fact]
    public async Task EFCoreModule_UnresolvedConnectionStringPlaceholder_FailsAtStartup()
    {
        var configuration = DatabaseConfiguration(enableRetry: false, connectionString: "Data Source=:memory:;Password=${TNZI_TEST_UNSET_DB_PASSWORD}");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => RunModuleAsync(configuration));

        Assert.Contains("TNZI_TEST_UNSET_DB_PASSWORD", ex.ToString());
    }

    [Fact]
    public void GetEffectiveConnectionString_UnresolvedPlaceholder_Throws()
    {
        var config = new DbContextConfiguration
        {
            Name = "Diagnostics",
            ConnectionString = "Host=${TNZI_TEST_UNSET_DB_HOST};Password=${TNZI_TEST_UNSET_DB_PASSWORD}",
            Provider = DatabaseProvider.PostgreSQL,
        };

        var ex = Assert.Throws<InvalidOperationException>(() => config.GetEffectiveConnectionString(configuration: null));

        Assert.Contains("TNZI_TEST_UNSET_DB_HOST", ex.Message);
        Assert.Contains("TNZI_TEST_UNSET_DB_PASSWORD", ex.Message);
        Assert.Contains("Diagnostics", ex.Message);
    }

    [Fact]
    public void GetEffectiveConnectionString_ResolvedPlaceholder_Expands()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TNZI_TEST_DB_HOST"] = "db.local" })
            .Build();
        var config = new DbContextConfiguration
        {
            Name = "Diagnostics",
            ConnectionString = "Host=${TNZI_TEST_DB_HOST};Database=app",
            Provider = DatabaseProvider.PostgreSQL,
        };

        Assert.Equal("Host=db.local;Database=app", config.GetEffectiveConnectionString(configuration));
    }
}
