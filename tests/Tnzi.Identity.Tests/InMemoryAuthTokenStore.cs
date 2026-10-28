namespace Tnzi.Identity.Tests;

/// <summary>
/// 内存版的 <see cref="IAuthTokenService"/>：按 (UserId, LoginProvider, Name) upsert、按值查找、条件消费，
/// 与 <c>AuthTokenService</c> 的语义一致（值在这里不加密，<see cref="IAuthTokenService.RevealTokenValue"/> 原样返回）。
/// </summary>
internal sealed class InMemoryAuthTokenStore
{
    private readonly List<AuthToken> _tokens = [];

    public InMemoryAuthTokenStore()
    {
        var service = new Mock<IAuthTokenService>();

        service
            .Setup(x => x.SaveTokenAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<Guid>()))
            .ReturnsAsync((Guid userId, string provider, string name, string value, DateTime? expiresAt, Guid _) =>
            {
                LastSavedValue = value;
                var token = new AuthToken
                {
                    Id = Guid.NewGuid(), UserId = userId, LoginProvider = provider, Name = name, Value = value, ExpiresAt = expiresAt,
                };
                _tokens.RemoveAll(t => t.UserId == userId && t.LoginProvider == provider && t.Name == name);
                _tokens.Add(token);
                return token.Id;
            });

        service
            .Setup(x => x.FindTokenByValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string provider, string name, string value) =>
                _tokens.FirstOrDefault(t => t.LoginProvider == provider && t.Name == name && t.Value == value));

        service
            .Setup(x => x.GetUserTokensAsync(It.IsAny<Guid>(), It.IsAny<string?>()))
            .ReturnsAsync((Guid userId, string? provider) =>
                _tokens.Where(t => t.UserId == userId && (provider == null || t.LoginProvider == provider)).ToList());

        service
            .Setup(x => x.RevealTokenValue(It.IsAny<AuthToken>()))
            .Returns((AuthToken token) => token.Value);

        service
            .Setup(x => x.MarkTokenAsUsedAsync(It.IsAny<Guid>()))
            .ReturnsAsync((Guid id) =>
            {
                var token = _tokens.FirstOrDefault(t => t.Id == id);
                if (token == null || token.IsUsed)
                {
                    return false;
                }

                token.IsUsed = true;
                return true;
            });

        Service = service.Object;
    }

    public IAuthTokenService Service { get; }

    /// <summary>最近一次写入的落库值。</summary>
    public string? LastSavedValue { get; private set; }

    /// <summary>当前所有行（只读视图）。</summary>
    public IReadOnlyList<AuthToken> Tokens => _tokens;

    public void ExpireAll()
    {
        foreach (var token in _tokens)
        {
            token.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        }
    }
}
