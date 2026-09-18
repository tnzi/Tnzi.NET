namespace Tnzi.Identity.Services;

/// <inheritdoc cref="IStepUpService"/>
/// <remarks>
/// 确认记录借 <c>AuthToken</c> 表存放（<c>LoginProvider = "StepUp"</c>，<c>Name</c> = 范围），
/// 零迁移，且自动继承既有的过期清理后台任务与 <c>[AuditIgnore]</c> 豁免 ——
/// 与 passkey 注册令牌是同一个路子。
/// </remarks>
public class StepUpService : ApplicationService, IStepUpService
{
    /// <summary>确认记录在 <c>AuthToken</c> 里的归属标记。</summary>
    internal const string TokenLoginProvider = "StepUp";

    /// <summary>范围名的长度上限，与 <c>AuthToken.Name</c> 的列宽对齐。</summary>
    private const int MaxScopeLength = 128;

    private readonly IAuthTokenService _authTokenService;
    private readonly IPasskeyService _passkeyService;
    private readonly ITwoFactorService _twoFactorService;
    private readonly IOptionsMonitor<IdentityOptions> _options;

    /// <summary>
    /// 初始化一个 <see cref="StepUpService"/> 类型的新实例。
    /// </summary>
    public StepUpService(
        IServiceProvider serviceProvider,
        IAuthTokenService authTokenService,
        IPasskeyService passkeyService,
        ITwoFactorService twoFactorService,
        IOptionsMonitor<IdentityOptions> options)
        : base(serviceProvider)
    {
        _authTokenService = Check.NotNull(authTokenService);
        _passkeyService = Check.NotNull(passkeyService);
        _twoFactorService = Check.NotNull(twoFactorService);
        _options = Check.NotNull(options);
    }

    private StepUpOptions StepUp => _options.CurrentValue.StepUp;

    /// <inheritdoc />
    public async Task<bool> IsSatisfiedAsync(string scope, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(scope);

        if (!StepUp.Enabled)
        {
            // 加固项没开就照常放行 —— 让端点因为漏配而全部 401，比不做这个能力还糟。
            return true;
        }

        var userId = CurrentUser?.Id;
        if (userId == null || userId == Guid.Empty)
        {
            return false;
        }

        // ★★ 记录按 (用户, 范围, 会话) 命中，不只按用户：同一用户的另一条会话（被盗令牌）不能搭
        //   本人这次确认的便车 —— step-up 的威胁模型正是「终端已易手」，只绑用户等于挡的不是它。
        //   没有会话 claim 的部署（未启用会话 / 遗留令牌）两边都是 Guid.Empty，与绑定之前逐字相同。
        var sessionId = CurrentSessionId();
        var tokens = await _authTokenService.GetUserTokensAsync(userId.Value, TokenLoginProvider);
        var entry = tokens.FirstOrDefault(t =>
            string.Equals(t.Name, Normalize(scope), StringComparison.Ordinal)
            && t.SessionId == sessionId
            && !t.IsUsed
            && (t.ExpiresAt == null || t.ExpiresAt > DateTime.UtcNow));

        if (entry == null)
        {
            return false;
        }

        if (!StepUp.SingleUse)
        {
            return true;
        }

        // ★ 必须按返回值决定放行。<see cref="IAuthTokenService.MarkTokenAsUsedAsync"/> 是条件更新
        // （`WHERE Id = @id AND IsUsed = false`），返回 false 说明有并发请求抢先消费了这一次确认。
        // 忽略它、两个请求都放行，等于「一次确认只用一次」在并发下形同虚设 ——
        // 而这个选项存在的全部理由就是单次动作的后果太重。
        return await _authTokenService.MarkTokenAsUsedAsync(entry.Id);
    }

    /// <inheritdoc />
    public async Task<Result<StepUpGrantDto>> VerifyWithPasskeyAsync(PasskeyCompleteDto input, string scope)
    {
        Check.NotNull(input);

        var precondition = EnsureCanGrant(scope);
        if (!precondition.Succeeded)
        {
            return Fail<StepUpGrantDto>(precondition.Message!, precondition.Code ?? 400, precondition.ErrorCode);
        }

        var assertion = await _passkeyService.VerifyAssertionAsync(input);
        if (!assertion.Succeeded)
        {
            return Fail<StepUpGrantDto>(assertion.Message!, assertion.Code ?? 401, assertion.ErrorCode);
        }

        // ★★★ 断言成功只说明「有一把注册过的 passkey 在场」。若它属于另一个账号，
        // 那证明的是别人在场 —— 而这里要证明的恰恰是「就是当前这个会话的主人」。
        // 少了这一比，任何持有自己 passkey 的人都能替一个被接管的会话完成确认。
        if (assertion.Data != CurrentUser!.Id)
        {
            LogWarning(
                "Step-up rejected: the passkey belongs to user {AssertedUserId} but the session belongs to {SessionUserId}.",
                assertion.Data,
                CurrentUser.Id);

            return InvalidVerification();
        }

        return await GrantAsync(CurrentUser.Id!.Value, scope);
    }

    /// <inheritdoc />
    public async Task<Result<string?>> SendCodeAsync(TwoFactorType type)
    {
        var userId = CurrentUser?.Id;
        if (userId is null || userId == Guid.Empty)
        {
            return Fail<string?>("Not authenticated", 401, ErrorCodes.UNAUTHORIZED);
        }

        // 用途是 StepUp：这枚码只能用来完成二次确认，登录 / 换绑 / 找回密码都认不出它。
        return await _twoFactorService.SendCodeToUserAsync(userId.Value, type, VerificationCodePurpose.StepUp);
    }

    /// <inheritdoc />
    public async Task<Result<StepUpGrantDto>> VerifyWithCodeAsync(string code, TwoFactorType type, string scope)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return InvalidVerification();
        }

        var precondition = EnsureCanGrant(scope);
        if (!precondition.Succeeded)
        {
            return Fail<StepUpGrantDto>(precondition.Message!, precondition.Code ?? 400, precondition.ErrorCode);
        }

        var userId = CurrentUser!.Id!.Value;

        // 验证码本就是按用户签发与核销的，因此不存在 passkey 那种「验的是别人」的可能。
        // ★ 用途是 StepUp 而不是 TwoFactor：一枚为登录发出的码不该能确认一笔转账。
        // 对应的发码入口是 SendCodeAsync —— 二次确认有自己的码，不借用别的流程的。
        var verified = await _twoFactorService.VerifyCodeAsync(userId, code, type, VerificationCodePurpose.StepUp);
        if (!verified.Succeeded)
        {
            return InvalidVerification();
        }

        return await GrantAsync(userId, scope);
    }

    /// <summary>
    /// 记下一次确认。同一用户同一范围<b>同一会话</b>只保留最新的一条（唯一索引含 SessionId，也是想要的语义）。
    /// 绑定会话的顺带好处：会话被撤销时按 SessionId 删令牌，会把这条确认记录一并收走。
    /// </summary>
    private async Task<Result<StepUpGrantDto>> GrantAsync(Guid userId, string scope)
    {
        var lifetime = TimeSpan.FromSeconds(Math.Clamp(StepUp.LifetimeSeconds, 30, 3600));
        var expiresAt = DateTime.UtcNow.Add(lifetime);
        var normalized = Normalize(scope);

        // 值本身不作为凭据下发，但仍然存哈希：一条能被读出来的记录若哪天被谁拿去复用，
        // 「确认过」这件事就会凭空成立。存哈希让这条记录在库里读到也没有用。
        await _authTokenService.SaveTokenAsync(
            userId,
            TokenLoginProvider,
            normalized,
            OneTimeToken.Hash(OneTimeToken.Create()),
            expiresAt,
            CurrentSessionId());

        LogInformation("Step-up granted to user {UserId} for scope {Scope} until {ExpiresAt:o}.", userId, normalized, expiresAt);

        return Ok(new StepUpGrantDto
        {
            Scope = normalized,
            ExpiresAt = expiresAt,
            SingleUse = StepUp.SingleUse
        });
    }

    /// <summary>
    /// 必须已登录，且范围必须合法。二次确认是加在会话之上的，没有会话就无从加起。
    /// </summary>
    private Result EnsureCanGrant(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope) || scope.Length > MaxScopeLength)
        {
            return Fail("Invalid step-up scope", 400, ErrorCodes.VALIDATION_ERROR);
        }

        var userId = CurrentUser?.Id;

        return userId is null || userId == Guid.Empty
            ? Fail("Not authenticated", 401, ErrorCodes.UNAUTHORIZED)
            : Ok();
    }

    /// <summary>
    /// 验证失败一律同一句话：区分「码错了」与「passkey 不是你的」等于在帮人试探。
    /// </summary>
    private Result<StepUpGrantDto> InvalidVerification()
        => Fail<StepUpGrantDto>("Verification failed", 401, ErrorCodes.UNAUTHORIZED);

    /// <summary>范围名归一化：去空白 + 转小写，免得 <c>Tip.Download</c> 与 <c>tip.download</c> 算成两个。</summary>
    private static string Normalize(string scope) => scope.Trim().ToLowerInvariant();

    /// <summary>当前主体的 <c>session_id</c> claim；取不到（未启用会话 / 遗留令牌）为 <see cref="Guid.Empty"/>。</summary>
    private Guid CurrentSessionId()
    {
        var raw = CurrentUser?.FindClaim(IdentityConstants.ClaimTypeNames.SessionId);
        return !string.IsNullOrEmpty(raw) && Guid.TryParse(raw, out var sid) ? sid : Guid.Empty;
    }
}
