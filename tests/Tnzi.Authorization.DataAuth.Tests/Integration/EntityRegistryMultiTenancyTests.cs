using Microsoft.Data.Sqlite;
using Tnzi.Authorization.DataAuth.Dtos;
using Tnzi.MultiTenancy;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.Authorization.DataAuth.Tests.Integration;

/// <summary>
/// 多租户开启时，实体登记（<see cref="EntityInfo"/>，全局表）只能在宿主上下文里改，删除时的关联检查跨租户。
/// </summary>
/// <remarks>
/// ★★★ 写登记表的权限码与维护本租户 <see cref="EntityRole"/> 的是同一组，按租户授予；而登记表没有 <c>TenantId</c>。
/// 此前租户 A 的管理员把某个实体的 <c>IsDataAuthEnabled</c> 改成 false，所有租户在该实体上的行级规则同时失效。
/// 删除检查同理：在宿主上下文里按租户过滤只看得见宿主的规则，别家租户还挂着的规则会指向一行墓碑。
/// </remarks>
public class EntityRegistryMultiTenancyTests : IAsyncLifetime
{
    private static readonly Guid TenantA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid TenantB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");
    private static readonly Guid HostAdmin = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid AdminA = Guid.Parse("10000000-0000-0000-0000-00000000000a");
    private static readonly Guid AdminB = Guid.Parse("10000000-0000-0000-0000-00000000000b");

    private readonly SqliteConnection _connection;
    private readonly Guid _entityInfoId = Guid.NewGuid();

    public EntityRegistryMultiTenancyTests()
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    public async Task InitializeAsync()
    {
        using var host = BuildProvider(tenantId: null, HostAdmin);
        var db = host.GetRequiredService<DataAuthTestDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.EntityInfos.Add(new EntityInfo { Id = _entityInfoId, Name = "Ticket", TypeName = typeof(Ticket).FullName!, IsDataAuthEnabled = true });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Registry_writes_in_a_tenant_context_are_refused()
    {
        using (var a = BuildProvider(TenantA, AdminA))
        {
            var service = a.GetRequiredService<DataAuthService>();

            (await service.UpdateEntityInfoAsync(_entityInfoId, new UpdateEntityInfoRequest { Name = "Ticket", IsDataAuthEnabled = false })).Code.ShouldBe(403);
            (await service.DeleteEntityInfoAsync(_entityInfoId)).Code.ShouldBe(403);
            (await service.CreateEntityInfoAsync(new CreateEntityInfoRequest { Name = "Other", TypeName = typeof(LongTicket).FullName! })).Code.ShouldBe(403);
        }

        using var host = BuildProvider(tenantId: null, HostAdmin);
        var db = host.GetRequiredService<DataAuthTestDbContext>();
        db.EntityInfos.Single(e => e.Id == _entityInfoId).IsDataAuthEnabled.ShouldBeTrue();
        db.EntityInfos.Count().ShouldBe(1);
    }

    [Fact]
    public async Task Registry_writes_in_the_host_context_are_allowed()
    {
        using (var host = BuildProvider(tenantId: null, HostAdmin))
        {
            var result = await host.GetRequiredService<DataAuthService>()
                .UpdateEntityInfoAsync(_entityInfoId, new UpdateEntityInfoRequest { Name = "Ticket", IsDataAuthEnabled = false });
            result.Succeeded.ShouldBeTrue(result.Message);
        }

        using var check = BuildProvider(tenantId: null, HostAdmin);
        check.GetRequiredService<DataAuthTestDbContext>().EntityInfos.Single(e => e.Id == _entityInfoId).IsDataAuthEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Deleting_a_registration_counts_rules_in_every_tenant()
    {
        using (var b = BuildProvider(TenantB, AdminB))
        {
            var db = b.GetRequiredService<DataAuthTestDbContext>();
            db.EntityRoles.Add(new EntityRole
            {
                Id = Guid.NewGuid(), EntityInfoId = _entityInfoId, RoleId = Guid.NewGuid(),
                Operation = DataAuthOperation.Query, IsEnabled = true, TenantId = TenantB,
            });
            await db.SaveChangesAsync();
        }

        using var host = BuildProvider(tenantId: null, HostAdmin);
        var result = await host.GetRequiredService<DataAuthService>().DeleteEntityInfoAsync(_entityInfoId);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        host.GetRequiredService<DataAuthTestDbContext>().EntityInfos.Any(e => e.Id == _entityInfoId).ShouldBeTrue();
    }

    /// <summary>一次「请求」：自己的当前用户与当前租户，共享的连接。</summary>
    private ServiceProvider BuildProvider(Guid? tenantId, Guid userId)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);

        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(u => u.Id).Returns(userId);
        currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        currentUser.Setup(u => u.TenantId).Returns(tenantId);
        services.AddScoped(_ => currentUser.Object);

        var currentTenant = new Mock<ICurrentTenant>();
        currentTenant.Setup(t => t.Id).Returns(tenantId);
        services.AddScoped(_ => currentTenant.Object);

        services.AddSingleton(MsOptions.Create(new MultiTenancyOptions { Enabled = true }));

        services.AddDbContext<DataAuthTestDbContext>(options =>
        {
            options.UseSqlite(_connection);
            options.UseTnziMultiTenancy(true);
        });
        services.AddScoped<IRepository<EntityInfo, Guid>>(sp =>
            new EFCoreRepository<DataAuthTestDbContext, EntityInfo, Guid>(sp.GetRequiredService<DataAuthTestDbContext>(), serviceProvider: sp));
        services.AddScoped<IRepository<EntityRole, Guid>>(sp =>
            new EFCoreRepository<DataAuthTestDbContext, EntityRole, Guid>(sp.GetRequiredService<DataAuthTestDbContext>(), serviceProvider: sp));

        // 与模块同一形状的装配：可选依赖由容器按在不在注入。
        services.AddScoped<DataAuthService>();

        return services.BuildServiceProvider();
    }

    public Task DisposeAsync()
    {
        _connection.Dispose();
        return Task.CompletedTask;
    }
}
