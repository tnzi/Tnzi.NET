using Tnzi.Data;
using Tnzi.EFCore.Data;

namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 外部运行时是<b>宿主级资源</b>：多租户开启时，宿主探测写下的那一行对每个租户都是同一行，
/// 租户可以读、可以绑定、可以派发，但不能探测 / 改 / 删。
/// </summary>
/// <remarks>
/// 用真实的 <c>CurrentTenant</c>（AsyncLocal 覆盖）+ 真实的全局过滤器 + 多租户开启的模型。
/// 若 <see cref="CliRuntime"/> 仍按租户过滤，「租户读宿主行」的几条会全部变红。
/// </remarks>
public class CliRuntimeHostLevelTests : IntegratedTestBase<CliMultiTenantDbContext>
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid AgentId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public CliRuntimeHostLevelTests()
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IDataFilterManager, DataFilterManager>();
        services.AddScoped<ICurrentTenant, CurrentTenant>();
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions { Enabled = true }));

        services.AddScoped<IRepository<CliRun, Guid>, EFCoreRepository<CliMultiTenantDbContext, CliRun, Guid>>();
        services.AddScoped<IRepository<CliRunMessage, Guid>, EFCoreRepository<CliMultiTenantDbContext, CliRunMessage, Guid>>();
        services.AddScoped<IRepository<CliAgentBinding, Guid>, EFCoreRepository<CliMultiTenantDbContext, CliAgentBinding, Guid>>();
        services.AddScoped<IRepository<CliRuntime, Guid>, EFCoreRepository<CliMultiTenantDbContext, CliRuntime, Guid>>();
    }

    [Fact]
    public void Model_CliRuntime_HasNoTenantColumn_AndIsUniqueOnHostAndProvider()
    {
        DbContext.IsMultiTenancyEnabled.ShouldBeTrue();
        var entityType = DbContext.Model.FindEntityType(typeof(CliRuntime))!;

        entityType.FindProperty("TenantId").ShouldBeNull("a host-level resource carries no tenant");
        var unique = entityType.GetIndexes().Where(i => i.IsUnique).ToList();
        unique.Count.ShouldBe(1);
        unique[0].Properties.Select(p => p.Name).ShouldBe(["HostId", "ProviderKey"]);
    }

    /// <summary>后台探测（无租户）写下的行，租户列表里看得见，且只有这一行。</summary>
    [Fact]
    public async Task HostProbe_Row_IsVisibleToEveryTenant()
    {
        var probe = await ProbeAsHostAsync();
        probe.Succeeded.ShouldBeTrue(probe.Message);

        foreach (var tenant in new[] { TenantA, TenantB })
        {
            using var scope = ServiceProvider.CreateScope();
            using var _ = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(tenant);
            var list = await CreateRuntimeService(scope.ServiceProvider).GetListAsync();

            list.Succeeded.ShouldBeTrue(list.Message);
            list.Data!.Count.ShouldBe(1, $"tenant {tenant} must see the single host row");
        }
    }

    /// <summary>租户把自己的 Agent 绑到宿主运行时：运行时查得到（此前是 404）。</summary>
    [Fact]
    public async Task TenantBinding_ToHostRuntime_Succeeds()
    {
        var runtimeId = (await ProbeAsHostAsync()).Data!.Runtimes.Single().Id;

        using var scope = ServiceProvider.CreateScope();
        using var _ = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(TenantA);
        var agents = new Mock<IRepository<Agent, Guid>>();
        agents.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Agent, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var bindings = new CliAgentBindingService(
            scope.ServiceProvider.GetRequiredService<IRepository<CliAgentBinding, Guid>>(),
            scope.ServiceProvider.GetRequiredService<IRepository<CliRuntime, Guid>>(),
            agents.Object,
            scope.ServiceProvider);

        var result = await bindings.UpsertAsync(AgentId, new UpsertCliAgentBindingDto { CliRuntimeId = runtimeId });

        result.Succeeded.ShouldBeTrue(result.Message);
        var stored = await DbContext.Set<CliAgentBinding>().IgnoreQueryFilters().AsNoTracking().SingleAsync();
        stored.TenantId.ShouldBe(TenantA, "the binding stays tenant-owned; only the runtime is shared");
    }

    /// <summary>派发的「运行时可用」检查在租户里看得见宿主行（此前是 409）。</summary>
    [Fact]
    public async Task TenantDispatch_BoundToHostRuntime_IsNotRejectedAsUnavailable()
    {
        var runtimeId = (await ProbeAsHostAsync()).Data!.Runtimes.Single().Id;
        DbContext.Set<CliAgentBinding>().Add(new CliAgentBinding { AgentId = AgentId, CliRuntimeId = runtimeId, TenantId = TenantA });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        using var scope = ServiceProvider.CreateScope();
        using var _ = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(TenantA);
        var dispatcher = new CliAgentDispatcher(
            scope.ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>(),
            scope.ServiceProvider.GetRequiredService<IRepository<CliRunMessage, Guid>>(),
            scope.ServiceProvider.GetRequiredService<IRepository<CliAgentBinding, Guid>>(),
            scope.ServiceProvider.GetRequiredService<IRepository<CliRuntime, Guid>>(),
            Mock.Of<IRepository<AgentThread, Guid>>(),
            new CliRunSignalHub(),
            new CliRunCancellationRegistry(),
            EnabledOptions(),
            scope.ServiceProvider);

        var result = await dispatcher.EnqueueAsync(new CliRunRequestDto { AgentId = AgentId, Prompt = "hi" });

        result.ErrorCode.ShouldNotBe(ErrorCodes.CliRuntimeNotFound, result.Message);
        result.Succeeded.ShouldBeTrue(result.Message);
    }

    /// <summary>租户上下文里探测：拒绝，且不以租户身份再插一份。</summary>
    [Fact]
    public async Task TenantProbe_IsForbidden_AndWritesNothing()
    {
        using var scope = ServiceProvider.CreateScope();
        using var _ = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(TenantA);

        var result = await CreateRuntimeService(scope.ServiceProvider).ProbeAsync();

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        result.ErrorCode.ShouldBe(ErrorCodes.CliRuntimeHostManaged);
        (await DbContext.Set<CliRuntime>().IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    /// <summary>探测两次（后台 + 宿主管理端）仍是一行：宿主行被更新而不是叠加。</summary>
    [Fact]
    public async Task RepeatedHostProbe_UpdatesTheSingleRow()
    {
        await ProbeAsHostAsync();
        await ProbeAsHostAsync();

        (await DbContext.Set<CliRuntime>().IgnoreQueryFilters().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task TenantUpdate_And_Delete_AreForbidden_AndLeaveTheRowIntact()
    {
        var runtimeId = (await ProbeAsHostAsync()).Data!.Runtimes.Single().Id;

        using var scope = ServiceProvider.CreateScope();
        using var _ = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(TenantA);
        var service = CreateRuntimeService(scope.ServiceProvider);

        var update = await service.UpdateAsync(runtimeId, new UpdateCliRuntimeDto { Status = CliRuntimeStatus.Disabled });
        update.Code.ShouldBe(403);
        update.ErrorCode.ShouldBe(ErrorCodes.CliRuntimeHostManaged);

        var delete = await service.DeleteAsync(runtimeId);
        delete.Code.ShouldBe(403);

        DbContext.ChangeTracker.Clear();
        var stored = await DbContext.Set<CliRuntime>().AsNoTracking().SingleAsync();
        stored.Status.ShouldBe(CliRuntimeStatus.Online);
        stored.IsDeleted.ShouldBeFalse();
    }

    /// <summary>宿主删运行时要数<b>所有租户</b>的绑定：租户在用时拒绝，而不是只看见无租户的绑定就放行。</summary>
    [Fact]
    public async Task HostDelete_WhileATenantIsBound_IsRejected()
    {
        var runtimeId = (await ProbeAsHostAsync()).Data!.Runtimes.Single().Id;
        DbContext.Set<CliAgentBinding>().Add(new CliAgentBinding { AgentId = AgentId, CliRuntimeId = runtimeId, TenantId = TenantB });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        using var scope = ServiceProvider.CreateScope();
        var result = await CreateRuntimeService(scope.ServiceProvider).DeleteAsync(runtimeId);

        result.Code.ShouldBe(409, result.Message);
        (await DbContext.Set<CliRuntime>().AsNoTracking().SingleAsync()).IsDeleted.ShouldBeFalse();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<Tnzi.Results.Result<CliRuntimeProbeResultDto>> ProbeAsHostAsync()
    {
        using var scope = ServiceProvider.CreateScope();
        var result = await CreateRuntimeService(scope.ServiceProvider).ProbeAsync();
        DbContext.ChangeTracker.Clear();
        return result;
    }

    private static CliRuntimeService CreateRuntimeService(IServiceProvider scoped)
    {
        var claude = CliBuiltInProviders.All["claude"];
        var registry = new Mock<ICliProviderRegistry>();
        registry.Setup(r => r.GetEnabled()).Returns([claude]);
        registry.Setup(r => r.GetAll()).Returns([claude]);
        registry.Setup(r => r.Find(It.IsAny<string>())).Returns(claude);

        var resolver = new Mock<ICliExecutableResolver>();
        resolver.Setup(r => r.Resolve(It.IsAny<CliProviderDescriptor>())).Returns("/usr/bin/claude");
        resolver.Setup(r => r.DetectVersionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("1.0.0");

        return new CliRuntimeService(
            scoped.GetRequiredService<IRepository<CliRuntime, Guid>>(),
            scoped.GetRequiredService<IRepository<CliAgentBinding, Guid>>(),
            registry.Object,
            Mock.Of<ICliProtocolAdapterFactory>(),
            resolver.Object,
            EnabledOptions(),
            scoped,
            scoped.GetRequiredService<ICurrentTenant>(),
            scoped.GetRequiredService<IOptions<MultiTenancyOptions>>());
    }

    private static IOptionsMonitor<CliAgentOptions> EnabledOptions()
    {
        var monitor = new Mock<IOptionsMonitor<CliAgentOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(new CliAgentOptions { Enabled = true });
        return monitor.Object;
    }
}
