namespace Tnzi.Identity.Services;

/// <summary>
/// 认证服务实现
/// 提供用户登录、登出、Token刷新、双因素认证等功能
/// </summary>
public class AuthService : ApplicationService, IAuthService
{
    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;
    private readonly ITokenService _tokenService;
    private readonly IOptionsMonitor<IdentityOptions> _identityOptionsMonitor;
    private readonly IEventBus? _eventBus;
    private readonly ICaptchaService? _captchaService;
    private readonly IAuthTokenService? _authTokenService;
    private readonly IPasswordPolicyService? _passwordPolicyService;
    private readonly ISessionService? _sessionService;
    private readonly ILoginSecurityService? _loginSecurityService;
    private readonly ITwoFactorService? _twoFactorService;
    private readonly ILoginSessionCoordinator? _loginSessionCoordinator;
    private readonly ILoginGuardEvaluator? _loginGuardEvaluator;
    private readonly ICurrentTenant? _currentTenant;
    private readonly bool _multiTenancyEnabled;

    private IdentityOptions IdentityOptions => _identityOptionsMonitor.CurrentValue;

    public AuthService(
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        ITokenService tokenService,
        IOptionsMonitor<IdentityOptions> identityOptions,
        IServiceProvider serviceProvider,
        IEventBus? eventBus = null,
        ICaptchaService? captchaService = null,
        IAuthTokenService? authTokenService = null,
        IPasswordPolicyService? passwordPolicyService = null,
        ISessionService? sessionService = null,
        ILoginSecurityService? loginSecurityService = null,
        ITwoFactorService? twoFactorService = null,
        ICurrentTenant? currentTenant = null,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null,
        ILoginSessionCoordinator? loginSessionCoordinator = null,
        ILoginGuardEvaluator? loginGuardEvaluator = null)
        : base(serviceProvider)
    {
        _userManager = Check.NotNull(userManager);
        _signInManager = Check.NotNull(signInManager);
        _tokenService = Check.NotNull(tokenService);
        _identityOptionsMonitor = Check.NotNull(identityOptions);
        _eventBus = eventBus;
        _captchaService = captchaService;
        _authTokenService = authTokenService;
        _passwordPolicyService = passwordPolicyService;
        _sessionService = sessionService;
        _loginSecurityService = loginSecurityService;
        _twoFactorService = twoFactorService;
        _loginSessionCoordinator = loginSessionCoordinator;
        _loginGuardEvaluator = loginGuardEvaluator;
        _currentTenant = currentTenant;
        _multiTenancyEnabled = multiTenancyOptions?.Value.Enabled ?? false;
    }

    /// <summary>
    /// 获取公开认证配置：把现有 IdentityOptions 中的登录方式 / 注册 / 找回 / 第三方开关
    /// 映射为登录页可消费的布尔标志。只读，不含任何密钥。
    /// </summary>
    public Result<AuthConfigDto> GetAuthConfig()
    {
        var opt = IdentityOptions;
        var signIn = opt.SignIn;
        var registration = opt.Registration;
        var recovery = opt.Recovery;
        var otp = opt.Otp;
        var captcha = opt.Captcha;

        var dto = new AuthConfigDto
        {
            AllowUserNameLogin = signIn.AllowUserNameLogin,
            AllowEmailLogin = signIn.AllowEmailLogin,
            AllowSmsLogin = signIn.AllowSmsLogin,
            UseEmailAsUserName = signIn.UseEmailAsUserName,

            // 与渠道开关是「与」的关系：登录方式本身要开着，且至少有一条渠道能送达。
            EnableCodeLogin = signIn.AllowCodeLogin && (otp.EnableSms || otp.EnableEmail),
            CodeLoginViaSms = signIn.AllowCodeLogin && otp.EnableSms,
            CodeLoginViaEmail = signIn.AllowCodeLogin && otp.EnableEmail,

            EnableRegistration = registration.EnableQuickRegisterEmail || registration.EnableQuickRegisterSms,
            RegisterViaEmail = registration.EnableQuickRegisterEmail,
            RegisterViaSms = registration.EnableQuickRegisterSms,

            EnablePasswordRecovery = recovery.EnablePasswordResetByEmail || recovery.EnablePasswordResetBySms,
            RecoveryViaEmail = recovery.EnablePasswordResetByEmail,
            RecoveryViaSms = recovery.EnablePasswordResetBySms,

            EnableCaptchaOnLogin = captcha.EnableCaptchaOnLogin,
            EnableCaptchaOnRegister = captcha.EnableCaptchaOnRegister,

            EnablePasskey = opt.Passkey.Enabled,

            OAuthProviders = BuildEnabledOAuthProviders(opt.OAuth),
        };

        return Ok(dto);
    }

    /// <summary>
    /// 已知第三方登录提供商注册表（key + 展示名 + 从 OAuthOptions 取对应配置的选择器）。
    /// </summary>
    private static List<AuthProviderRegistration> KnownOAuthProviders { get; } =
    [
        new("google", "Google", o => o.Google),
        new("microsoft", "Microsoft", o => o.Microsoft),
        new("facebook", "Facebook", o => o.Facebook),
        new("twitter", "Twitter", o => o.Twitter),
        new("github", "GitHub", o => o.GitHub),
    ];

    /// <summary>
    /// 把 OAuth 配置中已填写 ClientId/ClientSecret（即 Enabled）的提供商映射为公开信息列表。
    /// </summary>
    private static List<OAuthProviderInfoDto> BuildEnabledOAuthProviders(OAuthOptions oauth)
    {
        var result = new List<OAuthProviderInfoDto>();
        foreach (var registration in KnownOAuthProviders)
        {
            if (registration.Selector(oauth).Enabled)
            {
                result.Add(new OAuthProviderInfoDto
                {
                    Provider = registration.Key,
                    DisplayName = registration.DisplayName,
                });
            }
        }

        return result;
    }

    private sealed record AuthProviderRegistration(
        string Key,
        string DisplayName,
        Func<OAuthOptions, OAuthProviderOptions> Selector);

    public async Task<Result<string>> LoginAsync(LoginDto input)
    {
        // 执行公共登录验证逻辑
        var validationResult = await ValidateLoginAndGetUserAsync(input);
        if (!validationResult.Succeeded)
        {
            // 将验证失败结果转换为 Result<string>
            return Fail<string>(validationResult.Message ?? "Login validation failed", validationResult.Code ?? 400, validationResult.ErrorCode, validationResult.ErrorDetails);
        }

        var (user, loginIdentifier) = validationResult.Data;

        // 凭据之外的准入策略（IP 白名单 / 设备 / 时段）。在 2FA 挑战之前，
        // 这样被拒的登录不会白发一条验证码短信，也不留任何登录成功的痕迹。
        var guardResult = await RunLoginGuardsAsync(user, LoginMethod.Password, loginIdentifier);
        if (!guardResult.Allowed)
        {
            return Fail<string>(guardResult.Message!, guardResult.Code, guardResult.ErrorCode);
        }

        // 检查是否需要 2FA:总开关开着 且 至少一种方式当前可用(渠道未被关闭)。
        // 若已启用的方式因部署渠道全部关闭而无一可用,则按"未开启 2FA"直接放行。
        if (await _userManager.GetTwoFactorEnabledAsync(user))
        {
            var supportedTypes = await ResolveSupportedTwoFactorTypesAsync(user);
            if (supportedTypes.Count > 0)
            {
                return await Handle2FAChallengeAsync<string>(user, supportedTypes);
            }
            LogInformation("User {UserId} has 2FA enabled but no usable method (all channels disabled); signing in without challenge.", user.Id);
        }

        // 建立登录会话（应用多登录策略；Reject 达上限则拒绝本次登录）
        var sessionResult = await EstablishLoginSessionAsync(user);
        if (!sessionResult.Succeeded)
        {
            return Fail<string>(sessionResult.Message ?? "Login rejected", sessionResult.Code ?? 403, sessionResult.ErrorCode);
        }

        // 生成 Token（携带 session_id claim，供服务端每请求校验会话）
        var roles = await GetRolesWithTenantContextAsync(user);
        var token = _tokenService.GenerateToken(user, roles, sessionId: ToSessionClaim(sessionResult.Data));

        // 清除登录失败记录
        if (_captchaService != null)
        {
            await _captchaService.ClearLoginFailureAsync(loginIdentifier);
        }

        // 发布登录成功事件
        var ipAddress = ScopedContext?.ClientIpAddress;
        var userAgent = ScopedContext?.UserAgent;
        await PublishLoginSuccessEventAsync(user, ipAddress, userAgent, IdentityConstants.LoginProvider.JWT);
        await CheckAndPublishAbnormalLoginAsync(user, ipAddress, userAgent);

        return Result<string>.Success(token);
    }

    public async Task<Result<TokenResult>> LoginWithRefreshTokenAsync(LoginDto input)
    {
        var jwtOptions = IdentityOptions.Jwt;

        // 检查是否启用 RefreshToken
        if (!jwtOptions.EnableRefreshToken)
        {
            return Fail<TokenResult>("Refresh token is not enabled", 400);
        }

        // 执行公共登录验证逻辑
        var validationResult = await ValidateLoginAndGetUserAsync(input);
        if (!validationResult.Succeeded)
        {
            // 将验证失败结果转换为 Result<TokenResult>
            return Fail<TokenResult>(validationResult.Message ?? "Validation failed", validationResult.Code ?? 400, validationResult.ErrorCode, validationResult.ErrorDetails);
        }

        var (user, loginIdentifier) = validationResult.Data;

        // 凭据之外的准入策略（IP 白名单 / 设备 / 时段）。在 2FA 挑战之前，
        // 这样被拒的登录不会白发一条验证码短信，也不留任何登录成功的痕迹。
        var guardResult = await RunLoginGuardsAsync(user, LoginMethod.PasswordWithRefreshToken, loginIdentifier);
        if (!guardResult.Allowed)
        {
            return Fail<TokenResult>(guardResult.Message!, guardResult.Code, guardResult.ErrorCode);
        }

        // 检查是否需要 2FA:总开关开着 且 至少一种方式当前可用(渠道未被关闭)。
        // 若已启用的方式因部署渠道全部关闭而无一可用,则按"未开启 2FA"直接放行。
        if (await _userManager.GetTwoFactorEnabledAsync(user))
        {
            var supportedTypes = await ResolveSupportedTwoFactorTypesAsync(user);
            if (supportedTypes.Count > 0)
            {
                return await Handle2FAChallengeAsync<TokenResult>(user, supportedTypes);
            }
            LogInformation("User {UserId} has 2FA enabled but no usable method (all channels disabled); signing in without challenge.", user.Id);
        }

        // 建立登录会话（应用多登录策略；Reject 达上限则拒绝本次登录）
        var sessionResult = await EstablishLoginSessionAsync(user);
        if (!sessionResult.Succeeded)
        {
            return Fail<TokenResult>(sessionResult.Message ?? "Login rejected", sessionResult.Code ?? 403, sessionResult.ErrorCode);
        }

        // 生成TokenResult并保存RefreshToken（access token 携带 session_id，刷新令牌绑定该会话）
        var tokenResult = await GenerateAndSaveTokenResultAsync(user, sessionResult.Data, enableRefreshToken: true);

        // 清除登录失败记录
        if (_captchaService != null)
        {
            await _captchaService.ClearLoginFailureAsync(loginIdentifier);
        }

        // 发布登录成功事件
        var ipAddress = ScopedContext?.ClientIpAddress;
        var userAgent = ScopedContext?.UserAgent;
        await PublishLoginSuccessEventAsync(user, ipAddress, userAgent, IdentityConstants.LoginProvider.JWT);
        await CheckAndPublishAbnormalLoginAsync(user, ipAddress, userAgent);

        return Result<TokenResult>.Success(tokenResult);
    }

    /// <inheritdoc />
    public async Task<Result<TokenResult>> IssueTokenAsync(User user, LoginMethod method, TwoFactorType? satisfiedFactor = null)
    {
        Check.NotNull(user);

        // 与 LoginWithRefreshTokenAsync 的后半段**逐步同构**，这是刻意的：
        // 凭据校验可以发生在别处（passkey 的断言由运行时完成），但校验通过之后的每一步
        // 都必须与密码登录一致，否则准入策略、多设备策略、会话绑定、登录日志
        // 会在每一条新增的登录方式上重新失效一遍。

        // ★ 账号锁定 / 停用由守卫链上的 LockedAccountLoginGuard 判定，不在这里单独查一遍：
        // 那道检查在密码路径上是 SignInManager 顺手做掉的，凭据校验挪到别处就跟着消失，
        // 所以它被收口成了框架内置守卫 —— 一处实现覆盖全部签发路径。
        var guardResult = await RunLoginGuardsAsync(user, method, loginIdentifier: null);
        if (!guardResult.Allowed)
        {
            return Fail<TokenResult>(guardResult.Message!, guardResult.Code, guardResult.ErrorCode);
        }

        // 2FA 照常判定：框架不替部署方决定「passkey 是否已经算两个因子」。
        if (await _userManager.GetTwoFactorEnabledAsync(user))
        {
            var supportedTypes = await ResolveSupportedTwoFactorTypesAsync(user);

            // ★ 扣除本次校验已经证明过的那个因子 —— 再问一次问的是同一件事。
            // 扣的是**一个**因子，不是整张表：还开着别的方式就照样挑战。
            var challengeTypes = satisfiedFactor.HasValue
                ? supportedTypes.Where(t => t != satisfiedFactor.Value).ToList()
                : supportedTypes;

            if (challengeTypes.Count > 0)
            {
                return await Handle2FAChallengeAsync<TokenResult>(user, challengeTypes);
            }

            if (supportedTypes.Count > 0)
            {
                LogInformation(
                    "User {UserId} signed in via {Method} using {Factor}, which already satisfies every enabled two-factor method; no further challenge.",
                    user.Id, method, satisfiedFactor);
            }
            else
            {
                LogInformation("User {UserId} has 2FA enabled but no usable method (all channels disabled); signing in without challenge.", user.Id);
            }
        }

        var sessionResult = await EstablishLoginSessionAsync(user);
        if (!sessionResult.Succeeded)
        {
            return Fail<TokenResult>(sessionResult.Message ?? "Login rejected", sessionResult.Code ?? 403, sessionResult.ErrorCode);
        }

        // 刷新令牌照部署配置签发。此前这里硬编码 true，于是把 EnableRefreshToken 关掉的部署
        // 在这条出口上仍会拿到刷新令牌 —— 一个配置项对不同登录方式给出不同结果。
        var tokenResult = await GenerateAndSaveTokenResultAsync(
            user, sessionResult.Data, enableRefreshToken: IdentityOptions.Jwt.EnableRefreshToken);

        var ipAddress = ScopedContext?.ClientIpAddress;
        var userAgent = ScopedContext?.UserAgent;
        await PublishLoginSuccessEventAsync(user, ipAddress, userAgent, ResolveLoginProvider(method));
        await CheckAndPublishAbnormalLoginAsync(user, ipAddress, userAgent);

        return Result<TokenResult>.Success(tokenResult);
    }

    /// <summary>
    /// 登录方式到登录日志里 provider 名的映射。
    /// </summary>
    private static string ResolveLoginProvider(LoginMethod method) => method switch
    {
        LoginMethod.Passkey => IdentityConstants.LoginProvider.Passkey,
        LoginMethod.VerificationCode => IdentityConstants.LoginProvider.CodeLogin,
        LoginMethod.Registration => IdentityConstants.LoginProvider.Registration,
        _ => IdentityConstants.LoginProvider.JWT
    };

    /// <summary>
    /// 公共的登录验证逻辑
    /// 验证码校验、用户查找、密码验证、状态检查（邮箱/手机确认、密码过期）、多登录策略
    /// </summary>
    /// <param name="input">登录信息</param>
    /// <returns>验证成功返回用户和登录标识符，失败返回错误信息</returns>
    private async Task<Result<(User User, string LoginIdentifier)>> ValidateLoginAndGetUserAsync(LoginDto input)
    {
        var ipAddress = ScopedContext?.ClientIpAddress;
        var userAgent = ScopedContext?.UserAgent;
        var options = IdentityOptions;
        var signInOptions = options.SignIn;
        var captchaOptions = options.Captcha;
        var registrationOptions = options.Registration;
        var loginIdentifier = ipAddress ?? input.UserName;

        // 1. 验证码校验(自适应:同一登录标识失败次数达阈值才要求验证码)。
        //    需要但缺失/无效时,返回专用错误码 IDENTITY_CAPTCHA_REQUIRED + 一张新验证码图,
        //    前端据此内联渲染验证码框并让用户重试(平时登录零打扰)。
        if (captchaOptions.EnableCaptchaOnLogin && _captchaService != null)
        {
            var captchaRequired = await _captchaService.IsCaptchaRequiredAsync(loginIdentifier);
            if (captchaRequired)
            {
                var captchaValid = await VerifyCaptchaAsync(input.CaptchaId, input.CaptchaCode, "login");
                if (!captchaValid)
                {
                    return await BuildCaptchaRequiredResultAsync<(User, string)>("login");
                }
            }
        }

        // 2. 查找用户
        var user = await FindUserByLoginInputAsync(input.UserName, signInOptions);
        if (user == null)
        {
            if (_captchaService != null)
            {
                await _captchaService.RecordLoginFailureAsync(loginIdentifier);
            }
            await PublishLoginFailedEventAsync(null, input.UserName, "User not found", ipAddress, userAgent);
            return Fail<(User, string)>("Invalid username or password", 400);
        }

        // 3. 密码验证（lockoutOnFailure 由配置决定）
        var signInResult = await _signInManager.CheckPasswordSignInAsync(user, input.Password, options.AccountSecurity.EnableLockout);
        if (!signInResult.Succeeded)
        {
            if (_captchaService != null)
            {
                await _captchaService.RecordLoginFailureAsync(loginIdentifier);
            }
            await PublishLoginFailedEventAsync(user.Id, user.UserName, signInResult.ToString(), ipAddress, userAgent);
            return Fail<(User, string)>("Invalid username or password", 400);
        }

        // 4. 邮箱确认检查
        if (registrationOptions.RequireConfirmedEmail && !user.EmailConfirmed)
        {
            return Fail<(User, string)>(
                "Email address has not been confirmed",
                403,
                ErrorCodes.IDENTITY_EMAIL_NOT_CONFIRMED,
                new { userId = user.Id, email = user.Email });
        }

        // 5. 手机确认检查
        if (registrationOptions.RequireConfirmedPhone && !user.PhoneNumberConfirmed)
        {
            return Fail<(User, string)>("Phone number has not been confirmed", 403);
        }

        // 6. 密码过期检查
        if (_passwordPolicyService != null)
        {
            var expirationResult = await _passwordPolicyService.CheckPasswordExpirationAsync(user.Id);
            if (expirationResult.IsExpired)
            {
                return Fail<(User, string)>("Password has expired, please reset your password", 403);
            }
        }

        // 注意：多登录策略（单设备/限并发/踢旧/拒新）已移至 EstablishLoginSessionAsync，
        // 在**签发令牌前、建立会话时**统一处理（2FA 路径也经此），避免旧实现"校验点与
        // 会话创建点分离"导致的竞态与不生效。

        // 验证通过，返回用户和登录标识符
        return Result<(User, string)>.Success((user, loginIdentifier));
    }

    public async Task<Result<TokenResult>> RefreshTokenAsync(string refreshToken)
    {
        if (string.IsNullOrEmpty(refreshToken))
            return Fail<TokenResult>("Refresh token is required", 400);

        if (_authTokenService == null)
            return Fail<TokenResult>("Token service is not available", 500);

        var tokenEntry = await _authTokenService.FindTokenByValueAsync(IdentityConstants.TokenProvider.JWT, IdentityConstants.TokenName.RefreshToken, refreshToken);
        if (tokenEntry == null)
        {
            return Fail<TokenResult>("Invalid or expired refresh token", 400);
        }

        if (tokenEntry.IsUsed)
        {
            return Fail<TokenResult>("Refresh token has already been used", 400);
        }

        if (tokenEntry.ExpiresAt.HasValue && tokenEntry.ExpiresAt.Value < DateTime.UtcNow)
        {
            return Fail<TokenResult>("Refresh token has expired", 400);
        }

        var userId = tokenEntry.UserId;
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
            return Fail<TokenResult>("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);

        // 会话校验：刷新令牌绑定了会话（SessionId 非空）时，会话被撤销/过期即拒绝刷新，
        // 使"踢下线"对刷新链路也生效（被踢设备无法用未过期的刷新令牌续命）。
        // 遗留令牌（SessionId=Guid.Empty）跳过，向后兼容。
        if (ShouldEnforceSessionValidation() && tokenEntry.SessionId != Guid.Empty && _sessionService != null)
        {
            var sessionValid = await _sessionService.IsSessionValidAsync(tokenEntry.SessionId);
            if (!sessionValid)
            {
                return Fail<TokenResult>("Session has been revoked or expired", 401, ErrorCodes.IDENTITY_SESSION_REVOKED);
            }
        }

        // 先标记旧 token 为已使用（带并发控制，防止 token 重放攻击）
        var marked = await _authTokenService.MarkTokenAsUsedAsync(tokenEntry.Id);
        if (!marked)
        {
            return Fail<TokenResult>("Refresh token has already been used", 400);
        }

        // 再生成新 token，沿用同一会话（session_id claim + 刷新令牌绑定不变）
        var tokenResult = await GenerateAndSaveTokenResultAsync(user, tokenEntry.SessionId, enableRefreshToken: true);

        // 滑动续期会话：使活跃用户随刷新持续在线（会话硬过期跟随新刷新令牌生命周期）
        if (tokenEntry.SessionId != Guid.Empty && _sessionService != null)
        {
            var newExpiresAt = DateTime.UtcNow.AddDays(IdentityOptions.Jwt.RefreshTokenExpirationDays);
            await _sessionService.RenewSessionAsync(tokenEntry.SessionId, newExpiresAt);
        }

        return Result<TokenResult>.Success(tokenResult);
    }

    public async Task<Result<string>> LogoutAsync(Guid userId)
    {
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return Fail<string>("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        var ipAddress = ScopedContext?.ClientIpAddress;
        var userAgent = ScopedContext?.UserAgent;

        await _signInManager.SignOutAsync();

        if (_sessionService != null)
        {
            // 只登出当前设备：从 access token 的 session_id claim 取当前会话，仅撤销它。
            // 取不到会话（遗留令牌/未启用会话强制）时回退撤销该用户全部会话（旧行为）。
            var currentSessionId = ParseCurrentSessionId();
            if (currentSessionId.HasValue)
            {
                await _sessionService.RevokeSessionAsync(currentSessionId.Value);
            }
            else
            {
                await _sessionService.RevokeAllSessionsAsync(userId);
            }
        }

        if (_eventBus != null)
        {
            await _eventBus.PublishAsync(new UserLoggedOutEvent
            {
                UserId = userId,
                UserName = user.UserName ?? string.Empty,
                LogoutTime = DateTime.UtcNow,
                IpAddress = ipAddress,
                UserAgent = userAgent
            }, cancellationToken: default);
        }

        return Result<string>.Success("Logout successfully");
    }

    public async Task<Result<TwoFactorChallengeDto>> SendTwoFactorCodeAsync(SendTwoFactorCodeDto input)
    {
        if (_twoFactorService == null)
        {
            return Fail<TwoFactorChallengeDto>("Two-factor service is not available", 500);
        }

        // 从临时Token获取用户ID
        if (_authTokenService == null)
        {
            return Fail<TwoFactorChallengeDto>("Token service is not available", 500);
        }

        var tokenEntry = await _authTokenService.FindTokenByValueAsync(IdentityConstants.TokenProvider.TwoFactor, IdentityConstants.TokenName.TempToken, input.TempToken);
        if (tokenEntry == null)
        {
            return Fail<TwoFactorChallengeDto>("Invalid or expired temporary token", 400);
        }

        var userId = tokenEntry.UserId;
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return Fail<TwoFactorChallengeDto>("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        // 只接受登录挑战实际提供的方式：挑战列表已按"用户已启用的方式 ∩ 部署开启的渠道"
        // 过滤，此处复算并校验，否则持有临时令牌者可绕过用户单独关闭的某种方式。
        var usableTypes = await ResolveSupportedTwoFactorTypesAsync(user);
        if (usableTypes is { Count: > 0 } && !usableTypes.Contains(input.Type))
        {
            return Fail<TwoFactorChallengeDto>("The selected two-factor method is not enabled", 400);
        }

        bool sent = false;
        string? maskedAddress = null;
        if (input.Type == TwoFactorType.Sms)
        {
            if (string.IsNullOrWhiteSpace(user.PhoneNumber))
            {
                return Fail<TwoFactorChallengeDto>("Phone number is not set", 400);
            }
            var smsResult = await _twoFactorService.SendSmsCodeAsync(userId, user.PhoneNumber, VerificationCodePurpose.TwoFactor);
            sent = smsResult.Succeeded;
            maskedAddress = ContactAddressMasking.MaskPhone(user.PhoneNumber);
        }
        else if (input.Type == TwoFactorType.Email)
        {
            if (string.IsNullOrWhiteSpace(user.Email))
            {
                return Fail<TwoFactorChallengeDto>("Email is not set", 400);
            }
            var emailResult = await _twoFactorService.SendEmailCodeAsync(userId, user.Email, VerificationCodePurpose.TwoFactor);
            sent = emailResult.Succeeded;
            maskedAddress = ContactAddressMasking.MaskEmail(user.Email);
        }
        else
        {
            return Fail<TwoFactorChallengeDto>("Invalid two-factor type", 400);
        }

        if (!sent)
        {
            return Fail<TwoFactorChallengeDto>("Failed to send verification code", 500);
        }

        // 回执里的可选方式与登录挑战保持同一口径（用户已启用 ∩ 部署开启的渠道），
        // 否则前端会重新渲染出用户已单独关闭的方式（此前还漏掉 TOTP）。
        // 解析不出方式时（无 2FA 服务）回退为"已验证地址"派生。
        var supportedTypes = new List<TwoFactorType>();
        if (usableTypes is { Count: > 0 })
        {
            supportedTypes.AddRange(usableTypes);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(user.PhoneNumber) && user.PhoneNumberConfirmed)
            {
                supportedTypes.Add(TwoFactorType.Sms);
            }
            if (!string.IsNullOrWhiteSpace(user.Email) && user.EmailConfirmed)
            {
                supportedTypes.Add(TwoFactorType.Email);
            }
        }

        // 回填 CodeSent + MaskedAddress，让登录页可显示"验证码已发送到 j•••@example.com"，
        // 消除用户"到底发没发"的疑虑（此前 DTO 有字段但从不填充 = 死字段）。
        return Result<TwoFactorChallengeDto>.Success(new TwoFactorChallengeDto
        {
            RequiresTwoFactor = true,
            SupportedTypes = supportedTypes,
            TempToken = input.TempToken,
            CodeSent = sent,
            MaskedAddress = maskedAddress
        });
    }

    /// <summary>邮箱脱敏：保留首字符与域名（<c>john@ex.com</c> → <c>j***@ex.com</c>）。</summary>
    private static string? MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var at = email.IndexOf('@');
        if (at <= 0) return "***";
        var name = email[..at];
        var domain = email[at..]; // 含 '@'
        var visible = name.Length <= 1 ? name : name[..1];
        return $"{visible}***{domain}";
    }

    /// <summary>手机号脱敏：仅保留末 4 位（<c>+14155552671</c> → <c>•••••2671</c>）。</summary>
    private static string? MaskPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length <= 4) return "••••";
        return $"•••••{digits[^4..]}";
    }

    public async Task<Result<TokenResult>> VerifyTwoFactorAndLoginAsync(VerifyTwoFactorDto input)
    {
        if (_twoFactorService == null)
        {
            return Fail<TokenResult>("Two-factor service is not available", 500);
        }

        if (_authTokenService == null)
        {
            return Fail<TokenResult>("Token service is not available", 500);
        }

        // 从临时Token获取用户ID
        var tokenEntry = await _authTokenService.FindTokenByValueAsync(IdentityConstants.TokenProvider.TwoFactor, IdentityConstants.TokenName.TempToken, input.TempToken);
        if (tokenEntry == null)
        {
            return Fail<TokenResult>("Invalid or expired temporary token", 400);
        }

        var userId = tokenEntry.UserId;
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return Fail<TokenResult>("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        // 获取客户端信息（用于日志记录和异常检测）
        var ipAddress = ScopedContext?.ClientIpAddress;
        var userAgent = ScopedContext?.UserAgent;

        // 与 SendTwoFactorCodeAsync 同款校验：只接受当前确实可用的方式，
        // 使"单独关闭某方式"在验证环节也生效（挑战列表之外的方式一律拒绝）。
        var usableTypes = await ResolveSupportedTwoFactorTypesAsync(user);
        if (usableTypes is { Count: > 0 } && !usableTypes.Contains(input.Type))
        {
            await PublishLoginFailedEventAsync(userId, user.UserName, "2FA method not enabled", ipAddress, userAgent);
            return Fail<TokenResult>("The selected two-factor method is not enabled", 400);
        }

        // 验证2FA验证码
        var isValid = await _twoFactorService.VerifyCodeAsync(userId, input.Code, input.Type, VerificationCodePurpose.TwoFactor);
        if (!isValid.Succeeded)
        {
            await PublishLoginFailedEventAsync(userId, user.UserName, "Invalid 2FA code", ipAddress, userAgent);
            return Fail<TokenResult>("Invalid verification code", 400);
        }

        // 标记临时Token为已使用（核心业务逻辑，必须同步执行）
        await _authTokenService.MarkTokenAsUsedAsync(tokenEntry.Id);

        // 凭据之外的准入策略。密码路径已经跑过一次，这里再跑是因为 2FA 是独立请求：
        // 中间可能换了网络，且 OAuth / 验证码登录并不经过密码路径。
        var guardResult = await RunLoginGuardsAsync(user, LoginMethod.TwoFactor, loginIdentifier: null);
        if (!guardResult.Allowed)
        {
            return Fail<TokenResult>(guardResult.Message!, guardResult.Code, guardResult.ErrorCode);
        }

        // 2FA 通过后才建立登录会话（应用多登录策略；Reject 达上限则拒绝）
        var sessionResult = await EstablishLoginSessionAsync(user);
        if (!sessionResult.Succeeded)
        {
            return Fail<TokenResult>(sessionResult.Message ?? "Login rejected", sessionResult.Code ?? 403, sessionResult.ErrorCode);
        }

        // 生成TokenResult并保存RefreshToken
        var tokenResult = await GenerateAndSaveTokenResultAsync(user, sessionResult.Data, enableRefreshToken: true);

        // 发布用户登录事件（由事件处理器处理日志记录）
        await PublishLoginSuccessEventAsync(user, ipAddress, userAgent, IdentityConstants.LoginProvider.JWT);

        return Result<TokenResult>.Success(tokenResult);
    }

    #region Private Methods

    /// <summary>
    /// 生成TokenResult并保存RefreshToken（如果启用）
    /// 统一处理Token生成、RefreshToken保存逻辑，减少代码重复。
    /// <paramref name="sessionId"/> 为登录会话ID：写入 access token 的 session_id claim，
    /// 并把刷新令牌绑定到该会话（<see cref="Guid.Empty"/> 表示不绑定，向后兼容）。
    /// </summary>
    private async Task<TokenResult> GenerateAndSaveTokenResultAsync(User user, Guid sessionId, bool enableRefreshToken = true)
    {
        var roles = await GetRolesWithTenantContextAsync(user);
        var jwtOptions = IdentityOptions.Jwt;

        // 生成AccessToken（携带 session_id claim）
        var accessToken = _tokenService.GenerateToken(user, roles, sessionId: ToSessionClaim(sessionId));
        var accessTokenExpiresAt = DateTime.UtcNow.AddMinutes(jwtOptions.AccessTokenExpirationMinutes);

        string? refreshToken = null;
        DateTime? refreshTokenExpiresAt = null;

        // 如果启用RefreshToken，生成并保存（按会话绑定：多设备各自独立刷新令牌）
        if (enableRefreshToken && jwtOptions.EnableRefreshToken)
        {
            refreshToken = _tokenService.GenerateRefreshToken();
            refreshTokenExpiresAt = DateTime.UtcNow.AddDays(jwtOptions.RefreshTokenExpirationDays);

            // 保存RefreshToken
            if (_authTokenService != null)
            {
                await _authTokenService.SaveTokenAsync(
                    user.Id,
                    IdentityConstants.TokenProvider.JWT,
                    IdentityConstants.TokenName.RefreshToken,
                    refreshToken,
                    refreshTokenExpiresAt,
                    sessionId);
            }
        }

        return new TokenResult
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken ?? string.Empty,
            ExpiresAt = accessTokenExpiresAt,
            ExpiresIn = jwtOptions.AccessTokenExpirationMinutes * 60,
            RefreshTokenExpiresIn = enableRefreshToken && jwtOptions.EnableRefreshToken
                ? jwtOptions.RefreshTokenExpirationDays * 24 * 60 * 60
                : null
        };
    }

    /// <summary>会话ID为 <see cref="Guid.Empty"/> 时返回 null，令牌不写 session_id claim（不受强制校验）。</summary>
    private static Guid? ToSessionClaim(Guid sessionId) => sessionId == Guid.Empty ? null : sessionId;

    /// <summary>是否强制会话校验（Session 配置开关，默认开）。</summary>
    private bool ShouldEnforceSessionValidation()
        => (ServiceProvider?.GetService<IOptions<SessionOptions>>()?.Value ?? new SessionOptions()).EnforceSessionValidation;

    /// <summary>从当前请求主体的 session_id claim 解析会话ID；无则返回 null。</summary>
    private Guid? ParseCurrentSessionId()
    {
        var raw = CurrentUser?.FindClaim(IdentityConstants.ClaimTypeNames.SessionId);
        return !string.IsNullOrEmpty(raw) && Guid.TryParse(raw, out var sid) && sid != Guid.Empty ? sid : null;
    }

    /// <summary>
    /// 建立登录会话并应用多登录策略。无协调器（如纯单元测试）时返回空会话（不做绑定/强制），
    /// 令牌退回旧的无 session_id 行为。
    /// </summary>
    private async Task<Result<Guid>> EstablishLoginSessionAsync(User user)
    {
        if (_loginSessionCoordinator == null)
        {
            return Ok(Guid.Empty);
        }

        return await _loginSessionCoordinator.EstablishAsync(user.Id);
    }

    /// <summary>
    /// 跑一遍登录守卫（IP 白名单 / 设备 / 时段这类凭据之外的准入策略）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 调用点必须在**身份校验通过之后、2FA 挑战与会话建立之前**：那是最早的安全点。
    /// 放在这里，被拒的登录既不会白发一条 2FA 短信，也不会建立会话（多设备策略据此
    /// 踢掉其它设备）、清零失败计数或在登录日志里留下一条成功记录。
    /// </para>
    /// <para>
    /// 拒绝时的副作用与「密码错误」完全一致：累加失败计数 + 记一条登录失败（带守卫给的
    /// 真实原因，对外文案则同形），这样自适应验证码与锁定策略照常对被拒的尝试生效。
    /// </para>
    /// </remarks>
    private async Task<LoginGuardResult> RunLoginGuardsAsync(User user, LoginMethod method, string? loginIdentifier)
    {
        if (_loginGuardEvaluator is not { HasGuards: true })
        {
            return LoginGuardResult.Allow();
        }

        var ipAddress = ScopedContext?.ClientIpAddress;
        var userAgent = ScopedContext?.UserAgent;

        var result = await _loginGuardEvaluator.EvaluateAsync(
            new LoginGuardContext(user, method, ipAddress, userAgent));
        if (result.Allowed)
        {
            return result;
        }

        if (_captchaService != null && !string.IsNullOrEmpty(loginIdentifier))
        {
            await _captchaService.RecordLoginFailureAsync(loginIdentifier);
        }

        await PublishLoginFailedEventAsync(
            user.Id, user.UserName, result.AuditReason ?? "Denied by a login guard", ipAddress, userAgent);

        return result;
    }

    private async Task<bool> VerifyCaptchaAsync(string? captchaId, string? captchaCode, string purpose)
    {
        if (_captchaService == null) return true;
        if (string.IsNullOrEmpty(captchaId) || string.IsNullOrEmpty(captchaCode)) return false;
        return await _captchaService.VerifyAsync(captchaId, captchaCode, purpose);
    }

    /// <summary>
    /// 构建"需要图形验证码"失败结果:生成一张新验证码,连同专用错误码
    /// <see cref="ErrorCodes.IDENTITY_CAPTCHA_REQUIRED"/> 一并返回,前端据此内联渲染
    /// 验证码框(id + base64 图片)并让用户重试。缓存不可用(无法生成)时回退为不带图片的同码错误。
    /// </summary>
    private async Task<Result<T>> BuildCaptchaRequiredResultAsync<T>(string purpose)
    {
        object? details = null;
        if (_captchaService is { IsCacheAvailable: true })
        {
            var captcha = await _captchaService.GenerateAsync(purpose);
            details = new CaptchaDto
            {
                CaptchaId = captcha.CaptchaId,
                ImageBase64 = Convert.ToBase64String(captcha.ImageBytes),
                ExpirationSeconds = captcha.ExpirationSeconds,
            };
        }

        return Fail<T>("Captcha verification is required", 400, ErrorCodes.IDENTITY_CAPTCHA_REQUIRED, details);
    }

    private async Task<User?> FindUserByLoginInputAsync(string loginInput, TnziSignInOptions signInOptions)
    {
        User? user = null;
        if (signInOptions.AllowUserNameLogin)
        {
            user = await _userManager.FindByNameAsync(loginInput);
        }
        if (user == null && signInOptions.AllowEmailLogin)
        {
            user = await _userManager.FindByEmailAsync(loginInput);
        }
        if (user == null && signInOptions.AllowSmsLogin)
        {
            user = await _userManager.FindByPhoneNumberAsync(loginInput, requireConfirmed: true);
        }
        return user;
    }

    private async Task<IList<string>> GetRolesWithTenantContextAsync(User user)
    {
        if (_multiTenancyEnabled && user.TenantId.HasValue && _currentTenant != null)
        {
            using (_currentTenant.Change(user.TenantId.Value))
            {
                return await _userManager.GetRolesAsync(user);
            }
        }

        return await _userManager.GetRolesAsync(user);
    }

    /// <summary>
    /// 在独立 DI scope 中持久化 2FA 临时令牌，使其立即提交、不被外层请求事务回滚。
    /// 原因见 <see cref="Handle2FAChallengeAsync{T}"/> —— 2FA 挑战返回失败信封会触发
    /// UnitOfWork 过滤器回滚，独立 scope 的 DbContext 无外层事务、写入即时提交。
    /// </summary>
    private async Task PersistTwoFactorTempTokenAsync(Guid userId, string tempToken, DateTime expiresAt)
    {
        // 无 ServiceProvider（理论上不发生）时回退常规保存 —— 至少在未启用全局 UoW 的
        // 部署下仍能工作。
        if (ServiceProvider == null)
        {
            if (_authTokenService != null)
            {
                await _authTokenService.SaveTokenAsync(
                    userId, IdentityConstants.TokenProvider.TwoFactor, IdentityConstants.TokenName.TempToken, tempToken, expiresAt);
            }
            return;
        }

        using var scope = ServiceProvider.CreateScope();
        var tokenService = scope.ServiceProvider.GetService<IAuthTokenService>();
        if (tokenService != null)
        {
            await tokenService.SaveTokenAsync(
                userId, IdentityConstants.TokenProvider.TwoFactor, IdentityConstants.TokenName.TempToken, tempToken, expiresAt);
        }
    }

    /// <summary>
    /// 计算登录挑战应展示的 2FA 方式集合:优先经 <see cref="ITwoFactorService"/>（已按部署渠道
    /// 开关 EnableSms/EnableEmail/EnableTotp + 地址验证过滤）；无该服务时回退，回退路径同样按
    /// OTP 渠道开关门控（关闭的渠道不出现）。返回空集合表示当前无任何可用方式。
    /// </summary>
    private async Task<List<TwoFactorType>> ResolveSupportedTwoFactorTypesAsync(User user)
    {
        if (_twoFactorService != null)
        {
            return await _twoFactorService.GetEnabledTwoFactorTypesAsync(user);
        }

        // 无 2FA 服务时回退:按 OTP 渠道开关 + 已验证地址/已配置 TOTP 计算。
        var otpOptions = IdentityOptions.Otp;
        var supportedTypes = new List<TwoFactorType>();
        if (otpOptions.EnableSms && !string.IsNullOrWhiteSpace(user.PhoneNumber) && user.PhoneNumberConfirmed)
        {
            supportedTypes.Add(TwoFactorType.Sms);
        }
        if (otpOptions.EnableEmail && !string.IsNullOrWhiteSpace(user.Email) && user.EmailConfirmed)
        {
            supportedTypes.Add(TwoFactorType.Email);
        }
        if (otpOptions.EnableTotp)
        {
            var authenticatorKey = await _userManager.GetAuthenticatorKeyAsync(user);
            if (!string.IsNullOrEmpty(authenticatorKey))
            {
                supportedTypes.Add(TwoFactorType.Totp);
            }
        }
        return supportedTypes;
    }

    private async Task<Result<T>> Handle2FAChallengeAsync<T>(User user, List<TwoFactorType> supportedTypes)
    {
        // 使用 CSPRNG 生成安全的临时令牌，防止可预测性攻击
        var tempToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        // 2FA 挑战以"失败"信封返回(403 2FA_REQUIRED)，让前端停在登录页切到验证步骤。
        // 但在 EnableGlobalUnitOfWork 下，UnitOfWork 过滤器对非成功结果会**回滚整个请求
        // 事务** —— 这会把刚保存的临时令牌一并丢弃，导致后续 verify-2fa / send-2fa-code
        // 全部因 "Invalid or expired temporary token" 失败（2FA 登录彻底不可用）。
        // 因此在**独立 DI scope**（独立 DbContext + 连接、无外层事务）中保存，令其立即
        // 提交、不受外层回滚影响；无 scope 时回退到常规保存。
        await PersistTwoFactorTempTokenAsync(user.Id, tempToken, DateTime.UtcNow.AddMinutes(10));

        return Fail<T>("Two-factor authentication required", 403, ErrorCodes.IDENTITY_2FA_REQUIRED, new { TempToken = tempToken, SupportedTypes = supportedTypes });
    }

    private async Task PublishLoginSuccessEventAsync(User user, string? ipAddress, string? userAgent, string provider)
    {
        if (_eventBus != null)
        {
            await _eventBus.PublishAsync(new UserLoggedInEvent
            {
                UserId = user.Id,
                UserName = user.UserName ?? string.Empty,
                LoginTime = DateTime.UtcNow,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                LoginProvider = provider
            }, cancellationToken: default);
        }
    }

    private async Task PublishLoginFailedEventAsync(Guid? userId, string? userName, string failureReason, string? ipAddress, string? userAgent)
    {
        if (_eventBus != null)
        {
            await _eventBus.PublishAsync(new UserLoginFailedEvent
            {
                UserId = userId,
                UserName = userName,
                FailureReason = failureReason,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                FailureTime = DateTime.UtcNow
            }, cancellationToken: default);
        }
    }

    private async Task CheckAndPublishAbnormalLoginAsync(User user, string? ipAddress, string? userAgent)
    {
        if (_loginSecurityService == null || !IdentityOptions.AccountSecurity.EnableAbnormalLoginDetection) return;
        var result = await _loginSecurityService.DetectAbnormalLoginAsync(user.Id, ipAddress, userAgent);
        if (result.IsAbnormal && _eventBus != null)
        {
            await _eventBus.PublishAsync(new AbnormalLoginDetectedEvent
            {
                UserId = user.Id,
                UserName = user.UserName ?? string.Empty,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                DetectedTime = DateTime.UtcNow,
                AbnormalTypes = result.AbnormalTypes.Select(t => t.ToString()).ToList(),
                RiskLevel = result.RiskLevel,
                Details = result.Details,
                RecommendedAction = result.RecommendedAction.ToString()
            }, cancellationToken: default);
        }
    }

    /// <summary>
    /// 发布用户注册事件
    /// </summary>
    /// <param name="user">注册的用户</param>
    private async Task PublishUserRegisteredEventAsync(User user)
    {
        if (_eventBus != null)
        {
            await _eventBus.PublishAsync(new UserRegisteredEvent
            {
                UserId = user.Id,
                UserName = user.UserName ?? string.Empty,
                Email = user.Email,
                RegistrationTime = DateTime.UtcNow
            }, cancellationToken: default);
        }
    }

    private Guid? ResolveNewUserTenantId()
    {
        if (!_multiTenancyEnabled)
        {
            return null;
        }

        return _currentTenant?.Id ?? CurrentUser?.TenantId;
    }

    #endregion

    #region 验证码登录

    /// <inheritdoc />
    public async Task<Result<string>> SendCodeLoginCodeAsync(SendCodeLoginCodeDto input)
    {
        if (_twoFactorService == null)
        {
            return Fail<string>("Two-factor service is not available", 500);
        }

        // 验证输入
        var address = input.Type == TwoFactorType.Email ? input.Email : input.PhoneNumber;
        if (string.IsNullOrWhiteSpace(address))
        {
            return Fail<string>(
                input.Type == TwoFactorType.Email ? "Email is required" : "Phone number is required",
                400);
        }

        // 图形验证码校验（启用登录验证码时，发出短信/邮件之前先过图形验证码）。
        // ★ 与密码登录的「自适应」刻意不同，这里**无条件**要求：发码这条路径没有「失败次数」
        // 可以累计（发码本身不会失败），而它恰恰是唯一一条每次调用都真的产生短信/邮件费用的入口。
        // 少了这道门，开着 EnableCaptchaOnLogin 的部署仍然留着一个无验证码的发信入口。
        // 图形验证码校验（启用登录验证码时，发出短信/邮件之前先过图形验证码）。
        // ★ 与密码登录的「自适应」刻意不同，这里**无条件**要求：发码这条路径没有「失败次数」
        // 可以累计（发码本身不会失败），而它恰恰是唯一一条每次调用都真的产生短信/邮件费用的入口。
        // 少了这道门，开着 EnableCaptchaOnLogin 的部署仍然留着一个无验证码的发信入口。
        var captchaOptions = IdentityOptions.Captcha;
        if (captchaOptions.EnableCaptchaOnLogin)
        {
            var captchaValid = await VerifyCaptchaAsync(input.CaptchaId, input.CaptchaCode, "login");
            if (!captchaValid)
            {
                return await BuildCaptchaRequiredResultAsync<string>("login");
            }
        }

        // ★ 登录方式本身被关掉时，在**发码之前**就拒绝：这是端点自己的门，
        // 不能只靠 /auth/config 让前端隐藏入口 —— 隐藏的是入口，端点仍然可达。
        if (!IdentityOptions.SignIn.AllowCodeLogin)
        {
            return Fail<string>("Verification-code sign-in is not enabled", 400);
        }

        // 检查配置
        var otpOptions = IdentityOptions.Otp;
        if (input.Type == TwoFactorType.Email && !otpOptions.EnableEmail)
        {
            return Fail<string>("Email verification is not enabled", 400);
        }
        if (input.Type == TwoFactorType.Sms && !otpOptions.EnableSms)
        {
            return Fail<string>("SMS verification is not enabled", 400);
        }

        // 查找用户（可能不存在）
        Guid? userId = null;
        if (input.Type == TwoFactorType.Email)
        {
            var user = await _userManager.FindByEmailAsync(address);
            userId = user?.Id;
        }
        else
        {
            var user = await _userManager.FindByPhoneNumberAsync(address);
            userId = user?.Id;
        }

        // 发送验证码
        var result = await _twoFactorService.SendCodeByAddressAsync(address, input.Type, VerificationCodePurpose.CodeLogin, userId);
        if (!result.Succeeded)
        {
            return Fail<string>(result.Message ?? "Failed to send verification code", result.Code ?? 500);
        }

        return Result<string>.Success("Verification code sent successfully");
    }

    /// <inheritdoc />
    public async Task<Result<CodeLoginResultDto>> CodeLoginAsync(CodeLoginDto input)
    {
        if (_twoFactorService == null)
        {
            return Fail<CodeLoginResultDto>("Two-factor service is not available", 500);
        }

        // 与发码入口同一道门。两处都要有：关掉开关之后，手里还攥着一枚未用码的人
        // 依然能直接调这里，只挡发码等于给那些码留了一扇后门。
        if (!IdentityOptions.SignIn.AllowCodeLogin)
        {
            return Fail<CodeLoginResultDto>("Verification-code sign-in is not enabled", 400);
        }

        var ipAddress = ScopedContext?.ClientIpAddress;
        var userAgent = ScopedContext?.UserAgent;
        var codeLoginOptions = IdentityOptions;
        var jwtOptions = codeLoginOptions.Jwt;
        var registrationOptions = codeLoginOptions.Registration;

        // 验证输入
        var address = input.Type == TwoFactorType.Email ? input.Email : input.PhoneNumber;
        if (string.IsNullOrWhiteSpace(address))
        {
            return Fail<CodeLoginResultDto>(
                input.Type == TwoFactorType.Email ? "Email is required" : "Phone number is required",
                400);
        }

        // 验证验证码。★ 用途限定为 CodeLogin：为找回密码 / 换绑 / 二次确认发出的码到不了这里。
        var verifyResult = await _twoFactorService.VerifyCodeByAddressAndMarkUsedAsync(
            address, input.Code, input.Type, VerificationCodePurpose.CodeLogin);
        if (!verifyResult.Succeeded)
        {
            await PublishLoginFailedEventAsync(null, address, "Invalid verification code", ipAddress, userAgent);
            return Fail<CodeLoginResultDto>(verifyResult.Message ?? "Invalid verification code", verifyResult.Code ?? 400);
        }

        // 查找用户
        User? user = null;
        bool isNewUser = false;

        if (input.Type == TwoFactorType.Email)
        {
            user = await _userManager.FindByEmailAsync(address);
        }
        else
        {
            user = await _userManager.FindByPhoneNumberAsync(address);
        }

        // 用户不存在，检查是否可以自动注册
        if (user == null)
        {
            var canAutoRegister = (input.Type == TwoFactorType.Email && registrationOptions.EnableQuickRegisterEmail)
                               || (input.Type == TwoFactorType.Sms && registrationOptions.EnableQuickRegisterSms);

            if (!canAutoRegister)
            {
                return Fail<CodeLoginResultDto>(
                    "User not found. Quick registration is not enabled for this verification type.",
                    404);
            }

            // 自动注册
            var registerResult = await AutoRegisterByCodeAsync(
                input.Type == TwoFactorType.Email ? address : null,
                input.Type == TwoFactorType.Sms ? address : null,
                input.Type);

            if (!registerResult.Succeeded)
            {
                return Fail<CodeLoginResultDto>(registerResult.Message ?? "Failed to register user", registerResult.Code ?? 400);
            }

            user = registerResult.Data;
            isNewUser = true;
        }
        else
        {
            // 用户存在，验证码登录时自动确认邮箱/手机号
            if (input.Type == TwoFactorType.Email && !user.EmailConfirmed)
            {
                user.EmailConfirmed = true;
                await _userManager.UpdateAsync(user);
            }
            else if (input.Type == TwoFactorType.Sms && !user.PhoneNumberConfirmed)
            {
                user.PhoneNumberConfirmed = true;
                await _userManager.UpdateAsync(user);
            }
        }

        // ★★★ 签发一律经 IssueTokenAsync：登录守卫 → 2FA 判定 → 会话协调器 → 带 session_id
        // 的令牌 → 登录成功事件，与 passkey 走同一条出口。此前这里手工复制了它的后半段、
        // 唯独漏掉 2FA 那一步 —— 于是开着 TOTP 的账号只要能收到一封邮件就绕过了 TOTP，
        // 强度阶梯还是反的（passkey 这种强凭据要过 2FA，邮箱验证码反而不用）。
        //
        // satisfiedFactor 传本次真正用掉的那个渠道：邮箱验证码登录已经证明「能收这个邮箱」，
        // 邮箱 2FA 再问一遍问的是同一件事（消除冗余）；TOTP / 短信仍留在挑战列表里（不放宽）。
        var issueResult = await IssueTokenAsync(user!, LoginMethod.VerificationCode, satisfiedFactor: input.Type);
        if (!issueResult.Succeeded)
        {
            // 2FA 挑战也从这里出来（403 + IDENTITY_2FA_REQUIRED + 临时令牌）。
            // ErrorDetails 必须原样带出，否则前端拿不到 tempToken，挑战无从继续。
            return Fail<CodeLoginResultDto>(
                issueResult.Message ?? "Login rejected",
                issueResult.Code ?? 400,
                issueResult.ErrorCode,
                issueResult.ErrorDetails);
        }

        // 设置密码令牌放在签发成功之后才生成：挑战没过就先发一枚令牌是没有意义的。
        // ⚠ 需要走 2FA 挑战的账号完成 verify-2fa 后拿到的是 TokenResult，不含这枚令牌 ——
        // 「还没设过密码」是一句提示而不是安全边界，用户可在个人中心补设。
        var requirePasswordSetup = string.IsNullOrEmpty(user!.PasswordHash);
        string? setPasswordToken = null;

        if (requirePasswordSetup)
        {
            // 生成设置密码的 Token
            setPasswordToken = await _userManager.GeneratePasswordResetTokenAsync(user);

            // 存储 Token
            if (_authTokenService != null)
            {
                await _authTokenService.SaveTokenAsync(
                    user.Id,
                    IdentityConstants.TokenProvider.Identity,
                    IdentityConstants.TokenName.SetPassword,
                    setPasswordToken,
                    DateTime.UtcNow.AddMinutes(IdentityOptions.Registration.SetPasswordTokenExpirationMinutes));
            }
        }

        var tokenResult = issueResult.Data!;

        return Result<CodeLoginResultDto>.Success(new CodeLoginResultDto
        {
            AccessToken = tokenResult.AccessToken,
            RefreshToken = tokenResult.RefreshToken,
            ExpiresIn = jwtOptions.AccessTokenExpirationMinutes * 60,
            RefreshTokenExpiresIn = tokenResult.RefreshTokenExpiresIn,
            RequirePasswordSetup = requirePasswordSetup,
            SetPasswordToken = setPasswordToken,
            UserId = user.Id,
            UserName = user.UserName,
            IsNewUser = isNewUser
        });
    }

    /// <summary>
    /// 通过验证码自动注册用户
    /// </summary>
    private async Task<Result<User>> AutoRegisterByCodeAsync(string? email, string? phoneNumber, TwoFactorType type)
    {
        var registrationOptions = IdentityOptions.Registration;

        // 确定基础用户名
        string baseUserName;
        if (type == TwoFactorType.Email && !string.IsNullOrEmpty(email))
        {
            baseUserName = email;
        }
        else if (!string.IsNullOrEmpty(phoneNumber))
        {
            baseUserName = phoneNumber;
        }
        else
        {
            return Fail<User>("Email or phone number is required for registration", 400);
        }

        // 生成唯一用户名（循环检查直到找到唯一用户名）
        var userName = await UserNameGenerator.GenerateUniqueAsync(baseUserName, async (name) => await _userManager.FindByNameAsync(name) != null);

        // 创建用户（无密码）
        var user = new User
        {
            UserName = userName,
            Email = email,
            PhoneNumber = phoneNumber,
            EmailConfirmed = type == TwoFactorType.Email,
            PhoneNumberConfirmed = type == TwoFactorType.Sms,
            TenantId = ResolveNewUserTenantId()
        };

        var result = await _userManager.CreateAsync(user);
        if (!result.Succeeded)
        {
            return Fail<User>(
                result.FormatErrors(),
                400);
        }

        // 发布用户注册事件
        await PublishUserRegisteredEventAsync(user);

        return Result<User>.Success(user);
    }

    #endregion

    #region 验证码找回密码

    /// <summary>
    /// 发送密码找回验证码
    /// </summary>
    public async Task<Result<string>> SendPasswordRecoveryCodeAsync(SendPasswordRecoveryCodeDto input)
    {
        var otpOptions = IdentityOptions.Otp;
        var recoveryOptions = IdentityOptions.Recovery;

        // 验证类型是否启用
        if (input.Type == TwoFactorType.Email && !otpOptions.EnableEmail)
        {
            return Fail<string>("Email verification is not enabled", 400, ErrorCodes.VALIDATION_ERROR);
        }
        if (input.Type == TwoFactorType.Sms && !otpOptions.EnableSms)
        {
            return Fail<string>("SMS verification is not enabled", 400, ErrorCodes.VALIDATION_ERROR);
        }

        // 验证输入
        string address;
        if (input.Type == TwoFactorType.Email)
        {
            if (string.IsNullOrWhiteSpace(input.Email))
            {
                return Fail<string>("Email is required", 400, ErrorCodes.VALIDATION_ERROR);
            }
            address = input.Email;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(input.PhoneNumber))
            {
                return Fail<string>("Phone number is required", 400, ErrorCodes.VALIDATION_ERROR);
            }
            address = input.PhoneNumber;
        }

        // 查找用户
        User? user;
        if (input.Type == TwoFactorType.Email)
        {
            user = await _userManager.FindByEmailAsync(input.Email!);
        }
        else
        {
            user = await _userManager.FindByPhoneNumberAsync(input.PhoneNumber);
        }

        if (user == null)
        {
            return Fail<string>("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        if (_twoFactorService == null)
        {
            return Fail<string>("Two-factor service is not available", 500);
        }

        // 发送验证码
        var result = await _twoFactorService.SendCodeByAddressAsync(address, input.Type, VerificationCodePurpose.PasswordRecovery, user.Id);
        if (!result.Succeeded)
        {
            return Fail<string>(result.Message ?? "Failed to send verification code", (int)(result.Code ?? 400));
        }

        return Result<string>.Success("Verification code sent successfully");
    }

    /// <summary>
    /// 验证码重置密码
    /// </summary>
    public async Task<Result<string>> ResetPasswordByCodeAsync(ResetPasswordByCodeDto input)
    {
        // 验证输入
        string address;
        if (input.Type == TwoFactorType.Email)
        {
            if (string.IsNullOrWhiteSpace(input.Email))
            {
                return Fail<string>("Email is required", 400, ErrorCodes.VALIDATION_ERROR);
            }
            address = input.Email!; // 已在上方验证非空
        }
        else
        {
            if (string.IsNullOrWhiteSpace(input.PhoneNumber))
            {
                return Fail<string>("Phone number is required", 400, ErrorCodes.VALIDATION_ERROR);
            }
            address = input.PhoneNumber!; // 已在上方验证非空
        }

        if (_twoFactorService == null)
        {
            return Fail<string>("Two-factor service is not available", 500);
        }

        // 验证验证码。★ 用途限定为 PasswordRecovery：拿一枚登录码来重置密码会在这里落空。
        var verifyResult = await _twoFactorService.VerifyCodeByAddressAndMarkUsedAsync(
            address, input.Code, input.Type, VerificationCodePurpose.PasswordRecovery);
        if (!verifyResult.Succeeded)
        {
            return Fail<string>(verifyResult.Message ?? "Verification failed", (int)(verifyResult.Code ?? 400));
        }

        // 查找用户
        User? user;
        if (verifyResult.Data.HasValue)
        {
            user = await _userManager.FindByGuidAsync(verifyResult.Data.Value);
        }
        else
        {
            user = input.Type == TwoFactorType.Email
                ? await _userManager.FindByEmailAsync(input.Email!)
                : await _userManager.FindByPhoneNumberAsync(input.PhoneNumber);
        }

        if (user == null)
        {
            return Fail<string>("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        // 验证密码强度
        if (_passwordPolicyService != null)
        {
            var strengthError = _passwordPolicyService.ValidatePasswordStrength(input.NewPassword);
            if (strengthError != null)
            {
                return Fail<string>(strengthError, 400, ErrorCodes.VALIDATION_ERROR);
            }
        }

        // 检查用户是否已有密码
        var hasPassword = await _userManager.HasPasswordAsync(user);

        IdentityResult result;
        if (hasPassword)
        {
            // 用户已有密码，需要生成重置令牌然后重置
            var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
            result = await _userManager.ResetPasswordAsync(user, resetToken, input.NewPassword);
        }
        else
        {
            // 用户没有密码，直接添加密码
            result = await _userManager.AddPasswordAsync(user, input.NewPassword);
        }

        if (!result.Succeeded)
        {
            return Fail<string>(
                result.FormatErrors(),
                400,
                ErrorCodes.VALIDATION_ERROR);
        }

        // 发布密码重置事件
        if (_eventBus != null)
        {
            await _eventBus.PublishAsync(new UserPasswordResetEvent
            {
                UserId = user.Id,
                UserName = user.UserName ?? string.Empty,
                ResetTime = DateTime.UtcNow,
                IsSelfReset = true
            }, cancellationToken: default);
        }

        return Result<string>.Success("Password reset successfully");
    }

    #endregion
}
