namespace Tnzi.Identity.Services;

/// <inheritdoc cref="IOAuthLinkTokenService"/>
public class OAuthLinkTokenService : ApplicationService, IOAuthLinkTokenService
{
    /// <summary>
    /// 令牌在 <c>AuthToken</c> 里的归属标记（唯一索引是 <c>(UserId, LoginProvider, Name, SessionId)</c>；
    /// <c>Name</c> 放提供商名，于是同一用户对同一提供商同时只有一枚，补发一张就让上一张作废）。
    /// </summary>
    internal const string TokenLoginProvider = "OAuthLink";

    /// <summary>
    /// 暂存「已过提供商、还没回到发起会话确认」的绑定。<c>Name</c> 同样放提供商名：
    /// 同一账号对同一提供商同时只有一份暂存。
    /// </summary>
    internal const string PendingLoginProvider = "OAuthLinkPending";

    /// <summary>
    /// 暂存的有效期。回调页跳回前端、前端调完成端点只要几秒；留余量给慢网络与登录页的一次往返，
    /// 但不该长到让一份没人认领的暂存躺上一天。
    /// </summary>
    private static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(10);

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

    /// <inheritdoc />
    public async Task<Result<string>> StagePendingLinkAsync(Guid userId, string provider, ClaimsPrincipal principal)
    {
        Check.NotNull(principal);

        var normalized = Normalize(provider);
        if (userId == Guid.Empty || normalized.Length == 0)
        {
            return Fail<string>("User and provider are required", 400, ErrorCodes.VALIDATION_ERROR);
        }

        var providerKey = OAuthService.ExtractProviderKey(principal);
        if (string.IsNullOrEmpty(providerKey))
        {
            return Fail<string>("Provider key not found in claims", 400, ErrorCodes.IDENTITY_OAUTH_ERROR);
        }

        // 完成码与令牌同一原语：256 位随机数，库里只存哈希。外部身份本身存服务端：
        // 完成端点绝不从请求里接收 provider key，否则谁都能声称自己刚过了提供商。
        var code = OneTimeToken.Create();
        var payload = JsonSerializer.Serialize(new PendingLinkPayload(
            OneTimeToken.Hash(code), providerKey, OAuthService.ExtractDisplayName(principal)));
        var expiresAt = DateTime.UtcNow.Add(PendingLifetime);
        await _authTokenService.SaveTokenAsync(userId, PendingLoginProvider, normalized, payload, expiresAt);

        LogInformation("Staged an OAuth link for user {UserId} and provider {Provider}, awaiting confirmation from the issuing session until {ExpiresAt:o}.",
            userId, normalized, expiresAt);
        return Result<string>.Success(code);
    }

    /// <inheritdoc />
    public async Task<Result<PendingOAuthLinkDto>> ConsumePendingLinkAsync(Guid userId, string provider, string code)
    {
        var normalized = Normalize(provider);
        if (userId == Guid.Empty || normalized.Length == 0 || string.IsNullOrWhiteSpace(code))
        {
            return InvalidLinkCode();
        }

        // ★ 只在当前账号名下找：别的账号暂存的那一份从这里根本够不着。
        var entry = (await _authTokenService.GetUserTokensAsync(userId, PendingLoginProvider))
            .FirstOrDefault(t => t.Name == normalized);
        if (entry == null || entry.IsUsed || (entry.ExpiresAt.HasValue && entry.ExpiresAt.Value <= DateTime.UtcNow))
        {
            return InvalidLinkCode();
        }

        var payload = ReadPayload(_authTokenService.RevealTokenValue(entry));
        if (payload == null
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(payload.CodeHash), Encoding.UTF8.GetBytes(OneTimeToken.Hash(code))))
        {
            return InvalidLinkCode();
        }

        // 条件更新即抢占：并发的两次完成只有一次拿到 true。
        if (!await _authTokenService.MarkTokenAsUsedAsync(entry.Id))
        {
            return InvalidLinkCode();
        }

        return Ok(new PendingOAuthLinkDto
        {
            Provider = normalized,
            ProviderKey = payload.ProviderKey,
            DisplayName = payload.DisplayName,
        });
    }

    /// <summary>暂存的外部身份；完成码只存哈希。</summary>
    private sealed record PendingLinkPayload(string CodeHash, string ProviderKey, string? DisplayName);

    /// <summary>读回暂存；读不出来（被外部改过、key ring 换过）一律当作没有。</summary>
    private PendingLinkPayload? ReadPayload(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<PendingLinkPayload>(raw);
            return payload is { CodeHash.Length: > 0, ProviderKey.Length: > 0 } ? payload : null;
        }
        catch (JsonException ex)
        {
            LogWarning("A staged OAuth link could not be read ({Error}); treating it as absent.", ex.Message);
            return null;
        }
    }

    /// <summary>失效、过期、不存在、账号不符共用同一个回答。</summary>
    private Result<PendingOAuthLinkDto> InvalidLinkCode()
        => Fail<PendingOAuthLinkDto>("Invalid or expired link confirmation", 400, ErrorCodes.VALIDATION_ERROR);

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
