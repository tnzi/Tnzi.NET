namespace Tnzi.Identity.Services;

/// <inheritdoc cref="IOAuthLinkTokenService"/>
public class OAuthLinkTokenService : ApplicationService, IOAuthLinkTokenService
{
    /// <summary>
    /// 令牌在 <c>AuthToken</c> 里的归属标记（唯一索引是 <c>(UserId, LoginProvider, Name, SessionId)</c>；
    /// <c>Name</c> 放提供商名，于是同一用户对同一提供商同时只有一枚，补发一张就让上一张作废）。
    /// </summary>
    internal const string TokenLoginProvider = "OAuthLink";

    private readonly IAuthTokenService _authTokenService;
    private readonly IOptionsMonitor<IdentityOptions> _options;

    /// <summary>
    /// 初始化一个 <see cref="OAuthLinkTokenService"/> 类型的新实例。
    /// </summary>
    public OAuthLinkTokenService(
        IServiceProvider serviceProvider,
        IAuthTokenService authTokenService,
        IOptionsMonitor<IdentityOptions> options)
        : base(serviceProvider)
    {
        _authTokenService = Check.NotNull(authTokenService);
        _options = Check.NotNull(options);
    }

    /// <inheritdoc />
    public async Task<Result<OAuthLinkTokenDto>> IssueAsync(Guid userId, string provider)
    {
        if (userId == Guid.Empty)
        {
            return Fail<OAuthLinkTokenDto>("User id is required", 400, ErrorCodes.VALIDATION_ERROR);
        }

        var normalized = Normalize(provider);
        if (normalized.Length == 0)
        {
            return Fail<OAuthLinkTokenDto>("Provider is required", 400, ErrorCodes.VALIDATION_ERROR);
        }

        var lifetime = TimeSpan.FromMinutes(Math.Max(1, _options.CurrentValue.OAuth.LinkTokenLifetimeMinutes));
        var expiresAt = DateTime.UtcNow.Add(lifetime);

        // 256 位随机数：没有字典可查，所以存哈希时不需要加盐或慢哈希（理由写在原语里）。
        var token = OneTimeToken.Create();
        await _authTokenService.SaveTokenAsync(userId, TokenLoginProvider, normalized, OneTimeToken.Hash(token), expiresAt);

        LogInformation("Issued an OAuth link token for user {UserId} and provider {Provider}, valid until {ExpiresAt:o}.", userId, normalized, expiresAt);

        return Ok(new OAuthLinkTokenDto { Token = token, Provider = normalized, ExpiresAt = expiresAt });
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> PeekAsync(string token, string provider)
    {
        var entry = await FindUsableAsync(token, provider);
        return entry == null ? InvalidToken() : Ok(entry.UserId);
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> ConsumeAsync(string token, string provider)
    {
        var entry = await FindUsableAsync(token, provider);
        if (entry == null)
        {
            return InvalidToken();
        }

        // ★ 按返回值决定：MarkTokenAsUsedAsync 是条件更新（WHERE IsUsed = false），
        //   并发的两次回调只有一次拿到 true，另一次与「已用」同一个回答。
        return await _authTokenService.MarkTokenAsUsedAsync(entry.Id) ? Ok(entry.UserId) : InvalidToken();
    }

    /// <summary>按哈希取回一枚仍然可用的令牌。失效 / 过期 / 不存在 / 提供商不符一律 <c>null</c>。</summary>
    private async Task<AuthToken?> FindUsableAsync(string token, string provider)
    {
        var normalized = Normalize(provider);
        if (string.IsNullOrWhiteSpace(token) || normalized.Length == 0)
        {
            return null;
        }

        var entry = await _authTokenService.FindTokenByValueAsync(TokenLoginProvider, normalized, OneTimeToken.Hash(token));
        if (entry == null || entry.IsUsed)
        {
            return null;
        }

        return entry.ExpiresAt.HasValue && entry.ExpiresAt.Value <= DateTime.UtcNow ? null : entry;
    }

    /// <summary>失效、过期、不存在、提供商不符共用同一个回答 —— 区分开就是在帮人试探哪些令牌是真的。</summary>
    private Result<Guid> InvalidToken()
        => Fail<Guid>("Invalid or expired link token", 400, ErrorCodes.VALIDATION_ERROR);

    private static string Normalize(string? provider) => (provider ?? string.Empty).Trim().ToLowerInvariant();
}
