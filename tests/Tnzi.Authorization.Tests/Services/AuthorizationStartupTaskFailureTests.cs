using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.Data;
using Tnzi.Modules;
using AuthOptions = Tnzi.Authorization.Options.AuthorizationOptions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.Authorization.Tests.Services;

/// <summary>
/// 启动那一刻数据库不可达：授权模块的启动任务必须<b>抛出</b>，迁移后启动任务的运行器才会在后台重试它。
/// 此前权限目录播种的失败在任务内部被吞掉、只记一条「直到下次成功启动」的 Error：
/// 本次部署新声明的权限码一直没有行，不能分配、对非超管一律拒绝，直到重启。
/// </summary>
public class AuthorizationStartupTaskFailureTests
{
    private sealed class Provider : IPermissionDefinitionProvider
    {
        public void Define(IPermissionDefinitionContext context)
        {
            context.AddGroup("ops", "Ops");
            context.AddPermission("ops.view", "View", parentName: "ops");
        }
    }

    private static async Task<IPostMigrationStartupTask> RegisteredTaskAsync(IServiceProvider services)
    {
        var registrations = new ServiceCollection();
        await new AuthorizationModule().ConfigureServicesAsync(
            new ServiceConfigurationContext(registrations, new ConfigurationBuilder().Build()));
        var descriptor = registrations.Single(d =>
            d.ServiceType == typeof(IPostMigrationStartupTask) && d.ImplementationType?.Assembly == typeof(AuthorizationModule).Assembly);
        return (IPostMigrationStartupTask)ActivatorUtilities.CreateInstance(services, descriptor.ImplementationType!);
    }

    [Fact]
    public async Task APermissionSeedThatCannotReachTheDatabase_FailsTheTask_SoItIsRetried()
    {
        var unreachable = new Mock<IRepository<FunctionModule, Guid>>();
        unreachable.Setup(r => r.AsQueryable(It.IsAny<bool>())).Throws(new TimeoutException("Timeout during connecting"));
        unreachable.Setup(r => r.AsQueryable()).Throws(new TimeoutException("Timeout during connecting"));
        var functions = new Mock<IRepository<ModuleFunction, Guid>>();
        functions.Setup(r => r.AsQueryable(It.IsAny<bool>())).Throws(new TimeoutException("Timeout during connecting"));
        functions.Setup(r => r.AsQueryable()).Throws(new TimeoutException("Timeout during connecting"));

        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IPermissionDefinitionProvider, Provider>();
        services.AddSingleton(unreachable.Object);
        services.AddSingleton(functions.Object);
        services.AddScoped<PermissionDbSeeder>();
        // No super-admin roles: the only database work left is the catalogue seed.
        services.AddSingleton(MsOptions.Create(new AuthOptions { SuperAdminRoles = [], SeedBuiltInAdminRoles = false }));
        await using var provider = services.BuildServiceProvider();

        var task = await RegisteredTaskAsync(provider);

        var thrown = await Should.ThrowAsync<AggregateException>(() => task.ExecuteAsync(provider));
        thrown.InnerExceptions.ShouldHaveSingleItem().Message.ShouldContain("PermissionDbSeeder failed");
        task.RequiredForReadiness.ShouldBeFalse("a missing catalogue row denies; it does not grant");
    }
}
