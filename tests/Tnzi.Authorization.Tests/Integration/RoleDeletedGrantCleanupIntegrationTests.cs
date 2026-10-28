using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.Authorization.Events.Handlers;
using Tnzi.Identity.Events;
using AuthOptions = Tnzi.Authorization.Options.AuthorizationOptions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.Authorization.Tests.Integration;

/// <summary>
/// 角色删除之后它的授权行（<see cref="RoleFunction"/>）随之删除。
/// </summary>
/// <remarks>
/// ★ <c>RoleFunction.RoleId</c> 没有外键，角色没了授权行还在：它们不再授予任何人什么，
/// 却让「删除功能」永远答「还有角色挂着这个码」，而那个角色已经找不到了，界面上没有任何入口摘掉它们。
/// </remarks>
public class RoleDeletedGrantCleanupIntegrationTests : IntegratedTestBase<AuthorizationTestDbContext>
{
    private static readonly Guid DeletedRoleId = Guid.NewGuid();
    private static readonly Guid SurvivingRoleId = Guid.NewGuid();

    private readonly AuthOptions _authOptions = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        AddRepo<FunctionModule>(services);
        AddRepo<ModuleFunction>(services);
        AddRepo<RoleFunction>(services);
        AddRepo<UserFunction>(services);
        services.AddScoped(_ => MsOptions.Create(_authOptions));
    }

    private static void AddRepo<TEntity>(IServiceCollection services) where TEntity : class, Tnzi.Domain.Entities.IEntity<Guid>
    {
        services.AddScoped<IRepository<TEntity, Guid>>(sp =>
            new EFCoreRepository<AuthorizationTestDbContext, TEntity, Guid>(
                sp.GetRequiredService<AuthorizationTestDbContext>(), serviceProvider: sp));
    }

    private sealed class DemoProvider : IPermissionDefinitionProvider
    {
        public void Define(IPermissionDefinitionContext context)
        {
            context.AddGroup("demo", "Demo");
            context.AddPermission("demo.thing.view", "View", parentName: "demo");
        }
    }

    private async Task<Guid> SeedFunctionAsync()
    {
        await new PermissionDbSeeder(
            ServiceProvider.GetRequiredService<IRepository<FunctionModule, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<ModuleFunction, Guid>>(),
            NullLogger<PermissionDbSeeder>.Instance,
            MsOptions.Create(_authOptions),
            ServiceProvider.GetRequiredService<IRepository<RoleFunction, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<UserFunction, Guid>>()).SeedAsync([new DemoProvider()]);

        var function = await ServiceProvider.GetRequiredService<IRepository<ModuleFunction, Guid>>()
            .FirstOrDefaultAsync(f => f.Code == "demo.thing.view");
        Assert.NotNull(function);
        return function!.Id;
    }

    [Fact]
    public async Task DeletingARole_RemovesItsGrants_AndLeavesOtherRolesGrantsAlone()
    {
        var functionId = await SeedFunctionAsync();
        var grants = ServiceProvider.GetRequiredService<IRepository<RoleFunction, Guid>>();
        await grants.InsertAsync(new RoleFunction { RoleId = DeletedRoleId, FunctionId = functionId, IsEnabled = true });
        await grants.InsertAsync(new RoleFunction { RoleId = SurvivingRoleId, FunctionId = functionId, IsEnabled = true });

        var handler = new RoleDeletedEventHandler(grants, NullLogger<RoleDeletedEventHandler>.Instance);
        await handler.HandleAsync(new RoleDeletedEvent { RoleId = DeletedRoleId, RoleName = "Payroll", DeletedTime = DateTime.UtcNow });

        Assert.False(await grants.AnyAsync(rf => rf.RoleId == DeletedRoleId));
        Assert.True(await grants.AnyAsync(rf => rf.RoleId == SurvivingRoleId));
    }

    /// <summary>端到端：授权行清掉之后，删除那个功能不再被一个已经不存在的角色挡住。</summary>
    [Fact]
    public async Task AfterTheRoleIsGone_TheFunctionNoLongerCountsItsGrants()
    {
        var functionId = await SeedFunctionAsync();
        var grants = ServiceProvider.GetRequiredService<IRepository<RoleFunction, Guid>>();
        await grants.InsertAsync(new RoleFunction { RoleId = DeletedRoleId, FunctionId = functionId, IsEnabled = true });

        var handler = new RoleDeletedEventHandler(grants, NullLogger<RoleDeletedEventHandler>.Instance);
        await handler.HandleAsync(new RoleDeletedEvent { RoleId = DeletedRoleId, RoleName = "Payroll", DeletedTime = DateTime.UtcNow });

        var stillHeld = await DbContext.RoleFunctions
            .IgnoreQueryFilters()
            .AnyAsync(rf => rf.FunctionId == functionId && !rf.IsDeleted);
        Assert.False(stillHeld);
    }
}
