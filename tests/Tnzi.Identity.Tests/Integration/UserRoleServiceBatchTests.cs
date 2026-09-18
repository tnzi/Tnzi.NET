namespace Tnzi.Identity.Tests.Integration;

/// <summary>
/// <see cref="UserRoleService.GetUserRoleIdsAsync(IEnumerable{Guid})"/>：授权模块批量解析
/// N 个用户的授予时用它一次拿到每个人的角色 Id（一条 IN 查询），与既有的
/// <c>GetUserRolesAsync</c>（返回名称）同形。
/// </summary>
public class UserRoleServiceBatchTests : IntegratedTestBase<UserRoleTestDbContext>
{
    private static readonly Guid RoleA = Guid.NewGuid();
    private static readonly Guid RoleB = Guid.NewGuid();
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static readonly Guid Nobody = Guid.NewGuid();

    private async Task<UserRoleService> BuildAsync()
    {
        DbContext.Set<Role>().AddRange(
            new Role { Id = RoleA, Name = "A", NormalizedName = "A" },
            new Role { Id = RoleB, Name = "B", NormalizedName = "B" });
        DbContext.Set<UserRole>().AddRange(
            new UserRole { UserId = Alice, RoleId = RoleA },
            new UserRole { UserId = Alice, RoleId = RoleB },
            new UserRole { UserId = Bob, RoleId = RoleB });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return new UserRoleService(DbContext, ServiceProvider);
    }

    [Fact]
    public async Task BatchRoleIds_GroupsByUser_AndOmitsUsersWithoutRoles()
    {
        var service = await BuildAsync();

        var result = await service.GetUserRoleIdsAsync([Alice, Bob, Nobody, Alice]);

        result.Keys.ShouldBe([Alice, Bob], ignoreOrder: true);
        result[Alice].ShouldBe([RoleA, RoleB], ignoreOrder: true);
        result[Bob].ShouldBe([RoleB]);
    }

    [Fact]
    public async Task BatchRoleIds_MatchesThePerUserLookup()
    {
        var service = await BuildAsync();

        var batch = await service.GetUserRoleIdsAsync([Alice, Bob]);

        foreach (var userId in new[] { Alice, Bob })
        {
            var single = await service.GetUserRoleIdsAsync(userId);
            batch[userId].ShouldBe(single, ignoreOrder: true);
        }
    }

    [Fact]
    public async Task BatchRoleIds_EmptyInput_ReturnsEmpty()
    {
        var service = await BuildAsync();

        (await service.GetUserRoleIdsAsync([])).ShouldBeEmpty();
    }
}

/// <summary>只映射角色成员关系的最小 DbContext（不拉入 Identity 全模型）。</summary>
public class UserRoleTestDbContext : TnziDbContext<UserRoleTestDbContext>
{
    public UserRoleTestDbContext(DbContextOptions<UserRoleTestDbContext> options, Security.Claims.ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Role>(b =>
        {
            b.ToTable("Identity_Role");
            b.HasKey(r => r.Id);
        });
        modelBuilder.Entity<UserRole>(b =>
        {
            b.ToTable("Identity_UserRole");
            b.HasKey(ur => new { ur.UserId, ur.RoleId });
        });

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}
