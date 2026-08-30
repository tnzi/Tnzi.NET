namespace Tnzi.Identity.Services;

/// <inheritdoc cref="IPasskeyEnrollmentTokenService"/>
public class PasskeyEnrollmentTokenService : ApplicationService, IPasskeyEnrollmentTokenService
{
    /// <summary>
    /// 令牌在 <c>AuthToken</c> 里的归属标记。与刷新令牌、2FA 临时令牌互不干扰
    /// （唯一索引是 <c>(UserId, LoginProvider, Name, SessionId)</c>）。
    /// </summary>
    internal const string TokenLoginProvider = IdentityConstants.LoginProvider.Passkey;

    /// <summary>同一用户同时只保留一枚：补发一张就该让上一张作废。</summary>
    internal const string TokenName = "EnrollmentToken";

    private readonly IAuthTokenService _authTokenService;
    private readonly IOptionsMonitor<IdentityOptions> _options;

    /// <summary>
    /// 初始化一个 <see cref="PasskeyEnrollmentTokenService"/> 类型的新实例。
    /// </summary>
    public PasskeyEnrollmentTokenService(
        IServiceProvider serviceProvider,
        IAuthTokenService authTokenService,
        IOptionsMonitor<IdentityOptions> options)
        : base(serviceProvider)
    {
        _authTokenService = Check.NotNull(authTokenService);
        _options = Check.NotNull(options);
    }

    /// <inheritdoc />
    public async Task<Result<PasskeyEnrollmentTokenDto>> IssueAsync(Guid userId, TimeSpan? lifetime = null)
    {
        if (userId == Guid.Empty)
        {
            return Fail<PasskeyEnrollmentTokenDto>("User id is required", 400, ErrorCodes.VALIDATION_ERROR);
        }

        var passkey = _options.CurrentValue.Passkey;
        var ttl = lifetime ?? TimeSpan.FromMinutes(Math.Max(1, passkey.EnrollmentTokenLifetimeMinutes));
        var expiresAt = DateTime.UtcNow.Add(ttl);

        // 256 位随机数：没有字典可查，所以存哈希时不需要加盐或慢哈希（理由写在原语里）。
        var token = OneTimeToken.Create();

        await _authTokenService.SaveTokenAsync(
            userId,
            TokenLoginProvider,
            TokenName,
            OneTimeToken.Hash(token),
            expiresAt);

        LogInformation("Issued a passkey enrollment token for user {UserId}, valid until {ExpiresAt:o}.", userId, expiresAt);

        return Ok(new PasskeyEnrollmentTokenDto
        {
            Token = token,
            UserId = userId,
            ExpiresAt = expiresAt
        });
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> ValidateAsync(string token)
    {
        var entry = await FindUsableAsync(token);
        return entry == null
            ? InvalidToken<Guid>()
            : Ok(entry.UserId);
    }

    /// <inheritdoc />
    public async Task<Result> ConsumeAsync(string token)
    {
        var entry = await FindUsableAsync(token);
        if (entry == null)
        {
            // 幂等：已经消费过的令牌再消费一次不算错误，调用方不必自己判重。
            // 真正的守卫是"已消费的令牌换不到注册选项"，那条在 ValidateAsync 上。
            return Ok();
        }

        await _authTokenService.MarkTokenAsUsedAsync(entry.Id);
        return Ok();
    }

    /// <summary>
    /// 按哈希取回一枚仍然可用的令牌。失效 / 过期 / 不存在一律得到 <c>null</c>。
    /// </summary>
    private async Task<AuthToken?> FindUsableAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var entry = await _authTokenService.FindTokenByValueAsync(TokenLoginProvider, TokenName, OneTimeToken.Hash(token));
        if (entry == null || entry.IsUsed)
        {
            return null;
        }

        return entry.ExpiresAt.HasValue && entry.ExpiresAt.Value <= DateTime.UtcNow ? null : entry;
    }

    /// <summary>
    /// 失效、过期、不存在共用同一个回答 —— 区分开就是在帮人试探哪些令牌是真的。
    /// </summary>
    private Result<T> InvalidToken<T>()
        => Fail<T>("Invalid or expired enrollment token", 400, ErrorCodes.VALIDATION_ERROR);
}
