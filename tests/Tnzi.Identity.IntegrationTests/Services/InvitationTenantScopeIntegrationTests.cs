using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Tnzi.Identity.Services;
using Tnzi.MultiTenancy;
using Tnzi.Identity.Extensions;
using IdentityConstants = Tnzi.Identity.Metadata.IdentityConstants;
using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.IntegrationTests.Services;

/// <summary>
/// 多租户开启时，邀请的重发与撤销按当前租户裁剪。
///
/// ★ 08048ce9 给 <c>UserService.DeleteAsync</c> 加了范围校验之后，<c>InvitationService.RevokeAsync</c>
/// 变成了一次「半截操作」：它先作废令牌再删账号，而只有删账号那一步被裁剪 —— 租户 A 的管理员
/// 对租户 B 的待接受邀请 DELETE 一次，拿到 404，而 B 那条邀请链接已经悄悄失效、账号留在 Pending、
/// 再没有令牌。<c>ResendAsync</c> 则让外租户管理员能轮换别人的邀请并触发发信。
/// 令牌表（<c>AuthToken</c>）不是 <c>IMultiTenant</c>，全局过滤器管不到，只能在动它之前先问归属。
/// </summary>
public class InvitationTenantScopeIntegrationTests : RelationalIdentityIntegrationTestBase
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private readonly Mock<ICurrentTenant> _currentTenant = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IInvitationUrlGenerator> _urlGenerator = new();
    private readonly AuthTokenService _tokens;
    private readonly InvitationService _invitations;

    private Guid? _tenantId;

    public InvitationTenantScopeIntegrationTests()
        : base(configureServices: services => services.Configure<MultiTenancyOptions>(o => o.Enabled = true))
    {
        _currentTenant.Setup(t => t.Id).Returns(() => _tenantId);
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _currentUser.Setup(u => u.TenantId).Returns(() => _tenantId);
        _urlGenerator.Setup(g => g.GenerateUrl(It.IsAny<User>(), It.IsAny<string>())).Returns("https://example.test/accept");

        var multiTenancy = Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions { Enabled = true });
        var scope = new UserTenantScopeProvider(CreateRepository<User>(), _currentTenant.Object, _currentUser.Object, multiTenancy);
        var users = new UserService(
            UserManager,
            ServiceProvider.GetRequiredService<RoleManager<Role>>(),
            CreateRepository<User>(),
            ServiceProvider,
            eventBus: EventBusMock.Object,
            currentUser: _currentUser.Object,
            cache: Cache,
            currentTenant: _currentTenant.Object,
            multiTenancyOptions: multiTenancy);
        _tokens = new AuthTokenService(
            CreateRepository<AuthToken>(),
            ServiceProvider.GetRequiredService<IDataProtectionProvider>(),
            ServiceProvider);

        var options = new Mock<IOptionsMonitor<IdentityOptions>>();
        options.Setup(o => o.CurrentValue).Returns(new IdentityOptions());

        _invitations = new InvitationService(
            ServiceProvider,
            UserManager,
            users,
            _tokens,
            new Mock<IInvitationAcceptanceHandler>().Object,
            _urlGenerator.Object,
            options.Object,
            scope);
    }

    private void ActAs(Guid adminId, Guid? tenantId)
    {
        _currentUser.Setup(u => u.Id).Returns(adminId);
        _tenantId = tenantId;
    }

    private Task<string?> InvitationTokenOfAsync(Guid userId)
        => _tokens.GetTokenAsync(userId, IdentityConstants.LoginProvider.Invitation, IdentityConstants.TokenName.InvitationToken);

    /// <summary>以租户 B 的管理员身份发出一份邀请，返回待接受的账号 id 与令牌哈希。</summary>
    private async Task<(Guid InviteeId, string TokenHash)> InviteInTenantBAsync()
    {
        ActAs(Guid.NewGuid(), TenantB);
        var invited = await _invitations.InviteAsync(new CreateInvitationDto { UserName = "invitee-b", Email = "invitee-b@example.com" });
        Assert.True(invited.Succeeded, invited.Message);
        var tokenHash = await InvitationTokenOfAsync(invited.Data!.UserId);
        Assert.NotNull(tokenHash);
        DbContext.ChangeTracker.Clear();
        return (invited.Data!.UserId, tokenHash!);
    }

    private async Task<User> ReloadAsync(Guid id)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == id);
    }

    [Fact]
    public async Task TenantAdmin_Revoke_OnOtherTenantInvitee_Returns404_AndLeavesTheInvitationIntact()
    {
        var (inviteeId, tokenHash) = await InviteInTenantBAsync();
        ActAs(Guid.NewGuid(), TenantA);

        var result = await _invitations.RevokeAsync(inviteeId);

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
        Assert.Equal(tokenHash, await InvitationTokenOfAsync(inviteeId));
        var invitee = await ReloadAsync(inviteeId);
        Assert.False(invitee.IsDeleted);
        Assert.True(invitee.HasPendingAction(PendingUserActions.InvitationPending));
    }

    [Fact]
    public async Task TenantAdmin_Resend_OnOtherTenantInvitee_Returns404_AndDoesNotRotateTheToken()
    {
        var (inviteeId, tokenHash) = await InviteInTenantBAsync();
        ActAs(Guid.NewGuid(), TenantA);
        _urlGenerator.Invocations.Clear();

        var result = await _invitations.ResendAsync(inviteeId);

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
        Assert.Equal(tokenHash, await InvitationTokenOfAsync(inviteeId));
        _urlGenerator.Verify(g => g.GenerateUrl(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task TenantAdmin_CanResendAndRevoke_OwnTenantInvitee()
    {
        var (inviteeId, tokenHash) = await InviteInTenantBAsync();
        ActAs(Guid.NewGuid(), TenantB);

        var resent = await _invitations.ResendAsync(inviteeId);
        Assert.True(resent.Succeeded, resent.Message);
        Assert.NotEqual(tokenHash, await InvitationTokenOfAsync(inviteeId));

        var revoked = await _invitations.RevokeAsync(inviteeId);
        Assert.True(revoked.Succeeded, revoked.Message);
        Assert.Null(await InvitationTokenOfAsync(inviteeId));
        Assert.True((await ReloadAsync(inviteeId)).IsDeleted);
    }

    [Fact]
    public async Task GlobalAdmin_WithNoTenantContext_CanRevokeAnyTenantInvitee()
    {
        var (inviteeId, _) = await InviteInTenantBAsync();
        ActAs(Guid.NewGuid(), null);

        var revoked = await _invitations.RevokeAsync(inviteeId);

        Assert.True(revoked.Succeeded, revoked.Message);
        Assert.Null(await InvitationTokenOfAsync(inviteeId));
    }
}
