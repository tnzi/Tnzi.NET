using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Tnzi.Caching;

namespace Tnzi.Authorization.DataAuth.Tests.Integration;

/// <summary>
/// 过滤表达式缓存的失效范围。缓存键带租户段（版本号 + 内存缓存），<c>EntityRole</c> 是按租户的表，
/// 按租户段失效没问题；但 <c>EntityInfo</c> 是<b>全局</b>表 —— 一次改动影响所有租户，失效却只 bump 了
/// 调用者所在租户的版本号，其它租户的内存缓存最长 15 分钟继续用旧表达式，事故与操作在时间上对不上。
/// </summary>
public class DataAuthCacheInvalidationTests : IntegratedTestBase<DataAuthTestDbContext>
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid RoleId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly Mock<IUserRoleService> _userRoleService = new();
    private readonly IMemoryCache _memoryCache = new MemoryCache(new MemoryCacheOptions());
    private readonly ICache _cache;

    public DataAuthCacheInvalidationTests()
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
        _userRoleService.Setup(s => s.GetUserRoleIdsAsync(UserId)).ReturnsAsync([RoleId]);
        _cache = new MemoryCacheService(
            _memoryCache,
            Mock.Of<ILogger<MemoryCacheService>>(),
            Microsoft.Extensions.Options.Options.Create(new CachingOptions()));
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        AddRepo<EntityInfo>(services);
        AddRepo<EntityRole>(services);
        AddRepo<Ticket>(services);
        services.AddScoped(_ => _userRoleService.Object);
    }

    private static void AddRepo<TEntity>(IServiceCollection services) where TEntity : class, Tnzi.Domain.Entities.IEntity<Guid>
    {
        services.AddScoped<IRepository<TEntity, Guid>>(sp =>
            new EFCoreRepository<DataAuthTestDbContext, TEntity, Guid>(
                sp.GetRequiredService<DataAuthTestDbContext>(), serviceProvider: sp));
    }

    /// <summary>同一批仓储、同一份缓存，只换「当前用户属于哪个租户」。</summary>
    private DataAuthService ServiceForTenant(Guid tenantId)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(u => u.Id).Returns(UserId);
        currentUser.Setup(u => u.TenantId).Returns(tenantId);
        currentUser.Setup(u => u.IsAuthenticated).Returns(true);

        var provider = new TenantScopedProvider(ServiceProvider, currentUser.Object);
        return new DataAuthService(
            ServiceProvider.GetRequiredService<IRepository<EntityInfo, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<EntityRole, Guid>>(),
            provider,
            _userRoleService.Object,
            _cache,
            _memoryCache);
    }

    private async Task<EntityInfo> SeedAsync()
    {
        var entityInfos = ServiceProvider.GetRequiredService<IRepository<EntityInfo, Guid>>();
        var entityInfo = new EntityInfo { Name = "Ticket", TypeName = typeof(Ticket).FullName!, IsDataAuthEnabled = true };
        await entityInfos.InsertAsync(entityInfo);

        await ServiceProvider.GetRequiredService<IRepository<EntityRole, Guid>>().InsertAsync(new EntityRole
        {
            EntityInfoId = entityInfo.Id, RoleId = RoleId, Operation = DataAuthOperation.Query, IsEnabled = true,
            Filter = $$$"""{"logic":"And","rules":[{"field":"OwnerId","operator":"Equal","value":"{{{UserId}}}"}]}""",
        });
        return entityInfo;
    }

    /// <summary>★租户 A 关掉全局开关，租户 B 下一次读必须看到「不限制」，而不是命中自己租户段里的旧表达式。</summary>
    [Fact]
    public async Task UpdateEntityInfo_InvalidatesFilterCacheAcrossTenants()
    {
        var entityInfo = await SeedAsync();
        var tenantB = ServiceForTenant(TenantB);

        // B 先读一次，把表达式填进 B 租户段的内存缓存。
        (await tenantB.GetDataFilterAsync<Ticket>(UserId, DataAuthOperation.Query)).ShouldNotBeNull();

        var update = await ServiceForTenant(TenantA).UpdateEntityInfoAsync(entityInfo.Id, new Dtos.UpdateEntityInfoRequest
        {
            Name = entityInfo.Name, IsDataAuthEnabled = false,
        });
        update.Succeeded.ShouldBeTrue(update.Message);

        (await tenantB.GetDataFilterAsync<Ticket>(UserId, DataAuthOperation.Query))
            .ShouldBeNull("tenant B must not keep serving the expression cached before the global switch was turned off");
    }

    /// <summary>EntityRole 是按租户的表，它的失效仍按租户段：租户 A 的规则改动不作废租户 B 的缓存（既有语义）。</summary>
    [Fact]
    public async Task DeleteEntityRole_OnlyInvalidatesTheCallersTenantSegment()
    {
        var entityInfo = await SeedAsync();
        var tenantB = ServiceForTenant(TenantB);
        var tenantA = ServiceForTenant(TenantA);

        var cachedForB = await tenantB.GetDataFilterAsync<Ticket>(UserId, DataAuthOperation.Query);
        cachedForB.ShouldNotBeNull();

        var role = (await tenantA.GetEntityRolesByEntityAsync(entityInfo.Id)).Data!.Single();
        (await tenantA.DeleteEntityRoleAsync(role.Id)).Succeeded.ShouldBeTrue();

        // B 的段没被碰：还是同一个缓存实例。
        (await tenantB.GetDataFilterAsync<Ticket>(UserId, DataAuthOperation.Query)).ShouldBeSameAs(cachedForB);
    }

    /// <summary>只替换 <see cref="ICurrentUser"/>，其余服务原样透传。</summary>
    private sealed class TenantScopedProvider(IServiceProvider inner, ICurrentUser currentUser) : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => serviceType == typeof(ICurrentUser) ? currentUser : inner.GetService(serviceType);
    }
}
