using Microsoft.Extensions.Options;
using Tnzi.Identity.Options;
using Tnzi.Identity.Services;

namespace Tnzi.Identity.IntegrationTests.Services;

/// <summary>
/// 接受邀请时设下的第一个密码，要记成「最近一次改密」。
/// </summary>
/// <remarks>
/// ★★ 密码到期以密码历史里最新的一行为参照，没有就回退到账号创建时间。邀请建号在发邀请那一刻，
/// 而本人设密码在接受那一刻，中间可以隔几周（待接受期间可以一再重发）。默认接受处理器此前直接 <c>AddPasswordAsync</c>、
/// 不写那一行：一个建号 85 天才接受的人，在开着 90 天到期的部署里，刚设好的密码 5 天后就被判到期。
/// </remarks>
public class InvitationAcceptancePasswordAgeIntegrationTests : RelationalIdentityIntegrationTestBase
{
    private readonly PasswordPolicyService _policy;

    public InvitationAcceptancePasswordAgeIntegrationTests()
        : base(configureIdentity: options => options.PasswordPolicy.PasswordExpirationDays = 90)
    {
        _policy = new PasswordPolicyService(
            CreateRepository<PasswordHistory>(),
            UserManager,
            ServiceProvider,
            ServiceProvider.GetRequiredService<IOptionsMonitor<IdentityOptions>>());
    }

    private async Task<User> CreateInvitedAccountAsync(DateTime creationTime)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = $"invitee_{Guid.NewGuid():N}",
            Email = "invitee@example.com",
            CreationTime = creationTime,
        };
        var created = await UserManager.CreateAsync(user);
        Assert.True(created.Succeeded, created.Errors.FirstOrDefault()?.Description);
        return user;
    }

    [Fact]
    public async Task The_password_set_on_acceptance_counts_as_the_latest_change()
    {
        var user = await CreateInvitedAccountAsync(DateTime.UtcNow.AddDays(-100));
        var handler = new DefaultInvitationAcceptanceHandler(ServiceProvider, UserManager, _policy);

        var outcome = await handler.AcceptAsync(user, new AcceptInvitationDto { Token = "unused", Password = "Accepted123!" }, profile: null);

        Assert.True(outcome.Succeeded, outcome.Message);
        Assert.True(outcome.Data!.Completed);
        Assert.False((await _policy.CheckPasswordExpirationAsync(user.Id)).IsExpired);
    }
}
