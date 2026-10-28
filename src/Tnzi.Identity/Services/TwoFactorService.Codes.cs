namespace Tnzi.Identity.Services;

/// <summary>
/// <see cref="TwoFactorService"/> 中<strong>基于地址</strong>的收发码实现：
/// 免密验证码登录、快速注册、找回密码、换绑联系方式都走这里 —— 它们的共同点是
/// 发码时用户可能还不存在，或者收件地址还不属于任何账号。
/// </summary>
/// <remarks>
/// 与主文件拆开是因为上面那个共同点：主文件的 TOTP / 恢复码都挂在一个**已存在的账号**上，
/// 这里的每条路径都得先回答「这个地址属于谁、属不属于人」。类、可见性、注册方式一律不变。
/// <para>
/// ★ 这一段是 <see cref="VerificationCodePurpose"/> 真正落地的地方：
/// 写入时记下用途，查询时把它放进谓词。少了任何一半，用途绑定就只是个没人读的字段。
/// </para>
/// </remarks>
public partial class TwoFactorService
{
    #region 验证码登录支持（基于地址，无需 UserId）

    /// <inheritdoc />
    public async Task<Result> SendCodeByAddressAsync(string address, TwoFactorType type, VerificationCodePurpose purpose, Guid? userId = null)
    {
        if (type is TwoFactorType.Totp or TwoFactorType.Passkey)
        {
            return Fail($"{type} does not require sending verification codes", 400, ErrorCodes.VALIDATION_ERROR);
        }

        // ★ Unknown 只属于加列迁移之前的历史行，验码时永不匹配 —— 用它发码等于发一枚
        // 立刻就作废的码。拒绝在这里，而不是让调用方在验码阶段才发现。
        if (purpose == VerificationCodePurpose.Unknown)
        {
            return Fail("A verification code purpose must be specified", 400, ErrorCodes.VALIDATION_ERROR);
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

        // ★★★ 投递用去掉首尾空白的原样写法，其余（存储、查询、节流）一律用规范形态：
        //   同一个地址换个大小写不能换出一个新的节流桶（见 CanonicalAddress）。
        var deliveryAddress = address.Trim();
        var canonicalAddress = CanonicalAddress(deliveryAddress, type);

        // 列宽之外的地址答 400：交给插入，PostgreSQL 上是 500，SQLite 上原样落库。
        if (canonicalAddress.Length > TwoFactorCode.AddressMaxLength)
        {
            return Fail($"Address must be at most {TwoFactorCode.AddressMaxLength} characters", 400, ErrorCodes.VALIDATION_ERROR);
        }

        // 优先检查缓存。★ key 含 purpose：不同用途各自计时 —— 否则刚收到登录码的人
        // 会因为「发得太频繁」而换不了绑定邮箱，两件互不相干的事被同一个节流串在一起。
        var resendCacheKey = $"2FA_Resend_Timestamp:{canonicalAddress}:{(int)type}:{(int)purpose}";
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
                .Where(tfc => tfc.Address == canonicalAddress && tfc.Type == type && tfc.Purpose == purpose && !tfc.IsUsed)
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
            Purpose = purpose,
            Address = canonicalAddress,
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
                Address = deliveryAddress,
                Code = code,
                ExpiresAt = expiresAt,
                ExpirationMinutes = _otpOptions.ExpirationMinutes
            }, cancellationToken: default);

            LogInformation("Verification code event published for address {Address}, type {Type}, purpose {Purpose}", address, type, purpose);

            // 更新发送时间缓存
            if (_cache != null)
            {
                await _cache.SetAsync(resendCacheKey, DateTime.UtcNow, TimeSpan.FromSeconds(_otpOptions.ResendIntervalSeconds));
            }
            return Ok();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to publish verification code event for address {Address}, type {Type}, purpose {Purpose}", address, type, purpose);
            return Fail("Failed to send verification code", 500, ErrorCodes.INTERNAL_SERVER_ERROR);
        }
    }

    /// <inheritdoc />
    public async Task<Result<string?>> SendCodeToUserAsync(Guid userId, TwoFactorType type, VerificationCodePurpose purpose)
    {
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return Fail<string?>("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        if (type is TwoFactorType.Totp or TwoFactorType.Passkey)
        {
            return Fail<string?>($"{type} does not require sending verification codes", 400, ErrorCodes.VALIDATION_ERROR);
        }

        // ★ 要求地址**已验证**，而不是「填了就行」：往一个未经证实的地址发码，
        // 等于让任何能改这个字段的人把码引到自己那里去。
        var usable = type == TwoFactorType.Email ? CanConfigureEmail(user) : CanConfigureSms(user);
        if (!usable)
        {
            return Fail<string?>($"{type} is not available for this account", 400, ErrorCodes.VALIDATION_ERROR);
        }

        var address = type == TwoFactorType.Email ? user.Email! : user.PhoneNumber!;
        var sendResult = await SendCodeByAddressAsync(address, type, purpose, userId);
        if (!sendResult.Succeeded)
        {
            return Fail<string?>(sendResult.Message ?? "Failed to send verification code", sendResult.Code ?? 500, sendResult.ErrorCode);
        }

        return Ok<string?>(ContactAddressMasking.Mask(address, type));
    }

    /// <inheritdoc />
    public async Task<Result<Guid?>> VerifyCodeByAddressAndMarkUsedAsync(string address, string code, TwoFactorType type, VerificationCodePurpose purpose)
    {
        if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(code))
        {
            return Fail<Guid?>("Address and code are required", 400, ErrorCodes.VALIDATION_ERROR);
        }

        // ★ 与 TOTP 分支共用同一组 helper（见 TwoFactorService.FailureCacheKey）：
        // 两种方式给出不同的爆破成本是不可接受的 —— 用户看到的是同一个「两步验证」，
        // 而「各写一遍、其中一份漏了」没有任何症状。键用规范形态：同一个地址换个写法不是新的 5 次。
        var canonicalAddress = CanonicalAddress(address, type);
        var cacheKey = FailureCacheKey(canonicalAddress, type, purpose);

        // 先占用一次尝试再验码：并发在途的请求每一个都被计入（见 TryReserveAttemptAsync）。
        if (!await TryReserveAttemptAsync(cacheKey))
        {
            var lockedOut = TooManyAttempts();
            return Fail<Guid?>(lockedOut.Message!, lockedOut.Code ?? 429, lockedOut.ErrorCode);
        }

        // 查找未使用且未过期的验证码。★ Purpose 进谓词：为别的流程发出的码在这里查不到，
        // 因此与「码不对」同一种结果 —— 用途不匹配不单独报错，否则等于告诉试探者
        // 「这枚码是真的，只是用错了地方」。
        // ★ 库里的比较只用来缩小候选，最终判定在内存里按序数比较规范形态：不区分大小写、忽略尾随空格、
        //   忽略全角半角的排序规则会把另一种写法也判成相等，那种写法却有它自己的失败计数；让排序规则替我们
        //   决定「哪些写法是同一个地址」，失败计数就又能被绕开。原样写法那一支只为规范化之前写入、仍在有效期内的码。
        var trimmedAddress = address.Trim();
        var trimmedCode = code.Trim();
        var now = DateTime.UtcNow;
        var candidates = await _repository
            .Where(tfc => (tfc.Address == canonicalAddress || tfc.Address == trimmedAddress)
                && tfc.Code == trimmedCode
                && tfc.Type == type
                && tfc.Purpose == purpose
                && !tfc.IsUsed
                && tfc.ExpiresAt > now)
            .AsNoTracking()
            .OrderByDescending(tfc => tfc.CreationTime)
            .ToListAsync();

        var twoFactorCode = candidates.FirstOrDefault(tfc =>
            string.Equals(CanonicalAddress(tfc.Address, type), canonicalAddress, StringComparison.Ordinal)
            && string.Equals(tfc.Code, trimmedCode, StringComparison.Ordinal));

        if (twoFactorCode == null)
        {
            return Fail<Guid?>("Invalid or expired verification code", 400, ErrorCodes.VALIDATION_ERROR);
        }

        // ★ 标记已用是条件更新（WHERE Id = @id AND IsUsed = 0），影响 0 行 = 并发的另一个请求已经用掉了它。
        //   此前是读出实体、改字段、整行写回：两个同时拿着同一枚码的请求都读到「未使用」，都写回「已使用」，都成功。
        var consumed = await _repository
            .Where(tfc => tfc.Id == twoFactorCode.Id && !tfc.IsUsed)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(tfc => tfc.IsUsed, true)
                .SetProperty(tfc => tfc.UsedAt, now));
        if (consumed == 0)
        {
            return Fail<Guid?>("Invalid or expired verification code", 400, ErrorCodes.VALIDATION_ERROR);
        }

        await ClearFailuresAsync(cacheKey);

        LogInformation("Verification code verified and marked used for address {Address}, type {Type}, purpose {Purpose}", address, type, purpose);

        // 返回关联的 UserId（可能为空）
        return Ok<Guid?>(twoFactorCode.UserId);
    }

    /// <inheritdoc />
    public async Task<int> CleanExpiredCodesAsync(CancellationToken cancellationToken = default)
    {
        var retentionHours = _otpOptions.RetentionHours;
        if (retentionHours <= 0)
        {
            return 0;
        }

        // 按过期时刻算保留期（不是创建时刻）：ExpiresAt 上有索引，且「过期多久」才是这一行还有没有排查价值的判据。
        // 单条 DELETE … WHERE，不把行读进内存：这张表在发码频繁的部署里是全模块增长最快的一张。
        var cutoff = DateTime.UtcNow.AddHours(-retentionHours);
        var removed = await _repository
            .Where(c => c.ExpiresAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        if (removed > 0)
        {
            LogInformation("Removed {Count} verification codes that expired before {Cutoff:o}.", removed, cutoff);
        }

        return removed;
    }

    #endregion
}
