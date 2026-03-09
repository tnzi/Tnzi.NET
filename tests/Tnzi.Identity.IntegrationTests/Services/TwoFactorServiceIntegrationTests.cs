using Microsoft.Extensions.Options;
using Tnzi.Identity.Events;
using Tnzi.Identity.Options;
using Tnzi.Identity.Services;

namespace Tnzi.Identity.IntegrationTests.Services;

public class TwoFactorServiceIntegrationTests : RelationalIdentityIntegrationTestBase
{
    private readonly TwoFactorService _service;

    public TwoFactorServiceIntegrationTests()
    {
        _service = new TwoFactorService(
            CreateRepository<TwoFactorCode>(),
            UserManager,
            ServiceProvider,
            EventBusMock.Object,
            ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>(),
            cache: null);
    }

    [Fact]
    public async Task SendSmsCodeAsync_WithValidInput_ReturnsTrue()
    {
        var user = await CreateUserAsync(phoneNumber: "13800138000");

        var result = await _service.SendSmsCodeAsync(user.Id, user.PhoneNumber!);

        Assert.True(result.Succeeded);
        Assert.Single(DbContext.Set<TwoFactorCode>());
        Assert.Equal(TwoFactorType.Sms, DbContext.Set<TwoFactorCode>().Single().Type);
        EventBusMock.Verify(x => x.PublishAsync(It.IsAny<TwoFactorCodeSentEvent>(), default), Times.Once);
    }

    [Fact]
    public async Task SendEmailCodeAsync_WithValidInput_ReturnsTrue()
    {
        var user = await CreateUserAsync(email: "2fa@example.com");

        var result = await _service.SendEmailCodeAsync(user.Id, user.Email!);

        Assert.True(result.Succeeded);
        Assert.Equal(TwoFactorType.Email, DbContext.Set<TwoFactorCode>().Single().Type);
    }

    [Fact]
    public async Task VerifyCodeAsync_WithValidCode_ReturnsTrue()
    {
        var user = await CreateUserAsync(email: "verify@example.com");
        DbContext.Set<TwoFactorCode>().Add(new TwoFactorCode
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Address = user.Email!,
            Code = "123456",
            Type = TwoFactorType.Email,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            IsUsed = false,
            CreationTime = DateTime.UtcNow
        });
        await SaveChangesAsync();

        var result = await _service.VerifyCodeAsync(user.Id, "123456", TwoFactorType.Email);

        Assert.True(result.Succeeded);
        Assert.True(DbContext.Set<TwoFactorCode>().Single().IsUsed);
    }

    [Fact]
    public async Task VerifyCodeAsync_WithExpiredCode_ReturnsFalse()
    {
        var user = await CreateUserAsync(email: "expired@example.com");
        DbContext.Set<TwoFactorCode>().Add(new TwoFactorCode
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Address = user.Email!,
            Code = "654321",
            Type = TwoFactorType.Email,
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
            IsUsed = false,
            CreationTime = DateTime.UtcNow.AddMinutes(-2)
        });
        await SaveChangesAsync();

        var result = await _service.VerifyCodeAsync(user.Id, "654321", TwoFactorType.Email);

        Assert.False(result.Succeeded);
        Assert.False(DbContext.Set<TwoFactorCode>().Single().IsUsed);
    }

    [Fact]
    public async Task DisableTwoFactorAsync_WithValidUserId_Disables2FA()
    {
        var user = await CreateUserAsync(email: "disable@example.com");
        await UserManager.SetTwoFactorEnabledAsync(user, true);
        DbContext.Set<TwoFactorCode>().Add(new TwoFactorCode
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Address = user.Email!,
            Code = "123123",
            Type = TwoFactorType.Email,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            IsUsed = false,
            CreationTime = DateTime.UtcNow
        });
        await SaveChangesAsync();

        var result = await _service.DisableTwoFactorAsync(user.Id);

        Assert.True(result.Succeeded);
        Assert.False((await UserManager.FindByIdAsync(user.Id.ToString()))!.TwoFactorEnabled);
        Assert.Empty(DbContext.Set<TwoFactorCode>());
    }
}
