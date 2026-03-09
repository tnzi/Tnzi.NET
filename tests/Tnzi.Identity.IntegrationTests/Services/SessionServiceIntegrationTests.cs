using Tnzi.Identity.Services;

namespace Tnzi.Identity.IntegrationTests.Services;

public class SessionServiceIntegrationTests : RelationalIdentityIntegrationTestBase
{
    private readonly DatabaseSessionService _service;

    public SessionServiceIntegrationTests()
    {
        _service = new DatabaseSessionService(CreateRepository<UserSession>(), ServiceProvider);
    }

    [Fact]
    public async Task GetUserSessionsAsync_WithValidUserId_ReturnsSessions()
    {
        var user = await CreateUserAsync();
        DbContext.UserSessions.AddRange(
            new UserSession { Id = Guid.NewGuid(), UserId = user.Id, DeviceInfo = "Chrome", LastActivityTime = DateTime.UtcNow, IsRevoked = false },
            new UserSession { Id = Guid.NewGuid(), UserId = user.Id, DeviceInfo = "Safari", LastActivityTime = DateTime.UtcNow.AddMinutes(-5), IsRevoked = false },
            new UserSession { Id = Guid.NewGuid(), UserId = user.Id, DeviceInfo = "Old", LastActivityTime = DateTime.UtcNow.AddMinutes(-10), IsRevoked = true });
        await SaveChangesAsync();

        var result = await _service.GetUserSessionsAsync(user.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.Count());
        Assert.DoesNotContain(result.Data!, x => x.IsRevoked);
    }

    [Fact]
    public async Task GetUserSessionsAsync_WithIncludeRevoked_ReturnsAllSessions()
    {
        var user = await CreateUserAsync();
        DbContext.UserSessions.AddRange(
            new UserSession { Id = Guid.NewGuid(), UserId = user.Id, LastActivityTime = DateTime.UtcNow, IsRevoked = false },
            new UserSession { Id = Guid.NewGuid(), UserId = user.Id, LastActivityTime = DateTime.UtcNow.AddMinutes(-1), IsRevoked = true });
        await SaveChangesAsync();

        var result = await _service.GetUserSessionsAsync(user.Id, includeRevoked: true);

        Assert.Equal(2, result.Data!.Count());
    }

    [Fact]
    public async Task RevokeAllSessionsAsync_WithValidUserId_RevokesAllSessions()
    {
        var user = await CreateUserAsync();
        DbContext.UserSessions.AddRange(
            new UserSession { Id = Guid.NewGuid(), UserId = user.Id, IsRevoked = false },
            new UserSession { Id = Guid.NewGuid(), UserId = user.Id, IsRevoked = false });
        await SaveChangesAsync();

        var result = await _service.RevokeAllSessionsAsync(user.Id);

        Assert.True(result.Succeeded);
        Assert.All(DbContext.UserSessions.Where(x => x.UserId == user.Id), session => Assert.True(session.IsRevoked));
    }

    [Fact]
    public async Task RevokeAllSessionsAsync_WithExcludeSessionId_ExcludesSession()
    {
        var user = await CreateUserAsync();
        var keepId = Guid.NewGuid();
        DbContext.UserSessions.AddRange(
            new UserSession { Id = keepId, UserId = user.Id, IsRevoked = false },
            new UserSession { Id = Guid.NewGuid(), UserId = user.Id, IsRevoked = false });
        await SaveChangesAsync();

        var result = await _service.RevokeAllSessionsAsync(user.Id, keepId);

        Assert.True(result.Succeeded);
        Assert.False(DbContext.UserSessions.Single(x => x.Id == keepId).IsRevoked);
        Assert.True(DbContext.UserSessions.Single(x => x.Id != keepId).IsRevoked);
    }
}
