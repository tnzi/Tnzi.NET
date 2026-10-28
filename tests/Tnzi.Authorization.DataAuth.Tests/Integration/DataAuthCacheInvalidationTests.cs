using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Tnzi.Authorization.DataAuth.Events.Handlers;
using Tnzi.Caching;
using Tnzi.Data;
using Tnzi.Identity.Events;

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

    /// <summary>工作单元替身：<see cref="_inTransaction"/> 为真时表示「事务进行中，写入到提交才可见」。默认不在事务里。</summary>
    private readonly Mock<IUnitOfWorkManager> _unitOfWork = new();
    private readonly PostCommitActionQueue _postCommit = new();
    private bool _inTransaction;

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
        _unitOfWork.SetupGet(u => u.IsEnabledTransaction).Returns(() => _inTransaction);
        services.AddScoped(_ => _unitOfWork.Object);
        services.AddScoped<IPostCommitActionQueue>(_ => _postCommit);
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

    private static readonly Guid WideRoleId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid SomeoneElse = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    /// <summary>
    /// ★★★ 用户同时持有「只看自己的单」与「看另一个人的单」两个角色，OR 表达式已被缓存。
    /// 管理员把宽的那个角色摘掉之后，下一次读必须只剩自己的单：缓存键里没有角色集，
    /// 不随成员变更失效，被摘掉的角色在 15 分钟里照样放出它的行。
    /// 处理器在提交后的独立作用域里跑、没有用户上下文，而读的人在租户 A 里：版本键不能带调用者的租户段。
    /// </summary>
    [Fact]
    public async Task A_role_removal_drops_that_roles_rows_from_the_cached_filter_at_once()
    {
        var entityInfo = await SeedAsync();
        await ServiceProvider.GetRequiredService<IRepository<EntityRole, Guid>>().InsertAsync(new EntityRole
        {
            EntityInfoId = entityInfo.Id, RoleId = WideRoleId, Operation = DataAuthOperation.Query, IsEnabled = true,
            Filter = $$$"""{"logic":"And","rules":[{"field":"OwnerId","operator":"Equal","value":"{{{SomeoneElse}}}"}]}""",
        });
        _userRoleService.Setup(s => s.GetUserRoleIdsAsync(UserId)).ReturnsAsync([RoleId, WideRoleId]);
        var tenantA = ServiceForTenant(TenantA);

        var before = (await tenantA.GetDataFilterAsync<Ticket>(UserId, DataAuthOperation.Query))!.Compile();
        before(new Ticket { OwnerId = SomeoneElse }).ShouldBeTrue("precondition: the wide role's rows are visible");

        _userRoleService.Setup(s => s.GetUserRoleIdsAsync(UserId)).ReturnsAsync([RoleId]);
        var handler = new UserRolesChangedDataFilterCacheHandler(
            Mock.Of<ILogger<UserRolesChangedDataFilterCacheHandler>>(), _cache);
        await handler.HandleAsync(new UserRolesChangedEvent
        {
            UserId = UserId,
            UserName = "user",
            AddedRoleIds = [],
            RemovedRoleIds = [WideRoleId],
            ChangeType = UserRolesChangeType.Removed,
            ChangedTime = DateTime.UtcNow,
        });

        var after = (await tenantA.GetDataFilterAsync<Ticket>(UserId, DataAuthOperation.Query))!.Compile();
        after(new Ticket { OwnerId = SomeoneElse }).ShouldBeFalse("the removed role must stop contributing rows");
        after(new Ticket { OwnerId = UserId }).ShouldBeTrue();
    }

    /// <summary>一个用户的角色变更不作废别人的缓存：失效按人，不是全清。</summary>
    [Fact]
    public async Task A_role_change_leaves_other_users_cached_filters_alone()
    {
        await SeedAsync();
        var otherUser = Guid.NewGuid();
        _userRoleService.Setup(s => s.GetUserRoleIdsAsync(otherUser)).ReturnsAsync([RoleId]);
        var tenantA = ServiceForTenant(TenantA);
        var cachedForOther = await tenantA.GetDataFilterAsync<Ticket>(otherUser, DataAuthOperation.Query);
        cachedForOther.ShouldNotBeNull();

        await new UserRolesChangedDataFilterCacheHandler(Mock.Of<ILogger<UserRolesChangedDataFilterCacheHandler>>(), _cache)
            .HandleAsync(new UserRolesChangedEvent
            {
                UserId = UserId, UserName = "user", AddedRoleIds = [], RemovedRoleIds = [RoleId],
                ChangeType = UserRolesChangeType.Removed, ChangedTime = DateTime.UtcNow,
            });

        (await tenantA.GetDataFilterAsync<Ticket>(otherUser, DataAuthOperation.Query)).ShouldBeSameAs(cachedForOther);
    }

    /// <summary>
    /// ★★ 规则写在事务里（宿主开了全局工作单元）：版本号当场 bump，删除却要到提交才可见。
    /// 这之间另一个请求按新版本键未命中、读到提交前的旧规则、编译并缓存 15 分钟，之后再没有 bump 作废它。
    /// 提交后必须再 bump 一次。提交用「先落库、再执行提交后队列」表示，与真实工作单元管理器提交成功后做的事相同。
    /// </summary>
    [Fact]
    public async Task A_rule_removed_inside_a_transaction_is_not_kept_alive_by_a_read_before_commit()
    {
        var entityInfo = await SeedAsync();
        var tenantA = ServiceForTenant(TenantA);
        var role = (await tenantA.GetEntityRolesByEntityAsync(entityInfo.Id)).Data!.Single();

        _inTransaction = true;
        (await tenantA.DeleteEntityRoleAsync(role.Id)).Succeeded.ShouldBeTrue();

        // 并发请求：提交前读，库里还是旧规则，表达式按 bump 之后的版本键缓存下来。
        (await tenantA.GetDataFilterAsync<Ticket>(UserId, DataAuthOperation.Query)).ShouldNotBeNull("precondition: the pre-commit read still sees the rule");

        _inTransaction = false;
        await DbContext.SaveChangesAsync();
        await _postCommit.ExecuteAsync();

        (await tenantA.GetDataFilterAsync<Ticket>(UserId, DataAuthOperation.Query))
            .ShouldBeNull("the expression compiled from the pre-commit rows must not outlive the commit");
    }

    /// <summary>只替换 <see cref="ICurrentUser"/>，其余服务原样透传。</summary>
    private sealed class TenantScopedProvider(IServiceProvider inner, ICurrentUser currentUser) : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => serviceType == typeof(ICurrentUser) ? currentUser : inner.GetService(serviceType);
    }
}
