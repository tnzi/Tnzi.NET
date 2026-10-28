using Microsoft.Extensions.Caching.Memory;
using Tnzi.Data;
using AuthOptions = Tnzi.Authorization.Options.AuthorizationOptions;
using IdentityRole = Tnzi.Identity.Entities.Role;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.Authorization.Tests.Integration;

/// <summary>
/// 权限写路径之后的每用户权限缓存失效：提交之后必须再失效一次，失效失败不能报成功。真实 SQLite 仓储 + 真实缓存。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ 在工作单元里（宿主开了全局工作单元，或写路径自己的 <c>ExecuteInUnitOfWorkAsync</c>），写入到请求末尾才提交，
/// 而缓存此前是在方法里当场清的。这之间另一个请求缓存未命中，读到提交前的旧授权并回填缓存，之后再没有任何东西失效它：
/// 被撤销的码在缓存期（30 分钟）里照样放行。工作单元用一个可控的替身表示「事务进行中」，
/// 提交用执行提交后队列表示，与真实工作单元管理器提交成功后做的事相同。
/// </para>
/// <para>
/// ★★★ 失效失败此前被吞成一条警告，接口照样 200：撤销已写库、缓存还是旧集合，所有实例在缓存期内继续放行。
/// </para>
/// </remarks>
public class PermissionCacheInvalidationIntegrationTests : IntegratedTestBase<AuthorizationTestDbContext>
{
    private static readonly Guid GrantorId = TestHelper.DefaultTestUserId;
    private static readonly Guid MemberId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid RoleId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private readonly Mock<IUserRoleService> _userRoleService = new();
    private readonly Mock<IUnitOfWorkManager> _unitOfWork = new();
    private readonly PostCommitActionQueue _postCommit = new();
    private readonly AuthOptions _authOptions = new() { SuperAdminRoles = ["SuperAdmin"] };
    private bool _inTransaction;
    private ICache _cache;

    public PermissionCacheInvalidationIntegrationTests()
    {
        _cache = new MemoryCacheService(
            new MemoryCache(new MemoryCacheOptions()),
            Mock.Of<ILogger<MemoryCacheService>>(),
            MsOptions.Create(new CachingOptions()));

        // 授权者是超管：委托护栏整体放行，用例只看缓存。
        _userRoleService
            .Setup(s => s.GetUserRolesAsync(It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync((IEnumerable<Guid> ids) => ids
                .Where(id => id == GrantorId)
                .ToDictionary(id => id, _ => (IEnumerable<string>)["SuperAdmin"]));
        _userRoleService.Setup(s => s.GetUserRoleIdsAsync(It.IsAny<Guid>())).ReturnsAsync(Array.Empty<Guid>());
        _userRoleService.Setup(s => s.GetRoleUserIdsAsync(RoleId)).ReturnsAsync([MemberId]);

        _unitOfWork.SetupGet(u => u.IsEnabledTransaction).Returns(() => _inTransaction);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        AddRepo<FunctionModule>(services);
        AddRepo<ModuleFunction>(services);
        AddRepo<RoleFunction>(services);
        AddRepo<UserFunction>(services);
        AddRepo<IdentityRole>(services);

        services.AddScoped(_ => _userRoleService.Object);
        services.AddScoped(_ => MsOptions.Create(_authOptions));
        services.AddScoped(_ => _unitOfWork.Object);
        services.AddScoped<IPostCommitActionQueue>(_ => _postCommit);
        services.AddScoped(_ => new FunctionAuthCache(_cache));

        services.AddScoped(sp => new FunctionAuthorizationService(
            sp.GetRequiredService<IRepository<FunctionModule, Guid>>(),
            sp.GetRequiredService<IRepository<ModuleFunction, Guid>>(),
            sp.GetRequiredService<IRepository<RoleFunction, Guid>>(),
            sp.GetRequiredService<IRepository<UserFunction, Guid>>(),
            sp,
            sp.GetRequiredService<IUserRoleService>(),
            functionAuthCache: sp.GetRequiredService<FunctionAuthCache>(),
            options: sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthOptions>>(),
            roleRepository: sp.GetRequiredService<IRepository<IdentityRole, Guid>>()));

        services.AddScoped(sp => new UserFunctionService(
            sp,
            sp.GetRequiredService<IRepository<UserFunction, Guid>>(),
            sp.GetRequiredService<IRepository<ModuleFunction, Guid>>(),
            sp.GetRequiredService<FunctionAuthorizationService>(),
            functionAuthCache: sp.GetRequiredService<FunctionAuthCache>()));
    }

    private static void AddRepo<TEntity>(IServiceCollection services) where TEntity : class, Tnzi.Domain.Entities.IEntity<Guid>
    {
        services.AddScoped<IRepository<TEntity, Guid>>(sp =>
            new EFCoreRepository<AuthorizationTestDbContext, TEntity, Guid>(
                sp.GetRequiredService<AuthorizationTestDbContext>(), serviceProvider: sp));
    }

    private FunctionAuthorizationService Authorization => ServiceProvider.GetRequiredService<FunctionAuthorizationService>();

    private UserFunctionService UserFunctions => ServiceProvider.GetRequiredService<UserFunctionService>();

    private FunctionAuthCache AuthCache => ServiceProvider.GetRequiredService<FunctionAuthCache>();

    private async Task<(Guid ModuleId, Guid FunctionId)> SeedAsync()
    {
        var module = new FunctionModule { Id = Guid.NewGuid(), Code = "finance", Name = "Finance", IsEnabled = true, CreationTime = DateTime.UtcNow };
        var function = new ModuleFunction
        {
            Id = Guid.NewGuid(), ModuleId = module.Id, Code = "finance.payment.execute", Name = "Execute", IsEnabled = true, CreationTime = DateTime.UtcNow,
        };
        DbContext.FunctionModules.Add(module);
        DbContext.ModuleFunctions.Add(function);
        DbContext.IdentityRoles.Add(new IdentityRole { Id = RoleId, Name = "Payments" });
        DbContext.RoleFunctions.Add(new RoleFunction { Id = Guid.NewGuid(), RoleId = RoleId, FunctionId = function.Id, IsEnabled = true });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return (module.Id, function.Id);
    }

    /// <summary>并发请求在提交之前缓存未命中、读到提交前的旧授权，把它写回缓存。</summary>
    private Task RefillFromStaleReadAsync()
        => AuthCache.SetUserPermissionNamesAsync(MemberId, "stale-read", ["finance.payment.execute"]);

    private Task<bool> MemberIsCachedAsync() => _cache.ExistsAsync(CacheKeys.Authorization.UserFunctions(MemberId));

    /// <summary>事务提交成功：执行提交后队列。</summary>
    private Task CommitAsync() => _postCommit.ExecuteAsync();

    [Fact]
    public async Task Revoking_a_role_grant_inside_a_transaction_invalidates_the_members_again_after_commit()
    {
        var (_, functionId) = await SeedAsync();
        _inTransaction = true;

        (await Authorization.RemoveFunctionsFromRoleAsync(RoleId, [functionId])).Succeeded.ShouldBeTrue();
        await RefillFromStaleReadAsync();
        await CommitAsync();

        (await MemberIsCachedAsync()).ShouldBeFalse("a cache entry refilled before commit must not survive the commit");
    }

    [Fact]
    public async Task Denying_a_user_a_code_inside_a_transaction_invalidates_them_again_after_commit()
    {
        var (_, functionId) = await SeedAsync();
        _inTransaction = true;

        (await UserFunctions.SetUserDeniedFunctionsAsync(MemberId, [functionId])).Succeeded.ShouldBeTrue();
        await RefillFromStaleReadAsync();
        await CommitAsync();

        (await MemberIsCachedAsync()).ShouldBeFalse();
    }

    /// <summary>模块级联停用在自己的工作单元里清缓存，那一遍同样早于提交。</summary>
    [Fact]
    public async Task Disabling_a_module_invalidates_everyone_again_after_commit()
    {
        var (moduleId, _) = await SeedAsync();
        _inTransaction = true;

        (await Authorization.DisableModuleAsync(moduleId)).Succeeded.ShouldBeTrue();
        await RefillFromStaleReadAsync();
        await CommitAsync();

        (await MemberIsCachedAsync()).ShouldBeFalse();
    }

    /// <summary>不在事务里时写入已经自动提交，当场那一遍就是提交后的，不往队列里塞多余的动作。</summary>
    [Fact]
    public async Task Outside_a_transaction_the_invalidation_happens_once_and_at_once()
    {
        var (_, functionId) = await SeedAsync();
        await RefillFromStaleReadAsync();

        (await Authorization.RemoveFunctionsFromRoleAsync(RoleId, [functionId])).Succeeded.ShouldBeTrue();

        (await MemberIsCachedAsync()).ShouldBeFalse();
        _postCommit.Count.ShouldBe(0);
    }

    /// <summary>
    /// ★★★ 按人失效失败时退到清空整个权限缓存（宽但正确），而不是记一条警告就报成功。
    /// </summary>
    [Fact]
    public async Task A_failed_per_user_invalidation_falls_back_to_clearing_the_whole_permission_cache()
    {
        var (_, functionId) = await SeedAsync();
        var flaky = FlakyCache(failUserKeys: true, failEverything: false);
        _cache = flaky.Object;

        var role = await Authorization.RemoveFunctionsFromRoleAsync(RoleId, [functionId]);
        var user = await UserFunctions.SetUserDeniedFunctionsAsync(MemberId, [functionId]);

        role.Succeeded.ShouldBeTrue();
        user.Succeeded.ShouldBeTrue();
        flaky.Verify(c => c.RemoveByPrefixAsync(FunctionAuthCache.UserFunctionsCachePrefix, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    /// <summary>★★★ 连清空也失败：不能给出一张「已撤销」的成功回执。</summary>
    [Fact]
    public async Task When_even_clearing_the_cache_fails_the_revocation_does_not_report_success()
    {
        var (_, functionId) = await SeedAsync();
        _cache = FlakyCache(failUserKeys: true, failEverything: true).Object;

        await Should.ThrowAsync<InvalidOperationException>(() => Authorization.RemoveFunctionsFromRoleAsync(RoleId, [functionId]));
        await Should.ThrowAsync<InvalidOperationException>(() => UserFunctions.SetUserDeniedFunctionsAsync(MemberId, [functionId]));
    }

    /// <summary>分布式缓存的瞬时故障：按人删除抛异常；<paramref name="failEverything"/> 时前缀清空也抛。</summary>
    private static Mock<ICache> FlakyCache(bool failUserKeys, bool failEverything)
    {
        var cache = new Mock<ICache>();
        if (failUserKeys)
        {
            cache
                .Setup(c => c.RemoveAsync(It.Is<string>(k => k.StartsWith(FunctionAuthCache.UserFunctionsCachePrefix)), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("cache unavailable"));
        }

        if (failEverything)
        {
            cache
                .Setup(c => c.RemoveByPrefixAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("cache unavailable"));
        }

        return cache;
    }
}
