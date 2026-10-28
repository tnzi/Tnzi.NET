using Tnzi.MultiTenancy;

namespace Tnzi.Identity.Tests.Integration;

/// <summary>
/// 生日与租户到期时间在写库前必须是 <see cref="DateTimeKind.Utc"/>。
/// </summary>
/// <remarks>
/// <para>
/// ★ 管理端与个人中心把日期选择器选中的那一天作为 <c>YYYY-MM-DD</c> 发出，JSON 反序列化得到的是
/// <see cref="DateTimeKind.Unspecified"/>；PostgreSQL 的 Npgsql 拒绝把它写进 <c>timestamp with time zone</c> 列，
/// 保存就以 500 结束。SQLite 不分 Kind，所以这里不读回库里的值（转换器会把它读成 UTC），
/// 断言的是交给 EF 的那个实例。
/// </para>
/// <para>
/// 生日是纯日期：落成那一天的 UTC 零点（<c>ToUtcDate</c>），与框架其余 date-only 字段同口径。
/// 租户到期是时刻（与 <c>UtcNow</c> 比较）：带时区的值换算成 UTC，不带时区的按 UTC 解读。
/// </para>
/// </remarks>
public class IdentityDateValueKindTests : IntegrationTestBase
{
    private readonly Guid _userId = Guid.NewGuid();

    private async Task<UserDetailService> BuildDetailServiceAsync()
    {
        if (!await DbContext.Users.AnyAsync(u => u.Id == _userId))
        {
            DbContext.Users.Add(new User { Id = _userId, UserName = "birthday", NormalizedUserName = "BIRTHDAY" });
            await DbContext.SaveChangesAsync();
            DbContext.ChangeTracker.Clear();
        }

        var store = new Mock<IUserStore<User>>();
        var userManager = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        userManager.Setup(m => m.FindByIdAsync(_userId.ToString())).ReturnsAsync(new User { Id = _userId, UserName = "birthday" });
        return new UserDetailService(
            new EFCoreRepository<IdentityTestDbContext, UserDetail, Guid>(DbContext, serviceProvider: ServiceProvider),
            userManager.Object,
            ServiceProvider,
            new UserTenantScopeProvider(new EFCoreRepository<IdentityTestDbContext, User, Guid>(DbContext, serviceProvider: ServiceProvider)));
    }

    private UserDetail TrackedDetail() => DbContext.ChangeTracker.Entries<UserDetail>().Single(e => e.Entity.UserId == _userId).Entity;

    [Fact]
    public async Task Birthday_FromADateOnlyValue_IsWrittenAsThatDayInUtc()
    {
        using var mapper = MapperExtensions.PushMapper(new Mapper());
        var service = await BuildDetailServiceAsync();

        var result = await service.CreateOrUpdateAsync(_userId, new CreateUserDetailDto
        {
            Birthday = new DateTime(1990, 5, 1, 0, 0, 0, DateTimeKind.Unspecified),
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        var birthday = TrackedDetail().Birthday!.Value;
        birthday.Kind.ShouldBe(DateTimeKind.Utc);
        birthday.ShouldBe(new DateTime(1990, 5, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Birthday_OnUpdate_IsWrittenAsThatDayInUtc()
    {
        using var mapper = MapperExtensions.PushMapper(new Mapper());
        var service = await BuildDetailServiceAsync();
        await service.CreateOrUpdateAsync(_userId, new CreateUserDetailDto { Nickname = "first" });
        DbContext.ChangeTracker.Clear();

        var result = await (await BuildDetailServiceAsync()).CreateOrUpdateAsync(_userId, new CreateUserDetailDto
        {
            Nickname = "first",
            Birthday = new DateTime(1990, 5, 1, 0, 0, 0, DateTimeKind.Unspecified),
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        TrackedDetail().Birthday!.Value.Kind.ShouldBe(DateTimeKind.Utc);
        TrackedDetail().Birthday!.Value.ShouldBe(new DateTime(1990, 5, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Birthday_Cleared_StaysCleared()
    {
        using var mapper = MapperExtensions.PushMapper(new Mapper());
        var service = await BuildDetailServiceAsync();

        var result = await service.CreateOrUpdateAsync(_userId, new CreateUserDetailDto { Nickname = "no birthday" });

        result.Succeeded.ShouldBeTrue(result.Message);
        TrackedDetail().Birthday.ShouldBeNull();
    }
}

public class TenantExpiryKindTests
{
    private readonly Mock<IRepository<Tenant, Guid>> _repository = new();
    private readonly TenantService _service;

    public TenantExpiryKindTests()
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        _service = new TenantService(
            _repository.Object,
            services,
            multiTenancyOptions: Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions { Enabled = true }));
    }

    [Fact]
    public async Task Create_WithADateOnlyExpiry_WritesItAsUtc()
    {
        Tenant? written = null;
        _repository.Setup(r => r.InsertAsync(It.IsAny<Tenant>(), It.IsAny<CancellationToken>()))
            .Callback<Tenant, CancellationToken>((t, _) => written = t)
            .Returns(Task.CompletedTask);

        using (MapperExtensions.PushMapper(new Mapper()))
        {
            var result = await _service.CreateAsync(new CreateTenantDto
            {
                Name = "Gamma",
                ExpiredAt = new DateTime(2027, 1, 31, 0, 0, 0, DateTimeKind.Unspecified),
            });
            result.Succeeded.ShouldBeTrue(result.Message);
        }

        written.ShouldNotBeNull();
        written.ExpiredAt!.Value.Kind.ShouldBe(DateTimeKind.Utc);
        written.ExpiredAt.Value.ShouldBe(new DateTime(2027, 1, 31, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Update_WithAnOffsetExpiry_WritesTheSameInstantInUtc()
    {
        var id = Guid.NewGuid();
        var tenant = new Tenant { Id = id, Name = "Acme" };
        _repository.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        _repository.Setup(r => r.UpdateAsync(It.IsAny<Tenant>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var instant = new DateTimeOffset(2027, 6, 30, 13, 0, 0, TimeSpan.FromHours(-4));

        using (MapperExtensions.PushMapper(new Mapper()))
        {
            var result = await _service.UpdateAsync(id, new UpdateTenantDto
            {
                Name = "Acme",
                IsEnabled = true,
                ExpiredAt = instant.LocalDateTime,
            });
            result.Succeeded.ShouldBeTrue(result.Message);
        }

        tenant.ExpiredAt!.Value.Kind.ShouldBe(DateTimeKind.Utc);
        tenant.ExpiredAt.Value.ShouldBe(instant.UtcDateTime);
    }
}
