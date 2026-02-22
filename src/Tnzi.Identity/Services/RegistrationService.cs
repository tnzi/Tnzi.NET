namespace Tnzi.Identity.Services;

/// <summary>
/// 用户注册服务实现
/// 提供用户注册、快速注册、邮箱确认等功能
/// </summary>
public class RegistrationService : ApplicationService, IRegistrationService
{
    private readonly UserManager<User> _userManager;
    private readonly IdentityOptions _identityOptions;
    private readonly IEventBus? _eventBus;
    private readonly ICaptchaService? _captchaService;
    private readonly ITwoFactorService? _twoFactorService;
    private readonly IAuthTokenService? _authTokenService;
    private readonly IPasswordPolicyService? _passwordPolicyService;
    private readonly IUserDetailService? _userDetailService;
    private readonly ITokenService? _tokenService;

    public RegistrationService(
        UserManager<User> userManager,
        IOptions<IdentityOptions> identityOptions,
        IServiceProvider serviceProvider,
        IEventBus? eventBus = null,
        ICaptchaService? captchaService = null,
        ITwoFactorService? twoFactorService = null,
        IAuthTokenService? authTokenService = null,
        IPasswordPolicyService? passwordPolicyService = null,
        IUserDetailService? userDetailService = null,
        ITokenService? tokenService = null)
        : base(serviceProvider)
    {
        _userManager = Check.NotNull(userManager);
        _identityOptions = Check.NotNull(identityOptions).Value;
        _eventBus = eventBus;
        _captchaService = captchaService;
        _twoFactorService = twoFactorService;
        _authTokenService = authTokenService;
        _passwordPolicyService = passwordPolicyService;
        _userDetailService = userDetailService;
        _tokenService = tokenService;
    }

    public async Task<Result<TokenResult>> RegisterAsync(RegisterDto input)
    {
        var registrationOptions = _identityOptions.Registration;
        var captchaOptions = _identityOptions.Captcha;
        var jwtOptions = _identityOptions.Jwt;

        // 验证码校验（如果启用）
        if (captchaOptions.EnableCaptchaOnRegister)
        {
            var captchaValid = await VerifyCaptchaAsync(input.CaptchaId, input.CaptchaCode, "register");
            if (!captchaValid)
            {
                return Fail<TokenResult>("Invalid or expired captcha", 400);
            }
        }

        // 验证邮箱必填
        if (string.IsNullOrWhiteSpace(input.Email))
        {
            return Fail<TokenResult>("Email is required", 400);
        }

        // 用户名默认策略：如果没有提供用户名，使用邮箱作为用户名
        var userName = input.UserName;
        if (string.IsNullOrWhiteSpace(userName) && registrationOptions.DefaultUserNameFromEmail)
        {
            userName = input.Email;
        }

        if (string.IsNullOrWhiteSpace(userName))
        {
            return Fail<TokenResult>("Username is required. Either provide a username or enable DefaultUserNameFromEmail option.", 400);
        }

        var user = new User
        {
            UserName = userName,
            Email = input.Email
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

        var roles = await _userManager.GetRolesAsync(user);
        var token = _tokenService.GenerateToken(user, roles);
        var ipAddress = ScopedContext?.ClientIpAddress ?? string.Empty;
        var userAgent = ScopedContext?.UserAgent ?? string.Empty;

        string? refreshToken = null;
        DateTime? refreshTokenExpiresAt = null;

        // 如果启用了RefreshToken，生成并保存
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
                    refreshTokenExpiresAt);
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

    public async Task<Result<string>> SendQuickRegisterCodeAsync(SendQuickRegisterCodeDto input)
    {
        var registrationOptions = _identityOptions.Registration;
        var otpOptions = _identityOptions.Otp;

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
        // 无需额外发布 QuickRegisterCodeSentEvent，Hosting 的 handler 已处理通知发送
        if (_twoFactorService != null)
        {
            var address = isEmailRequest ? input.Email! : input.PhoneNumber!;
            var type = isEmailRequest ? TwoFactorType.Email : TwoFactorType.Sms;
            var sendResult = await _twoFactorService.SendCodeByAddressAsync(address, type, userId: null);
            if (!sendResult.Succeeded)
            {
                return Fail<string>(sendResult.Message ?? "Failed to send verification code", sendResult.Code ?? 500);
            }
        }

        return Result<string>.Success("Verification code sent successfully");
    }

    public async Task<Result<QuickRegisterResultDto>> QuickRegisterAsync(QuickRegisterDto input)
    {
        var registrationOptions = _identityOptions.Registration;

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

        if (string.IsNullOrEmpty(input.Code))
        {
            return Fail<QuickRegisterResultDto>("Verification code is required", 400);
        }

        // 验证验证码
        if (_twoFactorService != null)
        {
            var address = isEmailRequest ? input.Email! : input.PhoneNumber!;
            var type = isEmailRequest ? TwoFactorType.Email : TwoFactorType.Sms;
            var verifyResult = await _twoFactorService.VerifyCodeByAddressAndMarkUsedAsync(address, input.Code, type);
            if (!verifyResult.Succeeded)
            {
                return Fail<QuickRegisterResultDto>(
                    verifyResult.Message ?? "Invalid or expired verification code",
                    verifyResult.Code ?? 400);
            }
        }
        else
        {
            // TwoFactorService 不可用时，无法验证验证码
            LogWarning("TwoFactorService is not available, verification code cannot be validated");
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

        var userName = input.UserName;
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
            PhoneNumberConfirmed = isSmsRequest
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
                DateTime.UtcNow.AddMinutes(_identityOptions.Registration.SetPasswordTokenExpirationMinutes)
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
        var user = await _userManager.FindByGuidAsync(input.UserId);
        if (user == null)
        {
            return Fail<string>("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        // 验证令牌
        if (_authTokenService != null)
        {
            var tokenEntry = await _authTokenService.FindTokenByValueAsync(IdentityConstants.TokenProvider.Identity, IdentityConstants.TokenName.SetPassword, input.Token);
            if (tokenEntry == null || tokenEntry.UserId != input.UserId)
            {
                return Fail<string>("Invalid or expired token", 400);
            }

            if (tokenEntry.ExpiresAt.HasValue && tokenEntry.ExpiresAt.Value < DateTime.UtcNow)
            {
                return Fail<string>("Token has expired", 400);
            }

            // 标记令牌已使用
            await _authTokenService.MarkTokenAsUsedAsync(tokenEntry.Id);
        }

        // 验证密码强度
        if (_passwordPolicyService != null)
        {
            var strengthError = _passwordPolicyService.ValidatePasswordStrength(input.Password);
            if (strengthError != null)
            {
                return Fail<string>(strengthError, 400);
            }
        }

        // 检查用户是否已有密码
        var hasPassword = await _userManager.HasPasswordAsync(user);

        IdentityResult result;
        if (hasPassword)
        {
            // 用户已有密码，使用重置密码流程
            result = await _userManager.ResetPasswordAsync(user, input.Token, input.Password);
        }
        else
        {
            // 用户没有密码（快速注册用户），使用添加密码
            result = await _userManager.AddPasswordAsync(user, input.Password);
        }

        if (!result.Succeeded)
        {
            return Fail<string>(
                result.FormatErrors(), 400);
        }

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

    private async Task<bool> VerifyCaptchaAsync(string? captchaId, string? captchaCode, string purpose)
    {
        if (_captchaService == null) return true;
        if (string.IsNullOrEmpty(captchaId) || string.IsNullOrEmpty(captchaCode)) return false;
        return await _captchaService.VerifyAsync(captchaId, captchaCode, purpose);
    }

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
        var encodedToken = WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(token));

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
            decodedToken = System.Text.Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
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

    /// <inheritdoc />
    public async Task<Result<string>> ResendEmailConfirmationAsync(ResendEmailConfirmationDto input)
    {
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

        return Result<string>.Success("Email confirmation sent");
    }

    #region Private Methods

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
