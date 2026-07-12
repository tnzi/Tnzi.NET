namespace Tnzi.Identity.Services;

/// <summary>
/// 双因素认证服务实现
/// </summary>
public class TwoFactorService : ApplicationService, ITwoFactorService
{
    private static readonly TimeSpan TwoFactorFailureCacheExpiration = TimeSpan.FromMinutes(15);
    private const int MaxTwoFactorFailureAttempts = 5;

    private readonly IRepository<TwoFactorCode, Guid> _repository;
    private readonly UserManager<User> _userManager;
    private readonly IEventBus? _eventBus;
    private readonly OtpOptions _otpOptions;
    private readonly ICache? _cache;

    public TwoFactorService(
        IRepository<TwoFactorCode, Guid> repository,
        UserManager<User> userManager,
        IServiceProvider serviceProvider,
        IEventBus? eventBus = null,
        IOptionsSnapshot<IdentityOptions>? identityOptions = null,
        ICache? cache = null)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _userManager = Check.NotNull(userManager);
        _eventBus = eventBus;
        // Scoped 服务：IOptionsSnapshot 每请求重算，构造期捕获 Otp 即随请求热更新。
        _otpOptions = identityOptions?.Value.Otp ?? new OtpOptions();
        _cache = cache;
    }

    public async Task<Result> SendSmsCodeAsync(Guid userId, string phoneNumber)
    {
        // 验证用户存在
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        // 委托给基于地址的通用方法
        return await SendCodeByAddressAsync(phoneNumber, TwoFactorType.Sms, userId);
    }

    public async Task<Result> SendEmailCodeAsync(Guid userId, string email)
    {
        // 验证用户存在
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        // 委托给基于地址的通用方法
        return await SendCodeByAddressAsync(email, TwoFactorType.Email, userId);
    }

    public async Task<Result> VerifyCodeAsync(Guid userId, string code, TwoFactorType type)
    {
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        // TOTP 验证：直接走 UserManager 内置验证，不查数据库
        if (type == TwoFactorType.Totp)
        {
            var isValid = await _userManager.VerifyTwoFactorTokenAsync(
                user, _userManager.Options.Tokens.AuthenticatorTokenProvider, code);
            return isValid ? Ok() : Fail("Invalid TOTP code", 400, ErrorCodes.VALIDATION_ERROR);
        }

        // SMS/Email 验证：查数据库
        var address = type == TwoFactorType.Email ? user.Email : user.PhoneNumber;
        if (string.IsNullOrWhiteSpace(address))
        {
            return Fail($"{type} address is not set for user", 400, ErrorCodes.VALIDATION_ERROR);
        }

        // 委托给基于地址的验证并标记已使用方法
        var result = await VerifyCodeByAddressAndMarkUsedAsync(address, code, type);
        if (!result.Succeeded)
        {
            return Fail(result.Message ?? "Verification failed", result.Code ?? 400, result.ErrorCode);
        }

        return Ok();
    }

    public async Task<Result> DisableTwoFactorAsync(Guid userId)
    {
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        await _userManager.SetTwoFactorEnabledAsync(user, false);

        // 清理未使用的验证码
        var unusedCodes = await _repository
            .Where(tfc => tfc.UserId == userId && !tfc.IsUsed)
            .ToListAsync();

        if (unusedCodes.Any())
        {
            await _repository.DeleteManyAsync(unusedCodes);
        }

        LogInformation("2FA disabled for user {UserId}", userId);
        return Ok();
    }

    public async Task<Result<TwoFactorStatusDto>> GetTwoFactorStatusAsync(Guid userId)
    {
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return Fail<TwoFactorStatusDto>("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        var isEnabled = await _userManager.GetTwoFactorEnabledAsync(user);
        var supportedTypes = new List<TwoFactorType>();

        // 检查 SMS 支持：需要启用 SMS 且手机号已确认
        if (_otpOptions.EnableSms && !string.IsNullOrWhiteSpace(user.PhoneNumber) && user.PhoneNumberConfirmed)
        {
            supportedTypes.Add(TwoFactorType.Sms);
        }

        // 检查 Email 支持：需要启用 Email 且邮箱已确认
        if (_otpOptions.EnableEmail && !string.IsNullOrWhiteSpace(user.Email) && user.EmailConfirmed)
        {
            supportedTypes.Add(TwoFactorType.Email);
        }

        // 检查 TOTP 支持
        var authenticatorKey = await _userManager.GetAuthenticatorKeyAsync(user);
        var isTotpConfigured = !string.IsNullOrEmpty(authenticatorKey);
        if (isTotpConfigured)
        {
            supportedTypes.Add(TwoFactorType.Totp);
        }

        var status = new TwoFactorStatusDto
        {
            IsEnabled = isEnabled,
            SupportedTypes = supportedTypes,
            IsTotpEnabled = isTotpConfigured && isEnabled
        };

        return Ok(status);
    }

    public async Task<Result<string>> EnableTwoFactorAsync(Guid userId, EnableTwoFactorDto input)
    {
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return Fail<string>("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        // 验证地址是否有效
        if (input.Type == TwoFactorType.Sms)
        {
            if (string.IsNullOrWhiteSpace(user.PhoneNumber) || !user.PhoneNumberConfirmed)
            {
                return Fail<string>("Phone number is not verified", 400, ErrorCodes.VALIDATION_ERROR);
            }
        }
        else if (input.Type == TwoFactorType.Email)
        {
            if (string.IsNullOrWhiteSpace(user.Email) || !user.EmailConfirmed)
            {
                return Fail<string>("Email is not verified", 400, ErrorCodes.VALIDATION_ERROR);
            }
        }
        else
        {
            return Fail<string>("Invalid two-factor type", 400, ErrorCodes.VALIDATION_ERROR);
        }

        await _userManager.SetTwoFactorEnabledAsync(user, true);
        LogInformation("2FA enabled for user {UserId}, type: {TwoFactorType}", userId, input.Type);
        return Ok<string>("Two-factor authentication enabled successfully");
    }

    public async Task<Result<TotpSetupDto>> GetTotpSetupInfoAsync(Guid userId)
    {
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
            return Fail<TotpSetupDto>("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);

        // 重置并获取 authenticator key
        await _userManager.ResetAuthenticatorKeyAsync(user);
        var unformattedKey = await _userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(unformattedKey))
            return Fail<TotpSetupDto>("Failed to generate authenticator key", 500);

        // 生成 otpauth URI
        var email = await _userManager.GetEmailAsync(user);
        var authenticatorUri = GenerateQrCodeUri(email ?? user.UserName ?? "user", unformattedKey);

        return Ok(new TotpSetupDto
        {
            SharedKey = FormatKey(unformattedKey),
            AuthenticatorUri = authenticatorUri
        });
    }

    public async Task<Result> EnableTotpAsync(Guid userId, string verificationCode)
    {
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);

        // 验证 TOTP 代码
        var isValid = await _userManager.VerifyTwoFactorTokenAsync(
            user, _userManager.Options.Tokens.AuthenticatorTokenProvider, verificationCode);
        if (!isValid)
            return Fail("Invalid verification code", 400, ErrorCodes.VALIDATION_ERROR);

        // 启用 2FA
        await _userManager.SetTwoFactorEnabledAsync(user, true);

        LogInformation("TOTP enabled for user {UserId}", userId);
        return Ok();
    }

    public async Task<Result> DisableTotpAsync(Guid userId)
    {
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);

        // 重置 authenticator key（使已配置的 TOTP 失效）
        await _userManager.ResetAuthenticatorKeyAsync(user);

        // 如果没有其他 2FA 方式，禁用 2FA
        var hasOtherMethods = (!string.IsNullOrWhiteSpace(user.PhoneNumber) && user.PhoneNumberConfirmed)
                           || (!string.IsNullOrWhiteSpace(user.Email) && user.EmailConfirmed);
        if (!hasOtherMethods)
        {
            await _userManager.SetTwoFactorEnabledAsync(user, false);
        }

        LogInformation("TOTP disabled for user {UserId}", userId);
        return Ok();
    }

    #region 验证码登录支持（基于地址，无需 UserId）

    /// <inheritdoc />
    public async Task<Result> SendCodeByAddressAsync(string address, TwoFactorType type, Guid? userId = null)
    {
        if (type == TwoFactorType.Totp)
        {
            return Fail("TOTP does not require sending verification codes", 400, ErrorCodes.VALIDATION_ERROR);
        }

        // 验证类型和配置
        if (type == TwoFactorType.Sms && !_otpOptions.EnableSms)
        {
            return Fail("SMS verification is not enabled", 400, ErrorCodes.CONFIGURATION_ERROR);
        }

        if (type == TwoFactorType.Email && !_otpOptions.EnableEmail)
        {
            return Fail("Email verification is not enabled", 400, ErrorCodes.CONFIGURATION_ERROR);
        }

        if (_eventBus == null)
        {
            return Fail("IEventBus is not available, cannot send verification code", 500, ErrorCodes.CONFIGURATION_ERROR);
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            return Fail("Address is required", 400, ErrorCodes.VALIDATION_ERROR);
        }

        // 优先检查缓存
        var resendCacheKey = $"2FA_Resend_Timestamp:{address}:{(int)type}";
        if (_cache != null)
        {
            var lastSent = await _cache.GetAsync<DateTime?>(resendCacheKey);
            if (lastSent.HasValue && lastSent.Value.AddSeconds(_otpOptions.ResendIntervalSeconds) > DateTime.UtcNow)
            {
                var remaining = (int)(lastSent.Value.AddSeconds(_otpOptions.ResendIntervalSeconds) - DateTime.UtcNow).TotalSeconds;
                return Fail($"Verification code sent too frequently, please wait {remaining} seconds", 429, ErrorCodes.VALIDATION_ERROR);
            }
        }

        // 检查数据库
        if (_cache == null)
        {
            var lastCode = await _repository
                .Where(tfc => tfc.Address == address && tfc.Type == type && !tfc.IsUsed)
                .OrderByDescending(tfc => tfc.CreationTime)
                .FirstOrDefaultAsync();

            if (lastCode != null && lastCode.CreationTime.AddSeconds(_otpOptions.ResendIntervalSeconds) > DateTime.UtcNow)
            {
                var remainingSeconds = (int)(lastCode.CreationTime.AddSeconds(_otpOptions.ResendIntervalSeconds) - DateTime.UtcNow).TotalSeconds;
                return Fail($"Verification code sent too frequently, please wait {remainingSeconds} seconds", 429, ErrorCodes.VALIDATION_ERROR);
            }
        }

        // 生成验证码
        var code = GenerateCode(_otpOptions.CodeLength);
        var expiresAt = DateTime.UtcNow.AddMinutes(_otpOptions.ExpirationMinutes);

        // 保存验证码（UserId 可为空）
        var twoFactorCode = new TwoFactorCode
        {
            UserId = userId,
            Code = code,
            Type = type,
            Address = address,
            ExpiresAt = expiresAt,
            IsUsed = false,
            CreationTime = DateTime.UtcNow
        };

        await _repository.InsertAsync(twoFactorCode);

        // 获取用户名（如果 userId 有值）
        string? userName = null;
        if (userId.HasValue)
        {
            var user = await _userManager.FindByGuidAsync(userId.Value);
            userName = user?.UserName;
        }

        // 发布事件，由应用层处理发送
        try
        {
            await _eventBus.PublishAsync(new TwoFactorCodeSentEvent
            {
                UserId = userId ?? Guid.Empty,
                UserName = userName ?? string.Empty,
                Type = type == TwoFactorType.Email ? IdentityConstants.TwoFactorTypeName.Email : IdentityConstants.TwoFactorTypeName.Sms,
                Address = address,
                Code = code,
                ExpiresAt = expiresAt,
                ExpirationMinutes = _otpOptions.ExpirationMinutes
            }, cancellationToken: default);

            LogInformation("Verification code event published for address {Address}, type {Type}", address, type);

            // 更新发送时间缓存
            if (_cache != null)
            {
                await _cache.SetAsync(resendCacheKey, DateTime.UtcNow, TimeSpan.FromSeconds(_otpOptions.ResendIntervalSeconds));
            }
            return Ok();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to publish verification code event for address {Address}, type {Type}", address, type);
            return Fail("Failed to send verification code", 500, ErrorCodes.INTERNAL_SERVER_ERROR);
        }
    }

    /// <inheritdoc />
    public async Task<Result> VerifyCodeByAddressAsync(string address, string code, TwoFactorType type)
    {
        if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(code))
        {
            return Fail("Address and code are required", 400, ErrorCodes.VALIDATION_ERROR);
        }

        // 查找未使用且未过期的验证码
        var twoFactorCode = await _repository
            .Where(tfc => tfc.Address == address
                && tfc.Code == code
                && tfc.Type == type
                && !tfc.IsUsed
                && tfc.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(tfc => tfc.CreationTime)
            .FirstOrDefaultAsync();

        if (twoFactorCode == null)
        {
            return Fail("Invalid or expired verification code", 400, ErrorCodes.VALIDATION_ERROR);
        }

        // 只验证，不标记为已使用
        LogInformation("Verification code validated for address {Address}, type {Type}", address, type);
        return Ok();
    }

    /// <inheritdoc />
    public async Task<Result<Guid?>> VerifyCodeByAddressAndMarkUsedAsync(string address, string code, TwoFactorType type)
    {
        if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(code))
        {
            return Fail<Guid?>("Address and code are required", 400, ErrorCodes.VALIDATION_ERROR);
        }

        var cacheKey = $"2FA_Verify_Fail_Count:{address}:{(int)type}";

        // 检查锁定
        if (_cache != null)
        {
            var failCount = await _cache.GetAsync<int>(cacheKey);
            if (failCount >= MaxTwoFactorFailureAttempts)
            {
                return Fail<Guid?>("Too many failed attempts. Please try again later.", 429, ErrorCodes.VALIDATION_ERROR);
            }
        }

        // 查找未使用且未过期的验证码
        var twoFactorCode = await _repository
            .Where(tfc => tfc.Address == address
                && tfc.Code == code
                && tfc.Type == type
                && !tfc.IsUsed
                && tfc.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(tfc => tfc.CreationTime)
            .FirstOrDefaultAsync();

        if (twoFactorCode == null)
        {
            // 记录失败
            if (_cache != null)
            {
                await _cache.IncrementAsync(cacheKey, 1, TwoFactorFailureCacheExpiration);
            }
            return Fail<Guid?>("Invalid or expired verification code", 400, ErrorCodes.VALIDATION_ERROR);
        }

        // 标记为已使用
        twoFactorCode.IsUsed = true;
        twoFactorCode.UsedAt = DateTime.UtcNow;
        await _repository.UpdateAsync(twoFactorCode);

        // 清除失败记录
        if (_cache != null)
        {
            await _cache.RemoveAsync(cacheKey);
        }

        LogInformation("Verification code verified and marked used for address {Address}, type {Type}", address, type);

        // 返回关联的 UserId（可能为空）
        return Ok<Guid?>(twoFactorCode.UserId);
    }

    #endregion

    /// <summary>
    /// 生成加密安全的验证码
    /// 使用均匀分布的随机数生成，避免 % 10 导致的分布不均匀问题
    /// </summary>
    private static string GenerateCode(int length)
    {
        var code = new StringBuilder(length);
        using var rng = RandomNumberGenerator.Create();

        for (int i = 0; i < length; i++)
        {
            // 使用 GetInt32 生成均匀分布的 0-9 随机数
            var digit = RandomNumberGenerator.GetInt32(0, 10);
            code.Append(digit);
        }
        return code.ToString();
    }

    #region Private Methods

    /// <summary>
    /// 格式化密钥（每 4 字符加空格，提高手动输入可读性）
    /// </summary>
    private static string FormatKey(string unformattedKey)
    {
        var result = new StringBuilder();
        int currentPosition = 0;
        while (currentPosition + 4 < unformattedKey.Length)
        {
            result.Append(unformattedKey.AsSpan(currentPosition, 4)).Append(' ');
            currentPosition += 4;
        }
        if (currentPosition < unformattedKey.Length)
        {
            result.Append(unformattedKey.AsSpan(currentPosition));
        }
        return result.ToString().Trim();
    }

    /// <summary>
    /// 生成标准 otpauth:// URI（用于二维码扫描）
    /// </summary>
    private string GenerateQrCodeUri(string email, string unformattedKey)
    {
        const string authenticatorUriFormat = "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6";
        var issuer = Uri.EscapeDataString("Tnzi");
        var accountName = Uri.EscapeDataString(email);
        return string.Format(authenticatorUriFormat, issuer, accountName, unformattedKey);
    }

    #endregion
}
