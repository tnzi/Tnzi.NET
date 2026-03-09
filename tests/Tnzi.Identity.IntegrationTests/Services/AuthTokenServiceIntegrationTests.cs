using Tnzi.EFCore;
using Tnzi.Identity.Services;
using Microsoft.Data.Sqlite;

namespace Tnzi.Identity.IntegrationTests.Services;

public class AuthTokenServiceIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;
    private readonly TestIdentityDbContext _dbContext;
    private readonly AuthTokenService _service;

    public AuthTokenServiceIntegrationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(x => x.Id).Returns(Guid.NewGuid());
        currentUserMock.Setup(x => x.UserName).Returns("testuser");
        services.AddSingleton(currentUserMock.Object);
        services.AddDbContext<TestIdentityDbContext>(options =>
        {
            options.UseSqlite(_connection);
            options.EnableSensitiveDataLogging();
        });

        _serviceProvider = services.BuildServiceProvider();
        _dbContext = _serviceProvider.GetRequiredService<TestIdentityDbContext>();
        _dbContext.Database.EnsureCreated();

        var repository = new EFCoreRepository<TestIdentityDbContext, AuthToken, Guid>(
            _dbContext,
            serviceProvider: _serviceProvider);
        _service = new AuthTokenService(repository, _serviceProvider);
    }

    [Fact]
    public async Task SaveTokenAsync_WithNewToken_CreatesToken()
    {
        var userId = Guid.NewGuid();
        await EnsureUserExistsAsync(userId);

        var tokenId = await _service.SaveTokenAsync(userId, "email", "reset", "token-1");

        var saved = await _dbContext.AuthTokens.FindAsync(tokenId);
        Assert.NotNull(saved);
        Assert.Equal("token-1", saved.Value);
        Assert.False(saved.IsUsed);
    }

    [Fact]
    public async Task SaveTokenAsync_WithExistingToken_UpdatesToken()
    {
        var userId = Guid.NewGuid();
        await EnsureUserExistsAsync(userId);
        var tokenId = await _service.SaveTokenAsync(userId, "email", "reset", "token-1");

        var updatedTokenId = await _service.SaveTokenAsync(userId, "email", "reset", "token-2");

        Assert.Equal(tokenId, updatedTokenId);
        Assert.Single(_dbContext.AuthTokens);
        Assert.Equal("token-2", _dbContext.AuthTokens.Single().Value);
    }

    [Fact]
    public async Task GetTokenAsync_WithValidToken_ReturnsTokenValue()
    {
        var userId = Guid.NewGuid();
        await EnsureUserExistsAsync(userId);
        await _service.SaveTokenAsync(userId, "email", "reset", "token-1", DateTime.UtcNow.AddMinutes(10));

        var value = await _service.GetTokenAsync(userId, "email", "reset");

        Assert.Equal("token-1", value);
    }

    [Fact]
    public async Task GetTokenAsync_WithUsedToken_ReturnsNull()
    {
        var userId = Guid.NewGuid();
        await EnsureUserExistsAsync(userId);
        var tokenId = await _service.SaveTokenAsync(userId, "email", "reset", "token-1", DateTime.UtcNow.AddMinutes(10));
        await _service.MarkTokenAsUsedAsync(tokenId);

        var value = await _service.GetTokenAsync(userId, "email", "reset");

        Assert.Null(value);
    }

    [Fact]
    public async Task GetTokenAsync_WithExpiredToken_ReturnsNull()
    {
        var userId = Guid.NewGuid();
        await EnsureUserExistsAsync(userId);
        await _service.SaveTokenAsync(userId, "email", "reset", "token-1", DateTime.UtcNow.AddMinutes(-1));

        var value = await _service.GetTokenAsync(userId, "email", "reset");

        Assert.Null(value);
    }

    [Fact]
    public async Task RemoveTokenAsync_WithExistingToken_RemovesToken()
    {
        var userId = Guid.NewGuid();
        await EnsureUserExistsAsync(userId);
        await _service.SaveTokenAsync(userId, "email", "reset", "token-1");

        await _service.RemoveTokenAsync(userId, "email", "reset");

        Assert.Empty(_dbContext.AuthTokens);
    }

    [Fact]
    public async Task RemoveAllTokensAsync_WithValidUserId_RemovesAllTokens()
    {
        var userId = Guid.NewGuid();
        await EnsureUserExistsAsync(userId);
        await _service.SaveTokenAsync(userId, "email", "reset", "token-1");
        await _service.SaveTokenAsync(userId, "sms", "verify", "token-2");

        await _service.RemoveAllTokensAsync(userId);

        Assert.Empty(_dbContext.AuthTokens);
    }

    [Fact]
    public async Task MarkTokenAsUsedAsync_WithValidTokenId_MarksAsUsed()
    {
        var userId = Guid.NewGuid();
        await EnsureUserExistsAsync(userId);
        var tokenId = await _service.SaveTokenAsync(userId, "email", "reset", "token-1");

        var marked = await _service.MarkTokenAsUsedAsync(tokenId);

        Assert.True(marked);
        var saved = await _dbContext.AuthTokens.FindAsync(tokenId);
        Assert.NotNull(saved);
        await _dbContext.Entry(saved).ReloadAsync();
        Assert.True(saved.IsUsed);
        Assert.NotNull(saved.UsedAt);
    }

    [Fact]
    public async Task CleanExpiredTokensAsync_WithExpiredTokens_RemovesTokens()
    {
        var userId = Guid.NewGuid();
        await EnsureUserExistsAsync(userId);
        await _service.SaveTokenAsync(userId, "email", "expired", "token-1", DateTime.UtcNow.AddMinutes(-1));
        await _service.SaveTokenAsync(userId, "email", "active", "token-2", DateTime.UtcNow.AddMinutes(10));

        var count = await _service.CleanExpiredTokensAsync();

        Assert.Equal(1, count);
        Assert.Single(_dbContext.AuthTokens);
        Assert.Equal("active", _dbContext.AuthTokens.Single().Name);
    }

    [Fact]
    public async Task FindTokenByValueAsync_WithValidValue_ReturnsToken()
    {
        var userId = Guid.NewGuid();
        await EnsureUserExistsAsync(userId);
        var tokenId = await _service.SaveTokenAsync(userId, "email", "reset", "token-1", DateTime.UtcNow.AddMinutes(10));

        var token = await _service.FindTokenByValueAsync("email", "reset", "token-1");

        Assert.NotNull(token);
        Assert.Equal(tokenId, token.Id);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _serviceProvider.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task EnsureUserExistsAsync(Guid userId)
    {
        if (await _dbContext.Users.FindAsync(userId) != null)
        {
            return;
        }

        _dbContext.Users.Add(new User
        {
            Id = userId,
            UserName = $"user_{userId:N}",
            Email = $"{userId:N}@example.com",
            NormalizedUserName = $"USER_{userId:N}".ToUpperInvariant(),
            NormalizedEmail = $"{userId:N}@EXAMPLE.COM"
        });

        await _dbContext.SaveChangesAsync();
    }
}
