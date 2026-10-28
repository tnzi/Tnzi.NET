using Microsoft.AspNetCore.Identity;
using Tnzi.Identity.Services;

namespace Tnzi.Identity.IntegrationTests.Services;

/// <summary>
/// <c>PUT admin/users/{id}</c> 里资料那一半（姓名 / 昵称 / 头像 / 性别 / 生日 / 简介 / 地址 / 网站）的写入语义。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ 资料是<b>整组替换</b>：请求里为 null 的资料字段会被清空（自助资料就靠这一点清掉某一栏）。
/// 而账号那一半（邮箱 / 手机 / 组织 / 角色）是「null = 不动」。同一个 DTO 两种语义，此前不论请求带没带资料字段，
/// 资料都按整组替换写一遍：管理端列表的编辑弹窗只改了手机号，那个人的姓名、昵称、头像、生日、简介就全没了，
/// 性别归零；头像引用一掉，文件还可能被孤儿清理删掉。
/// </para>
/// <para>
/// 口径：请求里<b>一个资料字段都没有</b>时不碰资料；带了任何一个就整组替换。自助端点（<c>UpdateProfileAsync</c>）
/// 只收资料字段，它的请求永远是一次整组替换，与此前逐字相同。
/// </para>
/// </remarks>
public class UserProfileUpdateSemanticsIntegrationTests : RelationalIdentityIntegrationTestBase
{
    private static readonly Guid AvatarFile = Guid.NewGuid();

    private readonly UserService _users;

    public UserProfileUpdateSemanticsIntegrationTests()
    {
        var details = new UserDetailService(CreateRepository<UserDetail>(), UserManager, ServiceProvider, new UserTenantScopeProvider(CreateRepository<User>()));
        _users = new UserService(
            UserManager,
            ServiceProvider.GetRequiredService<RoleManager<Role>>(),
            CreateRepository<User>(),
            ServiceProvider,
            eventBus: EventBusMock.Object,
            userDetailService: details);
    }

    private async Task<User> SeedUserWithProfileAsync()
    {
        var user = await CreateUserAsync(email: "ivy@corp.example", phoneNumber: "+15550000001");
        DbContext.UserDetails.Add(new UserDetail
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            FirstName = "Ivy",
            LastName = "Lee",
            Nickname = "ivy",
            AvatarId = AvatarFile,
            AvatarUrl = "https://cdn.example/ivy.png",
            Gender = 2,
            Birthday = new DateTime(1990, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            Bio = "Payroll lead",
            Address = "1 Main St",
            Website = "https://ivy.example",
        });
        await SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return user;
    }

    private async Task<UserDetail> StoredDetailAsync(Guid userId)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.UserDetails.AsNoTracking().SingleAsync(d => d.UserId == userId);
    }

    private static void AssertProfileUntouched(UserDetail detail)
    {
        Assert.Equal("Ivy", detail.FirstName);
        Assert.Equal("Lee", detail.LastName);
        Assert.Equal("ivy", detail.Nickname);
        Assert.Equal(AvatarFile, detail.AvatarId);
        Assert.Equal("https://cdn.example/ivy.png", detail.AvatarUrl);
        Assert.Equal(2, detail.Gender);
        Assert.Equal(new DateTime(1990, 5, 1, 0, 0, 0, DateTimeKind.Utc), detail.Birthday);
        Assert.Equal("Payroll lead", detail.Bio);
        Assert.Equal("1 Main St", detail.Address);
        Assert.Equal("https://ivy.example", detail.Website);
    }

    /// <summary>列表编辑弹窗的真实形状：只带了账号字段（外加列表行上的其它键，后端绑定时丢弃）。</summary>
    [Fact]
    public async Task An_account_only_update_leaves_the_profile_untouched()
    {
        var user = await SeedUserWithProfileAsync();

        var result = await _users.UpdateAsync(user.Id, new UpdateUserDto { Email = "ivy@corp.example", PhoneNumber = "+15550000002" });

        Assert.True(result.Succeeded, result.Message);
        AssertProfileUntouched(await StoredDetailAsync(user.Id));
        Assert.Equal("Ivy", result.Data!.FirstName);
    }

    [Fact]
    public async Task A_role_only_update_leaves_the_profile_untouched()
    {
        var user = await SeedUserWithProfileAsync();

        var result = await _users.UpdateAsync(user.Id, new UpdateUserDto { RoleIds = [] });

        Assert.True(result.Succeeded, result.Message);
        AssertProfileUntouched(await StoredDetailAsync(user.Id));
    }

    /// <summary>对照：带了资料字段就是整组替换，没带的那几栏被清空。这是契约，不是缺陷。</summary>
    [Fact]
    public async Task An_update_that_carries_profile_fields_replaces_the_profile()
    {
        var user = await SeedUserWithProfileAsync();

        var result = await _users.UpdateAsync(user.Id, new UpdateUserDto { Nickname = "ivy-2", AvatarId = AvatarFile, AvatarUrl = "https://cdn.example/ivy.png" });

        Assert.True(result.Succeeded, result.Message);
        var stored = await StoredDetailAsync(user.Id);
        Assert.Equal("ivy-2", stored.Nickname);
        Assert.Equal(AvatarFile, stored.AvatarId);
        Assert.Null(stored.FirstName);
        Assert.Null(stored.Bio);
        Assert.Equal(0, stored.Gender);
    }

    /// <summary>自助端点只收资料字段，一次全空的保存就是「把资料清空」，与此前相同。</summary>
    [Fact]
    public async Task A_self_service_save_with_every_field_empty_still_clears_the_profile()
    {
        var user = await SeedUserWithProfileAsync();

        var result = await _users.UpdateProfileAsync(user.Id, new UpdateProfileDto());

        Assert.True(result.Succeeded, result.Message);
        var stored = await StoredDetailAsync(user.Id);
        Assert.Null(stored.FirstName);
        Assert.Null(stored.Nickname);
        Assert.Null(stored.AvatarId);
    }
}
