using Microsoft.Extensions.Options;
using Tnzi.Extensions;
using Tnzi.MultiTenancy;

namespace Tnzi.Identity.Organization.Tests.Integration;

using Tnzi.Identity.Organization.Entities;

/// <summary>
/// 组织包的关系型集成测试基类（SQLite in-memory）。
/// </summary>
/// <remarks>
/// ★ 与其它模块的集成夹具不同，这里**必须往容器里放一个 <see cref="IModuleContainer"/>**：
/// 表名前缀是按实体所在程序集去模块容器里查的，容器缺席时框架会一声不吭地跳过前缀，
/// 于是"表名不变"这条断言会退化成"表名是 CLR 类名"而依然是绿的。装上两个模块，
/// 这里跑的就是消费方真实加载两个包时的模型。
/// </remarks>
public abstract class OrganizationIntegrationTestBase : IntegratedTestBase<OrganizationTestDbContext>
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IModuleContainer>(new ModuleContainer(
        [
            new ModuleDescriptor(typeof(IdentityModule), new IdentityModule()),
            new ModuleDescriptor(typeof(IdentityOrganizationModule), new IdentityOrganizationModule()),
        ]));

        // 全限定：简单名 `Options` 在 Tnzi.Identity 那一层会先命中命名空间
        // Tnzi.Identity.Options（与 `Organization` 同一族的名称遮蔽）。
        services.AddSingleton<IOptions<MultiTenancyOptions>>(
            Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions { Enabled = false }));

        services.AddScoped<IRepository<Organization, Guid>>(sp =>
            new EFCoreRepository<OrganizationTestDbContext, Organization, Guid>(
                sp.GetRequiredService<OrganizationTestDbContext>(), serviceProvider: sp));

        services.AddScoped<DbContext>(sp => sp.GetRequiredService<OrganizationTestDbContext>());
        services.AddScoped<IOrganizationService, OrganizationService>();
    }

    /// <summary>Mapster 的配置是进程级的，每个夹具自己压一份最小配置。</summary>
    protected OrganizationIntegrationTestBase()
    {
        var config = new TypeAdapterConfig();
        config.NewConfig<Organization, OrganizationDto>().MaxDepth(1);
        config.NewConfig<Organization, OrganizationTreeItemDto>().MaxDepth(1);
        config.NewConfig<User, UserListItemDto>()
            .Ignore(dest => dest.Roles)
            .Ignore(dest => dest.OrganizationName!);
        MapperExtensions.SetMapper(new Mapper(config));
    }

    protected IOrganizationService Service => ServiceProvider.GetRequiredService<IOrganizationService>();
}

/// <summary>
/// 同时装着核心 <c>User</c> 与本包 <c>Organization</c> 的测试上下文 —— 即"两个包都加载"的宿主。
/// </summary>
public class OrganizationTestDbContext : TnziDbContext<OrganizationTestDbContext>
{
    public OrganizationTestDbContext(DbContextOptions<OrganizationTestDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new Tnzi.Identity.Entities.Configs.UserConfiguration());
        modelBuilder.ApplyConfiguration(new OrganizationConfiguration());

        base.OnModelCreating(modelBuilder);

        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}
