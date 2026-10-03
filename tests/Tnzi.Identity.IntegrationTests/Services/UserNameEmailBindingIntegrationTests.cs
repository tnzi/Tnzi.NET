using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Tnzi.Identity.Extensions;
using Tnzi.Identity.Services;
using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.IntegrationTests.Services;

/// <summary>
/// 用户名与邮箱的绑定规则，走真 <see cref="UserManager{TUser}"/> 与真校验链：
/// 建号时用户名怎么定、改邮箱时用户名跟不跟、以及跨字段唯一性。
/// </summary>
public abstract class UserNameEmailBindingIntegrationTestBase : RelationalIdentityIntegrationTestBase
{
    protected readonly UserService Service;

    protected UserNameEmailBindingIntegrationTestBase(bool useEmailAsUserName)
        : base(
            configureIdentity: o => o.SignIn.UseEmailAsUserName = useEmailAsUserName,
            configureServices: s => s.AddScoped<IUserValidator<User>, CrossFieldIdentifierValidator>())
    {
        Service = new UserService(
            UserManager,
            ServiceProvider.GetRequiredService<RoleManager<Role>>(),
            CreateRepository<User>(),
            ServiceProvider,
            eventBus: EventBusMock.Object,
            currentUser: ServiceProvider.GetRequiredService<ICurrentUser>(),
            cache: Cache,
            identityOptions: ServiceProvider.GetRequiredService<IOptionsMonitor<IdentityOptions>>());
    }

    protected async Task<User> ReloadAsync(Guid id)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Users.SingleAsync(u => u.Id == id);
    }

    /// <summary>直接经 UserManager 建一个指定用户名与邮箱的账号（绕开服务层策略，用来摆出存量形态）。</summary>
    protected async Task<User> SeedAsync(string userName, string? email)
    {
        var user = new User { Id = Guid.NewGuid(), UserName = userName, Email = email, EmailConfirmed = true, CreationTime = DateTime.UtcNow };
        var result = await UserManager.CreateAsync(user, "Password123!");
        Assert.True(result.Succeeded, result.Errors.FirstOrDefault()?.Description);
        return user;
    }
}

public class UserNameEmailBindingDefaultModeTests : UserNameEmailBindingIntegrationTestBase
{
    public UserNameEmailBindingDefaultModeTests() : base(useEmailAsUserName: true) { }

    [Fact]
    public async Task Create_WithoutUserName_UsesEmail()
    {
        var created = await Service.CreateAsync(new CreateUserDto { Email = "alice@example.com", Password = "Password123!" });

        Assert.True(created.Succeeded, created.Message);
        Assert.Equal("alice@example.com", created.Data!.UserName);
    }

    [Fact]
    public async Task Create_WithUserNameEqualToEmail_IgnoringCase_Succeeds()
    {
        var created = await Service.CreateAsync(new CreateUserDto { UserName = "Alice@Example.com", Email = "alice@example.com", Password = "Password123!" });

        Assert.True(created.Succeeded, created.Message);
        Assert.Equal("alice@example.com", created.Data!.UserName);
    }

    [Fact]
    public async Task Create_WithDifferentUserName_IsRejected_AndNothingIsWritten()
    {
        var created = await Service.CreateAsync(new CreateUserDto { UserName = "alice", Email = "alice@example.com", Password = "Password123!" });

        Assert.False(created.Succeeded);
        Assert.Equal(400, created.Code);
        Assert.False(await DbContext.Users.AnyAsync());
    }

    [Fact]
    public async Task Create_WithoutEmail_RequiresUserName()
    {
        var missing = await Service.CreateAsync(new CreateUserDto { PhoneNumber = "+15550100", Password = "Password123!" });
        Assert.False(missing.Succeeded);
        Assert.Equal(400, missing.Code);

        var created = await Service.CreateAsync(new CreateUserDto { UserName = "field-agent", PhoneNumber = "+15550100", Password = "Password123!" });
        Assert.True(created.Succeeded, created.Message);
        Assert.Equal("field-agent", created.Data!.UserName);
    }

    [Fact]
    public async Task ChangeEmail_OnLinkedAccount_MovesUserNameInTheSameUpdate()
    {
        var created = await Service.CreateAsync(new CreateUserDto { Email = "old@example.com", Password = "Password123!" });
        Assert.True(created.Succeeded, created.Message);

        var changed = await Service.ChangeEmailAsync(created.Data!.Id, "new@example.com");
        Assert.True(changed.Succeeded, changed.Message);

        var reloaded = await ReloadAsync(created.Data.Id);
        Assert.Equal("new@example.com", reloaded.UserName);
        Assert.Equal(UserManager.NormalizeName("new@example.com"), reloaded.NormalizedUserName);
        Assert.Equal("new@example.com", reloaded.Email);
        Assert.Null(await UserManager.FindByNameAsync("old@example.com"));
    }

    [Fact]
    public async Task AdminUpdate_OnLinkedAccount_MovesUserName()
    {
        var created = await Service.CreateAsync(new CreateUserDto { Email = "old@example.com", Password = "Password123!" });
        Assert.True(created.Succeeded, created.Message);

        var updated = await Service.UpdateAsync(created.Data!.Id, new UpdateUserDto { Email = "new@example.com" });
        Assert.True(updated.Succeeded, updated.Message);
        Assert.Equal("new@example.com", updated.Data!.UserName);

        var reloaded = await ReloadAsync(created.Data.Id);
        Assert.Equal("new@example.com", reloaded.UserName);
        Assert.False(reloaded.EmailConfirmed);
    }

    [Fact]
    public async Task ChangeEmail_OnIndependentUserName_LeavesUserNameAlone()
    {
        var seeded = await SeedAsync("admin", "admin@example.com");

        var changed = await Service.ChangeEmailAsync(seeded.Id, "ops@example.com");
        Assert.True(changed.Succeeded, changed.Message);

        var reloaded = await ReloadAsync(seeded.Id);
        Assert.Equal("admin", reloaded.UserName);
        Assert.Equal("ops@example.com", reloaded.Email);
    }

    [Fact]
    public async Task ChangeEmail_ToAnotherAccountsUserName_FailsAndChangesNothing()
    {
        await SeedAsync("taken@example.com", "taken@example.com");
        var created = await Service.CreateAsync(new CreateUserDto { Email = "mine@example.com", Password = "Password123!" });
        Assert.True(created.Succeeded, created.Message);

        var changed = await Service.ChangeEmailAsync(created.Data!.Id, "taken@example.com");
        Assert.False(changed.Succeeded);

        var reloaded = await ReloadAsync(created.Data.Id);
        Assert.Equal("mine@example.com", reloaded.UserName);
        Assert.Equal("mine@example.com", reloaded.Email);
    }
}

public class UserNameEmailBindingIndependentModeTests : UserNameEmailBindingIntegrationTestBase
{
    public UserNameEmailBindingIndependentModeTests() : base(useEmailAsUserName: false) { }

    [Fact]
    public async Task Create_WithIndependentUserName_Succeeds()
    {
        var created = await Service.CreateAsync(new CreateUserDto { UserName = "E1024", Email = "bob@example.com", Password = "Password123!" });

        Assert.True(created.Succeeded, created.Message);
        Assert.Equal("E1024", created.Data!.UserName);
    }

    [Fact]
    public async Task Create_WithoutUserName_IsRejected()
    {
        var created = await Service.CreateAsync(new CreateUserDto { Email = "bob@example.com", Password = "Password123!" });

        Assert.False(created.Succeeded);
        Assert.Equal(400, created.Code);
    }

    [Fact]
    public async Task Create_WithSomeoneElsesEmailAsUserName_IsRejected()
    {
        var created = await Service.CreateAsync(new CreateUserDto { UserName = "victim@example.com", Email = "me@example.com", Password = "Password123!" });

        Assert.False(created.Succeeded);
        Assert.Equal(400, created.Code);
    }

    [Fact]
    public async Task ChangeEmail_OnLinkedAccount_StillFollows()
    {
        // 关闭开关只改建号规则；用户名等于邮箱的账号照样绑在一起
        var seeded = await SeedAsync("carol@example.com", "carol@example.com");

        var changed = await Service.ChangeEmailAsync(seeded.Id, "carol@corp.example.com");
        Assert.True(changed.Succeeded, changed.Message);

        var reloaded = await ReloadAsync(seeded.Id);
        Assert.Equal("carol@corp.example.com", reloaded.UserName);
    }
}

public class CrossFieldIdentifierValidatorIntegrationTests : UserNameEmailBindingIntegrationTestBase
{
    public CrossFieldIdentifierValidatorIntegrationTests() : base(useEmailAsUserName: false) { }

    [Fact]
    public async Task UserName_EqualToAnotherAccountsEmail_IsRejected()
    {
        await SeedAsync("victim", "victim@example.com");

        var squatter = new User { Id = Guid.NewGuid(), UserName = "Victim@Example.com", Email = "squatter@example.com", CreationTime = DateTime.UtcNow };
        var result = await UserManager.CreateAsync(squatter, "Password123!");

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Code == CrossFieldIdentifierValidator.UserNameIsAnotherUsersEmail);
    }

    [Fact]
    public async Task Email_EqualToAnotherAccountsUserName_IsRejected()
    {
        // 存量形态：A 的用户名停在旧邮箱上
        var stale = await SeedAsync("old@example.com", "new@example.com");

        var latecomer = new User { Id = Guid.NewGuid(), UserName = "latecomer", Email = "old@example.com", CreationTime = DateTime.UtcNow };
        var result = await UserManager.CreateAsync(latecomer, "Password123!");

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Code == CrossFieldIdentifierValidator.EmailIsAnotherUsersUserName);

        // 存量账号自己的更新不受影响（登录时重置失败计数等都要过校验）
        var tracked = await UserManager.FindByIdAsync(stale.Id.ToString());
        var own = await UserManager.AccessFailedAsync(tracked!);
        Assert.True(own.Succeeded, own.Errors.FirstOrDefault()?.Description);
    }

    [Fact]
    public async Task SetEmailWithUserName_Linked_FollowsAtomically()
    {
        var user = await SeedAsync("dave@example.com", "dave@example.com");
        var tracked = await UserManager.FindByIdAsync(user.Id.ToString());

        var result = await UserManager.SetEmailWithUserNameAsync(tracked!, "dave@new.example.com");

        Assert.True(result.Succeeded, result.Errors.FirstOrDefault()?.Description);
        var reloaded = await ReloadAsync(user.Id);
        Assert.Equal("dave@new.example.com", reloaded.UserName);
        Assert.Equal("dave@new.example.com", reloaded.Email);
    }
}
