namespace Tnzi.Identity.Services;

/// <inheritdoc cref="IPendingActionService"/>
public class PendingActionService : ApplicationService, IPendingActionService
{
    private readonly UserManager<User> _userManager;
    private readonly IAuthTokenService _authTokenService;
    private readonly IAuthService _authService;
    private readonly IPasswordService _passwordService;
    private readonly ITwoFactorService? _twoFactorService;

    /// <summary>
    /// 初始化一个 <see cref="PendingActionService"/> 类型的新实例。
    /// </summary>
    public PendingActionService(
        IServiceProvider serviceProvider,
        UserManager<User> userManager,
        IAuthTokenService authTokenService,
        IAuthService authService,
        IPasswordService passwordService,
        ITwoFactorService? twoFactorService = null)
        : base(serviceProvider)
    {
        _userManager = Check.NotNull(userManager);
        _authTokenService = Check.NotNull(authTokenService);
        _authService = Check.NotNull(authService);
        _passwordService = Check.NotNull(passwordService);
        _twoFactorService = twoFactorService;
    }

    /// <inheritdoc />
    public async Task<Result<PendingActionChallengeDto>> DescribeAsync(string tempToken, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAsync(tempToken);
        if (!resolved.Succeeded)
        {
            return Fail<PendingActionChallengeDto>(resolved.Message!, resolved.Code ?? 400, resolved.ErrorCode);
        }

        var user = resolved.Data!.User;
        var owed = user.GetOwedObligations();

        var dto = new PendingActionChallengeDto
        {
            RequiredActions = owed.ToActionNames(),
            UserName = user.UserName ?? string.Empty,
        };

        // 欠着绑验证器就把密钥一起带出来 —— 不带的话前端只知道「你得绑个验证器」，
        // 却没有二维码可扫，那一步走不下去。
        if ((owed & PendingUserActions.EnrollTotp) != PendingUserActions.None && _twoFactorService != null)
        {
            var setup = await _twoFactorService.GetTotpSetupInfoAsync(user.Id);
            if (setup.Succeeded)
            {
                dto.TotpSetup = setup.Data;
            }
            else
            {
                // 拿不到密钥（TOTP 渠道被关掉了）——说清楚，否则用户对着一个没有二维码的
                // 页面无从下手，而原因在部署配置里。
                LogWarning(
                    "User {UserId} owes TOTP enrolment but the setup key is unavailable: {Reason}",
                    user.Id, setup.Message ?? "unknown");
            }
        }

        if ((owed & PendingUserActions.ConfirmEmail) != PendingUserActions.None)
        {
            dto.MaskedEmail = ContactAddressMasking.MaskEmail(user.Email);
        }

        return Ok(dto);
    }

    /// <inheritdoc />
    public async Task<Result<PendingActionResultDto>> CompleteChangePasswordAsync(CompletePasswordChangeDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        return await CompleteAsync(
            input.TempToken,
            PendingUserActions.ChangePassword,
            user => _passwordService.ForceSetPasswordAsync(user, input.NewPassword));
    }

    /// <inheritdoc />
    public async Task<Result<PendingActionResultDto>> CompleteEnrollTotpAsync(CompletePendingActionCodeDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        if (_twoFactorService == null)
        {
            return Fail<PendingActionResultDto>("Two-factor service is not available", 500, ErrorCodes.IDENTITY_ERROR);
        }

        return await CompleteAsync(
            input.TempToken,
            PendingUserActions.EnrollTotp,
            user => _twoFactorService.EnableTotpAsync(user.Id, input.Code),
            satisfiedFactor: TwoFactorType.Totp);
    }

    /// <inheritdoc />
    public async Task<Result> SendEmailConfirmationCodeAsync(string tempToken, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAsync(tempToken);
        if (!resolved.Succeeded)
        {
            return Fail(resolved.Message!, resolved.Code ?? 400, resolved.ErrorCode);
        }

        var user = resolved.Data!.User;
        if (!user.HasPendingAction(PendingUserActions.ConfirmEmail))
        {
            return InvalidToken();
        }

        if (_twoFactorService == null)
        {
            return Fail("Verification code service is not available", 500, ErrorCodes.IDENTITY_ERROR);
        }

        // ★ 地址由服务端自己取，不接受调用方指定：这是一个匿名端点，让它决定「发到哪」
        //   等于把这件事交给一个可能已被接管的会话（与 step-up 发码同一判据）。
        var sent = await _twoFactorService.SendCodeToUserAsync(
            user.Id, TwoFactorType.Email, VerificationCodePurpose.ConfirmEmail);

        return sent.Succeeded ? Ok() : Fail(sent.Message ?? "Failed to send the code", sent.Code ?? 400, sent.ErrorCode);
    }

    /// <inheritdoc />
    public async Task<Result<PendingActionResultDto>> CompleteConfirmEmailAsync(CompletePendingActionCodeDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        if (_twoFactorService == null)
        {
            return Fail<PendingActionResultDto>("Verification code service is not available", 500, ErrorCodes.IDENTITY_ERROR);
        }

        return await CompleteAsync(
            input.TempToken,
            PendingUserActions.ConfirmEmail,
            async user =>
            {
                var verified = await _twoFactorService.VerifyCodeAsync(
                    user.Id, input.Code, TwoFactorType.Email, VerificationCodePurpose.ConfirmEmail);
                if (!verified.Succeeded)
                {
                    return verified;
                }

                // 只置标记，不在这里保存 —— 紧接着的清位是同一次 UpdateAsync，
                // 分两次写等于为同一件事往数据库跑两趟。
                user.EmailConfirmed = true;
                return Result.Success();
            },
            // 刚用邮箱验证码证明了「能收这个邮箱」，邮箱 2FA 再问一遍问的是同一件事。
            satisfiedFactor: TwoFactorType.Email);
    }

    /// <summary>
    /// 三条完成路径共享的编排：校验令牌 → 办事 → 清位 → 还欠就再挑战、全清了才签发。
    /// </summary>
    /// <remarks>
    /// ★★ <strong>共享这段是必须的。</strong>三份手抄会在「清位写成赋 <c>None</c>」
    /// 或者「办完就直接签发、不管还欠着什么」上各错一次，而这两种错都不会让任何测试变红：
    /// 前者悄悄免掉了用户其余的义务，后者让「必须先绑 TOTP」变成一句空话。
    /// </remarks>
    private async Task<Result<PendingActionResultDto>> CompleteAsync(
        string tempToken,
        PendingUserActions action,
        Func<User, Task<Result>> perform,
        TwoFactorType? satisfiedFactor = null)
    {
        var resolved = await ResolveAsync(tempToken);
        if (!resolved.Succeeded)
        {
            return Fail<PendingActionResultDto>(resolved.Message!, resolved.Code ?? 400, resolved.ErrorCode);
        }

        var (entry, user) = (resolved.Data!.Entry, resolved.Data.User);

        // ★ 只有真的欠着这件事才受理。否则这个端点就成了一条旁路：
        //   谁手里有一枚未过期的挑战令牌，都能凭它跳过原本要走的其它义务。
        if (!user.HasPendingAction(action))
        {
            return InvalidToken<PendingActionResultDto>();
        }

        var performed = await perform(user);
        if (!performed.Succeeded)
        {
            // 事没办成就不清位、不消费令牌 —— 用户可以拿同一枚令牌重试
            // （验证码输错是常事，烧掉令牌等于让他重新登录一遍）。
            return Fail<PendingActionResultDto>(performed.Message!, performed.Code ?? 400, performed.ErrorCode);
        }

        // ★ 清位而不是赋 None：这个人可能还欠着别的，赋 None 会把那些一起抹掉，
        //   而他从此再也不会被要求办它们。
        user.PendingActions &= ~action;
        var saved = await _userManager.UpdateAsync(user);
        if (!saved.Succeeded)
        {
            // 事已经办了（密码改了 / TOTP 绑了），退不回去。如实报错：再登录一次会再次
            // 收到同样的挑战，而那时刚办的那件事已经生效了。
            LogError(
                "Pending action {Action} was performed for user {UserId} but the flag could not be cleared: {Errors}.",
                action, user.Id, saved.FormatErrors());

            return Fail<PendingActionResultDto>(
                "The change was applied but your account could not be updated. Please sign in again.",
                500,
                ErrorCodes.IDENTITY_USER_UPDATE_FAILED);
        }

        var remaining = user.GetOwedObligations();
        if (remaining != PendingUserActions.None)
        {
            // 还欠着别的：**不消费令牌**，同一枚继续用来办下一件。
            LogInformation("User {UserId} completed {Action}; still owes {Remaining}.", user.Id, action, remaining);

            return Ok(new PendingActionResultDto
            {
                Completed = false,
                RemainingActions = remaining.ToActionNames(),
            });
        }

        // 全清了，令牌到此为止。
        await _authTokenService.MarkTokenAsUsedAsync(entry.Id);

        LogInformation("User {UserId} completed every pending action.", user.Id);

        // ★ 仍走共享签发出口：账号刚清掉最后一个义务，此刻它和任何账号一样要过守卫链
        //   （管理员完全可能在这中间把人停掉）。
        // ★★ satisfiedFactor 是必须的：绑定验证器会把 2FA 总开关一并打开（它是聚合值），
        //   于是这条出口上的 2FA 判定会当场再问一次验证器码 —— 而用户刚刚为了完成绑定
        //   输过一个。扣掉本次已经证明过的那个因子，其余仍启用的方式照常挑战（不放宽）。
        var issued = await _authService.IssueTokenAsync(user, LoginMethod.Password, satisfiedFactor);
        if (!issued.Succeeded)
        {
            // ★★★ 签发失败**不能压成一份 Token 为 null 的成功回答**。这条出口会挑战：
            //   账号开着别的 2FA 方式时，扣掉本次已证明的因子之后仍有剩余，
            //   于是 IssueTokenAsync 返回 403 + 一枚新的临时令牌 —— 那正是用户继续下去的凭据。
            //   压成 200 { completed: true, token: null } 之后，事情**已经办完了**
            //   （密码已改、义务位已清、这枚待办令牌也已消费），而界面只能显示「失败」，
            //   同页重试必然再次失败：那枚令牌已经不在了。
            // ★ ErrorDetails 必须原样带出，否则前端拿不到临时令牌，挑战无从继续
            //   —— 与 AuthService 的四个签发调用点逐字同形。
            return Fail<PendingActionResultDto>(
                issued.Message ?? "Login rejected",
                issued.Code ?? 403,
                issued.ErrorCode,
                issued.ErrorDetails);
        }

        return Ok(new PendingActionResultDto
        {
            Completed = true,
            Token = issued.Data,
        });
    }

    /// <summary>
    /// 按临时令牌取回它指向的账号。失效 / 已用 / 过期 / 账号不存在一律同一个回答。
    /// </summary>
    private async Task<Result<ResolvedChallenge>> ResolveAsync(string tempToken)
    {
        if (string.IsNullOrWhiteSpace(tempToken))
        {
            return InvalidToken<ResolvedChallenge>();
        }

        var entry = await _authTokenService.FindTokenByValueAsync(
            IdentityConstants.LoginProvider.PendingAction,
            IdentityConstants.TokenName.PendingActionToken,
            tempToken);

        if (entry == null || entry.IsUsed || (entry.ExpiresAt.HasValue && entry.ExpiresAt.Value <= DateTime.UtcNow))
        {
            return InvalidToken<ResolvedChallenge>();
        }

        var user = await _userManager.FindByGuidAsync(entry.UserId);
        return user == null
            ? InvalidToken<ResolvedChallenge>()
            : Ok(new ResolvedChallenge(entry, user));
    }

    /// <summary>失效、已用、过期、不存在共用同一个回答 —— 区分开就是在帮人试探。</summary>
    private Result<T> InvalidToken<T>()
        => Fail<T>("Invalid or expired token", 400, ErrorCodes.IDENTITY_TOKEN_INVALID);

    private Result InvalidToken()
        => Fail("Invalid or expired token", 400, ErrorCodes.IDENTITY_TOKEN_INVALID);

    private sealed record ResolvedChallenge(AuthToken Entry, User User);
}
