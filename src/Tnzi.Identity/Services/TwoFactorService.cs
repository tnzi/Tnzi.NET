namespace Tnzi.Identity.Services;

/// <summary>
/// 双因素认证服务实现
/// </summary>
public partial class TwoFactorService : ApplicationService, ITwoFactorService
{
    /// <summary>
    /// 验证尝试计数的窗口。固定 15 分钟：计数只在首次创建时定过期（见 <see cref="TryReserveAttemptAsync"/>），
    /// 上限读 <see cref="OtpOptions.MaxAttempts"/>。
    /// </summary>
    private static readonly TimeSpan TwoFactorFailureCacheExpiration = TimeSpan.FromMinutes(15);

    private readonly IRepository<TwoFactorCode, Guid> _repository;
    private readonly UserManager<User> _userManager;
    private readonly IEventBus? _eventBus;
    private readonly OtpOptions _otpOptions;
    private readonly PasskeyOptions _passkeyOptions;
    private readonly ICache? _cache;
    private readonly IConfiguration? _configuration;

    /// <summary>站点名没配时 otpauth URI 里的签发方。</summary>
    private const string DefaultTotpIssuer = "Tnzi";

    public TwoFactorService(
        IRepository<TwoFactorCode, Guid> repository,
        UserManager<User> userManager,
        IServiceProvider serviceProvider,
        IEventBus? eventBus = null,
        IOptionsSnapshot<IdentityOptions>? identityOptions = null,
        ICache? cache = null,
        IConfiguration? configuration = null)
        : base(serviceProvider)
    {
        _configuration = configuration;
        _repository = Check.NotNull(repository);
        _userManager = Check.NotNull(userManager);
        _eventBus = eventBus;
        // Scoped 服务：IOptionsSnapshot 每请求重算，构造期捕获 Otp 即随请求热更新。
        _otpOptions = identityOptions?.Value.Otp ?? new OtpOptions();
        _passkeyOptions = identityOptions?.Value.Passkey ?? new PasskeyOptions();
        _cache = cache;
    }

    /// <summary>
    /// 验证尝试计数的缓存键。<paramref name="scope"/> 是「针对谁」：
    /// 有收件地址的方式用<b>规范化后的</b>地址（<see cref="CanonicalAddress"/>），TOTP 用用户 ID。
    /// </summary>
    /// <remarks>
    /// ★ 这组 helper 存在的理由是「两条验证路径必须受同一道闸门管辖」。
    /// 此前 TOTP 与 SMS/Email 各写各的（准确说 TOTP 一行都没写），
    /// 而两者给出不同的爆破成本是不可接受的 —— 用户看到的是同一个「两步验证」。
    /// </remarks>
    private static string FailureCacheKey(string scope, TwoFactorType type, VerificationCodePurpose purpose)
        => $"2FA_Verify_Fail_Count:{scope}:{(int)type}:{(int)purpose}";

    /// <summary>
    /// 验证码子系统里一个收件地址的规范形态：存储、查询、重发节流、失败计数四处只认这一种写法。
    /// 邮箱按 <see cref="UserManager{TUser}.KeyNormalizer"/> 规范化（与账号解析 <c>FindByEmailAsync</c> 同一口径），
    /// 手机号去掉全部空白。
    /// </summary>
    /// <remarks>
    /// ★★★ 账号按规范化的邮箱解析，而节流与失败计数此前按请求原样的字符串分桶：<c>Victim@x.com</c>、
    /// <c>victim@x.com </c> 解析到同一个账号，却各有一个重发桶、各有 5 次猜测；在不区分大小写（或忽略尾随空格）的
    /// 排序规则下，每个变体查到的还是同一批码。于是「每个地址 5 次」从来不是「每个账号 5 次」，
    /// 同时攒出上千枚有效码再逐个变体去猜，验证码找回密码几千次请求内就能命中。
    /// 收窄只会让更多写法共用一个桶，不会让任何一种写法得到更多尝试，所以宁可多收不可少收。
    /// </remarks>
    private string CanonicalAddress(string address, TwoFactorType type)
    {
        var trimmed = address.Trim();
        if (type == TwoFactorType.Email)
        {
            return _userManager.KeyNormalizer?.NormalizeEmail(trimmed) ?? trimmed.ToUpperInvariant();
        }

        return string.Concat(trimmed.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
    }

    /// <summary>
    /// 占用一次验证尝试：<b>先原子递增、再按递增后的值判定</b>。超出上限返回 false，调用方答 429 且不再验码。
    /// </summary>
    /// <remarks>
    /// ★★★ 此前是「读计数 → 查库 / 验码 → 失败才递增」：同时在途的一批请求全都先读到同一个旧值，
    /// 并发喷射的猜测在计数到上限之前就远超上限。计的是尝试而不是失败，验证成功时清零
    /// （<see cref="ClearFailuresAsync"/>）；窗口固定（<c>IncrementAsync</c> 只在首次创建时定过期），
    /// 被挡下之后的重试不会把锁定续长。
    /// <para>
    /// ⚠ 无 ICache 时不设限。ICache 由核心 CachingModule 无条件注册，所以这一支
    /// 实际不发生；但要注意默认实现是**进程内**内存缓存：多实例部署未加载 Redis 时，
    /// 上限是「每实例 N 次」而不是「全局 N 次」。这一条写进 docs/modules/identity.md。
    /// </para>
    /// </remarks>
    private async Task<bool> TryReserveAttemptAsync(string cacheKey)
    {
        if (_cache == null) return true;
        var attempts = await _cache.IncrementAsync(cacheKey, 1, TwoFactorFailureCacheExpiration);
        return attempts <= MaxAttempts;
    }

    /// <summary>
    /// 每个窗口内的尝试上限，取设置中心的 <c>Identity:Otp:MaxAttempts</c>（每请求热读）。
    /// </summary>
    /// <remarks>
    /// ★★ 此前这里是一个写死的 5，而那个设置项只有选项校验器读：管理员把它收紧到 3，设置中心答「已保存」，
    /// 闸门照旧放 5 次。不足 1 按 1 处理：校验器挡得住配置文件里的 0，挡不住绕过校验写进来的值，
    /// 而 0 会让每一次尝试都被拒、本人也永远验不过。
    /// </remarks>
    private int MaxAttempts => Math.Max(1, _otpOptions.MaxAttempts);

    /// <summary>验证成功，清掉失败记录。</summary>
    private async Task ClearFailuresAsync(string cacheKey)
    {
        if (_cache == null) return;
        await _cache.RemoveAsync(cacheKey);
    }

    /// <summary>达到失败上限时的统一回答（429，与发码节流同码）。</summary>
    private Result TooManyAttempts()
        => Fail("Too many failed attempts. Please try again later.", 429, ErrorCodes.VALIDATION_ERROR);

    public async Task<Result> SendSmsCodeAsync(Guid userId, string phoneNumber, VerificationCodePurpose purpose)
    {
        // 验证用户存在
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        // 委托给基于地址的通用方法
        return await SendCodeByAddressAsync(phoneNumber, TwoFactorType.Sms, purpose, userId);
    }

    public async Task<Result> SendEmailCodeAsync(Guid userId, string email, VerificationCodePurpose purpose)
    {
        // 验证用户存在
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        // 委托给基于地址的通用方法
        return await SendCodeByAddressAsync(email, TwoFactorType.Email, purpose, userId);
    }

    public async Task<Result> VerifyCodeAsync(Guid userId, string code, TwoFactorType type, VerificationCodePurpose purpose)
    {
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        // Passkey 不是验证码：第二步是一次 WebAuthn 断言，走 IPasskeyService.VerifyAssertionAsync。
        // 落到这里说明调用方把它当成了输码方式，直接拒绝，别让它掉进下面的地址分支。
        if (type == TwoFactorType.Passkey)
        {
            return Fail("A passkey is verified by assertion, not by a code", 400, ErrorCodes.VALIDATION_ERROR);
        }

        // TOTP 验证：直接走 UserManager 内置验证，不查数据库。
        // ⚠ TOTP 码由时间与共享密钥派生，不存在「这枚码是为哪个用途发的」——
        // purpose 对它无从约束，这是 TOTP 的固有性质。需要按用途隔离的场景请用 SMS/Email。
        if (type == TwoFactorType.Totp)
        {
            // ★★★ 失败计数必须在这一支里自己做。此前这里直接 return，把下面
            // VerifyCodeByAddressAndMarkUsedAsync 的那道 5 次/15 分钟的闸门整个跳过了 ——
            // 而 TOTP 恰恰是唯一一种**没有发码动作**、因而也没有任何别的节流的方式：
            // 一枚 10 分钟的 2FA 临时令牌配上无限次尝试，六位数字的搜索空间是够得着的
            // （运行时按 ±2 个时间步验证，任一时刻有 5 枚码同时有效）。
            // 键按 userId 而不是 address：TOTP 不绑任何联系地址。
            var totpFailureKey = FailureCacheKey(userId.ToString("N"), type, purpose);
            if (!await TryReserveAttemptAsync(totpFailureKey))
            {
                return TooManyAttempts();
            }

            var isValid = await _userManager.VerifyTwoFactorTokenAsync(
                user, _userManager.Options.Tokens.AuthenticatorTokenProvider, code);
            if (!isValid)
            {
                return Fail("Invalid TOTP code", 400, ErrorCodes.VALIDATION_ERROR);
            }

            await ClearFailuresAsync(totpFailureKey);
            return Ok();
        }

        // SMS/Email 验证：查数据库
        var address = type == TwoFactorType.Email ? user.Email : user.PhoneNumber;
        if (string.IsNullOrWhiteSpace(address))
        {
            return Fail($"{type} address is not set for user", 400, ErrorCodes.VALIDATION_ERROR);
        }

        // 委托给基于地址的验证并标记已使用方法
        var result = await VerifyCodeByAddressAndMarkUsedAsync(address, code, type, purpose);
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
        // Passkey 只清开关不删凭据:凭据是登录 / 二次确认的凭据,归「通行密钥」管;
        // 这里关掉的是「拿它当第二因子」。
        var keyReset = await _userManager.ResetAuthenticatorKeyAsync(user);
        if (!keyReset.Succeeded)
        {
            return SaveFailed(keyReset);
        }

        user.SmsTwoFactorEnabled = false;
        user.EmailTwoFactorEnabled = false;
        user.AuthenticatorTwoFactorEnabled = false;
        user.PasskeyTwoFactorEnabled = false;
        user.PreferredTwoFactorType = null;
        user.TwoFactorEnabled = false;
        var saved = await _userManager.UpdateAsync(user);
        if (!saved.Succeeded)
        {
            return SaveFailed(saved);
        }

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
        var materialized = await MaterializeAsync(user);
        if (!materialized.Succeeded)
        {
            return SaveFailed(materialized);
        }

        // 只关总开关:登录不再挑战,但保留每种方式 flag + TOTP key + 首选。恢复即原样生效。
        user.TwoFactorEnabled = false;
        var saved = await _userManager.UpdateAsync(user);
        if (!saved.Succeeded)
        {
            return SaveFailed(saved);
        }

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
        var saved = await _userManager.UpdateAsync(user);
        if (!saved.Succeeded)
        {
            return SaveFailed(saved);
        }

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

        var materialized = await MaterializeAsync(user);
        if (!materialized.Succeeded)
        {
            return SaveFailed(materialized);
        }

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
                var keyReset = await _userManager.ResetAuthenticatorKeyAsync(user);
                if (!keyReset.Succeeded)
                {
                    return SaveFailed(keyReset);
                }

                user.AuthenticatorTwoFactorEnabled = false;
                break;
            case TwoFactorType.Passkey:
                // 只关「当第二因子」的开关;凭据留着,它们还是登录与二次确认的凭据。
                user.PasskeyTwoFactorEnabled = false;
                break;
            default:
                return Fail("Invalid two-factor type", 400, ErrorCodes.VALIDATION_ERROR);
        }

        var synced = await SyncAndSaveAsync(user);
        if (!synced.Succeeded)
        {
            return SaveFailed(synced);
        }

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

        var materialized = await MaterializeAsync(user);
        if (!materialized.Succeeded)
        {
            return SaveFailed(materialized);
        }

        // 首选方式必须是当前已启用的方式。
        if (!ExplicitEnabled(user).Contains(type))
        {
            return Fail("The selected method is not enabled", 400, ErrorCodes.VALIDATION_ERROR);
        }

        user.PreferredTwoFactorType = type;
        var saved = await _userManager.UpdateAsync(user);
        if (!saved.Succeeded)
        {
            return SaveFailed(saved);
        }

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
            // 最后一枚凭据被删掉、或部署关了渠道 ⇒ 登录不再提供它,别把人停在没人能完成的第二步。
            TwoFactorType.Passkey => await CanConfigurePasskeyAsync(user),
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
        // 都以显式 flag 为准。读路径上迁移没能落库不挡读：内存里的 flag 已按迁移后的口径展开，
        // 报出的状态是对的；下一次写会再迁移一次，而写路径会把失败报出来。
        var materialized = await MaterializeAsync(user);
        if (!materialized.Succeeded)
        {
            LogWarning("Two-factor settings of user {UserId} could not be migrated: {Errors}", userId, materialized.FormatErrors());
        }

        var configurable = await ComputeConfigurableAsync(user); // 可开启/配置的方式(TOTP 恒可设置)
        var enabled = ExplicitEnabled(user);                     // 迁移后显式 flag 即权威
        var preferred = user.PreferredTwoFactorType;

        // 每种方式一行:可配置、已启用、或"部署已开启该渠道但用户还差一步"三种情况都展示,
        // 后者以 RequiresAddress=true 提示用户先去验证手机/邮箱、或先登记一枚 passkey
        // (避免"开了全局配置却在列表看不到该方式"的困惑)。顺序 TOTP → Passkey → 短信 → 邮箱。
        var methods = new List<TwoFactorMethodDto>();
        foreach (var t in new[] { TwoFactorType.Totp, TwoFactorType.Passkey, TwoFactorType.Sms, TwoFactorType.Email })
        {
            var isConfigurable = configurable.Contains(t);
            var isEnabled = enabled.Contains(t);
            // 渠道在部署层是否开启:四种方式均看运行时 OtpOptions(可整体关闭);
            // passkey 还叠着 WebAuthn 接线本身的开关,接线没开这一行根本不出现。
            var channelOn = (t == TwoFactorType.Totp && _otpOptions.EnableTotp)
                || (t == TwoFactorType.Sms && _otpOptions.EnableSms)
                || (t == TwoFactorType.Email && _otpOptions.EnableEmail)
                || (t == TwoFactorType.Passkey && IsPasskeyChannelOn());
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

        var materialized = await MaterializeAsync(user);
        if (!materialized.Succeeded)
        {
            return SaveFailed<string>(materialized);
        }

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
            case TwoFactorType.Passkey:
                // 凭据在「通行密钥」里登记,这里只是把登记好的拿来当第二因子。
                // 没有凭据就开这个开关,登录会停在一个没人能完成的第二步。
                if (!await CanConfigurePasskeyAsync(user))
                    return Fail<string>("Passkey two-factor is disabled or no passkey is registered for this account", 400, ErrorCodes.VALIDATION_ERROR);
                user.PasskeyTwoFactorEnabled = true;
                break;
            default:
                return Fail<string>("Invalid two-factor type", 400, ErrorCodes.VALIDATION_ERROR);
        }

        var synced = await SyncAndSaveAsync(user);
        if (!synced.Succeeded)
        {
            return SaveFailed<string>(synced);
        }

        LogInformation("2FA method {Type} enabled for user {UserId}", input.Type, userId);
        return Ok<string>("Two-factor method enabled successfully");
    }

    public Task<Result<TotpSetupDto>> GetTotpSetupInfoAsync(Guid userId)
        => BuildTotpSetupAsync(userId, reuseUnconfirmedKey: false);

    /// <inheritdoc />
    public Task<Result<TotpSetupDto>> GetOrCreateTotpSetupInfoAsync(Guid userId)
        => BuildTotpSetupAsync(userId, reuseUnconfirmedKey: true);

    /// <summary>
    /// 交出登记验证器用的密钥与 otpauth URI。<paramref name="reuseUnconfirmedKey"/> 为 true 时，账号已有的那枚
    /// （尚未确认的）密钥原样交回，没有才生成；为 false 时每次都换一枚新的。
    /// </summary>
    /// <remarks>
    /// 复用是安全的：能走到这一步的账号没有启用验证器（已启用的在下面被 409 挡住），所以库里那枚密钥要么是
    /// 关闭验证器时换上的、从没给任何人看过的随机值，要么是本人在这次（或上次没走完的）登记里拿到的那一枚。
    /// </remarks>
    private async Task<Result<TotpSetupDto>> BuildTotpSetupAsync(Guid userId, bool reuseUnconfirmedKey)
    {
        // 部署层关闭了验证器方式 → 不允许生成密钥/设置 TOTP(与短信/邮箱渠道关闭时一致)。
        if (!_otpOptions.EnableTotp)
            return Fail<TotpSetupDto>("Authenticator (TOTP) two-factor is not enabled", 400, ErrorCodes.CONFIGURATION_ERROR);

        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
            return Fail<TotpSetupDto>("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);

        // 在重置 key 之前先迁移旧用户状态,避免新生成的未验证 key 被 legacy 回退误判为已启用。
        var materialized = await MaterializeAsync(user);
        if (!materialized.Succeeded)
            return SaveFailed<TotpSetupDto>(materialized);

        // ★★ 已启用验证器的账号不许再登记：下一行会无条件重置密钥，于是一枚被盗的访问令牌
        //   借这条路能把受害者的第二因子**换成攻击者自己的**（而不只是摘掉），受害者手里的
        //   验证器当场作废。换验证器的正路是先经二次确认禁用旧的（DisableTotp 标了 [RequireStepUp]），
        //   再登记新的。登录期 EnrollTotp 义务走的也是这个方法，但欠着那项义务的账号本就没启用 TOTP。
        if (user.AuthenticatorTwoFactorEnabled)
            return Fail<TotpSetupDto>("An authenticator is already enabled. Disable it before enrolling a new one.", 409, ErrorCodes.DATA_CONFLICT);

        var unformattedKey = reuseUnconfirmedKey ? await _userManager.GetAuthenticatorKeyAsync(user) : null;
        if (string.IsNullOrEmpty(unformattedKey))
        {
            // 重置并获取 authenticator key。重置没落库时不能把密钥交出去：用户扫进验证器的是一枚库里没有的密钥，
            // 下一步 totp/enable 必然失败，而症状是「验证码不对」。
            var keyReset = await _userManager.ResetAuthenticatorKeyAsync(user);
            if (!keyReset.Succeeded)
                return SaveFailed<TotpSetupDto>(keyReset);
            unformattedKey = await _userManager.GetAuthenticatorKeyAsync(user);
            if (string.IsNullOrEmpty(unformattedKey))
                return Fail<TotpSetupDto>("Failed to generate authenticator key", 500);
        }

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
        var materialized = await MaterializeAsync(user);
        if (!materialized.Succeeded)
            return SaveFailed(materialized);

        user.AuthenticatorTwoFactorEnabled = true;
        var synced = await SyncAndSaveAsync(user);
        if (!synced.Succeeded)
            return SaveFailed(synced);

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

    /// <summary>
    /// passkey 渠道是否开着:两个开关都要开 —— <c>Otp.EnablePasskey</c>(能不能当第二因子)叠在
    /// <c>Passkey.Enabled</c>(WebAuthn 接线本身)之上,接线没开时前者开了也没有断言可做。
    /// </summary>
    private bool IsPasskeyChannelOn()
        => _otpOptions.EnablePasskey && _passkeyOptions.Enabled;

    /// <summary>
    /// passkey 方式是否可配置:渠道开着,且账号至少登记了一枚凭据。
    /// </summary>
    /// <remarks>
    /// 渠道关着时<strong>不碰凭据存储</strong>:没接 passkey 的部署一次都不该为此查表。
    /// </remarks>
    private async Task<bool> CanConfigurePasskeyAsync(User user)
    {
        if (!IsPasskeyChannelOn()) return false;
        var passkeys = await _userManager.GetPasskeysAsync(user);
        return passkeys is { Count: > 0 };
    }

    private async Task<bool> IsTotpConfiguredAsync(User user)
        => !string.IsNullOrEmpty(await _userManager.GetAuthenticatorKeyAsync(user));

    /// <summary>显式启用的方式集合(按方式 flag)。</summary>
    private static HashSet<TwoFactorType> ExplicitEnabled(User user)
    {
        var set = new HashSet<TwoFactorType>();
        if (user.SmsTwoFactorEnabled) set.Add(TwoFactorType.Sms);
        if (user.EmailTwoFactorEnabled) set.Add(TwoFactorType.Email);
        if (user.AuthenticatorTwoFactorEnabled) set.Add(TwoFactorType.Totp);
        if (user.PasskeyTwoFactorEnabled) set.Add(TwoFactorType.Passkey);
        return set;
    }

    /// <summary>用户"可开启/配置"的方式集合(TOTP 需部署开启 EnableTotp;短信/邮箱需地址已验证;passkey 需渠道开着且已登记凭据)。</summary>
    private async Task<HashSet<TwoFactorType>> ComputeConfigurableAsync(User user)
    {
        var set = new HashSet<TwoFactorType>();
        if (CanConfigureTotp()) set.Add(TwoFactorType.Totp);
        if (CanConfigureSms(user)) set.Add(TwoFactorType.Sms);
        if (CanConfigureEmail(user)) set.Add(TwoFactorType.Email);
        if (await CanConfigurePasskeyAsync(user)) set.Add(TwoFactorType.Passkey);
        return set;
    }

    /// <summary>
    /// 旧用户(2FA 开着但无按方式 flag)登录时实际可用的方式 = 已配置的方式。
    /// 刻意不含 passkey:那是本模型之后才有的方式,登记过 passkey 的旧用户从没表示过要拿它当第二因子。
    /// </summary>
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
    private async Task<IdentityResult> MaterializeAsync(User user)
    {
        if (ExplicitEnabled(user).Count > 0 || !user.TwoFactorEnabled)
        {
            return IdentityResult.Success;
        }

        var legacy = await ComputeLegacyEnabledAsync(user);
        user.SmsTwoFactorEnabled = legacy.Contains(TwoFactorType.Sms);
        user.EmailTwoFactorEnabled = legacy.Contains(TwoFactorType.Email);
        user.AuthenticatorTwoFactorEnabled = legacy.Contains(TwoFactorType.Totp);
        if (!user.PreferredTwoFactorType.HasValue)
        {
            user.PreferredTwoFactorType = PickPreferred(legacy);
        }
        return await _userManager.UpdateAsync(user);
    }

    /// <summary>
    /// 同步聚合的 TwoFactorEnabled(= 任一方式启用) + 校正首选方式,并持久化。
    /// 首选若指向已禁用的方式则改为剩余启用方式中的首选;无首选但有启用方式则默认设一个。
    /// </summary>
    private async Task<IdentityResult> SyncAndSaveAsync(User user)
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

        return await _userManager.UpdateAsync(user);
    }

    /// <summary>
    /// 账号行没写进去时的回答。
    /// </summary>
    /// <remarks>
    /// ★ <see cref="UserManager{TUser}.UpdateAsync"/> 先跑全部 <c>IUserValidator</c> 再落库。存量账号过不了
    /// 当前规则（用户名 / 邮箱早已不合新校验器）或并发戳冲突时它返回失败而一个字都没写；
    /// 不看返回值，启用 / 禁用第二因子就答 200 而库里原样 —— 用户以为开了 2FA，登录照旧不挑战。
    /// </remarks>
    private Result SaveFailed(IdentityResult result)
        => Fail($"Failed to save two-factor settings: {result.FormatErrors()}", 500, ErrorCodes.IDENTITY_USER_UPDATE_FAILED);

    /// <inheritdoc cref="SaveFailed(IdentityResult)"/>
    private Result<T> SaveFailed<T>(IdentityResult result)
        => Fail<T>($"Failed to save two-factor settings: {result.FormatErrors()}", 500, ErrorCodes.IDENTITY_USER_UPDATE_FAILED);

    /// <summary>
    /// 无首选时的固定优先级:passkey &gt; TOTP &gt; 邮箱 &gt; 短信 —— 按抗钓鱼强度排,
    /// 也是登录挑战里首选之外的展示顺序。
    /// </summary>
    private static readonly TwoFactorType[] StrengthOrder =
    [
        TwoFactorType.Passkey, TwoFactorType.Totp, TwoFactorType.Email, TwoFactorType.Sms,
    ];

    /// <summary>按 <see cref="StrengthOrder"/> 从集合中选一个默认首选。</summary>
    private static TwoFactorType? PickPreferred(HashSet<TwoFactorType> set)
    {
        foreach (var t in StrengthOrder)
        {
            if (set.Contains(t)) return t;
        }
        return null;
    }

    /// <summary>把集合排为列表:首选置顶,其余按 <see cref="StrengthOrder"/>。</summary>
    private static List<TwoFactorType> OrderByPreferred(HashSet<TwoFactorType> set, TwoFactorType? preferred)
    {
        var ordered = new List<TwoFactorType>();
        if (preferred.HasValue && set.Contains(preferred.Value))
        {
            ordered.Add(preferred.Value);
        }
        foreach (var t in StrengthOrder)
        {
            if (set.Contains(t) && !ordered.Contains(t)) ordered.Add(t);
        }
        return ordered;
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
    /// <remarks>
    /// 签发方取部署方配置的站点名（<c>System:SiteName</c>，与邀请信、重置信同一个来源），没配才退回框架名。
    /// 此前写死 <c>Tnzi</c>：每个消费应用的用户在验证器里看到的都是框架名，两个基于本框架的站点的条目分不清，
    /// 输错码的人看不出是哪一个。签发方只在登记时写进验证器，改它不影响已经登记好的条目。
    /// </remarks>
    private string GenerateQrCodeUri(string email, string unformattedKey)
    {
        const string authenticatorUriFormat = "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6";
        var issuer = Uri.EscapeDataString(SiteNameResolver.Resolve(_configuration, Logger) ?? DefaultTotpIssuer);
        var accountName = Uri.EscapeDataString(email);
        return string.Format(authenticatorUriFormat, issuer, accountName, unformattedKey);
    }

    #endregion
}
