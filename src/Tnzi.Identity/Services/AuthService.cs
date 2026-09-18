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
    private readonly ICaptchaVerifier? _captchaVerifier;
    private readonly IAuthTokenService? _authTokenService;
    private readonly IPasswordPolicyService? _passwordPolicyService;
    private readonly ISessionService? _sessionService;
    private readonly ILoginSecurityService? _loginSecurityService;
    private readonly ITwoFactorService? _twoFactorService;
    private readonly ILoginSessionCoordinator? _loginSessionCoordinator;
    private readonly ILoginGuardEvaluator? _loginGuardEvaluator;
    private readonly ISessionRevocationService? _sessionRevocation;
    private readonly IPasswordService? _passwordService;
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
        ILoginGuardEvaluator? loginGuardEvaluator = null,
        ISessionRevocationService? sessionRevocation = null,
        IPasswordService? passwordService = null,
        ICaptchaVerifier? captchaVerifier = null)
        : base(serviceProvider)
    {
        _userManager = Check.NotNull(userManager);
        _signInManager = Check.NotNull(signInManager);
        _tokenService = Check.NotNull(tokenService);
        _identityOptionsMonitor = Check.NotNull(identityOptions);
        _eventBus = eventBus;
        _captchaService = captchaService;
        _captchaVerifier = captchaVerifier;
        _authTokenService = authTokenService;
        _passwordPolicyService = passwordPolicyService;
        _sessionService = sessionService;
        _loginSecurityService = loginSecurityService;
        _twoFactorService = twoFactorService;
        _loginSessionCoordinator = loginSessionCoordinator;
        _loginGuardEvaluator = loginGuardEvaluator;
        _sessionRevocation = sessionRevocation;
        _passwordService = passwordService;
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

            // ★ 三个开关的并集，而不是只看两个 quick-register 标志。
            // 漏掉 EnableSelfRegistration 会让「配置说开着、页面说关着」——
            // 而更早之前它漏掉的是相反的一半：页面说关着、端点却照样开户。
            EnableRegistration = registration.EnableSelfRegistration
                              || registration.EnableQuickRegisterEmail
                              || registration.EnableQuickRegisterSms,
            RegisterViaPassword = registration.EnableSelfRegistration,
            RegisterViaEmail = registration.EnableQuickRegisterEmail,
            RegisterViaSms = registration.EnableQuickRegisterSms,

            EnablePasswordRecovery = recovery.EnablePasswordResetByEmail || recovery.EnablePasswordResetBySms,
            RecoveryViaEmail = recovery.EnablePasswordResetByEmail,
            RecoveryViaSms = recovery.EnablePasswordResetBySms,

            EnableCaptchaOnLogin = captcha.EnableCaptchaOnLogin,
            EnableCaptchaOnRegister = captcha.EnableCaptchaOnRegister,
            EnableCaptchaOnPasswordRecovery = captcha.EnableCaptchaOnPasswordRecovery,
            // 与 GET /captcha/config 同一份：登录页只请求一次 /auth/config 就知道渲染哪家控件。
            Captcha = _captchaVerifier?.GetClientConfig() ?? new CaptchaClientConfigDto(),

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
            // ★ ErrorDetails 必须原样带出：待办挑战的临时令牌就在里面，丢了前端就没法继续。
            return Fail<string>(sessionResult.Message ?? "Login rejected", sessionResult.Code ?? 403, sessionResult.ErrorCode, sessionResult.ErrorDetails);
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
            // ★ ErrorDetails 必须原样带出：待办挑战的临时令牌就在里面，丢了前端就没法继续。
            return Fail<TokenResult>(sessionResult.Message ?? "Login rejected", sessionResult.Code ?? 403, sessionResult.ErrorCode, sessionResult.ErrorDetails);
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
            // ★ ErrorDetails 必须原样带出：待办挑战的临时令牌就在里面，丢了前端就没法继续。
            return Fail<TokenResult>(sessionResult.Message ?? "Login rejected", sessionResult.Code ?? 403, sessionResult.ErrorCode, sessionResult.ErrorDetails);
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
                var captchaValid = await VerifyCaptchaAsync(input, CaptchaPurpose.Login);
                if (!captchaValid)
                {
                    return await BuildCaptchaRequiredResultAsync<(User, string)>(CaptchaPurpose.Login);
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

        // 6. 密码过期：**记成一件待办，而不是把人挡在门外**。
        //    此前这里直接 403「请重置密码」，于是用户唯一的出路是去走找回密码流程收邮件 ——
        //    而「密码到期」的本意是「换一个」，不是「你被锁在外面了」。改成设义务位之后，
        //    他在同一条登录流程里改完密码就能继续，与 2FA 挑战同一形态。
        //    ★★★ 这一位**必须落库**。此前只在内存里置位，理由写的是「IssueTokenAsync 紧接着
        //      就会读它」—— 而那句话只对没开 2FA 的账号成立：开着 2FA 的登录在下面提前
        //      返回挑战，义务位随这个实体一起被丢掉；用户带着临时令牌回来时，
        //      VerifyTwoFactorAndLoginAsync 从库里取回的是一个**全新实体**，位不在上面，
        //      于是照常签发。结果是安全性最高的那批账号（开着两步验证的）整体绕过密码到期策略，
        //      而且没有任何症状：日志、响应、审计全都是一次正常登录。
        //    ★ 只在位还没置上时写一次，避免每次到期登录都多一趟写。
        //      写失败只告警不改变结果：这一次的强制性丢了，但密码校验本身是通过的，
        //      报成登录失败只会把一个凭据正确的用户挡在门外。
        if (_passwordPolicyService != null && !user.HasPendingAction(PendingUserActions.ChangePassword))
        {
            var expirationResult = await _passwordPolicyService.CheckPasswordExpirationAsync(user.Id);
            if (expirationResult.IsExpired)
            {
                user.PendingActions |= PendingUserActions.ChangePassword;

                var flagged = await _userManager.UpdateAsync(user);
                if (!flagged.Succeeded)
                {
                    LogWarning(
                        "Password expired for user {UserId} but the obligation could not be persisted ({Errors}); "
                        + "a two-factor sign-in will not be asked to change it.",
                        user.Id, flagged.FormatErrors());
                }
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

        var tokenEntry = await _authTokenService.FindTokenByValueAsync(
            IdentityConstants.TokenProvider.JWT, IdentityConstants.TokenName.RefreshToken, refreshToken);

        if (tokenEntry == null)
        {
            // 当前这一代里查不到。可能是一枚彻底无效的令牌，也可能是刚被轮换掉的上一代 ——
            // 后者要区分「并发刷新」与「重放」，那是这条链路上唯一能发现令牌被盗的地方。
            return await HandleRotatedRefreshTokenAsync(refreshToken);
        }

        if (tokenEntry.ExpiresAt.HasValue && tokenEntry.ExpiresAt.Value < DateTime.UtcNow)
        {
            return InvalidRefreshToken();
        }

        var user = await _userManager.FindByGuidAsync(tokenEntry.UserId);
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

        // ★★ 刷新是一条**签发路径**，因此和登录一样要过守卫链。
        // 少了这一步，「停用账号」对手里攥着刷新令牌的一方完全无效：access token 到期就换一个，
        // 无限续期，而管理员那边显示账号已停用。这与 LockedAccountLoginGuard 注释里
        // 描述的规律是同一件事 —— 检查绑在「登录」这个动作上，而刷新不是登录。
        var guardResult = await RunLoginGuardsAsync(user, LoginMethod.RefreshToken, loginIdentifier: null);
        if (!guardResult.Allowed)
        {
            // 已经不具备登录资格的账号，手里的令牌不该继续留着等下一次尝试。
            await RevokeSessionAsync(tokenEntry.SessionId, SessionRevocationReason.GuardDenied);
            return Fail<TokenResult>(guardResult.Message!, guardResult.Code, guardResult.ErrorCode);
        }

        return await RotateAndIssueAsync(user, tokenEntry, refreshToken);
    }

    /// <summary>
    /// 轮换刷新令牌并签发新的令牌对。抢占失败（别的请求先轮换了）时回到并发/重放分支。
    /// </summary>
    private async Task<Result<TokenResult>> RotateAndIssueAsync(User user, AuthToken tokenEntry, string presentedValue)
    {
        var jwtOptions = IdentityOptions.Jwt;
        var newRefreshToken = _tokenService.GenerateRefreshToken();
        var refreshExpiresAt = DateTime.UtcNow.AddDays(jwtOptions.RefreshTokenExpirationDays);

        var rotated = await _authTokenService!.RotateRefreshTokenAsync(
            tokenEntry.Id, presentedValue, newRefreshToken, refreshExpiresAt);

        if (!rotated)
        {
            // 抢占失败 = 另一个请求在这几毫秒里先轮换掉了同一枚令牌。这是并发刷新，不是攻击；
            // 交给同一个分支去判宽限窗，它会把当前这一代原样返回。
            return await HandleRotatedRefreshTokenAsync(presentedValue);
        }

        var roles = await GetRolesWithTenantContextAsync(user);
        var accessToken = _tokenService.GenerateToken(user, roles, sessionId: ToSessionClaim(tokenEntry.SessionId));

        // 滑动续期会话：使活跃用户随刷新持续在线（会话硬过期跟随新刷新令牌生命周期，
        // 但不会越过会话的绝对上限 —— 夹紧在 ISessionService.RenewSessionAsync 里做）。
        if (tokenEntry.SessionId != Guid.Empty && _sessionService != null)
        {
            await _sessionService.RenewSessionAsync(tokenEntry.SessionId, refreshExpiresAt);
        }

        return Result<TokenResult>.Success(BuildTokenResult(accessToken, newRefreshToken, jwtOptions));
    }

    /// <summary>
    /// 处理「当前这一代查不到」的刷新令牌：宽限窗内视为并发刷新，窗外视为重放。
    /// </summary>
    /// <remarks>
    /// ★★★ <b>这个方法是刷新令牌轮换从「装饰」变成「防护」的那一步。</b>
    /// 只轮换不检测的话，被盗令牌被用过之后，真用户下一次刷新拿到的回答与「令牌过期了」
    /// 完全一样：前端清掉状态、跳登录页、用户重新登录，<b>攻击者那条会话原封不动继续活着</b>，
    /// 全程没有任何日志、事件或告警。RFC 9700 §2.2.2 要求公开客户端的刷新令牌
    /// 「要么发送方受限，要么按 §4.14 轮换」，而 §4.14 的轮换定义里就含这半件事。
    /// </remarks>
    private async Task<Result<TokenResult>> HandleRotatedRefreshTokenAsync(string refreshToken)
    {
        var rotatedEntry = await _authTokenService!.FindTokenByPreviousValueAsync(
            IdentityConstants.TokenProvider.JWT, IdentityConstants.TokenName.RefreshToken, refreshToken);

        if (rotatedEntry == null)
        {
            // 哪一代都不是 —— 就是一枚无效令牌。
            return InvalidRefreshToken();
        }

        var overlapSeconds = Math.Max(0, IdentityOptions.Jwt.RefreshTokenRotationOverlapSeconds);
        var withinOverlap = rotatedEntry.RotatedAt.HasValue
            && DateTime.UtcNow - rotatedEntry.RotatedAt.Value <= TimeSpan.FromSeconds(overlapSeconds);

        if (!withinOverlap)
        {
            return await OnRefreshTokenReuseDetectedAsync(rotatedEntry);
        }

        // 宽限窗内：多标签页 / 重试 / 断线重连造成的并发刷新。
        // ★ 返回**当前这一代**而不是再轮换一次 —— 再轮换会让每个并发请求各推进一代，
        // 于是先到的那个手里的令牌立刻变成「上一代的上一代」，下次刷新必被判成重放。
        if (rotatedEntry.ExpiresAt.HasValue && rotatedEntry.ExpiresAt.Value < DateTime.UtcNow)
        {
            return InvalidRefreshToken();
        }

        var user = await _userManager.FindByGuidAsync(rotatedEntry.UserId);
        if (user == null)
        {
            return Fail<TokenResult>("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        if (ShouldEnforceSessionValidation() && rotatedEntry.SessionId != Guid.Empty && _sessionService != null
            && !await _sessionService.IsSessionValidAsync(rotatedEntry.SessionId))
        {
            return Fail<TokenResult>("Session has been revoked or expired", 401, ErrorCodes.IDENTITY_SESSION_REVOKED);
        }

        var guardResult = await RunLoginGuardsAsync(user, LoginMethod.RefreshToken, loginIdentifier: null);
        if (!guardResult.Allowed)
        {
            await RevokeSessionAsync(rotatedEntry.SessionId, SessionRevocationReason.GuardDenied);
            return Fail<TokenResult>(guardResult.Message!, guardResult.Code, guardResult.ErrorCode);
        }

        // ★ 宽限窗要把**当前这一代的明文**交回给并发刷新的那一方，而 Value 存的是密文，
        //   必须经撤销出口还原。解不开（key ring 轮换 / 丢失）时按无效令牌处理：
        //   这时任何一方都换不到能用的令牌，如实让它重新登录，而不是发一串密文出去。
        var currentValue = _authTokenService.RevealTokenValue(rotatedEntry);
        if (string.IsNullOrEmpty(currentValue))
        {
            return InvalidRefreshToken();
        }

        var roles = await GetRolesWithTenantContextAsync(user);
        var accessToken = _tokenService.GenerateToken(user, roles, sessionId: ToSessionClaim(rotatedEntry.SessionId));

        LogInformation(
            "Concurrent refresh for session {SessionId} served from the rotation overlap window.",
            rotatedEntry.SessionId);

        return Result<TokenResult>.Success(
            BuildTokenResult(accessToken, currentValue, IdentityOptions.Jwt));
    }

    /// <summary>
    /// 判定为刷新令牌重放：撤销整条会话（连同其上的刷新令牌）并发布事件。
    /// </summary>
    private async Task<Result<TokenResult>> OnRefreshTokenReuseDetectedAsync(AuthToken rotatedEntry)
    {
        var ipAddress = ScopedContext?.ClientIpAddress;
        var userAgent = ScopedContext?.UserAgent;

        LogWarning(
            "Refresh token reuse detected for user {UserId} (session {SessionId}); revoking the session.",
            rotatedEntry.UserId, rotatedEntry.SessionId);

        var revokedCount = await RevokeSessionAsync(rotatedEntry.SessionId, SessionRevocationReason.RefreshTokenReuse);

        if (_eventBus != null)
        {
            // ★ 事件是这条链路上唯一会自己冒出来的信号：令牌被盗的典型形态没有失败登录、
            // 没有异地登录，风控与用户都看不到。不发出去，等于检测了但没人知道。
            await _eventBus.PublishAsync(new RefreshTokenReuseDetectedEvent
            {
                UserId = rotatedEntry.UserId,
                SessionId = rotatedEntry.SessionId,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                RotatedAt = rotatedEntry.RotatedAt,
                DetectedTime = DateTime.UtcNow,
                RevokedSessionCount = revokedCount
            }, cancellationToken: default);
        }

        return Fail<TokenResult>(
            "Invalid or expired refresh token", 401, ErrorCodes.IDENTITY_REFRESH_TOKEN_REUSED);
    }

    /// <summary>
    /// 撤销会话（含其上的刷新令牌）。未注册撤销服务时退回只撤会话，绝不静默跳过。
    /// </summary>
    private async Task<int> RevokeSessionAsync(Guid sessionId, SessionRevocationReason reason)
    {
        if (sessionId == Guid.Empty)
        {
            return 0;
        }

        if (_sessionRevocation != null)
        {
            return await _sessionRevocation.RevokeSessionAsync(sessionId, reason);
        }

        if (_sessionService != null)
        {
            var result = await _sessionService.RevokeSessionAsync(sessionId);
            return result.Succeeded ? 1 : 0;
        }

        return 0;
    }

    /// <summary>
    /// 刷新令牌无效时的统一回答。
    /// </summary>
    /// <remarks>
    /// 「不存在」「已过期」「不是任何一代」一律同一句话 —— 区分开就是在帮试探者
    /// 分辨哪些令牌是真的（同 <see cref="OneTimeToken"/> 的那条约定）。
    /// </remarks>
    private Result<TokenResult> InvalidRefreshToken()
        => Fail<TokenResult>("Invalid or expired refresh token", 400);

    private static TokenResult BuildTokenResult(string accessToken, string refreshToken, JwtOptions jwtOptions) => new()
    {
        AccessToken = accessToken,
        RefreshToken = refreshToken,
        ExpiresAt = DateTime.UtcNow.AddMinutes(jwtOptions.AccessTokenExpirationMinutes),
        ExpiresIn = jwtOptions.AccessTokenExpirationMinutes * 60,
        RefreshTokenExpiresIn = jwtOptions.RefreshTokenExpirationDays * 24 * 60 * 60
    };

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

        // 只登出当前设备：从 access token 的 session_id claim 取当前会话，仅撤销它。
        // 取不到会话（遗留令牌/未启用会话强制）时回退撤销该用户全部会话（旧行为）。
        // ★ 走撤销出口而不是直接调 ISessionService：登出必须连带删掉该会话的刷新令牌，
        // 否则「登出」在关掉 EnforceSessionValidation 的部署上只是清了客户端的状态。
        var currentSessionId = ParseCurrentSessionId();
        if (currentSessionId.HasValue)
        {
            await RevokeSessionAsync(currentSessionId.Value, SessionRevocationReason.Logout);
        }
        else if (_sessionRevocation != null)
        {
            await _sessionRevocation.RevokeUserSessionsAsync(userId, SessionRevocationReason.Logout);
        }
        else if (_sessionService != null)
        {
            await _sessionService.RevokeAllSessionsAsync(userId);
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
            // ★★ 猜错必须有代价，否则这一步就是一台不限次的猜码机：临时令牌活 10 分钟、
            // 验证失败既不烧令牌也不动锁定计数，于是同一枚令牌可以一直试下去。
            // 接到账号锁定上（与密码猜测同一套阈值），锁定之后连令牌一并作废 ——
            // 只锁账号不烧令牌的话，锁定期一过那枚令牌还能接着用。
            await RecordTwoFactorFailureAsync(user, tokenEntry.Id);
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
            // ★ ErrorDetails 必须原样带出：待办挑战的临时令牌就在里面，丢了前端就没法继续。
            return Fail<TokenResult>(sessionResult.Message ?? "Login rejected", sessionResult.Code ?? 403, sessionResult.ErrorCode, sessionResult.ErrorDetails);
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

    /// <summary>
    /// 记一次 2FA 验证失败：累加账号锁定计数，锁定之后连带作废本次挑战的临时令牌。
    /// </summary>
    /// <remarks>
    /// ★★★ <strong>没有这一步，两步验证的第二步是不限次的。</strong>
    /// <see cref="ITwoFactorService"/> 内部有一道按（地址 / 用户）计数的 5 次闸门，但它挡的是
    /// <b>那一枚码</b>；挑战本身此前没有任何代价 —— 失败不烧临时令牌、不动
    /// <c>AccessFailedCount</c>，于是一枚活 10 分钟的令牌可以一直试，
    /// 而知道密码的人随时能再换一枚（登录本身不受这条链路约束）。
    /// <para>
    /// 接到账号锁定上而不是另造一套阈值：猜密码与猜第二因子对账号的威胁是同一件事，
    /// 部署方配的 <c>MaxFailedLoginAttempts</c> 应当同时管住两者。
    /// </para>
    /// <para>
    /// ★ 锁定之后<b>必须</b>把临时令牌一并烧掉。只锁账号的话，锁定期一过那枚令牌
    /// （若还在 10 分钟内）依然可用，等于给攻击者留了一个不受锁定管辖的入口。
    /// </para>
    /// </remarks>
    private async Task RecordTwoFactorFailureAsync(User user, Guid tempTokenId)
    {
        if (!IdentityOptions.AccountSecurity.EnableLockout || !_userManager.SupportsUserLockout)
        {
            return;
        }

        await _userManager.AccessFailedAsync(user);

        if (await _userManager.IsLockedOutAsync(user) && _authTokenService != null)
        {
            await _authTokenService.MarkTokenAsUsedAsync(tempTokenId);
            LogWarning(
                "User {UserId} was locked out during two-factor verification; the challenge token has been consumed.",
                user.Id);
        }
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
    /// <summary>
    /// 建立登录会话；在此之前先把账号欠着的<b>义务</b>拦下来。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★★★ <strong>义务检查放在这里，是因为这是四条签发路径的共同必经点</strong>
    /// （<c>LoginAsync</c> / <c>LoginWithRefreshTokenAsync</c> / <c>IssueTokenAsync</c> /
    /// <c>VerifyTwoFactorAndLoginAsync</c>），而且语义正好对上：
    /// <b>义务没办完就不该有会话</b>。逐条路径各查一遍的写法挡不住第五条路径出现 ——
    /// 这条规律 <see cref="LockedAccountLoginGuard"/> 的注释里已经写过一次。
    /// </para>
    /// <para>
    /// ★★★ <strong>为什么不做成登录守卫。</strong>守卫链跑在 <b>2FA 之前</b>，
    /// 而义务必须在 2FA <b>之后</b>：否则拿到泄露密码的人可以直接进入改密流程、
    /// <b>绕过两步验证</b>把密码改成自己的。顺序在这里是安全属性，不是体验偏好。
    /// </para>
    /// <para>
    /// ★ 也不能放到「建立会话之后」：那时令牌已经签出去了，业务接口已经能访问，
    /// 「强制」二字就没有了。
    /// </para>
    /// </remarks>
    private async Task<Result<Guid>> EstablishLoginSessionAsync(User user)
    {
        var obligations = user.GetOwedObligations();
        if (obligations != PendingUserActions.None)
        {
            return await HandlePendingActionsChallengeAsync<Guid>(user, obligations);
        }

        if (_loginSessionCoordinator == null)
        {
            return Ok(Guid.Empty);
        }

        return await _loginSessionCoordinator.EstablishAsync(user.Id);
    }

    /// <summary>
    /// 发出待办挑战：凭据已经过关，但账号欠着必须先办完的事。
    /// </summary>
    /// <remarks>
    /// 与 <c>Handle2FAChallengeAsync</c> 逐步同构，<b>包括那个独立 DI scope</b>：
    /// 挑战以失败信封返回，而启用了全局工作单元的部署会因此回滚整个请求事务、
    /// 把刚存下的临时令牌一并丢弃 —— 那会让后续的换令牌请求全部失败，
    /// 整条强制改密流程不可用（2026-07-20 在 2FA 上实发过一次）。
    /// </remarks>
    private async Task<Result<T>> HandlePendingActionsChallengeAsync<T>(User user, PendingUserActions obligations)
    {
        var tempToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        await PersistPendingActionTokenAsync(user.Id, tempToken, DateTime.UtcNow.AddMinutes(10));

        LogInformation(
            "User {UserId} passed authentication but owes pending actions: {Actions}.",
            user.Id, obligations);

        return Fail<T>(
            "You must complete a required action before continuing",
            403,
            ErrorCodes.IDENTITY_PENDING_ACTIONS_REQUIRED,
            new { TempToken = tempToken, RequiredActions = obligations.ToActionNames() });
    }

    /// <summary>
    /// 在独立 DI scope 里保存待办挑战的临时令牌，理由同 <c>PersistTwoFactorTempTokenAsync</c>。
    /// </summary>
    private async Task PersistPendingActionTokenAsync(Guid userId, string tempToken, DateTime expiresAt)
    {
        if (ServiceProvider == null)
        {
            if (_authTokenService != null)
            {
                await _authTokenService.SaveTokenAsync(
                    userId, IdentityConstants.LoginProvider.PendingAction, IdentityConstants.TokenName.PendingActionToken, tempToken, expiresAt);
            }
            return;
        }

        using var scope = ServiceProvider.CreateScope();
        var tokenService = scope.ServiceProvider.GetService<IAuthTokenService>();
        if (tokenService != null)
        {
            await tokenService.SaveTokenAsync(
                userId, IdentityConstants.LoginProvider.PendingAction, IdentityConstants.TokenName.PendingActionToken, tempToken, expiresAt);
        }
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

    /// <summary>
    /// 校验一次提交的人机验证。令牌形状由提交方决定（统一控件的 <c>CaptchaToken</c>，或图形验证码的 id + code），
    /// 交给 <see cref="ICaptchaVerifier"/> 按配置的提供商裁决。
    /// </summary>
    /// <remarks>
    /// ★ <b>验证器缺席时拒绝，不放行</b>：走到这里说明流程开关已经要求验证码，「没有人能校验」与「校验通过」
    /// 不是一回事 —— 放行会让一个 DI 缺口把整个验证码开关变成装饰，而响应、日志全部正常。
    /// </remarks>
    private async Task<bool> VerifyCaptchaAsync(ICaptchaSubmission input, string purpose)
    {
        if (_captchaVerifier == null)
        {
            Logger.LogError("Captcha is required for {Purpose} but ICaptchaVerifier is not registered; rejecting.", purpose);
            return false;
        }

        var verification = await _captchaVerifier.VerifyAsync(ImageCaptchaToken.Resolve(input), purpose);
        return verification.Passed;
    }

    /// <summary>
    /// 构建「需要人机验证」失败结果：专用错误码 <see cref="ErrorCodes.IDENTITY_CAPTCHA_REQUIRED"/> +
    /// <see cref="CaptchaDto"/>（带生效的提供商名）。提供商是内置图形验证码时顺带出一道新题
    /// （id + base64 图片）让前端内联渲染；其它提供商只回提供商名，前端按 <c>/auth/config</c> 的客户端配置渲染控件。
    /// 缓存不可用（无法出题）时回退为只带提供商名的同码错误。
    /// </summary>
    private async Task<Result<T>> BuildCaptchaRequiredResultAsync<T>(string purpose)
    {
        var provider = _captchaVerifier?.ProviderName ?? ImageCaptchaProvider.ProviderName;
        var details = new CaptchaDto { Provider = provider };

        if (string.Equals(provider, ImageCaptchaProvider.ProviderName, StringComparison.OrdinalIgnoreCase)
            && _captchaService is { IsCacheAvailable: true })
        {
            var captcha = await _captchaService.GenerateAsync(purpose);
            details.CaptchaId = captcha.CaptchaId;
            details.ImageBase64 = Convert.ToBase64String(captcha.ImageBytes);
            details.ExpirationSeconds = captcha.ExpirationSeconds;
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

        // ★ 登录方式本身被关掉时，在**发码之前**就拒绝：这是端点自己的门，
        // 不能只靠 /auth/config 让前端隐藏入口 —— 隐藏的是入口，端点仍然可达。
        // ★ 这道门排在图形验证码之前：关掉验证码登录的部署里，这个请求注定被拒，
        //   而先校验图形验证码会把用户手里那一张当场消费掉。
        if (!IdentityOptions.SignIn.AllowCodeLogin)
        {
            return Fail<string>("Verification-code sign-in is not enabled", 400);
        }

        // 图形验证码校验（启用登录验证码时，发出短信/邮件之前先过图形验证码）。
        // ★ 与密码登录的「自适应」刻意不同，这里**无条件**要求：发码这条路径没有「失败次数」
        // 可以累计（发码本身不会失败），而它恰恰是唯一一条每次调用都真的产生短信/邮件费用的入口。
        // 少了这道门，开着 EnableCaptchaOnLogin 的部署仍然留着一个无验证码的发信入口。
        var captchaOptions = IdentityOptions.Captcha;
        if (captchaOptions.EnableCaptchaOnLogin)
        {
            var captchaValid = await VerifyCaptchaAsync(input, CaptchaPurpose.Login);
            if (!captchaValid)
            {
                return await BuildCaptchaRequiredResultAsync<string>(CaptchaPurpose.Login);
            }
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

        // ★★★ 账号不存在、而且这一类快速注册也没开着：这枚码拿到 CodeLoginAsync 那边必然
        //   得到 404，所以发出去是**纯支出** —— 短信/邮件费用、发信人信誉，以及一封带着
        //   本部署品牌的验证码邮件落进一个与本站无关的邮箱（现成的钓鱼素材）。
        //   而节流是按地址分桶的，换一个地址就是一个新桶，挡不住这件事。
        // ★ 回同一句话、不报「查无此人」：这个端点匿名可达，区分开就等于交出一个
        //   「这个邮箱/手机号注册过没有」的枚举预言机。同模块的 SendPasswordRecoveryCodeAsync
        //   与 PasswordService.ForgotPasswordAsync 早就是这么做的，只有这一条漏了。
        //   真实原因记服务端日志。
        var canAutoRegister = (input.Type == TwoFactorType.Email && IdentityOptions.Registration.EnableQuickRegisterEmail)
                           || (input.Type == TwoFactorType.Sms && IdentityOptions.Registration.EnableQuickRegisterSms);

        if (userId == null && !canAutoRegister)
        {
            LogInformation(
                "Code-login code requested for an unknown address while quick registration is off; answering uniformly.");
            return Result<string>.Success(CodeLoginCodeSentMessage);
        }

        // 发送验证码
        var result = await _twoFactorService.SendCodeByAddressAsync(address, input.Type, VerificationCodePurpose.CodeLogin, userId);
        if (!result.Succeeded)
        {
            return Fail<string>(result.Message ?? "Failed to send verification code", result.Code ?? 500);
        }

        return Result<string>.Success(CodeLoginCodeSentMessage);
    }

    /// <summary>
    /// 发码端点对外的唯一一句回答。★ 单独抽出来是为了让「发了」与「没发」<b>逐字相同</b> ——
    /// 两处各写一遍字面量，改动其中一处就重新造出一个账号枚举预言机，而且不会有测试变红。
    /// </summary>
    private const string CodeLoginCodeSentMessage = "Verification code sent successfully";

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
    /// 找回密码发码的统一回答：地址存不存在、账号能不能走找回，一律同一句。
    /// </summary>
    private const string RecoveryCodeSentMessage = "If the account exists, a verification code has been sent.";

    /// <summary>
    /// 发送密码找回验证码
    /// </summary>
    public async Task<Result<string>> SendPasswordRecoveryCodeAsync(SendPasswordRecoveryCodeDto input)
    {
        var otpOptions = IdentityOptions.Otp;
        var recoveryOptions = IdentityOptions.Recovery;

        // 人机验证（启用找回密码验证码时，发出短信 / 邮件之前先过）。与验证码登录的发码门同形：无条件要求，
        // 这条路径没有「失败次数」可累计，而每次调用都真的产生费用。此前它不受任何验证码开关管辖。
        if (IdentityOptions.Captcha.EnableCaptchaOnPasswordRecovery)
        {
            var captchaValid = await VerifyCaptchaAsync(input, CaptchaPurpose.PasswordRecovery);
            if (!captchaValid)
            {
                return await BuildCaptchaRequiredResultAsync<string>(CaptchaPurpose.PasswordRecovery);
            }
        }

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
            // ★ 找回密码只认**已验证**的号码。手机号无唯一约束，允许未验证的号码走这条路，
            // 等于让「把自己的号码填成别人的」变成一条重置他人密码的入口。
            user = await _userManager.FindByPhoneNumberAsync(input.PhoneNumber, requireConfirmed: true);
        }

        if (_twoFactorService == null)
        {
            return Fail<string>("Two-factor service is not available", 500);
        }

        // ★★★ 账号不存在时也回同一句话。这是一个匿名端点，区分开就等于交出一个
        // 「这个邮箱/手机号注册过没有」的枚举预言机 —— 而同模块的
        // PasswordService.ForgotPasswordAsync 早就是这么做的（"If email exists, reset link sent."），
        // 只有这一条走验证码的路径漏了。真实原因记服务端日志。
        if (user == null)
        {
            LogInformation("Password recovery requested for an unknown address; answering uniformly.");
            return Result<string>.Success(RecoveryCodeSentMessage);
        }

        // ★ 与 ForgotPasswordAsync 同一道门：还没接受邀请的账号不走找回密码。
        // 那条路径不签发令牌，所以 PendingActionsLoginGuard 管不到它；不挡在这里，
        // 「设置密码」就成了绕开邀请流程的旁路（消费应用要求的表单与二次验证一个都不会跑）。
        if (user.HasPendingAction(PendingUserActions.InvitationPending))
        {
            LogInformation(
                "Password recovery suppressed for user {UserId}: invitation not yet accepted.", user.Id);
            return Result<string>.Success(RecoveryCodeSentMessage);
        }

        // 发送验证码
        var result = await _twoFactorService.SendCodeByAddressAsync(address, input.Type, VerificationCodePurpose.PasswordRecovery, user.Id);
        if (!result.Succeeded)
        {
            // 节流（429）等真实失败照常透出：那是本人也需要知道的信息，
            // 而且它只在账号存在时才可能发生 —— 不透出会让正常用户对着一个没反应的按钮。
            return Fail<string>(result.Message ?? "Failed to send verification code", (int)(result.Code ?? 400));
        }

        return Result<string>.Success(RecoveryCodeSentMessage);
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
                : await _userManager.FindByPhoneNumberAsync(input.PhoneNumber, requireConfirmed: true);
        }

        if (user == null)
        {
            return Fail<string>("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        // ★★★ 经共享出口写入，不要在这里手写一遍。
        // 此前这里自己驱动 UserManager 完成了「校验强度 → 重置」，而 IPasswordService
        // 的另外四条改密路径在那之后还各做了两件事：**查/写密码历史** 与 **撤销全部会话**。
        // 少掉后者的实际后果是：用户中招后被告知去「找回密码」，改完了，
        // 而攻击者手里的 access token 与刷新令牌原样有效 —— 唯一那条补救动作对他毫无影响。
        // 少掉前者则让「不许设回最近用过的密码」这条策略在这一条路径上凭空失效。
        if (_passwordService == null)
        {
            return Fail<string>("Password service is not available", 500);
        }

        var applied = await _passwordService.ForceSetPasswordAsync(user, input.NewPassword);
        if (!applied.Succeeded)
        {
            return Fail<string>(applied.Message ?? "Failed to reset password", applied.Code ?? 400, applied.ErrorCode);
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
