namespace Tnzi.Identity.Services;

/// <summary>
/// <see cref="TwoFactorService"/> 中<strong>基于地址</strong>的收发码实现：
/// 免密验证码登录、快速注册、找回密码、换绑联系方式都走这里 —— 它们的共同点是
/// 发码时用户可能还不存在，或者收件地址还不属于任何账号。
/// </summary>
/// <remarks>
/// 与主文件拆开只是为了篇幅（<c>FileLengthConventionTests</c> 的 800 行上限），
/// 类、可见性、注册方式一律不变。
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
        if (type == TwoFactorType.Totp)
        {
            return Fail("TOTP does not require sending verification codes", 400, ErrorCodes.VALIDATION_ERROR);
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

        // 优先检查缓存。★ key 含 purpose：不同用途各自计时 —— 否则刚收到登录码的人
        // 会因为「发得太频繁」而换不了绑定邮箱，两件互不相干的事被同一个节流串在一起。
        var resendCacheKey = $"2FA_Resend_Timestamp:{address}:{(int)type}:{(int)purpose}";
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
                .Where(tfc => tfc.Address == address && tfc.Type == type && tfc.Purpose == purpose && !tfc.IsUsed)
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

        if (type == TwoFactorType.Totp)
        {
            return Fail<string?>("TOTP does not require sending verification codes", 400, ErrorCodes.VALIDATION_ERROR);
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
    public async Task<Result> VerifyCodeByAddressAsync(string address, string code, TwoFactorType type, VerificationCodePurpose purpose)
    {
        if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(code))
        {
            return Fail("Address and code are required", 400, ErrorCodes.VALIDATION_ERROR);
        }

        // 查找未使用且未过期的验证码。★ Purpose 进谓词：为别的流程发出的码在这里查不到，
        // 因此与「码不对」同一种结果 —— 用途不匹配不单独报错，否则等于告诉试探者
        // 「这枚码是真的，只是用错了地方」。
        var twoFactorCode = await _repository
            .Where(tfc => tfc.Address == address
                && tfc.Code == code
                && tfc.Type == type
                && tfc.Purpose == purpose
                && !tfc.IsUsed
                && tfc.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(tfc => tfc.CreationTime)
            .FirstOrDefaultAsync();

        if (twoFactorCode == null)
        {
            return Fail("Invalid or expired verification code", 400, ErrorCodes.VALIDATION_ERROR);
        }

        // 只验证，不标记为已使用
        LogInformation("Verification code validated for address {Address}, type {Type}, purpose {Purpose}", address, type, purpose);
        return Ok();
    }

    /// <inheritdoc />
    public async Task<Result<Guid?>> VerifyCodeByAddressAndMarkUsedAsync(string address, string code, TwoFactorType type, VerificationCodePurpose purpose)
    {
        if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(code))
        {
            return Fail<Guid?>("Address and code are required", 400, ErrorCodes.VALIDATION_ERROR);
        }

        var cacheKey = $"2FA_Verify_Fail_Count:{address}:{(int)type}:{(int)purpose}";

        // 检查锁定
        if (_cache != null)
        {
            var failCount = await _cache.GetCounterAsync(cacheKey);
            if (failCount >= MaxTwoFactorFailureAttempts)
            {
                return Fail<Guid?>("Too many failed attempts. Please try again later.", 429, ErrorCodes.VALIDATION_ERROR);
            }
        }

        // 查找未使用且未过期的验证码。★ Purpose 进谓词，理由同 VerifyCodeByAddressAsync：
        // 用途不符与码不对返回同一种结果，不给出「码是真的」这条信息。
        var twoFactorCode = await _repository
            .Where(tfc => tfc.Address == address
                && tfc.Code == code
                && tfc.Type == type
                && tfc.Purpose == purpose
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

        LogInformation("Verification code verified and marked used for address {Address}, type {Type}, purpose {Purpose}", address, type, purpose);

        // 返回关联的 UserId（可能为空）
        return Ok<Guid?>(twoFactorCode.UserId);
    }

    #endregion
}
