using Microsoft.EntityFrameworkCore.Design;
using Tnzi.EFCore;
using Tnzi.Extensions;
using Tnzi.Modules;
using Tnzi.MultiTenancy;

namespace Tnzi.Identity.IntegrationTests;

/// <summary>
/// 设计时 DbContext 工厂，用于生成迁移并支持 MultiTenancy 开关验证。
/// </summary>
public class TestIdentityDesignTimeDbContextFactory : IDesignTimeDbContextFactory<TestIdentityDbContext>
{
    public TestIdentityDbContext CreateDbContext(string[] args)
    {
        // ★ 显式声明本夹具模拟的模块集合。
        //
        // 表名前缀是按实体所在**程序集**去模块容器里查的。设计时没有宿主进程，也就没有
        // ITnziApplication，框架于是回退到「扫程序集猜启动模块」：它按
        // `GetReferencedAssemblies()` 的顺序找到第一个只含一个具体模块类的引用程序集
        // （这里是 Tnzi.Identity → IdentityModule），然后只沿它的 [DependsOn] 树建容器。
        // 而 IdentityOrganizationModule 依赖 IdentityModule、不被它依赖，于是**落在树外** ——
        // Organization 查不到前缀，表名会安静地从 Identity_Organizations 变成 Organizations。
        //
        // 消费方不受这条影响：他们的 StartupModule 用 [DependsOn] 声明了要加载哪些模块，
        // 依赖树里两个都在。本夹具没有 StartupModule，所以必须自己说清楚。
        TableNamePrefixConfiguration.DesignTimeModuleContainer = new ModuleContainer(
        [
            // EFCoreModule 是 IdentityModule 的 [DependsOn]，真实宿主一定加载它；
            // 漏掉它，实体发现的范围会跟着缩小（DocumentSequence 会从模型里消失）。
            new ModuleDescriptor(typeof(EFCoreModule), new EFCoreModule()),
            new ModuleDescriptor(typeof(IdentityModule), new IdentityModule()),
            new ModuleDescriptor(
                typeof(Organization.IdentityOrganizationModule),
                new Organization.IdentityOrganizationModule()),
        ]);

        var optionsBuilder = new DbContextOptionsBuilder<TestIdentityDbContext>();
        optionsBuilder.UseSqlite("Data Source=test_identity_migrations.db");

        var multiTenancyEnabled = bool.TryParse(
            Environment.GetEnvironmentVariable("MultiTenancy__Enabled"),
            out var enabled) && enabled;

        var multiTenancyOptions = Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions
        {
            Enabled = multiTenancyEnabled
        });

        return new TestIdentityDbContext(
            optionsBuilder.Options,
            new DesignTimeCurrentUser(),
            multiTenancyOptions);
    }
}
