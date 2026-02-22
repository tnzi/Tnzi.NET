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
    private readonly IdentityOptions _identityOptions;
    private readonly IEventBus? _eventBus;
    private readonly ICaptchaService? _captchaService;
    private readonly IAuthTokenService? _authTokenService;
    private readonly IPasswordPolicyService? _passwordPolicyService;
    private readonly ISessionService? _sessionService;
    private readonly ILoginSecurityService? _loginSecurityService;
    private readonly ITwoFactorService? _twoFactorService;

    public AuthService(
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        ITokenService tokenService,
        IOptions<IdentityOptions> identityOptions,
        IServiceProvider serviceProvider,
        IEventBus? eventBus = null,
        ICaptchaService? captchaService = null,
        IAuthTokenService? authTokenService = null,
        IPasswordPolicyService? passwordPolicyService = null,
        ISessionService? sessionService = null,
        ILoginSecurityService? loginSecurityService = null,
        ITwoFactorService? twoFactorService = null)
        : base(serviceProvider)
    {
        _userManager = Check.NotNull(userManager);
        _signInManager = Check.NotNull(signInManager);
        _tokenService = Check.NotNull(tokenService);
        _identityOptions = Check.NotNull(identityOptions).Value;
        _eventBus = eventBus;
        _captchaService = captchaService;
        _authTokenService = authTokenService;
        _passwordPolicyService = passwordPolicyService;
        _sessionService = sessionService;
        _loginSecurityService = loginSecurityService;
        _twoFactorService = twoFactorService;
    }

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

        // 检查是否需要 2FA
        if (await _userManager.GetTwoFactorEnabledAsync(user))
        {
            return await Handle2FAChallengeAsync<string>(user);
        }

        // 生成 Token
        var roles = await _userManager.GetRolesAsync(user);
        var token = _tokenService.GenerateToken(user, roles);

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
        var jwtOptions = _identityOptions.Jwt;

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

        // 检查是否需要 2FA
        if (await _userManager.GetTwoFactorEnabledAsync(user))
        {
            return await Handle2FAChallengeAsync<TokenResult>(user);
        }

        // 生成TokenResult并保存RefreshToken
        var tokenResult = await GenerateAndSaveTokenResultAsync(user, enableRefreshToken: true);

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
        var signInOptions = _identityOptions.SignIn;
        var multiLoginOptions = _identityOptions.MultiLogin;
        var captchaOptions = _identityOptions.Captcha;
        var registrationOptions = _identityOptions.Registration;
        var loginIdentifier = ipAddress ?? input.UserName;

        // 1. 验证码校验
        if (captchaOptions.EnableCaptchaOnLogin && _captchaService != null)
        {
            var captchaRequired = await _captchaService.IsCaptchaRequiredAsync(loginIdentifier);
            if (captchaRequired)
            {
                var captchaValid = await VerifyCaptchaAsync(input.CaptchaId, input.CaptchaCode, "login");
                if (!captchaValid)
                {
                    return Fail<(User, string)>("Invalid or expired captcha", 400);
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
        var signInResult = await _signInManager.CheckPasswordSignInAsync(user, input.Password, _identityOptions.AccountSecurity.EnableLockout);
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

        // 7. 多登录策略检查
        var multiLoginCheck = await CheckMultiLoginPolicyAsync(user.Id, multiLoginOptions);
        if (!multiLoginCheck.Success)
        {
            return Fail<(User, string)>(multiLoginCheck.Message, 403);
        }

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

        // 先标记旧 token 为已使用（带并发控制，防止 token 重放攻击）
        var marked = await _authTokenService.MarkTokenAsUsedAsync(tokenEntry.Id);
        if (!marked)
        {
            return Fail<TokenResult>("Refresh token has already been used", 400);
        }

        // 再生成新 token
        var tokenResult = await GenerateAndSaveTokenResultAsync(user, enableRefreshToken: true);

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

        bool sent = false;
        if (input.Type == TwoFactorType.Sms)
        {
            if (string.IsNullOrWhiteSpace(user.PhoneNumber))
            {
                return Fail<TwoFactorChallengeDto>("Phone number is not set", 400);
            }
            var smsResult = await _twoFactorService.SendSmsCodeAsync(userId, user.PhoneNumber);
            sent = smsResult.Succeeded;
        }
        else if (input.Type == TwoFactorType.Email)
        {
            if (string.IsNullOrWhiteSpace(user.Email))
            {
                return Fail<TwoFactorChallengeDto>("Email is not set", 400);
            }
            var emailResult = await _twoFactorService.SendEmailCodeAsync(userId, user.Email);
            sent = emailResult.Succeeded;
        }
        else
        {
            return Fail<TwoFactorChallengeDto>("Invalid two-factor type", 400);
        }

        if (!sent)
        {
            return Fail<TwoFactorChallengeDto>("Failed to send verification code", 500);
        }

        var supportedTypes = new List<TwoFactorType>();
        if (!string.IsNullOrWhiteSpace(user.PhoneNumber) && user.PhoneNumberConfirmed)
        {
            supportedTypes.Add(TwoFactorType.Sms);
        }
        if (!string.IsNullOrWhiteSpace(user.Email) && user.EmailConfirmed)
        {
            supportedTypes.Add(TwoFactorType.Email);
        }

        return Result<TwoFactorChallengeDto>.Success(new TwoFactorChallengeDto
        {
            RequiresTwoFactor = true,
            SupportedTypes = supportedTypes,
            TempToken = input.TempToken
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

        // 验证2FA验证码
        var isValid = await _twoFactorService.VerifyCodeAsync(userId, input.Code, input.Type);
        if (!isValid.Succeeded)
        {
            await PublishLoginFailedEventAsync(userId, user.UserName, "Invalid 2FA code", ipAddress, userAgent);
            return Fail<TokenResult>("Invalid verification code", 400);
        }

        // 标记临时Token为已使用（核心业务逻辑，必须同步执行）
        await _authTokenService.MarkTokenAsUsedAsync(tokenEntry.Id);

        // 生成TokenResult并保存RefreshToken
        var tokenResult = await GenerateAndSaveTokenResultAsync(user, enableRefreshToken: true);

        // 发布用户登录事件（由事件处理器处理日志记录和会话创建）
        await PublishLoginSuccessEventAsync(user, ipAddress, userAgent, IdentityConstants.LoginProvider.JWT);

        return Result<TokenResult>.Success(tokenResult);
    }

    #region Private Methods

    /// <summary>
    /// 生成TokenResult并保存RefreshToken（如果启用）
    /// 统一处理Token生成、RefreshToken保存逻辑，减少代码重复
    /// </summary>
    private async Task<TokenResult> GenerateAndSaveTokenResultAsync(User user, bool enableRefreshToken = true)
    {
        var roles = await _userManager.GetRolesAsync(user);
        var jwtOptions = _identityOptions.Jwt;

        // 生成AccessToken
        var accessToken = _tokenService.GenerateToken(user, roles);
        var accessTokenExpiresAt = DateTime.UtcNow.AddMinutes(jwtOptions.AccessTokenExpirationMinutes);

        string? refreshToken = null;
        DateTime? refreshTokenExpiresAt = null;

        // 如果启用RefreshToken，生成并保存
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
                    refreshTokenExpiresAt);
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

    private async Task<bool> VerifyCaptchaAsync(string? captchaId, string? captchaCode, string purpose)
    {
        if (_captchaService == null) return true;
        if (string.IsNullOrEmpty(captchaId) || string.IsNullOrEmpty(captchaCode)) return false;
        return await _captchaService.VerifyAsync(captchaId, captchaCode, purpose);
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

    private async Task<(bool Success, string Message)> CheckMultiLoginPolicyAsync(Guid userId, MultiLoginOptions multiLoginOptions)
    {
        if (_sessionService == null) return (true, string.Empty);
        var sessionsResult = await _sessionService.GetUserSessionsAsync(userId);
        var activeSessions = sessionsResult.Succeeded ? sessionsResult.Data!.ToList() : new List<UserSessionDto>();

        if (!multiLoginOptions.AllowMultiLogin)
        {
            if (activeSessions.Any())
            {
                if (multiLoginOptions.OnConflict == LoginConflictPolicy.Reject)
                {
                    return (false, "Already logged in on another device");
                }
                await _sessionService.RevokeAllSessionsAsync(userId);
            }
        }
        else if (multiLoginOptions.MaxConcurrentSessions > 0)
        {
            if (activeSessions.Count >= multiLoginOptions.MaxConcurrentSessions)
            {
                if (multiLoginOptions.OnConflict == LoginConflictPolicy.Reject)
                {
                    return (false, "Maximum concurrent sessions reached");
                }
                var oldest = activeSessions.OrderBy(s => s.LastActivityTime).FirstOrDefault();
                if (oldest == null)
                {
                    Logger.LogWarning("Active sessions count indicates sessions exist, but FirstOrDefault returned null for user {UserId}", userId);
                    return (false, "Unable to revoke session: no active session found");
                }
                await _sessionService.RevokeSessionAsync(oldest.Id);
            }
        }
        return (true, string.Empty);
    }

    private async Task<Result<T>> Handle2FAChallengeAsync<T>(User user)
    {
        // 使用 CSPRNG 生成安全的临时令牌，防止可预测性攻击
        var tempToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        if (_authTokenService != null)
        {
            await _authTokenService.SaveTokenAsync(
                user.Id,
                IdentityConstants.TokenProvider.TwoFactor,
                IdentityConstants.TokenName.TempToken,
                tempToken,
                DateTime.UtcNow.AddMinutes(10));
        }

        var supportedTypes = new List<TwoFactorType>();
        if (!string.IsNullOrWhiteSpace(user.PhoneNumber) && user.PhoneNumberConfirmed)
        {
            supportedTypes.Add(TwoFactorType.Sms);
        }
        if (!string.IsNullOrWhiteSpace(user.Email) && user.EmailConfirmed)
        {
            supportedTypes.Add(TwoFactorType.Email);
        }

        // TOTP 检测（需要 TwoFactorType.Totp 枚举值，由 Phase 3 添加）
        var authenticatorKey = await _userManager.GetAuthenticatorKeyAsync(user);
        if (!string.IsNullOrEmpty(authenticatorKey))
        {
            supportedTypes.Add(TwoFactorType.Totp);
        }

        // Result<T> checks

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
        if (_loginSecurityService == null || !_identityOptions.AccountSecurity.EnableAbnormalLoginDetection) return;
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

        // 检查配置
        var otpOptions = _identityOptions.Otp;
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
        var result = await _twoFactorService.SendCodeByAddressAsync(address, input.Type, userId);
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

        var ipAddress = ScopedContext?.ClientIpAddress;
        var userAgent = ScopedContext?.UserAgent;
        var jwtOptions = _identityOptions.Jwt;
        var registrationOptions = _identityOptions.Registration;

        // 验证输入
        var address = input.Type == TwoFactorType.Email ? input.Email : input.PhoneNumber;
        if (string.IsNullOrWhiteSpace(address))
        {
            return Fail<CodeLoginResultDto>(
                input.Type == TwoFactorType.Email ? "Email is required" : "Phone number is required",
                400);
        }

        // 验证验证码
        var verifyResult = await _twoFactorService.VerifyCodeByAddressAndMarkUsedAsync(address, input.Code, input.Type);
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

        // 检查用户是否需要设置密码
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
                    DateTime.UtcNow.AddMinutes(_identityOptions.Registration.SetPasswordTokenExpirationMinutes));
            }
        }

        // 生成Token和RefreshToken并保存
        var tokenResult = await GenerateAndSaveTokenResultAsync(user, enableRefreshToken: jwtOptions.EnableRefreshToken);

        // 发布登录成功事件
        await PublishLoginSuccessEventAsync(user, ipAddress, userAgent, IdentityConstants.LoginProvider.CodeLogin);
        await CheckAndPublishAbnormalLoginAsync(user, ipAddress, userAgent);

        return Result<CodeLoginResultDto>.Success(new CodeLoginResultDto
        {
            AccessToken = tokenResult.AccessToken,
            RefreshToken = jwtOptions.EnableRefreshToken ? tokenResult.RefreshToken : null,
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
        var registrationOptions = _identityOptions.Registration;

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
            PhoneNumberConfirmed = type == TwoFactorType.Sms
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
        var otpOptions = _identityOptions.Otp;
        var recoveryOptions = _identityOptions.Recovery;

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
        var result = await _twoFactorService.SendCodeByAddressAsync(address, input.Type, user.Id);
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

        // 验证验证码
        var verifyResult = await _twoFactorService.VerifyCodeByAddressAndMarkUsedAsync(address, input.Code, input.Type);
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
