using Microsoft.Extensions.Caching.Memory;
using Tnzi.Data.Filtering;
using Tnzi.Extensions;

namespace Tnzi.Authorization.DataAuth.Tests.Integration;

/// <summary>
/// 过滤器表达式写进共享 <c>IMemoryCache</c> 时必须带 <c>Size</c>：
/// 共享缓存一旦被任何人设了 <c>SizeLimit</c>（核心的 <c>Caching:MemorySizeLimit</c>，
/// 或此前模板模块无条件设下的 1000），不带 <c>Size</c> 的写入会当场抛
/// <c>InvalidOperationException</c>，而这一行没有 try/catch —— 命中规则的每一次行级过滤都是 500。
/// </summary>
/// <remarks>
/// 既有单元测试全部以 <c>memoryCache = null</c> 构造服务，所以从没走到那一行。
/// 这里用真实 SQLite 仓储 + 真实 <c>MemoryCache</c>，只 mock 角色查询。
/// </remarks>
public class DataFilterCacheTests : IntegrationTestBase
{
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid RoleId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private async Task SeedRuleAsync()
    {
        var entityInfo = new EntityInfo
        {
            Id = Guid.NewGuid(),
            Name = nameof(EntityInfo),
            TypeName = typeof(EntityInfo).FullName!,
            IsDataAuthEnabled = true,
            CreationTime = DateTime.UtcNow
        };
        await DbContext.EntityInfos.AddAsync(entityInfo);

        var filter = new FilterGroup { Rules = [FilterRule.Equal(nameof(EntityInfo.Name), "visible")] };
        await DbContext.EntityRoles.AddAsync(new EntityRole
        {
            Id = Guid.NewGuid(),
            EntityInfoId = entityInfo.Id,
            RoleId = RoleId,
            Operation = DataAuthOperation.Query,
            Filter = filter.ToJsonString(),
            IsEnabled = true,
            CreationTime = DateTime.UtcNow
        });
        await DbContext.SaveChangesAsync();
    }

    private DataAuthService CreateService(IMemoryCache memoryCache)
    {
        var userRoles = new Mock<IUserRoleService>();
        userRoles.Setup(s => s.GetUserRoleIdsAsync(UserId)).ReturnsAsync([RoleId]);

        return new DataAuthService(
            new EFCoreRepository<DataAuthTestDbContext, EntityInfo, Guid>(DbContext, serviceProvider: ServiceProvider),
            new EFCoreRepository<DataAuthTestDbContext, EntityRole, Guid>(DbContext, serviceProvider: ServiceProvider),
            ServiceProvider,
            userRoles.Object,
            memoryCache: memoryCache);
    }

    /// <summary>★现形用例：共享缓存带上限时，命中规则的过滤解析此前当场抛异常。</summary>
    [Fact]
    public async Task A_matching_rule_is_cached_even_when_the_shared_cache_has_a_size_limit()
    {
        await SeedRuleAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
        var service = CreateService(cache);

        var filter = await service.GetDataFilterAsync<EntityInfo>(UserId, DataAuthOperation.Query);

        filter.ShouldNotBeNull();
        filter!.Compile()(new EntityInfo { Name = "visible" }).ShouldBeTrue();
        filter.Compile()(new EntityInfo { Name = "hidden" }).ShouldBeFalse();
        cache.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_second_call_is_served_from_the_cache()
    {
        await SeedRuleAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
        var service = CreateService(cache);

        var first = await service.GetDataFilterAsync<EntityInfo>(UserId, DataAuthOperation.Query);
        var second = await service.GetDataFilterAsync<EntityInfo>(UserId, DataAuthOperation.Query);

        second.ShouldBeSameAs(first);
    }
}
