namespace Tnzi.Identity.Services;

/// <summary>
/// 用户注册服务实现
/// 提供用户注册、快速注册、邮箱确认等功能
/// </summary>
public class RegistrationService : ApplicationService, IRegistrationService
{
    private readonly UserManager<User> _userManager;
    private readonly IOptionsMonitor<IdentityOptions> _identityOptionsMonitor;
    private readonly IEventBus? _eventBus;
    private readonly ICaptchaVerifier? _captchaVerifier;
    private readonly ITwoFactorService? _twoFactorService;
    private readonly IAuthTokenService? _authTokenService;
    private readonly IPasswordService? _passwordService;
    private readonly IUserDetailService? _userDetailService;
    private readonly ITokenService? _tokenService;
    private readonly ILoginSessionCoordinator? _loginSessionCoordinator;
    private readonly ILoginGuardEvaluator? _loginGuardEvaluator;
    private readonly ICurrentTenant? _currentTenant;
    private readonly bool _multiTenancyEnabled;

    private IdentityOptions IdentityOptions => _identityOptionsMonitor.CurrentValue;

    public RegistrationService(
        UserManager<User> userManager,
        IOptionsMonitor<IdentityOptions> identityOptions,
        IServiceProvider serviceProvider,
        IEventBus? eventBus = null,
        ITwoFactorService? twoFactorService = null,
        IAuthTokenService? authTokenService = null,
        IUserDetailService? userDetailService = null,
        ITokenService? tokenService = null,
        ICurrentTenant? currentTenant = null,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null,
        ILoginSessionCoordinator? loginSessionCoordinator = null,
        ILoginGuardEvaluator? loginGuardEvaluator = null,
        IPasswordService? passwordService = null,
        ICaptchaVerifier? captchaVerifier = null)
        : base(serviceProvider)
    {
        _userManager = Check.NotNull(userManager);
        _identityOptionsMonitor = Check.NotNull(identityOptions);
        _eventBus = eventBus;
        _captchaVerifier = captchaVerifier;
        _twoFactorService = twoFactorService;
        _authTokenService = authTokenService;
        _passwordService = passwordService;
        _userDetailService = userDetailService;
        _tokenService = tokenService;
        _loginSessionCoordinator = loginSessionCoordinator;
        _loginGuardEvaluator = loginGuardEvaluator;
        _currentTenant = currentTenant;
        _multiTenancyEnabled = multiTenancyOptions?.Value.Enabled ?? false;
    }

    public async Task<Result<TokenResult>> RegisterAsync(RegisterDto input)
    {
        var registrationOptions = IdentityOptions.Registration;
        var captchaOptions = IdentityOptions.Captcha;
        var jwtOptions = IdentityOptions.Jwt;

        // ★★★ 端点自己的门，第一句就判。前端按 /auth/config 隐藏注册入口只是体验，
        // 而这个端点匿名可达 —— 不在这里挡住，「关掉注册」就只是把按钮藏起来。
        if (!registrationOptions.EnableSelfRegistration)
        {
            return Fail<TokenResult>("Self-registration is not enabled", 400);
        }

        // 人机验证（如果启用）。错误码与发码端点同一个，前端据此重置控件。
        if (captchaOptions.EnableCaptchaOnRegister)
        {
            var captchaValid = await VerifyCaptchaAsync(input, CaptchaPurpose.Register);
            if (!captchaValid)
            {
                return Fail<TokenResult>("Captcha verification is required", 400, ErrorCodes.IDENTITY_CAPTCHA_REQUIRED, CaptchaChallenge());
            }
        }

        // 验证邮箱必填
        if (string.IsNullOrWhiteSpace(input.Email))
        {
            return Fail<TokenResult>("Email is required", 400);
        }

        // 用户名：UseEmailAsUserName 开启（默认）时就是邮箱；关闭时取用户给的，
        // 没给且 DefaultUserNameFromEmail 开启则退回邮箱（此时两者同样绑在一起，改邮箱会跟随）
        var resolvedUserName = UserNamePolicy.ResolveForNewAccount(input.UserName, input.Email, IdentityOptions.SignIn.UseEmailAsUserName);
        if (!resolvedUserName.Succeeded)
        {
            return Fail<TokenResult>(resolvedUserName.Message!, resolvedUserName.Code ?? 400, resolvedUserName.ErrorCode);
        }

        var userName = resolvedUserName.Data;
        if (string.IsNullOrWhiteSpace(userName) && registrationOptions.DefaultUserNameFromEmail)
        {
            userName = input.Email;
        }

        if (string.IsNullOrWhiteSpace(userName))
        {
            return Fail<TokenResult>("Username is required.", 400, ErrorCodes.VALIDATION_ERROR);
        }

        var user = new User
        {
            UserName = userName,
            Email = input.Email,
            TenantId = ResolveNewUserTenantId()
        };

        var result = await _userManager.CreateAsync(user, input.Password);
        if (!result.Succeeded)
        {
            return Fail<TokenResult>(result.FormatErrors(), 400);
        }

        // 发布用户注册事件
        await PublishUserRegisteredEventAsync(user);

        // 创建用户详情（如果提供了姓名）
        if (_userDetailService != null && (!string.IsNullOrEmpty(input.FirstName) || !string.IsNullOrEmpty(input.LastName)))
        {
            await _userDetailService.CreateOrUpdateAsync(user.Id, new CreateUserDetailDto
            {
                FirstName = input.FirstName,
                LastName = input.LastName
            });
        }

        // 如果需要邮箱确认，不返回 Token，让用户先确认邮箱
        if (registrationOptions.RequireConfirmedEmail)
        {
            return Result<TokenResult>.Success(new TokenResult
            {
                RequireEmailConfirmation = true,
                Email = user.Email,
                UserId = user.Id
            });
        }

        // 注册成功后自动登录并生成Token
        if (_tokenService == null)
        {
            return Fail<TokenResult>("Token service is not available", 500);
        }

        // 凭据之外的准入策略。注册后自动登录同样要过，否则「先注册一个号」
        // 就成了绕开 IP 白名单 / 时段限制的旁路。被拒时账号已建好，只是这一次
        // 不签发令牌——用户改从允许的位置正常登录即可。
        if (_loginGuardEvaluator is { HasGuards: true })
        {
            var guardResult = await _loginGuardEvaluator.EvaluateAsync(new LoginGuardContext(
                user, LoginMethod.Registration, ScopedContext?.ClientIpAddress, ScopedContext?.UserAgent));
            if (!guardResult.Allowed)
            {
                return Fail<TokenResult>(guardResult.Message!, guardResult.Code, guardResult.ErrorCode);
            }
        }

        // 注册后自动登录：建立登录会话（首登录无既有会话，策略平凡通过）。
        // ★★ 失败必须当场返回，不能当作「那就不带会话吧」继续。没有会话就没有
        //    session_id claim，而每请求的会话强制校验是按这个 claim 触发的 ——
        //    于是这条路签出来的令牌不受任何会话约束：踢不掉、「登出全部设备」对它无效、
        //    多设备策略也管不着它，而且外观与一次正常注册完全相同。
        //    AuthService 的四个签发出口在这一步失败时一律 return Fail，这里跟它们一致。
        //    协调器没注册（纯单元测试 / 精简部署）是另一回事：那时全局没有会话机制，
        //    退回无 session_id 的旧行为是一致的；这里区分的是「有机制但这一次没建起来」。
        var sessionId = Guid.Empty;
        if (_loginSessionCoordinator != null)
        {
            var sessionResult = await _loginSessionCoordinator.EstablishAsync(user.Id);
            if (!sessionResult.Succeeded)
            {
                return Fail<TokenResult>(
                    sessionResult.Message ?? "Login rejected",
                    sessionResult.Code ?? 403,
                    sessionResult.ErrorCode,
                    sessionResult.ErrorDetails);
            }

            sessionId = sessionResult.Data;
        }

        var roles = await GetRolesWithTenantContextAsync(user);
        var token = _tokenService.GenerateToken(user, roles, sessionId: sessionId == Guid.Empty ? null : sessionId);
        var ipAddress = ScopedContext?.ClientIpAddress ?? string.Empty;
        var userAgent = ScopedContext?.UserAgent ?? string.Empty;

        string? refreshToken = null;
        DateTime? refreshTokenExpiresAt = null;

        // 如果启用了RefreshToken，生成并保存（按会话绑定）
        if (jwtOptions.EnableRefreshToken)
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

        // 发布登录成功事件
        if (_eventBus != null)
        {
            await _eventBus.PublishAsync(new UserLoggedInEvent
            {
                UserId = user.Id,
                UserName = user.UserName ?? string.Empty,
                LoginTime = DateTime.UtcNow,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                LoginProvider = IdentityConstants.LoginProvider.Registration
            }, cancellationToken: default);
        }

        // AccessToken的过期时间
        var accessTokenExpiresAt = DateTime.UtcNow.AddMinutes(jwtOptions.AccessTokenExpirationMinutes);

        var tokenResult = new TokenResult
        {
            AccessToken = token,
            RefreshToken = refreshToken ?? string.Empty,
            ExpiresIn = jwtOptions.AccessTokenExpirationMinutes * 60,
            RefreshTokenExpiresIn = jwtOptions.EnableRefreshToken ? jwtOptions.RefreshTokenExpirationDays * 24 * 60 * 60 : null,
            ExpiresAt = accessTokenExpiresAt
        };

        return Result<TokenResult>.Success(tokenResult);
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

    public async Task<Result<string>> SendQuickRegisterCodeAsync(SendQuickRegisterCodeDto input)
    {
        var registrationOptions = IdentityOptions.Registration;
        var captchaOptions = IdentityOptions.Captcha;
        var otpOptions = IdentityOptions.Otp;

        // 人机验证(启用注册验证码时,发送短信/邮箱验证码前必须先过,
        // 防机器人刷发码接口造成短信/邮件费用;web admin 的注册走此快速注册流)。
        if (captchaOptions.EnableCaptchaOnRegister)
        {
            var captchaValid = await VerifyCaptchaAsync(input, CaptchaPurpose.Register);
            if (!captchaValid)
            {
                return Fail<string>("Captcha verification is required", 400, ErrorCodes.IDENTITY_CAPTCHA_REQUIRED, CaptchaChallenge());
            }
        }

        // 检查快速注册是否启用
        var isEmailRequest = !string.IsNullOrWhiteSpace(input.Email);
        var isSmsRequest = !string.IsNullOrWhiteSpace(input.PhoneNumber);

        if (isEmailRequest && !registrationOptions.EnableQuickRegisterEmail)
        {
            return Fail<string>("Quick registration by email is not enabled", 400);
        }

        if (isSmsRequest && !registrationOptions.EnableQuickRegisterSms)
        {
            return Fail<string>("Quick registration by SMS is not enabled", 400);
        }

        if (!isEmailRequest && !isSmsRequest)
        {
            return Fail<string>("Email or phone number is required", 400);
        }

        // 检查用户是否已存在
        if (isEmailRequest)
        {
            var existingUser = await _userManager.FindByEmailAsync(input.Email!);
            if (existingUser != null)
            {
                return Fail<string>("Email is already registered", 400);
            }
        }

        if (isSmsRequest)
        {
            var existingUser = await _userManager.FindByPhoneNumberAsync(input.PhoneNumber);
            if (existingUser != null)
            {
                return Fail<string>("Phone number is already registered", 400);
            }
        }

        // 使用 TwoFactorService 存储并发送验证码（内部已发布 TwoFactorCodeSentEvent）
        // 无需额外发布 QuickRegisterCodeSentEvent，Hosting 的 handler 已处理通知发送。
        // 服务缺失时必须报错而不是返回"已发送"：否则调用方以为收到了码，
        // 而 QuickRegisterAsync 也无从校验（见该方法的 fail-closed 分支）。
        if (_twoFactorService == null)
        {
            return Fail<string>("Verification code service is not available", 503, ErrorCodes.CONFIGURATION_ERROR);
        }

        var address = isEmailRequest ? input.Email! : input.PhoneNumber!;
        var type = isEmailRequest ? TwoFactorType.Email : TwoFactorType.Sms;
        var sendResult = await _twoFactorService.SendCodeByAddressAsync(
            address, type, VerificationCodePurpose.Registration, userId: null);
        if (!sendResult.Succeeded)
        {
            return Fail<string>(sendResult.Message ?? "Failed to send verification code", sendResult.Code ?? 500);
        }

        return Result<string>.Success("Verification code sent successfully");
    }

    public async Task<Result<QuickRegisterResultDto>> QuickRegisterAsync(QuickRegisterDto input)
    {
        var registrationOptions = IdentityOptions.Registration;

        // 检查快速注册是否启用
        var isEmailRequest = !string.IsNullOrWhiteSpace(input.Email);
        var isSmsRequest = !string.IsNullOrWhiteSpace(input.PhoneNumber);

        if (isEmailRequest && !registrationOptions.EnableQuickRegisterEmail)
        {
            return Fail<QuickRegisterResultDto>("Quick registration by email is not enabled", 400);
        }

        if (isSmsRequest && !registrationOptions.EnableQuickRegisterSms)
        {
            return Fail<QuickRegisterResultDto>("Quick registration by SMS is not enabled", 400);
        }

        if (!isEmailRequest && !isSmsRequest)
        {
            return Fail<QuickRegisterResultDto>("Email or phone number is required", 400);
        }

        // 用户名规则在核销验证码之前判：被拒时那枚码还能用，不必重新收一次
        var resolvedUserName = UserNamePolicy.ResolveForNewAccount(input.UserName, input.Email, IdentityOptions.SignIn.UseEmailAsUserName);
        if (!resolvedUserName.Succeeded)
        {
            return Fail<QuickRegisterResultDto>(resolvedUserName.Message!, resolvedUserName.Code ?? 400, resolvedUserName.ErrorCode);
        }

        if (string.IsNullOrEmpty(input.Code))
        {
            return Fail<QuickRegisterResultDto>("Verification code is required", 400);
        }

        // 验证验证码。服务缺失时 fail-closed：无法校验就绝不建账号，
        // 否则任何人都能用任意邮箱/手机号注册出 EmailConfirmed=true 的账号。
        if (_twoFactorService == null)
        {
            LogError("TwoFactorService is not available; quick registration is refused because the verification code cannot be validated.");
            return Fail<QuickRegisterResultDto>("Verification code service is not available", 503, ErrorCodes.CONFIGURATION_ERROR);
        }

        var address = isEmailRequest ? input.Email! : input.PhoneNumber!;
        var type = isEmailRequest ? TwoFactorType.Email : TwoFactorType.Sms;
        var verifyResult = await _twoFactorService.VerifyCodeByAddressAndMarkUsedAsync(
            address, input.Code, type, VerificationCodePurpose.Registration);
        if (!verifyResult.Succeeded)
        {
            return Fail<QuickRegisterResultDto>(
                verifyResult.Message ?? "Invalid or expired verification code",
                verifyResult.Code ?? 400);
        }

        // 检查用户是否已存在
        if (isEmailRequest)
        {
            var existingUser = await _userManager.FindByEmailAsync(input.Email!);
            if (existingUser != null)
            {
                return Fail<QuickRegisterResultDto>("Email is already registered", 400);
            }
        }

        if (isSmsRequest)
        {
            var existingUser = await _userManager.FindByPhoneNumberAsync(input.PhoneNumber);
            if (existingUser != null)
            {
                return Fail<QuickRegisterResultDto>("Phone number is already registered", 400);
            }
        }

        var userName = resolvedUserName.Data;
        if (string.IsNullOrEmpty(userName))
        {
            userName = isEmailRequest ? input.Email : input.PhoneNumber;
        }

        var user = new User
        {
            UserName = userName,
            Email = input.Email,
            PhoneNumber = input.PhoneNumber,
            EmailConfirmed = isEmailRequest,
            PhoneNumberConfirmed = isSmsRequest,
            TenantId = ResolveNewUserTenantId()
        };

        var result = await _userManager.CreateAsync(user);
        if (!result.Succeeded)
        {
            return Fail<QuickRegisterResultDto>(
                result.FormatErrors(), 400);
        }

        // 生成设置密码的Token
        var setPasswordToken = await _userManager.GeneratePasswordResetTokenAsync(user);

        // 存储Token，以便在设置密码时验证
        if (_authTokenService != null)
        {
            await _authTokenService.SaveTokenAsync(
                user.Id,
                IdentityConstants.TokenProvider.Identity,
                IdentityConstants.TokenName.SetPassword,
                setPasswordToken,
                DateTime.UtcNow.AddMinutes(IdentityOptions.Registration.SetPasswordTokenExpirationMinutes)
            );
        }

        // 发布用户注册事件
        await PublishUserRegisteredEventAsync(user);

        // 创建用户详情（如果提供了姓名）
        if (_userDetailService != null && (!string.IsNullOrEmpty(input.FirstName) || !string.IsNullOrEmpty(input.LastName)))
        {
            await _userDetailService.CreateOrUpdateAsync(user.Id, new CreateUserDetailDto
            {
                FirstName = input.FirstName,
                LastName = input.LastName
            });
        }

        // 发布快速注册完成事件
        if (_eventBus != null)
        {
            await _eventBus.PublishAsync(new QuickRegisterCompletedEvent
            {
                UserId = user.Id,
                UserName = user.UserName ?? string.Empty,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                RegistrationTime = DateTime.UtcNow,
                RequirePasswordSetup = true
            }, cancellationToken: default);
        }

        return Result<QuickRegisterResultDto>.Success(new QuickRegisterResultDto
        {
            UserId = user.Id,
            UserName = user.UserName ?? string.Empty,
            RequirePasswordSetup = true,
            SetPasswordToken = setPasswordToken
        });
    }

    public async Task<Result<string>> SetPasswordAsync(SetPasswordDto input)
    {
        Check.NotNull(input);

        var user = await _userManager.FindByGuidAsync(input.UserId);
        if (user == null)
        {
            return Fail<string>("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        // ★★ 令牌校验不再是「有服务才做」。这个端点匿名可达，而校验是它**唯一**的身份证明：
        //    服务缺席时放行等于任何人拿一个 userId 就能给别人设密码。
        //    此前有密码的账号还有 ResetPasswordAsync 里那道 Identity 令牌校验兜着，
        //    而没有密码的账号（正是这个端点的主要对象）走 AddPasswordAsync，一道门都没有。
        if (_authTokenService == null)
        {
            return Fail<string>("Token service is not available", 500);
        }

        var tokenEntry = await _authTokenService.FindTokenByValueAsync(IdentityConstants.TokenProvider.Identity, IdentityConstants.TokenName.SetPassword, input.Token);
        if (tokenEntry == null || tokenEntry.UserId != input.UserId)
        {
            return Fail<string>("Invalid or expired token", 400);
        }

        if (tokenEntry.ExpiresAt.HasValue && tokenEntry.ExpiresAt.Value < DateTime.UtcNow)
        {
            return Fail<string>("Token has expired", 400);
        }

        if (_passwordService == null)
        {
            return Fail<string>("Password service is not available", 500);
        }

        // 此前没有密码 = 快速注册的账号在设第一个密码。没有旧凭据要作废，
        // 而唯一在线的会话正是本人刚凭验证码换来的那一条，撤掉它等于设完密码当场被登出。
        var hasPassword = await _userManager.HasPasswordAsync(user);

        // ★★★ 走共享出口。此前这里是第六条改密路径，手写了「校验强度 + 写密码」，
        //   漏掉密码历史查重、历史写入与会话撤销 —— 与 09-01 修掉的第七条
        //   （ResetPasswordByCodeAsync）逐字同形：设回上一个密码会被接受，
        //   而账号失陷后按提示改了密码，攻击者手里的令牌原样有效。
        var set = await _passwordService.ForceSetPasswordAsync(user, input.Password, revokeExistingSessions: hasPassword);
        if (!set.Succeeded)
        {
            return Fail<string>(set.Message ?? "Failed to set password", set.Code ?? 400, set.ErrorCode);
        }

        // ★ 令牌在密码真的设上之后才消费。此前是先烧后设，于是一个不合强度要求的密码
        //   会把令牌一起赔掉，用户拿不回第二次机会。
        await _authTokenService.MarkTokenAsUsedAsync(tokenEntry.Id);

        // 密码设置成功，发布事件
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

        return Result<string>.Success("Password set successfully");
    }

    /// <summary>
    /// 校验一次提交的人机验证（同 <c>AuthService.VerifyCaptchaAsync</c>）。
    /// ★ 验证器缺席时拒绝：流程开关已经要求验证码，「没人能校验」不等于「校验通过」。
    /// </summary>
    private async Task<bool> VerifyCaptchaAsync(ICaptchaSubmission input, string purpose)
    {
        if (_captchaVerifier == null)
        {
            Logger.LogError("Captcha is required for {Purpose} but ICaptchaVerifier is not registered; rejecting.", purpose);
            return false;
        }

        var verification = await _captchaVerifier.VerifyAsync(ImageCaptchaToken.Resolve(input), purpose);
        // ★ 「未启用，放行」不是「校验通过」：走到这里说明流程开关已经要求验证码，而验证器报告没有生效的提供商。
        //   放行会让「开了验证码」变成装饰，响应、日志全部正常 —— 与验证器缺席同一条原则。
        if (verification.Skipped)
        {
            Logger.LogError("Captcha is required for {Purpose} but no captcha provider is enabled; rejecting.", purpose);
            return false;
        }

        return verification.Passed;
    }

    /// <summary>
    /// 拒绝时随响应带上生效的提供商名，前端据此决定刷新图形验证码还是重置第三方控件。
    /// 注册这几条路径都是「常显」控件（每次发码都要新解一次），所以不像登录那样顺带出一道新题。
    /// </summary>
    private CaptchaDto CaptchaChallenge()
        => new() { Provider = _captchaVerifier?.ProviderName ?? ImageCaptchaProvider.ProviderName };

    /// <inheritdoc />
    public async Task<Result<string>> GenerateEmailConfirmationTokenAsync(Guid userId)
    {
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return Fail<string>("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            return Fail<string>("User has no email address", 400, ErrorCodes.IDENTITY_EMAIL_NOT_SET);
        }

        if (user.EmailConfirmed)
        {
            return Fail<string>("Email is already confirmed", 400, ErrorCodes.IDENTITY_EMAIL_ALREADY_CONFIRMED);
        }

        // 使用 ASP.NET Core Identity 的令牌生成器
        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);

        // 将令牌编码为 URL 安全的 Base64
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

        return Result<string>.Success(encodedToken);
    }

    /// <inheritdoc />
    public async Task<Result> ConfirmEmailAsync(Guid userId, string token)
    {
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        if (user.EmailConfirmed)
        {
            return Fail("Email is already confirmed", 400, ErrorCodes.IDENTITY_EMAIL_ALREADY_CONFIRMED);
        }

        // 解码令牌
        string decodedToken;
        try
        {
            decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
        }
        catch
        {
            return Fail("Invalid token format", 400, ErrorCodes.IDENTITY_TOKEN_INVALID);
        }

        // 验证令牌并确认邮箱
        var result = await _userManager.ConfirmEmailAsync(user, decodedToken);
        if (!result.Succeeded)
        {
            return Fail(result.FormatErrors(), 400, ErrorCodes.IDENTITY_TOKEN_INVALID);
        }

        // 发布邮箱确认事件
        if (_eventBus != null)
        {
            await _eventBus.PublishAsync(new UserEmailConfirmedEvent
            {
                UserId = user.Id,
                Email = user.Email!,
                ConfirmationTime = DateTime.UtcNow
            }, cancellationToken: default);
        }

        return Result.Success();
    }

    /// <summary>邮箱确认重发的统一回答：账号存不存在、有没有邮箱、确认没确认，一律同一句。</summary>
    private const string ConfirmationResentMessage =
        "If the account exists and needs confirmation, an email has been sent.";

    /// <inheritdoc />
    public async Task<Result<string>> ResendEmailConfirmationAsync(ResendEmailConfirmationDto input)
    {
        // 人机验证：与注册发码同一个开关。重发确认邮件每次调用都真的发一封信，此前它不受任何验证码开关管辖。
        // 放在查用户之前：注定被拒的请求不该先消耗一次用户查询，更不该让「有没有这个账号」影响回答的时机。
        if (IdentityOptions.Captcha.EnableCaptchaOnRegister)
        {
            var captchaValid = await VerifyCaptchaAsync(input, CaptchaPurpose.Register);
            if (!captchaValid)
            {
                return Fail<string>("Captcha verification is required", 400, ErrorCodes.IDENTITY_CAPTCHA_REQUIRED, CaptchaChallenge());
            }
        }

        // 查找用户
        User? user = null;

        if (input.UserId.HasValue)
        {
            user = await _userManager.FindByGuidAsync(input.UserId.Value);
        }
        else if (!string.IsNullOrWhiteSpace(input.Email))
        {
            user = await _userManager.FindByEmailAsync(input.Email);
        }

        // ★★★ 三种结果一律同一句话。这是一个匿名端点，此前它会分别回答
        // 「查无此人」(404) /「已确认」(400) /「已发送」(200) —— 于是任何人都能拿它
        // 枚举出哪些邮箱注册过、并且顺带读出每一个的确认状态。
        // 同模块的 PasswordService.ForgotPasswordAsync 早就是统一回答，只有这一条漏了。
        // 真实分支记服务端日志，运维照样查得到。
        if (user == null)
        {
            LogInformation("Email confirmation resend requested for an unknown account; answering uniformly.");
            return Result<string>.Success(ConfirmationResentMessage);
        }

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            LogInformation("Email confirmation resend skipped for user {UserId}: no email address.", user.Id);
            return Result<string>.Success(ConfirmationResentMessage);
        }

        if (user.EmailConfirmed)
        {
            LogInformation("Email confirmation resend skipped for user {UserId}: already confirmed.", user.Id);
            return Result<string>.Success(ConfirmationResentMessage);
        }

        // 发布邮箱确认邮件重发事件（触发发送确认邮件，其中包含确认链接）
        if (_eventBus != null)
        {
            await _eventBus.PublishAsync(new EmailConfirmationResentEvent
            {
                UserId = user.Id,
                UserName = user.UserName ?? string.Empty,
                Email = user.Email,
                ResentTime = DateTime.UtcNow
            }, cancellationToken: default);
        }

        return Result<string>.Success(ConfirmationResentMessage);
    }

    #region Private Methods

    private Guid? ResolveNewUserTenantId()
    {
        if (!_multiTenancyEnabled)
        {
            return null;
        }

        return _currentTenant?.Id ?? CurrentUser?.TenantId;
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
}
