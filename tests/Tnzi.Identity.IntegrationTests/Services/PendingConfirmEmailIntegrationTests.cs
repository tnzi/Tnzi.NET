using Microsoft.Extensions.Options;
using Tnzi.Identity.Events;
using Tnzi.Identity.Services;
using Tnzi.Results;
using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.IntegrationTests.Services;

/// <summary>
/// 「必须先确认邮箱」这一项待办义务，从发码到办完，跑在真实的 <see cref="TwoFactorService"/> 上。
/// </summary>
/// <remarks>
/// ★★★ 这一位只会挂在邮箱<b>还没确认</b>的账号上，而发码此前走的是 <c>SendCodeToUserAsync</c>：
/// 它只往<b>已验证</b>的地址发（那是 step-up 与两步验证该有的前提）。于是每一次「发送验证码」都答 400，
/// 码永远收不到，义务永远清不掉，账号每次登录都停在这一步。单元测试把 <c>ITwoFactorService</c> 整个 mock 掉，
/// 那个前提从来没有被执行过，所以这里必须用真的。
/// </remarks>
public class PendingConfirmEmailIntegrationTests : RelationalIdentityIntegrationTestBase
{
    private const string TempToken = "pending-action-temp-token";
    private const string Email = "unconfirmed@example.com";

    private readonly Mock<IAuthTokenService> _authTokens = new();
    private readonly Mock<IAuthService> _authService = new();
    private AuthToken? _entry;

    public PendingConfirmEmailIntegrationTests()
    {
        _authTokens
            .Setup(x => x.FindTokenByValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string _, string _, string value) => value == TempToken && _entry is { IsUsed: false } ? _entry : null);
        _authTokens
            .Setup(x => x.MarkTokenAsUsedAsync(It.IsAny<Guid>()))
            .ReturnsAsync(() =>
            {
                _entry!.IsUsed = true;
                return true;
            });
        _authService
            .Setup(x => x.IssueTokenAsync(It.IsAny<User>(), It.IsAny<LoginMethod>(), It.IsAny<TwoFactorType?>()))
            .ReturnsAsync(Result<TokenResult>.Success(new TokenResult { AccessToken = "issued" }));
    }

    private PendingActionService CreateService(TwoFactorService twoFactor) => new(
        ServiceProvider,
        UserManager,
        _authTokens.Object,
        _authService.Object,
        new Mock<IPasswordService>().Object,
        twoFactor);

    private TwoFactorService CreateTwoFactorService() => new(
        CreateRepository<TwoFactorCode>(),
        UserManager,
        ServiceProvider,
        EventBusMock.Object,
        ServiceProvider.GetRequiredService<IOptionsSnapshot<IdentityOptions>>(),
        Cache);

    private async Task<User> CreateOwingUserAsync(string? email = Email)
    {
        var user = await CreateUserAsync(email: email, emailConfirmed: false);
        user.PendingActions = PendingUserActions.ConfirmEmail;
        Assert.True((await UserManager.UpdateAsync(user)).Succeeded);

        _entry = new AuthToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            LoginProvider = IdentityConstants.LoginProvider.PendingAction,
            Name = IdentityConstants.TokenName.PendingActionToken,
            Value = TempToken,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
        };
        return user;
    }

    /// <summary>
    /// ★★★ 邮箱未确认的账号收得到码、凭码办完、邮箱被标为已确认、义务清掉、签发令牌。
    /// </summary>
    [Fact]
    public async Task AnUnconfirmedAddress_ReceivesTheCode_AndTheObligationCanBeCompleted()
    {
        var user = await CreateOwingUserAsync();
        var service = CreateService(CreateTwoFactorService());

        var sent = await service.SendEmailConfirmationCodeAsync(TempToken);
        Assert.True(sent.Succeeded, sent.Message);

        // 码发到账号自己的地址，而不是任何调用方给的地址（这个端点是匿名的）。
        EventBusMock.Verify(
            x => x.PublishAsync(It.Is<TwoFactorCodeSentEvent>(e => e.Address == Email && e.UserId == user.Id), It.IsAny<CancellationToken>()),
            Times.Once);

        DbContext.ChangeTracker.Clear();
        var code = await DbContext.Set<TwoFactorCode>().AsNoTracking().SingleAsync();
        Assert.Equal(VerificationCodePurpose.ConfirmEmail, code.Purpose);

        var completed = await service.CompleteConfirmEmailAsync(new CompletePendingActionCodeDto { TempToken = TempToken, Code = code.Code });
        Assert.True(completed.Succeeded, completed.Message);
        Assert.True(completed.Data!.Completed);

        DbContext.ChangeTracker.Clear();
        var stored = await DbContext.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.True(stored.EmailConfirmed);
        Assert.Equal(PendingUserActions.None, stored.PendingActions);
    }

    /// <summary>账号上没有邮箱：如实说清，不发码。</summary>
    [Fact]
    public async Task AnAccountWithoutAnEmail_IsToldSo_AndNothingIsSent()
    {
        await CreateOwingUserAsync(email: null);
        var service = CreateService(CreateTwoFactorService());

        var sent = await service.SendEmailConfirmationCodeAsync(TempToken);

        Assert.False(sent.Succeeded);
        Assert.Equal(400, sent.Code);
        Assert.Empty(DbContext.Set<TwoFactorCode>().AsNoTracking());
    }
}
