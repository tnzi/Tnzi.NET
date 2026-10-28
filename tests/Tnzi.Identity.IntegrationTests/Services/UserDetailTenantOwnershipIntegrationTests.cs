using Microsoft.AspNetCore.Identity;
using Tnzi.Exceptions;
using Tnzi.Identity.Services;
using Tnzi.MultiTenancy;

namespace Tnzi.Identity.IntegrationTests.Services;

/// <summary>
/// 一个账号的详情行归这个账号，不归恰好在操作它的那个请求的租户上下文。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ <c>UserDetail</c> 是 <c>IMultiTenant</c>，全局过滤器是严格等值；<c>User</c> 不是，而且全局管理员
/// （当前租户为空）按 <c>UserTenantScope</c> 本来就该管得到所有租户的账号。此前详情按环境租户读写：
/// 全局管理员打开租户用户，资料一栏是空的；保存一次，过滤后的查找看不见那一行，于是再插一行，
/// 撞上 <c>UserId</c> 唯一索引答 500，而账号表那一半已经写下。对方还没有详情时，插进去的是一行
/// <c>TenantId = null</c>，对方在自己租户里从此读不到，自助资料每次都 500。
/// </para>
/// <para>
/// 用真实的 <see cref="CurrentTenant"/>（静态 AsyncLocal）同时驱动 EF 过滤器与范围判定，真实 SQLite。
/// </para>
/// </remarks>
public class UserDetailTenantOwnershipIntegrationTests : RelationalIdentityIntegrationTestBase
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _actor = new();
    private readonly ICurrentTenant _currentTenant;
    private readonly UserDetailService _details;
    private readonly UserService _users;

    public UserDetailTenantOwnershipIntegrationTests()
        : base(configureServices: services =>
        {
            services.Configure<MultiTenancyOptions>(o => o.Enabled = true);
            services.AddScoped<ICurrentTenant, CurrentTenant>();
        })
    {
        _currentTenant = ServiceProvider.GetRequiredService<ICurrentTenant>();
        _actor.Setup(u => u.IsAuthenticated).Returns(true);
        _actor.Setup(u => u.Id).Returns(Guid.NewGuid());

        var multiTenancy = Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions { Enabled = true });
        _details = new UserDetailService(
            CreateRepository<UserDetail>(),
            UserManager,
            ServiceProvider,
            new UserTenantScopeProvider(CreateRepository<User>(), _currentTenant, _actor.Object, multiTenancy));
        _users = new UserService(
            UserManager,
            ServiceProvider.GetRequiredService<RoleManager<Role>>(),
            CreateRepository<User>(),
            ServiceProvider,
            eventBus: EventBusMock.Object,
            currentUser: _actor.Object,
            userDetailService: _details,
            currentTenant: _currentTenant,
            multiTenancyOptions: multiTenancy);
    }

    private async Task<User> CreateTenantUserAsync(Guid? tenantId, string email)
    {
        var user = await CreateUserAsync(email: email);
        user.TenantId = tenantId;
        await SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return user;
    }

    private async Task SeedDetailAsync(Guid userId, Guid? tenantId, string firstName)
    {
        DbContext.UserDetails.Add(new UserDetail { Id = Guid.NewGuid(), UserId = userId, TenantId = tenantId, FirstName = firstName, Nickname = "bee" });
        await SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private async Task<List<UserDetail>> RowsOfAsync(Guid userId)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.UserDetails.IgnoreQueryFilters().Where(d => d.UserId == userId && !d.IsDeleted).ToListAsync();
    }

    [Fact]
    public async Task Host_admin_updates_a_tenant_users_profile_in_place()
    {
        var userB = await CreateTenantUserAsync(TenantB, "b@corp.example");
        await SeedDetailAsync(userB.Id, TenantB, "Bea");

        var result = await _users.UpdateAsync(userB.Id, new UpdateUserDto { PhoneNumber = "+15550000009", FirstName = "Beatrice", Nickname = "bee" });

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("Beatrice", result.Data!.FirstName);
        var row = Assert.Single(await RowsOfAsync(userB.Id));
        Assert.Equal(TenantB, row.TenantId);
        Assert.Equal("Beatrice", row.FirstName);
    }

    [Fact]
    public async Task Host_admin_sees_a_tenant_users_profile()
    {
        var userB = await CreateTenantUserAsync(TenantB, "b@corp.example");
        await SeedDetailAsync(userB.Id, TenantB, "Bea");

        var result = await _users.GetByIdAsync(userB.Id);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("Bea", result.Data!.FirstName);
        Assert.Equal("bee", result.Data.Nickname);
    }

    /// <summary>新建的详情行打的是账号自己的租户号：对方在自己租户里读得到它。</summary>
    [Fact]
    public async Task Host_admin_creating_a_profile_stamps_the_accounts_tenant()
    {
        var userB = await CreateTenantUserAsync(TenantB, "b@corp.example");

        var result = await _users.UpdateAsync(userB.Id, new UpdateUserDto { Nickname = "set-by-host" });

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(TenantB, Assert.Single(await RowsOfAsync(userB.Id)).TenantId);
        using (_currentTenant.Change(TenantB))
        {
            DbContext.ChangeTracker.Clear();
            Assert.Equal(["set-by-host"], await DbContext.UserDetails.Where(d => d.UserId == userB.Id).Select(d => d.Nickname).ToListAsync());
        }
    }

    /// <summary>按 userId 不看租户过滤器之后，跨租户的读取由范围判定拦住，回答与「没有这行」相同。</summary>
    [Fact]
    public async Task Tenant_admin_still_cannot_read_another_tenants_profile()
    {
        var userA = await CreateTenantUserAsync(TenantA, "a@corp.example");
        var userB = await CreateTenantUserAsync(TenantB, "b@corp.example");
        await SeedDetailAsync(userA.Id, TenantA, "Ann");
        await SeedDetailAsync(userB.Id, TenantB, "Bea");

        using (_currentTenant.Change(TenantA))
        {
            var foreign = await _details.GetByUserIdAsync(userB.Id);
            Assert.Equal(404, foreign.Code);
            Assert.Equal(ErrorCodes.RESOURCE_NOT_FOUND, foreign.ErrorCode);

            var own = await _details.GetByUserIdAsync(userA.Id);
            Assert.True(own.Succeeded, own.Message);
            Assert.Equal("Ann", own.Data!.FirstName);

            var batch = await _details.GetDetailsByUserIdsAsync([userA.Id, userB.Id]);
            Assert.Equal([userA.Id], batch.Data!.Keys);
        }
    }

    /// <summary>
    /// 修复之前种下的那种行（打着别的租户号）不再让账号的资料永久 500：按 userId 找得到它，写入时改回账号的租户。
    /// </summary>
    [Fact]
    public async Task A_row_stamped_with_the_wrong_tenant_is_found_and_reclaimed()
    {
        var userB = await CreateTenantUserAsync(TenantB, "b@corp.example");
        await SeedDetailAsync(userB.Id, tenantId: null, "Planted");

        using (_currentTenant.Change(TenantB))
        {
            var result = await _users.UpdateAsync(userB.Id, new UpdateUserDto { FirstName = "Bea" });
            Assert.True(result.Succeeded, result.Message);
        }

        var row = Assert.Single(await RowsOfAsync(userB.Id));
        Assert.Equal(TenantB, row.TenantId);
        Assert.Equal("Bea", row.FirstName);
    }
}
