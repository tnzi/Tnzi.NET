using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.Json;
using Tnzi.MultiTenancy;
using AuthOptions = Tnzi.Authorization.Options.AuthorizationOptions;
using IdentityRole = Tnzi.Identity.Entities.Role;
using IdentityUser = Tnzi.Identity.Entities.User;
using IdentityUserRole = Tnzi.Identity.Entities.UserRole;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.Authorization.Tests.Integration;

/// <summary>
/// 多租户开启时的功能授权：每用户权限缓存按租户分区、按用户 id 寻址的管理端入口按租户范围收窄、
/// 全局权限目录只能在宿主上下文里改。真实 SQLite（开着租户过滤器）、真实 DI 装配的服务、
/// 一份所有「请求」共用的缓存（经 JSON 往返，模拟分布式缓存）。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ 守的是一条跨租户链：用户-角色关联不按租户过滤，角色授权与用户直授按调用者的租户严格等值过滤，
/// 而缓存键此前只有用户 id。租户 A 的管理员替租户 B 的用户算一次权限（查他、给他直授），
/// 「A 视角下的集合」就落进 B 本人请求读的那个键：B 在 B 里持有 A 才有的码、失去自己本来的授权，
/// 键热的时候 A 还能读到 B 的完整权限集。
/// </para>
/// <para>
/// 每个「请求」一个新的服务容器（自己的当前用户与当前租户），数据库连接与缓存跨请求共享。
/// </para>
/// </remarks>
public class MultiTenantPermissionIntegrationTests : IAsyncLifetime
{
    private static readonly Guid TenantA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid TenantB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");

    private static readonly Guid HostAdmin = Guid.Parse("10000000-0000-0000-0000-000000000001"); // 全局账号（TenantId 为空）
    private static readonly Guid AdminA = Guid.Parse("10000000-0000-0000-0000-00000000000a"); // 租户 A 的管理员
    private static readonly Guid Bob = Guid.Parse("10000000-0000-0000-0000-00000000000b"); // 租户 B 的普通用户

    private static readonly Guid RoleA = Guid.Parse("20000000-0000-0000-0000-00000000000a");
    private static readonly Guid RoleB = Guid.Parse("20000000-0000-0000-0000-00000000000b");

    /// <summary>Bob 在租户 B 里经角色持有的码。</summary>
    private const string BobsCode = "report.view";

    /// <summary>只在租户 A 的视角里出现的码。</summary>
    private const string ForeignCode = "report.export";

    /// <summary>管理员自建的码（非系统托管，删得掉）。</summary>
    private const string CustomCode = "custom.audit";

    private readonly SqliteConnection _connection;
    private readonly ICache _sharedCache;
    private readonly Guid _bobsFunctionId = Guid.NewGuid();
    private readonly Guid _foreignFunctionId = Guid.NewGuid();
    private readonly Guid _customFunctionId = Guid.NewGuid();
    private readonly Guid _moduleId = Guid.NewGuid();

    public MultiTenantPermissionIntegrationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _sharedCache = new JsonRoundTripCache(new MemoryCacheService(
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<MemoryCacheService>.Instance,
            MsOptions.Create(new CachingOptions())));
    }

    public async Task InitializeAsync()
    {
        using (var host = BuildProvider(tenantId: null, userId: HostAdmin))
        {
            var db = host.GetRequiredService<AuthorizationTestDbContext>();
            db.Database.EnsureCreated();

            db.FunctionModules.Add(new FunctionModule { Id = _moduleId, Name = "Reports", Code = "report", IsEnabled = true, CreationTime = DateTime.UtcNow });
            db.ModuleFunctions.AddRange(
                NewFunction(_bobsFunctionId, BobsCode, systemManaged: true),
                NewFunction(_foreignFunctionId, ForeignCode, systemManaged: true),
                NewFunction(_customFunctionId, CustomCode, systemManaged: false));
            db.IdentityUsers.AddRange(
                new IdentityUser { Id = HostAdmin, UserName = "host", TenantId = null },
                new IdentityUser { Id = AdminA, UserName = "admin-a", TenantId = TenantA },
                new IdentityUser { Id = Bob, UserName = "bob", TenantId = TenantB });
            await db.SaveChangesAsync();
        }

        using (var b = BuildProvider(TenantB, Bob))
        {
            var db = b.GetRequiredService<AuthorizationTestDbContext>();
            db.IdentityRoles.Add(new IdentityRole { Id = RoleB, Name = "Readers", NormalizedName = "READERS", TenantId = TenantB });
            db.RoleFunctions.Add(new RoleFunction { Id = Guid.NewGuid(), RoleId = RoleB, FunctionId = _bobsFunctionId, IsEnabled = true, TenantId = TenantB });
            db.IdentityUserRoles.Add(new IdentityUserRole { UserId = Bob, RoleId = RoleB });
            await db.SaveChangesAsync();
        }

        using (var a = BuildProvider(TenantA, AdminA))
        {
            var db = a.GetRequiredService<AuthorizationTestDbContext>();
            db.IdentityRoles.Add(new IdentityRole { Id = RoleA, Name = "Root", NormalizedName = "ROOT", TenantId = TenantA });
            db.IdentityUserRoles.Add(new IdentityUserRole { UserId = AdminA, RoleId = RoleA });
            await db.SaveChangesAsync();
        }
    }

    // ── 缓存分区 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_read_from_another_tenant_does_not_replace_the_users_own_permissions()
    {
        // 租户 A 的上下文里替 Bob 算一次：A 看不见 B 的角色授权，算出来是空集。
        using (var a = BuildProvider(TenantA, AdminA))
        {
            (await Auth(a).CheckPermissionAsync(Bob, BobsCode)).ShouldBeFalse();
        }

        // Bob 在自己的租户里：仍持有自己的码。缓存不分区时这里读到的是 A 视角下的空集。
        using var b = BuildProvider(TenantB, Bob);
        (await Auth(b).CheckPermissionAsync(Bob, BobsCode)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_grant_written_in_another_tenant_is_not_carried_into_the_users_tenant()
    {
        // 越界写入的那一行（此前管理端入口不问目标归属，照写不误）+ 在 A 的上下文里重算一次。
        using (var a = BuildProvider(TenantA, AdminA))
        {
            var db = a.GetRequiredService<AuthorizationTestDbContext>();
            db.UserFunctions.Add(new UserFunction { Id = Guid.NewGuid(), UserId = Bob, FunctionId = _foreignFunctionId, IsGranted = true, IsEnabled = true, TenantId = TenantA });
            await db.SaveChangesAsync();

            (await Auth(a).GetUserPermissionNamesAsync(Bob)).ShouldBe(new[] { ForeignCode });
        }

        using var b = BuildProvider(TenantB, Bob);
        var auth = Auth(b);
        (await auth.CheckPermissionAsync(Bob, ForeignCode)).ShouldBeFalse();
        (await auth.CheckPermissionAsync(Bob, BobsCode)).ShouldBeTrue();
    }

    [Fact]
    public async Task The_users_own_partition_is_served_from_the_cache_after_a_json_round_trip()
    {
        using (var b = BuildProvider(TenantB, Bob))
        {
            (await Auth(b).GetUserPermissionNamesAsync(Bob)).ShouldBe(new[] { BobsCode });
        }

        // 绕开服务直接删掉授权行（不触发任何失效）：再读仍是缓存里那份，证明命中的是缓存、且经得起 JSON 往返。
        using (var host = BuildProvider(tenantId: null, userId: HostAdmin))
        {
            var db = host.GetRequiredService<AuthorizationTestDbContext>();
            await db.RoleFunctions.IgnoreQueryFilters().Where(rf => rf.RoleId == RoleB).ExecuteDeleteAsync();
        }

        using var again = BuildProvider(TenantB, Bob);
        (await Auth(again).CheckPermissionAsync(Bob, BobsCode)).ShouldBeTrue();
    }

    [Fact]
    public async Task Toggling_a_function_from_the_host_invalidates_holders_in_every_tenant()
    {
        using (var b = BuildProvider(TenantB, Bob))
        {
            (await Auth(b).CheckPermissionAsync(Bob, BobsCode)).ShouldBeTrue(); // 缓存热
        }

        using (var host = BuildProvider(tenantId: null, userId: HostAdmin))
        {
            (await Auth(host).DisableModuleFunctionAsync(_bobsFunctionId)).Succeeded.ShouldBeTrue();
        }

        // 宿主上下文按租户过滤只看得见宿主的授权行，精确失效会漏掉租户 B 的持有者。
        using var after = BuildProvider(TenantB, Bob);
        (await Auth(after).CheckPermissionAsync(Bob, BobsCode)).ShouldBeFalse();
    }

    // ── 按用户 id 寻址的入口按租户范围收窄 ─────────────────────────────────────

    [Fact]
    public async Task Reading_another_tenants_user_answers_not_found()
    {
        using var a = BuildProvider(TenantA, AdminA);
        var auth = Auth(a);

        var names = await auth.GetUserPermissionNamesWithResultAsync(Bob);
        names.Succeeded.ShouldBeFalse();
        names.Code.ShouldBe(404);

        var check = await auth.CheckPermissionWithResultAsync(Bob, BobsCode);
        check.Succeeded.ShouldBeFalse();
        check.Code.ShouldBe(404);
    }

    [Fact]
    public async Task Reading_a_user_in_scope_still_works()
    {
        using var b = BuildProvider(TenantB, Bob);
        var self = await Auth(b).GetUserPermissionNamesWithResultAsync(Bob);
        self.Succeeded.ShouldBeTrue();
        self.Data!.ShouldBe(new[] { BobsCode });

        using var host = BuildProvider(tenantId: null, userId: HostAdmin);
        (await Auth(host).CheckPermissionWithResultAsync(Bob, BobsCode)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Writing_direct_grants_for_another_tenants_user_answers_not_found_and_writes_nothing()
    {
        using (var a = BuildProvider(TenantA, AdminA))
        {
            var userFunctions = a.GetRequiredService<IUserFunctionService>();

            (await userFunctions.AssignFunctionsToUserAsync(Bob, [_foreignFunctionId])).Code.ShouldBe(404);
            (await userFunctions.SetUserFunctionsAsync(Bob, [_foreignFunctionId])).Code.ShouldBe(404);
            (await userFunctions.SetUserDeniedFunctionsAsync(Bob, [_foreignFunctionId])).Code.ShouldBe(404);
            (await userFunctions.ClearUserFunctionsAsync(Bob)).Code.ShouldBe(404);
            (await userFunctions.GetUserFunctionIdsAsync(Bob)).Code.ShouldBe(404);
        }

        using var host = BuildProvider(tenantId: null, userId: HostAdmin);
        host.GetRequiredService<AuthorizationTestDbContext>().UserFunctions.IgnoreQueryFilters()
            .Count(uf => uf.UserId == Bob).ShouldBe(0);
    }

    // ── 全局目录只能在宿主上下文里改 ───────────────────────────────────────────

    [Fact]
    public async Task Catalogue_writes_in_a_tenant_context_are_refused()
    {
        using (var a = BuildProvider(TenantA, AdminA))
        {
            var auth = Auth(a);
            (await auth.DisableModuleFunctionAsync(_bobsFunctionId)).Code.ShouldBe(403);
            (await auth.DisableModuleAsync(_moduleId)).Code.ShouldBe(403);
            (await auth.DeleteModuleFunctionAsync(_customFunctionId)).Code.ShouldBe(403);
            (await auth.CreateModuleFunctionAsync(new CreateModuleFunctionRequest { Code = "report.print", Name = "Print", ModuleId = _moduleId })).Code.ShouldBe(403);
            (await auth.UpdateModuleFunctionAsync(_customFunctionId, new UpdateModuleFunctionRequest { Code = CustomCode, Name = "Renamed", ModuleId = _moduleId })).Code.ShouldBe(403);
        }

        using var host = BuildProvider(tenantId: null, userId: HostAdmin);
        var db = host.GetRequiredService<AuthorizationTestDbContext>();
        db.ModuleFunctions.Single(f => f.Id == _bobsFunctionId).IsEnabled.ShouldBeTrue();
        db.ModuleFunctions.Any(f => f.Id == _customFunctionId).ShouldBeTrue();
        db.ModuleFunctions.Any(f => f.Code == "report.print").ShouldBeFalse();
        db.FunctionModules.Single(m => m.Id == _moduleId).IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Catalogue_writes_in_the_host_context_are_allowed()
    {
        using var host = BuildProvider(tenantId: null, userId: HostAdmin);
        var auth = Auth(host);
        (await auth.DisableModuleFunctionAsync(_customFunctionId)).Succeeded.ShouldBeTrue();
        (await auth.EnableModuleFunctionAsync(_customFunctionId)).Succeeded.ShouldBeTrue();
        (await auth.DeleteModuleFunctionAsync(_customFunctionId)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Deleting_a_function_counts_grants_in_every_tenant()
    {
        using (var b = BuildProvider(TenantB, Bob))
        {
            var db = b.GetRequiredService<AuthorizationTestDbContext>();
            db.RoleFunctions.Add(new RoleFunction { Id = Guid.NewGuid(), RoleId = RoleB, FunctionId = _customFunctionId, IsEnabled = true, TenantId = TenantB });
            await db.SaveChangesAsync();
        }

        // 宿主上下文按租户过滤只看得见宿主的行；关联检查必须跨租户。
        using var host = BuildProvider(tenantId: null, userId: HostAdmin);
        var result = await Auth(host).DeleteModuleFunctionAsync(_customFunctionId);
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        host.GetRequiredService<AuthorizationTestDbContext>().ModuleFunctions.Any(f => f.Id == _customFunctionId).ShouldBeTrue();
    }

    // ── 夹具 ────────────────────────────────────────────────────────────────

    private ModuleFunction NewFunction(Guid id, string code, bool systemManaged) => new()
    {
        Id = id, Code = code, Name = code, ModuleId = _moduleId, IsEnabled = true,
        IsSystemManaged = systemManaged, CreationTime = DateTime.UtcNow,
    };

    private static FunctionAuthorizationService Auth(IServiceProvider provider)
        => provider.GetRequiredService<FunctionAuthorizationService>();

    /// <summary>一次「请求」：自己的当前用户与当前租户，共享的连接与缓存。</summary>
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
        services.AddSingleton(MsOptions.Create(new AuthOptions { SuperAdminRoles = ["SuperAdmin"] }));

        services.AddDbContext<AuthorizationTestDbContext>(options =>
        {
            options.UseSqlite(_connection);
            options.UseTnziMultiTenancy(true);
        });
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<AuthorizationTestDbContext>());

        AddRepo<FunctionModule>(services);
        AddRepo<ModuleFunction>(services);
        AddRepo<RoleFunction>(services);
        AddRepo<UserFunction>(services);
        AddRepo<IdentityRole>(services);
        AddRepo<IdentityUser>(services);

        services.AddScoped<IUserRoleService>(sp => new UserRoleService(sp.GetRequiredService<DbContext>(), sp));
        services.AddScoped<IUserTenantScopeProvider, UserTenantScopeProvider>();
        services.AddSingleton(_sharedCache);
        services.AddSingleton<FunctionAuthCache>();

        // 与 AuthorizationModule 同一形状的装配：可选依赖由容器按在不在注入。
        services.AddScoped<FunctionAuthorizationService>();
        services.AddScoped<Tnzi.Authorization.Services.IFunctionAuthorizationService>(sp => sp.GetRequiredService<FunctionAuthorizationService>());
        services.AddScoped<IUserFunctionService, UserFunctionService>();

        return services.BuildServiceProvider();
    }

    private static void AddRepo<TEntity>(IServiceCollection services) where TEntity : class, Tnzi.Domain.Entities.IEntity<Guid>
    {
        services.AddScoped<IRepository<TEntity, Guid>>(sp =>
            new EFCoreRepository<AuthorizationTestDbContext, TEntity, Guid>(
                sp.GetRequiredService<AuthorizationTestDbContext>(), serviceProvider: sp));
    }

    public Task DisposeAsync()
    {
        _connection.Dispose();
        return Task.CompletedTask;
    }
}

/// <summary>
/// 用 <c>TnziJsonDefaults.Options</c> 把值序列化再还原，模拟分布式缓存那条路径（缓存的是 JSON，不是对象引用）。
/// </summary>
/// <remarks>只实现权限缓存用到的成员，其余显式不支持：一个假装什么都能干的桩会把缺陷伪装成通过。</remarks>
internal sealed class JsonRoundTripCache(ICache inner) : ICache
{
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var json = await inner.GetAsync<string>(key, cancellationToken);
        return json == null ? default : JsonSerializer.Deserialize<T>(json, TnziJsonDefaults.Options);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
        => inner.SetAsync(key, JsonSerializer.Serialize(value, TnziJsonDefaults.Options), expiration, cancellationToken);

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        => inner.RemoveAsync(key, cancellationToken);

    public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
        => inner.RemoveByPrefixAsync(prefix, cancellationToken);

    public Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<long> IncrementAsync(string key, long increment = 1, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<long> IncrementAsync(string key, long increment, TimeSpan expiration, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<long> DecrementAsync(string key, long decrement = 1, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> TrySetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task SetWithTagsAsync<T>(string key, T value, IEnumerable<string> tags, TimeSpan? expiration = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<Dictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task SetManyAsync<T>(IEnumerable<KeyValuePair<string, T>> items, TimeSpan? expiration = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
