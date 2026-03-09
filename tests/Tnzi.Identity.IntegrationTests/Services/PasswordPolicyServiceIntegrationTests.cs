using Microsoft.Extensions.Options;
using Tnzi.Identity.Options;
using Tnzi.Identity.Services;

namespace Tnzi.Identity.IntegrationTests.Services;

public class PasswordPolicyServiceIntegrationTests : RelationalIdentityIntegrationTestBase
{
    private readonly PasswordPolicyService _service;

    public PasswordPolicyServiceIntegrationTests()
    {
        _service = new PasswordPolicyService(
            CreateRepository<PasswordHistory>(),
            UserManager,
            ServiceProvider,
            ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>());
    }

    [Fact]
    public async Task CheckPasswordHistoryAsync_WithNewPassword_ReturnsFalse()
    {
        var user = await CreateUserAsync();
        var hash = UserManager.PasswordHasher.HashPassword(user, "OldPassword123!");
        DbContext.PasswordHistories.Add(new PasswordHistory { Id = Guid.NewGuid(), UserId = user.Id, PasswordHash = hash, CreationTime = DateTime.UtcNow.AddDays(-1) });
        await SaveChangesAsync();

        var result = await _service.CheckPasswordHistoryAsync(user.Id, "BrandNewPassword123!");

        Assert.False(result);
    }

    [Fact]
    public async Task SavePasswordHistoryAsync_WithValidInput_SavesHistory()
    {
        var user = await CreateUserAsync();
        var hash = UserManager.PasswordHasher.HashPassword(user, "Password123!");

        await _service.SavePasswordHistoryAsync(user.Id, hash);

        Assert.Single(DbContext.PasswordHistories);
    }

    [Fact]
    public async Task CheckPasswordExpirationAsync_WithNonExpiredPassword_ReturnsNotExpired()
    {
        var user = await CreateUserAsync(creationTime: DateTime.UtcNow.AddDays(-10));
        DbContext.PasswordHistories.Add(new PasswordHistory { Id = Guid.NewGuid(), UserId = user.Id, PasswordHash = "hash", CreationTime = DateTime.UtcNow.AddDays(-5) });
        await SaveChangesAsync();

        var result = await _service.CheckPasswordExpirationAsync(user.Id);

        Assert.False(result.IsExpired);
        Assert.True(result.DaysUntilExpiration > 0);
    }

    [Fact]
    public async Task GetLastPasswordChangeTimeAsync_WithUser_ReturnsTime()
    {
        var user = await CreateUserAsync();
        var latest = DateTime.UtcNow.AddHours(-1);
        DbContext.PasswordHistories.AddRange(
            new PasswordHistory { Id = Guid.NewGuid(), UserId = user.Id, PasswordHash = "old", CreationTime = DateTime.UtcNow.AddDays(-2) },
            new PasswordHistory { Id = Guid.NewGuid(), UserId = user.Id, PasswordHash = "latest", CreationTime = latest });
        await SaveChangesAsync();

        var result = await _service.GetLastPasswordChangeTimeAsync(user.Id);

        Assert.Equal(latest, result);
    }
}
