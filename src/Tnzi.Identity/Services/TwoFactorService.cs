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

        // 全部关闭:清空每种方式 flag + 首选 + 重置 authenticator key,聚合置 false。
        await _userManager.ResetAuthenticatorKeyAsync(user);
        user.SmsTwoFactorEnabled = false;
        user.EmailTwoFactorEnabled = false;
        user.AuthenticatorTwoFactorEnabled = false;
        user.PreferredTwoFactorType = null;
        user.TwoFactorEnabled = false;
        await _userManager.UpdateAsync(user);

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

    /// <inheritdoc />
    public async Task<Result> SuspendTwoFactorAsync(Guid userId)
    {
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        // 先迁移旧用户(把 legacy 单标志展开为 per-method flag),再暂停 —— 这样暂停
        // 也把"当前可用方式"固化下来,恢复时能原样带回。
        await MaterializeAsync(user);

        // 只关总开关:登录不再挑战,但保留每种方式 flag + TOTP key + 首选。恢复即原样生效。
        user.TwoFactorEnabled = false;
        await _userManager.UpdateAsync(user);

        LogInformation("2FA suspended (config preserved) for user {UserId}", userId);
        return Ok();
    }

    /// <inheritdoc />
    public async Task<Result> ResumeTwoFactorAsync(Guid userId)
    {
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        var explicitSet = ExplicitEnabled(user);
        if (explicitSet.Count == 0)
        {
            return Fail("No two-factor method is configured to resume", 400, ErrorCodes.VALIDATION_ERROR);
        }

        // 重新开启总开关;若首选缺失/失效则回落到一个已启用方式。
        user.TwoFactorEnabled = true;
        if (!user.PreferredTwoFactorType.HasValue || !explicitSet.Contains(user.PreferredTwoFactorType.Value))
        {
            user.PreferredTwoFactorType = PickPreferred(explicitSet);
        }
        await _userManager.UpdateAsync(user);

        LogInformation("2FA resumed for user {UserId}", userId);
        return Ok();
    }

    /// <inheritdoc />
    public async Task<Result> DisableTwoFactorMethodAsync(Guid userId, TwoFactorType type)
    {
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        await MaterializeAsync(user);

        switch (type)
        {
            case TwoFactorType.Sms:
                user.SmsTwoFactorEnabled = false;
                break;
            case TwoFactorType.Email:
                user.EmailTwoFactorEnabled = false;
                break;
            case TwoFactorType.Totp:
                // 移除 authenticator key(使 TOTP 彻底失效,再次启用需重新设置)。
                await _userManager.ResetAuthenticatorKeyAsync(user);
                user.AuthenticatorTwoFactorEnabled = false;
                break;
            default:
                return Fail("Invalid two-factor type", 400, ErrorCodes.VALIDATION_ERROR);
        }

        await SyncAndSaveAsync(user);
        LogInformation("2FA method {Type} disabled for user {UserId}", type, userId);
        return Ok();
    }

    /// <inheritdoc />
    public async Task<Result> SetPreferredTwoFactorAsync(Guid userId, TwoFactorType type)
    {
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        await MaterializeAsync(user);

        // 首选方式必须是当前已启用的方式。
        if (!ExplicitEnabled(user).Contains(type))
        {
            return Fail("The selected method is not enabled", 400, ErrorCodes.VALIDATION_ERROR);
        }

        user.PreferredTwoFactorType = type;
        await _userManager.UpdateAsync(user);
        LogInformation("Preferred 2FA method set to {Type} for user {UserId}", type, userId);
        return Ok();
    }

    /// <inheritdoc />
    public async Task<List<TwoFactorType>> GetEnabledTwoFactorTypesAsync(User user)
    {
        var explicitSet = ExplicitEnabled(user);
        if (explicitSet.Count > 0)
        {
            // 只保留部署渠道当前开启且可用的方式:关掉 EnableSms/EnableEmail/EnableTotp
            // (或地址失去验证)后,即使用户此前已启用该方式,登录也不再提供它 —— 用户可改
            // 用其它已启用方式;三种全不可用时集合为空,调用方据此按"未开启 2FA"处理。
            var usable = new HashSet<TwoFactorType>();
            foreach (var t in explicitSet)
            {
                if (await IsMethodUsableAsync(user, t)) usable.Add(t);
            }
            return OrderByPreferred(usable, user.PreferredTwoFactorType);
        }

        // 尚未迁移的旧用户:2FA 开着但无按方式 flag → 回退为"当前可用/已配置的方式"。
        if (user.TwoFactorEnabled)
        {
            return OrderByPreferred(await ComputeLegacyEnabledAsync(user), user.PreferredTwoFactorType);
        }

        return new List<TwoFactorType>();
    }

    /// <summary>
    /// 某方式在当前部署下是否"可用"(供登录挑战过滤):渠道开关开启,且短信/邮箱地址
    /// 已验证、TOTP 已配置 key。等同于该方式的"可配置"条件(方式已启用即隐含曾满足)。
    /// </summary>
    private async Task<bool> IsMethodUsableAsync(User user, TwoFactorType type)
        => type switch
        {
            TwoFactorType.Sms => CanConfigureSms(user),
            TwoFactorType.Email => CanConfigureEmail(user),
            TwoFactorType.Totp => CanConfigureTotp() && await IsTotpConfiguredAsync(user),
            _ => false,
        };

    public async Task<Result<TwoFactorStatusDto>> GetTwoFactorStatusAsync(Guid userId)
    {
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return Fail<TwoFactorStatusDto>("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        // 一次性把旧用户(单一 TwoFactorEnabled 标志)迁移为按方式 flag,之后所有读写
        // 都以显式 flag 为准。
        await MaterializeAsync(user);

        var configurable = await ComputeConfigurableAsync(user); // 可开启/配置的方式(TOTP 恒可设置)
        var enabled = ExplicitEnabled(user);                     // 迁移后显式 flag 即权威
        var preferred = user.PreferredTwoFactorType;

        // 每种方式一行:可配置、已启用、或"部署已开启该渠道但用户地址未验证"三种情况都展示,
        // 后者以 RequiresAddress=true 提示用户先去验证手机/邮箱(避免"开了全局配置却在列表看
        // 不到该方式"的困惑)。顺序 TOTP → 短信 → 邮箱。
        var methods = new List<TwoFactorMethodDto>();
        foreach (var t in new[] { TwoFactorType.Totp, TwoFactorType.Sms, TwoFactorType.Email })
        {
            var isConfigurable = configurable.Contains(t);
            var isEnabled = enabled.Contains(t);
            // 渠道在部署层是否开启:三种方式均看运行时 OtpOptions(TOTP 与短信/邮箱对称,可整体关闭)。
            var channelOn = (t == TwoFactorType.Totp && _otpOptions.EnableTotp)
                || (t == TwoFactorType.Sms && _otpOptions.EnableSms)
                || (t == TwoFactorType.Email && _otpOptions.EnableEmail);
            if (isConfigurable || isEnabled || channelOn)
            {
                methods.Add(new TwoFactorMethodDto
                {
                    Type = t,
                    Available = isConfigurable,
                    Enabled = isEnabled,
                    IsPreferred = preferred == t,
                    // 渠道开着、但既不可配置也未启用 ⇒ 缺已验证地址。
                    RequiresAddress = channelOn && !isConfigurable && !isEnabled,
                });
            }
        }

        var status = new TwoFactorStatusDto
        {
            // 总开关:仅 TwoFactorEnabled 反映"登录是否挑战"。暂停态(有配置但总开关关)
            // → IsEnabled=false 而 methods[].enabled 仍为 true,前端据此显示"配置已保留"。
            IsEnabled = user.TwoFactorEnabled,
            SupportedTypes = configurable.OrderBy(x => x).ToList(),
            IsTotpEnabled = user.AuthenticatorTwoFactorEnabled,
            PreferredType = preferred,
            CurrentType = preferred, // 兼容别名
            Methods = methods,
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

        await MaterializeAsync(user);

        // 按方式启用:地址已验证即可开启该渠道(信任账号已确认的手机/邮箱)。
        // TOTP 必须走 totp/setup + totp/enable(需验证一次性码),不在此处启用。
        switch (input.Type)
        {
            case TwoFactorType.Sms:
                if (!CanConfigureSms(user))
                    return Fail<string>("Phone number is not verified or SMS 2FA is disabled", 400, ErrorCodes.VALIDATION_ERROR);
                user.SmsTwoFactorEnabled = true;
                break;
            case TwoFactorType.Email:
                if (!CanConfigureEmail(user))
                    return Fail<string>("Email is not verified or email 2FA is disabled", 400, ErrorCodes.VALIDATION_ERROR);
                user.EmailTwoFactorEnabled = true;
                break;
            case TwoFactorType.Totp:
                return Fail<string>("Use the authenticator setup flow (totp/setup then totp/enable) to enable TOTP", 400, ErrorCodes.VALIDATION_ERROR);
            default:
                return Fail<string>("Invalid two-factor type", 400, ErrorCodes.VALIDATION_ERROR);
        }

        await SyncAndSaveAsync(user);
        LogInformation("2FA method {Type} enabled for user {UserId}", input.Type, userId);
        return Ok<string>("Two-factor method enabled successfully");
    }

    public async Task<Result<TotpSetupDto>> GetTotpSetupInfoAsync(Guid userId)
    {
        // 部署层关闭了验证器方式 → 不允许生成密钥/设置 TOTP(与短信/邮箱渠道关闭时一致)。
        if (!_otpOptions.EnableTotp)
            return Fail<TotpSetupDto>("Authenticator (TOTP) two-factor is not enabled", 400, ErrorCodes.CONFIGURATION_ERROR);

        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
            return Fail<TotpSetupDto>("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);

        // 在重置 key 之前先迁移旧用户状态,避免新生成的未验证 key 被 legacy 回退误判为已启用。
        await MaterializeAsync(user);

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
        // 部署层关闭了验证器方式 → 拒绝启用(与短信/邮箱渠道关闭时一致)。
        if (!_otpOptions.EnableTotp)
            return Fail("Authenticator (TOTP) two-factor is not enabled", 400, ErrorCodes.CONFIGURATION_ERROR);

        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);

        // 验证 TOTP 代码
        var isValid = await _userManager.VerifyTwoFactorTokenAsync(
            user, _userManager.Options.Tokens.AuthenticatorTokenProvider, verificationCode);
        if (!isValid)
            return Fail("Invalid verification code", 400, ErrorCodes.VALIDATION_ERROR);

        // 迁移旧用户其它方式,再启用 TOTP(不覆盖已启用的短信/邮箱)。
        await MaterializeAsync(user);
        user.AuthenticatorTwoFactorEnabled = true;
        await SyncAndSaveAsync(user);

        LogInformation("TOTP enabled for user {UserId}", userId);
        return Ok();
    }

    // TOTP 禁用 = 禁用 Totp 方式(重置 key + 聚合同步),与其它方式对称。
    public Task<Result> DisableTotpAsync(Guid userId)
        => DisableTwoFactorMethodAsync(userId, TwoFactorType.Totp);

    #region 按方式 2FA 内部辅助

    /// <summary>验证器(TOTP)渠道是否可配置:部署已开启 EnableTotp(无需已验证地址)。</summary>
    private bool CanConfigureTotp()
        => _otpOptions.EnableTotp;

    /// <summary>短信渠道是否可配置:部署已开启 EnableSms 且手机号已验证。</summary>
    private bool CanConfigureSms(User user)
        => _otpOptions.EnableSms && !string.IsNullOrWhiteSpace(user.PhoneNumber) && user.PhoneNumberConfirmed;

    /// <summary>邮箱渠道是否可配置:部署已开启 EnableEmail 且邮箱已验证。</summary>
    private bool CanConfigureEmail(User user)
        => _otpOptions.EnableEmail && !string.IsNullOrWhiteSpace(user.Email) && user.EmailConfirmed;

    private async Task<bool> IsTotpConfiguredAsync(User user)
        => !string.IsNullOrEmpty(await _userManager.GetAuthenticatorKeyAsync(user));

    /// <summary>显式启用的方式集合(按方式 flag)。</summary>
    private static HashSet<TwoFactorType> ExplicitEnabled(User user)
    {
        var set = new HashSet<TwoFactorType>();
        if (user.SmsTwoFactorEnabled) set.Add(TwoFactorType.Sms);
        if (user.EmailTwoFactorEnabled) set.Add(TwoFactorType.Email);
        if (user.AuthenticatorTwoFactorEnabled) set.Add(TwoFactorType.Totp);
        return set;
    }

    /// <summary>用户"可开启/配置"的方式集合(TOTP 需部署开启 EnableTotp;短信/邮箱需地址已验证)。</summary>
    private Task<HashSet<TwoFactorType>> ComputeConfigurableAsync(User user)
    {
        var set = new HashSet<TwoFactorType>();
        if (CanConfigureTotp()) set.Add(TwoFactorType.Totp);
        if (CanConfigureSms(user)) set.Add(TwoFactorType.Sms);
        if (CanConfigureEmail(user)) set.Add(TwoFactorType.Email);
        return Task.FromResult(set);
    }

    /// <summary>旧用户(2FA 开着但无按方式 flag)登录时实际可用的方式 = 已配置的方式。</summary>
    private async Task<HashSet<TwoFactorType>> ComputeLegacyEnabledAsync(User user)
    {
        var set = new HashSet<TwoFactorType>();
        if (CanConfigureSms(user)) set.Add(TwoFactorType.Sms);
        if (CanConfigureEmail(user)) set.Add(TwoFactorType.Email);
        if (CanConfigureTotp() && await IsTotpConfiguredAsync(user)) set.Add(TwoFactorType.Totp);
        return set;
    }

    /// <summary>
    /// 一次性把旧用户迁移到按方式模型:若 2FA 开着却无任何按方式 flag,则把 flag
    /// 设为当前已配置的方式并持久化。之后该用户完全走显式 flag。幂等;2FA 关闭时 no-op。
    /// </summary>
    private async Task MaterializeAsync(User user)
    {
        if (ExplicitEnabled(user).Count > 0 || !user.TwoFactorEnabled)
        {
            return;
        }

        var legacy = await ComputeLegacyEnabledAsync(user);
        user.SmsTwoFactorEnabled = legacy.Contains(TwoFactorType.Sms);
        user.EmailTwoFactorEnabled = legacy.Contains(TwoFactorType.Email);
        user.AuthenticatorTwoFactorEnabled = legacy.Contains(TwoFactorType.Totp);
        if (!user.PreferredTwoFactorType.HasValue)
        {
            user.PreferredTwoFactorType = PickPreferred(legacy);
        }
        await _userManager.UpdateAsync(user);
    }

    /// <summary>
    /// 同步聚合的 TwoFactorEnabled(= 任一方式启用) + 校正首选方式,并持久化。
    /// 首选若指向已禁用的方式则改为剩余启用方式中的首选;无首选但有启用方式则默认设一个。
    /// </summary>
    private async Task SyncAndSaveAsync(User user)
    {
        var explicitSet = ExplicitEnabled(user);
        var any = explicitSet.Count > 0;
        user.TwoFactorEnabled = any;

        if (!any)
        {
            user.PreferredTwoFactorType = null;
        }
        else if (!user.PreferredTwoFactorType.HasValue || !explicitSet.Contains(user.PreferredTwoFactorType.Value))
        {
            user.PreferredTwoFactorType = PickPreferred(explicitSet);
        }

        await _userManager.UpdateAsync(user);
    }

    /// <summary>按固定优先级(TOTP &gt; 邮箱 &gt; 短信)从集合中选一个默认首选。</summary>
    private static TwoFactorType? PickPreferred(HashSet<TwoFactorType> set)
    {
        foreach (var t in new[] { TwoFactorType.Totp, TwoFactorType.Email, TwoFactorType.Sms })
        {
            if (set.Contains(t)) return t;
        }
        return null;
    }

    /// <summary>把集合排为列表:首选置顶,其余按 TOTP &gt; 邮箱 &gt; 短信。</summary>
    private static List<TwoFactorType> OrderByPreferred(HashSet<TwoFactorType> set, TwoFactorType? preferred)
    {
        var ordered = new List<TwoFactorType>();
        if (preferred.HasValue && set.Contains(preferred.Value))
        {
            ordered.Add(preferred.Value);
        }
        foreach (var t in new[] { TwoFactorType.Totp, TwoFactorType.Email, TwoFactorType.Sms })
        {
            if (set.Contains(t) && !ordered.Contains(t)) ordered.Add(t);
        }
        return ordered;
    }

    #endregion

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
