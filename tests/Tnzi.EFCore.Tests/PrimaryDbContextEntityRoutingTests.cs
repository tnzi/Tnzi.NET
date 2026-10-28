using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tnzi.EFCore.Dapper;
using Tnzi.EFCore.DocumentNumbering;

namespace Tnzi.EFCore.Tests;

/// <summary>
/// 实体配置的 <c>DbContextType</c> 规则按主次上下文生效：未指定（null）的实体只进主上下文，
/// 指定了的只进那一个上下文；实体 → 上下文的反查对主上下文的实体答出主上下文类型。
/// </summary>
/// <remarks>
/// <para>
/// ★★ 此前 <c>EntityManager.GetEntityRegisters</c> 只看「这个上下文有没有显式绑定」：
/// 有就只返回显式绑定的那几个，没有才回退到未指定的那一组。它不知道谁是主上下文，于是
/// 主上下文只要有一个配置写了 <c>DbContextType =&gt; typeof(主上下文)</c>，所有框架模块实体从模型里消失
/// （仓储照样注册，首次查询抛「not included in the model」，下一次 <c>migrations add</c> 生成整批 DropTable）；
/// 反过来，没有任何显式绑定的次上下文拿到全部默认实体，迁移把主库的表在次库里重建一遍。
/// </para>
/// <para>
/// ★★ <c>GetDbContextTypeForEntity</c> 对未指定上下文的实体原样返回分组用的 <c>typeof(object)</c> 占位符：
/// <c>IDapperExecutor</c> 解析 <c>System.Object</c> 抛异常、审计保留期的硬删把它读成「未注册实体」、
/// 存储的孤立引用校验解析不出上下文而一律判「仍存在」。
/// </para>
/// <para>
/// 夹具用真实 <see cref="EntityManager"/>（不 Mock）与 <c>AddTnziDbContext</c> 装配；
/// 「未指定上下文」那一组的见证实体是框架自带的 <see cref="DocumentSequence"/>。
/// </para>
/// </remarks>
[Collection(DesignTimeStaticsCollection.Name)]
public class PrimaryDbContextEntityRoutingTests
{
    [Fact]
    public void PrimaryContext_WithAnExplicitBinding_StillGetsTheDefaultEntities()
    {
        using var binding = RoutingBoundEntityConfiguration.Bind();
        using var host = RuntimeHost.Create();
        using var scope = host.CreateScope();
        var primary = scope.ServiceProvider.GetRequiredService<RoutingPrimaryDbContext>();

        Assert.NotNull(primary.Model.FindEntityType(typeof(RoutingBoundEntity)));
        Assert.NotNull(primary.Model.FindEntityType(typeof(DocumentSequence)));
    }

    [Fact]
    public void SecondaryContext_WithoutBindings_DoesNotInheritTheDefaultEntities()
    {
        using var binding = RoutingBoundEntityConfiguration.Bind();
        using var host = RuntimeHost.Create();
        using var scope = host.CreateScope();
        var secondary = scope.ServiceProvider.GetRequiredService<RoutingSecondaryDbContext>();

        Assert.Null(secondary.Model.FindEntityType(typeof(DocumentSequence)));
        Assert.Null(secondary.Model.FindEntityType(typeof(RoutingBoundEntity)));
    }

    [Fact]
    public void GetDbContextTypeForEntity_ReturnsThePrimaryContextForDefaultEntities()
    {
        using var binding = RoutingBoundEntityConfiguration.Bind();
        using var host = RuntimeHost.Create();
        var entityManager = host.GetRequiredService<IEntityManager>();
        entityManager.Initialize();

        Assert.Equal(typeof(RoutingPrimaryDbContext), entityManager.GetDbContextTypeForEntity(typeof(DocumentSequence)));
        Assert.Equal(typeof(RoutingPrimaryDbContext), entityManager.GetDbContextTypeForEntity(typeof(RoutingBoundEntity)));
    }

    /// <summary>按文档「方式 2」用的 Dapper 执行器：此前对主上下文实体解析 <c>System.Object</c> 即抛。</summary>
    [Fact]
    public void DapperExecutorFactory_CanBuildAnExecutorForADefaultEntity()
    {
        // 每条用到主上下文的用例都打开绑定：EF 按上下文类型缓存模型，先建出的那一份会喂给后面每一条。
        using var binding = RoutingBoundEntityConfiguration.Bind();
        using var host = RuntimeHost.Create();
        using var scope = host.CreateScope();
        var entityManager = scope.ServiceProvider.GetRequiredService<IEntityManager>();
        entityManager.Initialize();

        var executor = new DapperExecutorFactory(scope.ServiceProvider, entityManager).Create<DocumentSequence, Guid>();

        Assert.NotNull(executor);
    }

    /// <summary>只登记了一个上下文、没标主：它就是主上下文（既有的单上下文用法不受影响）。</summary>
    [Fact]
    public void SingleContextNotMarkedPrimary_IsTreatedAsPrimary()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentUser>(new MockCurrentUser());
        services.AddSingleton<IEntityManager, EntityManager>();
        services.AddTnziDbContext<RoutingSoloDbContext>(o => o.UseSqlite("DataSource=:memory:"));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var solo = scope.ServiceProvider.GetRequiredService<RoutingSoloDbContext>();

        Assert.NotNull(solo.Model.FindEntityType(typeof(DocumentSequence)));
        var entityManager = provider.GetRequiredService<IEntityManager>();
        entityManager.Initialize();
        Assert.Equal(typeof(RoutingSoloDbContext), entityManager.GetDbContextTypeForEntity(typeof(DocumentSequence)));
    }

    /// <summary>
    /// 设计期（<c>dotnet ef</c>）与运行期得出同一个模型：工厂按 <c>Database</c> 节判定主次
    /// （<c>PrimaryDbContext</c> 指名的那一项，没指名就是第一项）。
    /// </summary>
    [Fact]
    public void DesignTimeFactory_ComposesTheSameModelsAsRuntime()
    {
        using var binding = RoutingBoundEntityConfiguration.Bind();
        var primaryName = typeof(RoutingPrimaryDbContext).AssemblyQualifiedName;
        var secondaryName = typeof(RoutingSecondaryDbContext).AssemblyQualifiedName;
        using var settings = new TemporaryAppSettings(new
        {
            Database = new
            {
                DbContexts = new[]
                {
                    new { Name = "Main", DbContextType = primaryName, ConnectionString = "Data Source=:memory:", Provider = "Sqlite" },
                    new { Name = "Side", DbContextType = secondaryName, ConnectionString = "Data Source=:memory:", Provider = "Sqlite" },
                },
            },
        });

        using var primary = new RoutingDesignTimeFactory<RoutingPrimaryDbContext>(settings.ConfigurationDirectory).CreateDbContext([]);
        using var secondary = new RoutingDesignTimeFactory<RoutingSecondaryDbContext>(settings.ConfigurationDirectory).CreateDbContext([]);

        Assert.NotNull(primary.Model.FindEntityType(typeof(RoutingBoundEntity)));
        Assert.NotNull(primary.Model.FindEntityType(typeof(DocumentSequence)));
        Assert.Null(secondary.Model.FindEntityType(typeof(DocumentSequence)));
    }

    /// <summary>主次登记 + 真实 EntityManager 的运行期宿主。</summary>
    private static class RuntimeHost
    {
        public static ServiceProvider Create()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<ICurrentUser>(new MockCurrentUser());
            services.AddSingleton<IEntityManager, EntityManager>();
            services.AddTnziDbContext<RoutingPrimaryDbContext>(o => o.UseSqlite("DataSource=:memory:"), isPrimary: true);
            services.AddTnziDbContext<RoutingSecondaryDbContext>(o => o.UseSqlite("DataSource=:memory:"));
            return services.BuildServiceProvider();
        }
    }
}

/// <summary>显式绑定到主上下文的实体（文档示范的写法）。</summary>
public class RoutingBoundEntity : EntityBase<Guid>
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// 显式绑定到主上下文的配置。它是公开类型，会被所有用真实 <see cref="EntityManager"/> 的用例扫描到；
/// 只在 <see cref="Bind"/> 打开的异步流里绑定 <see cref="RoutingPrimaryDbContext"/>，其余时候绑到一个非 DbContext 的占位
/// （与 Outbox 关闭时同一手法），不进任何模型，也不出现在别的用例的上下文发现里。
/// </summary>
public class RoutingBoundEntityConfiguration : EntityTypeConfigurationBase<RoutingBoundEntity, Guid>
{
    private static readonly AsyncLocal<bool> Bound = new();

    public override Type? DbContextType => Bound.Value ? typeof(RoutingPrimaryDbContext) : typeof(RoutingBoundEntityConfiguration);

    /// <summary>在当前异步流里打开绑定；释放时关闭。</summary>
    public static IDisposable Bind()
    {
        Bound.Value = true;
        return new Unbind();
    }

    private sealed class Unbind : IDisposable
    {
        public void Dispose() => Bound.Value = false;
    }

    public override void Configure(EntityTypeBuilder<RoutingBoundEntity> builder)
    {
        builder.ToTable("RoutingBoundEntities");
        builder.Property(x => x.Name).HasMaxLength(50);
    }
}

public class RoutingPrimaryDbContext : TnziDbContext<RoutingPrimaryDbContext>
{
    public RoutingPrimaryDbContext(DbContextOptions<RoutingPrimaryDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }
}

public class RoutingSecondaryDbContext : TnziDbContext<RoutingSecondaryDbContext>
{
    public RoutingSecondaryDbContext(DbContextOptions<RoutingSecondaryDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }
}

public class RoutingSoloDbContext : TnziDbContext<RoutingSoloDbContext>
{
    public RoutingSoloDbContext(DbContextOptions<RoutingSoloDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }
}

/// <summary>把设计期工厂指向一份临时 appsettings.json，并让每次建模都不命中模型缓存。</summary>
public class RoutingDesignTimeFactory<TDbContext>(string configurationDirectory) : DesignTimeDbContextFactoryBase<TDbContext>
    where TDbContext : DbContext
{
    protected override string? FindConfigurationPath() => configurationDirectory;

    protected override TDbContext CreateDbContextInstance(DbContextOptions<TDbContext> options, ICurrentUser currentUser)
    {
        var uncached = new DbContextOptionsBuilder<TDbContext>(options)
            .ReplaceService<IModelCacheKeyFactory, NoModelCacheKeyFactory>()
            .Options;
        return base.CreateDbContextInstance(uncached, currentUser);
    }
}
