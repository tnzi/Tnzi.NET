using Tnzi.Modules;
using Microsoft.Extensions.Options;
using Tnzi.Options;

namespace Tnzi.EFCore.Tests;

/// <summary>
/// 多租户开关如何到达 DbContext：运行期与设计期必须由同一处解析，且消费方构造函数一字不改。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ 守的是「配置生效、日志正常、接口 200，而多租户整条从未生效」：
/// 两个 DbContext 基类的开关取自<b>可选</b>构造参数 <c>IOptions&lt;MultiTenancyOptions&gt;</c>，
/// 而脚手架模板、文档示例与参考消费方的 DbContext 一律只声明 <c>(DbContextOptions, ICurrentUser)</c>。
/// DI 只能填声明过的形参，于是运行期那个参数<b>永远是 null</b>，开关恒 false；
/// 09-05 之后设计期又能从 appsettings 读到 true，迁移带 <c>TenantId</c> 与 <c>(TenantId, …)</c> 唯一索引，
/// 运行期却不写该列 —— PostgreSQL / SQLite 下 NULL 互不相等，那条唯一约束对每一行都成立。
/// </para>
/// <para>
/// 修法：开关随 <see cref="DbContextOptions"/> 走（<see cref="MultiTenancyOptionsExtension"/>），
/// <c>AddTnziDbContext</c> 与设计期工厂往同一个 options 里放同一个值，构造函数不需要任何新形参。
/// </para>
/// </remarks>
public class MultiTenancySwitchTests
{
    /// <summary>注入值（消费方显式转发）永远最优先。</summary>
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    public void InjectedValue_AlwaysWins(bool injected, bool fromOptions, bool expected)
    {
        var options = new DbContextOptionsBuilder<MultiTenancyProbeDbContext>()
            .UseTnziMultiTenancy(fromOptions)
            .Options;

        Assert.Equal(expected, MultiTenancySwitch.Resolve(injected, options));
    }

    /// <summary>没有注入值时读 options 里的扩展 —— 这是运行期与设计期共同的那条路。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WithoutInjectedValue_TheOptionsExtensionIsUsed(bool fromOptions)
    {
        var options = new DbContextOptionsBuilder<MultiTenancyProbeDbContext>()
            .UseTnziMultiTenancy(fromOptions)
            .Options;

        Assert.Equal(fromOptions, MultiTenancySwitch.Resolve(null, options));
    }

    /// <summary>两者都没有 = 裸 AddDbContext 且没转发选项 ⇒ 单租户，与既往行为逐字相同。</summary>
    [Fact]
    public void WithNeither_ItFallsBackToSingleTenant()
    {
        var options = new DbContextOptionsBuilder<MultiTenancyProbeDbContext>().Options;

        Assert.False(MultiTenancySwitch.Resolve(null, options));
    }

    /// <summary>
    /// 存在性证明（运行期）：一个只声明 <c>(options, currentUser)</c> 的消费方 DbContext，
    /// 经 <c>AddTnziDbContext</c> 由 DI 构造后，读到的是配置里的开关。
    /// </summary>
    /// <remarks>
    /// 修复前恒 false，与配置无关：模型里连 <c>TenantId</c> 列都没有。
    /// 断言落在模型层（列在不在），SQLite 掩盖不了。
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AddTnziDbContext_TwoArgConsumerContext_ReadsMultiTenancyFromConfiguration(bool enabled)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["MultiTenancy:Enabled"] = enabled.ToString() })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentUser>(new MockCurrentUser());
        services.AddTnziOptions<MultiTenancyOptions>(configuration);
        services.AddTnziDbContext<MultiTenancyProbeDbContext>(o => o.UseSqlite("DataSource=:memory:"), isPrimary: true);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MultiTenancyProbeDbContext>();

        Assert.Equal(enabled, context.IsMultiTenancyEnabled);
        var tenantColumn = context.Model.FindEntityType(typeof(ProbeTenantRow))!.FindProperty(nameof(ProbeTenantRow.TenantId));
        Assert.Equal(enabled, tenantColumn != null);
    }

    /// <summary>
    /// 消费方显式转发 <c>IOptions&lt;MultiTenancyOptions&gt;</c> 的形状（如框架自带的 RagDbContext）
    /// 不受影响：注入值与 options 扩展来自同一份配置，答案一致。
    /// </summary>
    [Fact]
    public void AddTnziDbContext_ForwardingContext_StillAgreesWithConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["MultiTenancy:Enabled"] = "true" })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentUser>(new MockCurrentUser());
        services.AddTnziOptions<MultiTenancyOptions>(configuration);
        services.AddTnziDbContext<ForwardingProbeDbContext>(o => o.UseSqlite("DataSource=:memory:"), isPrimary: true);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.True(scope.ServiceProvider.GetRequiredService<ForwardingProbeDbContext>().IsMultiTenancyEnabled);
    }

    /// <summary>
    /// 启动守卫：配置说开、某个 DbContext 却硬编码成关（转发了一份写死的 IOptions），
    /// 这种不一致意味着迁移与运行模型必然分叉，必须在启动时拒绝而不是带病上线。
    /// </summary>
    [Fact]
    public async Task EFCoreModule_MismatchedMultiTenancySwitch_FailsFast()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["MultiTenancy:Enabled"] = "true" })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentUser>(new MockCurrentUser());
        services.AddSingleton<IEntityManager, EntityManager>();
        services.AddTnziOptions<MultiTenancyOptions>(configuration);
        services.AddTnziDbContext<HardcodedSingleTenantProbeDbContext>(o => o.UseSqlite("DataSource=:memory:"), isPrimary: true);

        using var provider = services.BuildServiceProvider();
        var module = new EFCoreModule();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => module.OnApplicationInitializationAsync(new ApplicationInitializationContext(provider)));

        Assert.Contains(nameof(HardcodedSingleTenantProbeDbContext), ex.Message);
    }

    /// <summary>对照组：两侧一致时守卫不响。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EFCoreModule_ConsistentMultiTenancySwitch_StartsNormally(bool enabled)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["MultiTenancy:Enabled"] = enabled.ToString() })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentUser>(new MockCurrentUser());
        services.AddSingleton<IEntityManager, EntityManager>();
        services.AddTnziOptions<MultiTenancyOptions>(configuration);
        services.AddTnziDbContext<MultiTenancyProbeDbContext>(o => o.UseSqlite("DataSource=:memory:"), isPrimary: true);

        using var provider = services.BuildServiceProvider();

        await new EFCoreModule().OnApplicationInitializationAsync(new ApplicationInitializationContext(provider));
    }
}

/// <summary>
/// 设计期工厂与运行期同源：同一份 appsettings 下两条路建出的上下文开关相等。
/// </summary>
/// <remarks>
/// 工厂会写进程级静态（<c>TableNamePrefixConfiguration.DesignTimeModuleContainer</c>），
/// 故与其它改静态的设计期测试同归一个不并行的集合。
/// </remarks>
[Collection(DesignTimeStaticsCollection.Name)]
public class DesignTimeMultiTenancySwitchTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DesignTimeFactory_ProducesTheSameSwitchAsRuntime(bool enabled)
    {
        using var settings = new TemporaryAppSettings(new
        {
            MultiTenancy = new { Enabled = enabled },
            Database = new
            {
                DbContexts = new[]
                {
                    new { Name = "Default", ConnectionString = "Data Source=:memory:", Provider = "Sqlite" },
                },
            },
        });

        using var designTime = new ProbeDesignTimeFactory(settings.ConfigurationDirectory).CreateDbContext([]);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentUser>(new MockCurrentUser());
        services.AddTnziOptions<MultiTenancyOptions>(settings.Configuration);
        services.AddTnziDbContext<MultiTenancyProbeDbContext>(o => o.UseSqlite("DataSource=:memory:"), isPrimary: true);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var runtime = scope.ServiceProvider.GetRequiredService<MultiTenancyProbeDbContext>();

        Assert.Equal(enabled, designTime.IsMultiTenancyEnabled);
        Assert.Equal(designTime.IsMultiTenancyEnabled, runtime.IsMultiTenancyEnabled);
    }
}

/// <summary>只为存在性证明而设的最小上下文，构造形状与脚手架生成的消费方 DbContext 一致。</summary>
public class MultiTenancyProbeDbContext : TnziDbContext<MultiTenancyProbeDbContext>
{
    public MultiTenancyProbeDbContext(DbContextOptions<MultiTenancyProbeDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    public DbSet<ProbeTenantRow> Rows => Set<ProbeTenantRow>();

    // 三个可选协作者对外可见，供 DbContextCollaboratorResolutionTests 断言它们到底有没有到达上下文。
    public ICurrentTenant? ExposedCurrentTenant => CurrentTenant;
    public IDataFilterManager? ExposedDataFilterManager => DataFilterManager;
    public TimeProvider? ExposedTimeProvider => TimeProvider;
}

/// <summary>转发 IOptions 的形状（框架自带的 RagDbContext 就是这样写的）。</summary>
public class ForwardingProbeDbContext : TnziDbContext<ForwardingProbeDbContext>
{
    public ForwardingProbeDbContext(
        DbContextOptions<ForwardingProbeDbContext> options,
        ICurrentUser currentUser,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null)
        : base(options, currentUser, multiTenancyOptions: multiTenancyOptions)
    {
    }
}

/// <summary>刻意与配置唱反调的上下文：写死单租户。</summary>
public class HardcodedSingleTenantProbeDbContext : TnziDbContext<HardcodedSingleTenantProbeDbContext>
{
    public HardcodedSingleTenantProbeDbContext(DbContextOptions<HardcodedSingleTenantProbeDbContext> options, ICurrentUser currentUser)
        // ★ 写全名：本测试项目有 Tnzi.EFCore.Tests.Options 命名空间，裸写 Options 会被它遮住。
        : base(options, currentUser, multiTenancyOptions: Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions { Enabled = false }))
    {
    }
}

/// <summary>一张多租户表：开关开着时它有 TenantId 列，关着时没有。</summary>
public class ProbeTenantRow : MultiTenantAuditedEntity<Guid>
{
    public string Name { get; set; } = string.Empty;
}
