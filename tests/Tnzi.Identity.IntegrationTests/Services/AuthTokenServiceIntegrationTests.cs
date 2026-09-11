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
        // 令牌值以 DataProtection 密文落库、以 SHA-256 哈希查找。用短暂的临时 key ring
        // （EphemeralDataProtectionProvider）就够了：这一组测的是「写进去还读得回来、
        // 且按值查得到」，不是 key ring 的持久化行为。
        _service = new AuthTokenService(
            repository, new EphemeralDataProtectionProvider(), _serviceProvider);
    }

    [Fact]
    public async Task SaveTokenAsync_WithNewToken_CreatesToken()
    {
        var userId = Guid.NewGuid();
        await EnsureUserExistsAsync(userId);

        var tokenId = await _service.SaveTokenAsync(userId, "email", "reset", "token-1");

        var saved = await _dbContext.AuthTokens.FindAsync(tokenId);
        Assert.NotNull(saved);

        // ★★★ 落库的**不是**明文。这一条是本轮改动的核心断言：拿到这张表的人
        //   不应当直接得到一枚能用的刷新令牌。
        Assert.NotEqual("token-1", saved.Value);
        Assert.Equal(OneTimeToken.Hash("token-1"), saved.ValueHash);

        // 而经服务读回来仍是原值（宽限窗与邀请预填资料都依赖这条）。
        Assert.Equal("token-1", _service.RevealTokenValue(saved));
        Assert.False(saved.IsUsed);
    }

    /// <summary>
    /// 按值查找走哈希列。密文每次不同，若查询还打在 Value 上，这条必然落空 ——
    /// 也就是「所有刷新、2FA 验证、邀请接受一律失效」。
    /// </summary>
    [Fact]
    public async Task FindTokenByValueAsync_MatchesOnHash()
    {
        var userId = Guid.NewGuid();
        await EnsureUserExistsAsync(userId);
        await _service.SaveTokenAsync(userId, "email", "reset", "token-1", DateTime.UtcNow.AddMinutes(10));

        var found = await _service.FindTokenByValueAsync("email", "reset", "token-1");
        var miss = await _service.FindTokenByValueAsync("email", "reset", "token-x");

        Assert.NotNull(found);
        Assert.Equal(userId, found!.UserId);
        Assert.Null(miss);
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
        var row = _dbContext.AuthTokens.Single();
        Assert.Equal(OneTimeToken.Hash("token-2"), row.ValueHash);
        Assert.Equal("token-2", _service.RevealTokenValue(row));
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
