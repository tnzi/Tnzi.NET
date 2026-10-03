namespace Tnzi.Identity.Services;

/// <inheritdoc cref="IPasskeyService"/>
/// <remarks>
/// <para>
/// ★★ <strong>走底层 <c>IPasskeyHandler</c> 而不是 <c>SignInManager</c> 的 passkey 方法</strong>，
/// 有两个各自独立、单独成立的理由：
/// </para>
/// <para>
/// ① <strong>状态传递</strong>。<c>SignInManager.MakePasskeyCreationOptionsAsync</c> 把挑战状态写进 cookie
/// （<c>Identity.PasskeyAttestationState</c> / <c>Identity.PasskeyAssertionState</c>），
/// 而 Tnzi 是纯 Bearer 的前后端分离 API：cookie 会把这条流程绑死在同源浏览器上，
/// 跨域前端要额外开 CORS 凭据、移动端根本用不了。
/// <c>IPasskeyHandler</c> 把状态作为<strong>显式返回值与显式入参</strong>，于是框架可以自己决定它怎么往返。
/// </para>
/// <para>
/// ② <strong>签发出口</strong>。<c>SignInManager.PasskeySignInAsync</c> 走的是 cookie 登录，
/// 会绕过 <c>ILoginGuard</c>（IP 白名单这类准入策略）与 <c>ILoginSessionCoordinator</c>（多设备登录策略）。
/// 本服务只用它做<strong>断言校验</strong>，拿到用户后交给 <see cref="IAuthService.IssueTokenAsync"/> ——
/// 与其余五条签发路径同一个出口。
/// </para>
/// <para>
/// ★ <strong>状态存服务端缓存，客户端只拿一个不透明句柄。</strong>
/// <c>AttestationState</c> / <c>AssertionState</c> 是明文 JSON（挑战、用户实体都在里面），
/// 直接发给客户端等于让它自己决定挑战是什么。多实例部署需要分布式缓存，
/// 与框架里会话、验证码的既有做法一致。
/// </para>
/// </remarks>
public class PasskeyService : ApplicationService, IPasskeyService
{
    private const string StateCachePrefix = "tnzi:identity:passkey:state:";
    private const string KindAttestation = "attestation";
    private const string KindAssertion = "assertion";

    private readonly IPasskeyHandler<User> _passkeyHandler;
    private readonly UserManager<User> _userManager;
    private readonly IAuthService _authService;
    private readonly IPasskeyEnrollmentTokenService _enrollmentTokenService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ICache _cache;
    private readonly IOptionsMonitor<IdentityOptions> _options;

    /// <summary>
    /// 初始化一个 <see cref="PasskeyService"/> 类型的新实例。
    /// </summary>
    public PasskeyService(
        IServiceProvider serviceProvider,
        IPasskeyHandler<User> passkeyHandler,
        UserManager<User> userManager,
        IAuthService authService,
        IPasskeyEnrollmentTokenService enrollmentTokenService,
        IHttpContextAccessor httpContextAccessor,
        ICache cache,
        IOptionsMonitor<IdentityOptions> options)
        : base(serviceProvider)
    {
        _passkeyHandler = Check.NotNull(passkeyHandler);
        _userManager = Check.NotNull(userManager);
        _authService = Check.NotNull(authService);
        _enrollmentTokenService = Check.NotNull(enrollmentTokenService);
        _httpContextAccessor = Check.NotNull(httpContextAccessor);
        _cache = Check.NotNull(cache);
        _options = Check.NotNull(options);
    }

    private PasskeyOptions Passkey => _options.CurrentValue.Passkey;

    /// <inheritdoc />
    public async Task<Result<PasskeyOptionsDto>> BeginRegistrationAsync(string? enrollmentToken = null)
    {
        if (!Passkey.Enabled)
        {
            return Disabled<PasskeyOptionsDto>();
        }

        var userResult = await ResolveRegistrationTargetAsync(enrollmentToken);
        if (!userResult.Succeeded)
        {
            return Fail<PasskeyOptionsDto>(userResult.Message!, userResult.Code ?? 401, userResult.ErrorCode);
        }

        // ★ 已登录且没带注册令牌 = 给自己的账号新增一种登录方式，与 link-token 同一判据：绑上去的凭据在改密、
        //   撤销全部会话之后照样能登录，一枚被盗访问令牌借它换来的是永久的密码因子绕过。判定在服务层而不是
        //   [RequireStepUp]：路由是 [AllowAnonymous]（持令牌的人本来就没有会话可供二次确认），特性一挂就把那条路径打死；
        //   而且本控制器可被消费方整体替换，挂在特性上的守卫会跟着一起消失。持令牌那一半不判：令牌本身就是凭据。
        //   只判 begin 不判 complete：complete 要拿 begin 签发、绑定了用户的挑战句柄，没过这里就拿不到它；
        //   两处都判会让 SingleUse 的确认在第二处被消费掉而必然失败。
        // ★ IStepUpService 在调用点解析而不是构造注入：StepUpService 自己依赖 IPasskeyService（用 passkey 完成确认
        //   走的是同一份断言校验），构造注入会形成 IPasskeyService -> IStepUpService -> IPasskeyService 的环，
        //   ValidateOnBuild 让应用启动即失败（单测里两边都是替身，看不见这个环；真实 boot 才会炸）。
        if (string.IsNullOrWhiteSpace(enrollmentToken)
            && !await GetRequiredService<IStepUpService>().IsSatisfiedAsync(StepUpScopes.LoginMethodManage))
        {
            return StepUpRequired<PasskeyOptionsDto>(StepUpScopes.LoginMethodManage);
        }

        var user = userResult.Data!;
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return Fail<PasskeyOptionsDto>("Passkey registration requires an HTTP context", 400, ErrorCodes.VALIDATION_ERROR);
        }

        var entity = new PasskeyUserEntity
        {
            Id = user.Id.ToString(),
            Name = user.UserName ?? user.Id.ToString(),
            DisplayName = user.UserName ?? user.Id.ToString()
        };

        var created = await _passkeyHandler.MakeCreationOptionsAsync(entity, httpContext);
        if (created.AttestationState == null)
        {
            // 不该发生。宁可失败也不能拿一个空挑战往下走 —— 那样 complete 阶段
            // 会拿 null 去比对，把"挑战对不上"变成一个更难查的空引用。
            LogError("The passkey handler produced no attestation state for user {UserId}.", user.Id);
            return Fail<PasskeyOptionsDto>("Failed to create passkey options", 500, ErrorCodes.INTERNAL_SERVER_ERROR);
        }

        var stateId = await StoreStateAsync(KindAttestation, created.AttestationState, user.Id);

        return Ok(new PasskeyOptionsDto
        {
            OptionsJson = created.CreationOptionsJson,
            StateId = stateId
        });
    }

    /// <inheritdoc />
    public async Task<Result<PasskeyCredentialDto>> CompleteRegistrationAsync(PasskeyCompleteDto input, string? enrollmentToken = null)
    {
        if (!Passkey.Enabled)
        {
            return Disabled<PasskeyCredentialDto>();
        }

        Check.NotNull(input);

        var userResult = await ResolveRegistrationTargetAsync(enrollmentToken);
        if (!userResult.Succeeded)
        {
            return Fail<PasskeyCredentialDto>(userResult.Message!, userResult.Code ?? 401, userResult.ErrorCode);
        }

        var user = userResult.Data!;
        var state = await ConsumeStateAsync(KindAttestation, input.StateId);
        if (state == null)
        {
            return InvalidChallenge<PasskeyCredentialDto>();
        }

        // ★ 状态是绑用户签发的，必须回校一次：否则 A 可以拿自己的注册挑战
        // 配上 B 的注册令牌，把凭据挂到 B 名下。
        if (state.UserId != user.Id)
        {
            LogWarning("Passkey attestation state belongs to user {StateUserId} but registration targets {TargetUserId}; rejected.",
                state.UserId, user.Id);
            return InvalidChallenge<PasskeyCredentialDto>();
        }

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return Fail<PasskeyCredentialDto>("Passkey registration requires an HTTP context", 400, ErrorCodes.VALIDATION_ERROR);
        }

        var attestation = await _passkeyHandler.PerformAttestationAsync(new PasskeyAttestationContext
        {
            CredentialJson = input.CredentialJson,
            AttestationState = state.State,
            HttpContext = httpContext
        });

        if (!attestation.Succeeded)
        {
            LogWarning("Passkey attestation failed for user {UserId}: {Reason}", user.Id, attestation.Failure?.Message);
            return Fail<PasskeyCredentialDto>("Passkey registration failed", 400, ErrorCodes.VALIDATION_ERROR);
        }

        var passkey = attestation.Passkey;
        if (!string.IsNullOrWhiteSpace(input.DeviceName))
        {
            passkey.Name = input.DeviceName;
        }

        var identityResult = await _userManager.AddOrUpdatePasskeyAsync(user, passkey);
        if (!identityResult.Succeeded)
        {
            var message = string.Join("; ", identityResult.Errors.Select(e => e.Description));
            LogError("Failed to store passkey for user {UserId}: {Errors}", user.Id, message);
            return Fail<PasskeyCredentialDto>("Passkey registration failed", 400, ErrorCodes.VALIDATION_ERROR);
        }

        // 令牌只在凭据真的挂上去之后才作废 —— 用户在系统弹窗上点取消不该烧掉一枚令牌。
        if (!string.IsNullOrWhiteSpace(enrollmentToken))
        {
            await _enrollmentTokenService.ConsumeAsync(enrollmentToken);
        }

        LogInformation("User {UserId} registered a passkey.", user.Id);
        return Ok(ToDto(passkey));
    }

    /// <inheritdoc />
    public async Task<Result<PasskeyOptionsDto>> BeginAssertionAsync(PasskeyAssertionBeginDto input)
    {
        if (!Passkey.Enabled)
        {
            return Disabled<PasskeyOptionsDto>();
        }

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return Fail<PasskeyOptionsDto>("Passkey assertion requires an HTTP context", 400, ErrorCodes.VALIDATION_ERROR);
        }

        // ★ 查无此人时不报错，照常出一份选项。这个端点是匿名的，
        // 区分"用户不存在"与"用户存在但没有 passkey"就等于交出一个用户名枚举预言机。
        User? user = null;
        if (!string.IsNullOrWhiteSpace(input?.UserName))
        {
            user = await _userManager.FindByNameAsync(input.UserName);
        }
        else if (CurrentUser?.Id is { } currentId && currentId != Guid.Empty)
        {
            // 已登录且没报用户名 = 二次确认这类「证明还是我」的场景。带上本人的凭据列表，
            // 否则空的 allowCredentials 只认可发现凭据，YubiKey 这类不可发现的安全密钥当场就找不到。
            user = await _userManager.FindByGuidAsync(currentId);
        }

        var requested = await _passkeyHandler.MakeRequestOptionsAsync(user, httpContext);
        if (requested.AssertionState == null)
        {
            LogError("The passkey handler produced no assertion state.");
            return Fail<PasskeyOptionsDto>("Failed to create passkey options", 500, ErrorCodes.INTERNAL_SERVER_ERROR);
        }

        var stateId = await StoreStateAsync(KindAssertion, requested.AssertionState, userId: null);

        return Ok(new PasskeyOptionsDto
        {
            OptionsJson = requested.RequestOptionsJson,
            StateId = stateId
        });
    }

    /// <inheritdoc />
    public async Task<Result<PasskeyOptionsDto>> BeginAssertionForUserAsync(Guid userId)
    {
        if (!Passkey.Enabled)
        {
            return Disabled<PasskeyOptionsDto>();
        }

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return Fail<PasskeyOptionsDto>("Passkey assertion requires an HTTP context", 400, ErrorCodes.VALIDATION_ERROR);
        }

        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return Fail<PasskeyOptionsDto>("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        var requested = await _passkeyHandler.MakeRequestOptionsAsync(user, httpContext);
        if (requested.AssertionState == null)
        {
            LogError("The passkey handler produced no assertion state.");
            return Fail<PasskeyOptionsDto>("Failed to create passkey options", 500, ErrorCodes.INTERNAL_SERVER_ERROR);
        }

        var stateId = await StoreStateAsync(KindAssertion, requested.AssertionState, userId);

        return Ok(new PasskeyOptionsDto
        {
            OptionsJson = requested.RequestOptionsJson,
            StateId = stateId
        });
    }

    /// <inheritdoc />
    public async Task<Result<TokenResult>> CompleteAssertionAsync(PasskeyCompleteDto input)
    {
        var assertion = await PerformAssertionAsync<TokenResult>(input);
        if (!assertion.Succeeded)
        {
            return Fail<TokenResult>(assertion.Message!, assertion.Code ?? 401, assertion.ErrorCode);
        }

        // ★★ 与其余五条签发路径同一个出口：登录守卫 → 2FA 判定 → 会话协调器 → 带 session_id 的令牌。
        // satisfiedFactor 传 Passkey：这次登录已经证明过这枚 passkey，账号若把 passkey 也设成第二因子，
        // 再问一次问的是同一件事（与邮箱验证码登录扣掉 Email 因子同一条规则）；别的因子（TOTP / 短信）照常挑战。
        return await _authService.IssueTokenAsync(assertion.Data!, LoginMethod.Passkey, satisfiedFactor: TwoFactorType.Passkey);
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> VerifyAssertionAsync(PasskeyCompleteDto input)
    {
        var assertion = await PerformAssertionAsync<Guid>(input);

        return assertion.Succeeded
            ? Ok(assertion.Data!.Id)
            : Fail<Guid>(assertion.Message!, assertion.Code ?? 401, assertion.ErrorCode);
    }

    /// <summary>
    /// 校验一次断言，成功时返回断言所证明的那个用户。
    /// </summary>
    /// <typeparam name="TFailure">
    /// 仅用于让「功能未启用」「挑战无效」这两个共用的失败构造器保持各自调用方的返回类型，
    /// 与断言逻辑本身无关。
    /// </typeparam>
    /// <remarks>
    /// ★ <b>本方法只回答「这次断言证明了谁」，不做任何签发、不做任何授权判断。</b>
    /// 登录路径拿它去换令牌（那条路上还要过登录守卫与 2FA），二次确认路径拿它比对当前用户 ——
    /// 两条路的后续判定完全不同，唯一共享的是密码学校验这一段。
    /// </remarks>
    private async Task<Result<User>> PerformAssertionAsync<TFailure>(PasskeyCompleteDto input)
    {
        if (!Passkey.Enabled)
        {
            var disabled = Disabled<TFailure>();
            return Fail<User>(disabled.Message!, disabled.Code ?? 400, disabled.ErrorCode);
        }

        Check.NotNull(input);

        var state = await ConsumeStateAsync(KindAssertion, input.StateId);
        if (state == null)
        {
            var invalid = InvalidChallenge<TFailure>();
            return Fail<User>(invalid.Message!, invalid.Code ?? 400, invalid.ErrorCode);
        }

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return Fail<User>("Passkey assertion requires an HTTP context", 400, ErrorCodes.VALIDATION_ERROR);
        }

        var assertion = await _passkeyHandler.PerformAssertionAsync(new PasskeyAssertionContext
        {
            CredentialJson = input.CredentialJson,
            AssertionState = state.State,
            HttpContext = httpContext
        });

        if (!assertion.Succeeded)
        {
            LogWarning("Passkey assertion failed: {Reason}", assertion.Failure?.Message);
            return Fail<User>("Invalid passkey", 401, ErrorCodes.UNAUTHORIZED);
        }

        var user = assertion.User;

        // ★ 必须写回：签名计数器等字段更新后才谈得上克隆凭据检测。
        // 写失败不阻断（断言本身已经通过），但要留痕。
        var updateResult = await _userManager.AddOrUpdatePasskeyAsync(user, assertion.Passkey);
        if (!updateResult.Succeeded)
        {
            LogWarning("Passkey assertion succeeded for user {UserId} but the credential could not be updated; "
                + "clone detection will be degraded.", user.Id);
        }

        return Ok(user);
    }

    /// <inheritdoc />
    public async Task<Result<List<PasskeyCredentialDto>>> GetCredentialsAsync()
    {
        if (!Passkey.Enabled)
        {
            return Disabled<List<PasskeyCredentialDto>>();
        }

        var user = await GetCurrentUserAsync();
        if (user == null)
        {
            return Fail<List<PasskeyCredentialDto>>("Not authenticated", 401, ErrorCodes.UNAUTHORIZED);
        }

        var passkeys = await _userManager.GetPasskeysAsync(user);
        return Ok(passkeys.Select(ToDto).ToList());
    }

    /// <inheritdoc />
    public async Task<Result> DeleteCredentialAsync(string credentialId)
    {
        if (!Passkey.Enabled)
        {
            return Fail("Passkey is not enabled", 400, ErrorCodes.CONFIGURATION_ERROR);
        }

        if (string.IsNullOrWhiteSpace(credentialId))
        {
            return Fail("Credential id is required", 400, ErrorCodes.VALIDATION_ERROR);
        }

        var user = await GetCurrentUserAsync();
        if (user == null)
        {
            return Fail("Not authenticated", 401, ErrorCodes.UNAUTHORIZED);
        }

        byte[] rawId;
        try
        {
            rawId = WebEncoders.Base64UrlDecode(credentialId);
        }
        catch (FormatException)
        {
            // 格式错与不存在共用同一个回答，不透露该凭据是否存在。
            return Fail("Passkey not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        // ★ 经 UserManager 按用户删除，而不是按 credentialId 全局删：
        // 凭据标识由客户端提供，不绑用户就成了"删除任意人的 passkey"。
        var existing = await _userManager.GetPasskeyAsync(user, rawId);
        if (existing == null)
        {
            return Fail("Passkey not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        // ★ 删掉最后一枚凭据会顺带关掉「passkey 当第二因子」（见下），若它是唯一方式，账号的 2FA 就此整体关闭。
        //   这与 two-factor/method/disable 是同一个后果，必须过同一道 TwoFactorManage 二次确认，
        //   否则一枚被盗访问令牌一次 DELETE 就拆掉了第二因子。判定在服务层而不是控制器特性上：
        //   只有这一种删除会拆 2FA，删一把备用钥匙不该被拦；而且控制器可被消费方整体替换。
        //   step-up 未启用的部署 IsSatisfiedAsync 恒为 true，行为不变。
        if (user.PasskeyTwoFactorEnabled)
        {
            var before = await _userManager.GetPasskeysAsync(user);
            if (before is not { Count: > 1 }
                && !await GetRequiredService<IStepUpService>().IsSatisfiedAsync(StepUpScopes.TwoFactorManage))
            {
                return StepUpRequired(StepUpScopes.TwoFactorManage);
            }
        }

        var result = await _userManager.RemovePasskeyAsync(user, rawId);
        if (!result.Succeeded)
        {
            return Fail("Failed to remove passkey", 400, ErrorCodes.VALIDATION_ERROR);
        }

        LogInformation("User {UserId} removed a passkey.", user.Id);

        // ★ 最后一枚凭据没了而「拿 passkey 当第二因子」还开着,登录会停在一个没人能完成的第二步
        //   (登录挑战会把它过滤掉,但若它是唯一方式,账号就等于没开 2FA 而状态页还写着开着)。
        //   随手关掉这个开关并同步聚合;凭据没了的开关本来就没有意义。
        //   ITwoFactorService 在调用点解析:它不依赖本服务,但保持与 IStepUpService 同一种取法,别再给依赖图添边。
        if (user.PasskeyTwoFactorEnabled)
        {
            var remaining = await _userManager.GetPasskeysAsync(user);
            if (remaining is not { Count: > 0 })
            {
                var disabled = await GetRequiredService<ITwoFactorService>().DisableTwoFactorMethodAsync(user.Id, TwoFactorType.Passkey);
                if (!disabled.Succeeded)
                {
                    LogWarning("The last passkey of user {UserId} was removed but passkey two-factor could not be turned off: {Reason}", user.Id, disabled.Message);
                }
            }
        }

        return Ok();
    }

    /// <summary>
    /// 判定这次注册挂到谁名下：注册令牌优先，其次当前登录用户。
    /// </summary>
    /// <remarks>
    /// ★ 两条都不成立就拒绝。判定放这里而不是控制器特性上 ——
    /// 控制器是 <c>[DefaultController]</c>，消费方可在同路由整体替换掉它。
    /// </remarks>
    private async Task<Result<User>> ResolveRegistrationTargetAsync(string? enrollmentToken)
    {
        if (!string.IsNullOrWhiteSpace(enrollmentToken))
        {
            var validation = await _enrollmentTokenService.ValidateAsync(enrollmentToken);
            if (!validation.Succeeded)
            {
                return Fail<User>(validation.Message!, validation.Code ?? 400, validation.ErrorCode);
            }

            var invited = await _userManager.FindByIdAsync(validation.Data.ToString());
            return invited == null
                ? Fail<User>("Invalid or expired enrollment token", 400, ErrorCodes.VALIDATION_ERROR)
                : Ok(invited);
        }

        var current = await GetCurrentUserAsync();
        return current == null
            ? Fail<User>("Not authenticated", 401, ErrorCodes.UNAUTHORIZED)
            : Ok(current);
    }

    private async Task<User?> GetCurrentUserAsync()
    {
        var userId = CurrentUser?.Id;
        return userId == null ? null : await _userManager.FindByIdAsync(userId.Value.ToString());
    }

    /// <summary>
    /// 与 <c>StepUpFilter</c> 逐字同形的二次确认挑战：同一状态码、同一错误码、同一 <c>{ scope }</c> 详情，
    /// 前端的统一处理（<c>withStepUp</c>）才认得它并拉起确认交互后原样重试。
    /// </summary>
    private Result<T> StepUpRequired<T>(string scope)
        => Fail<T>(
            "This action requires re-authentication",
            _options.CurrentValue.StepUp.ChallengeStatusCode,
            ErrorCodes.IDENTITY_STEP_UP_REQUIRED,
            new { scope });

    /// <inheritdoc cref="StepUpRequired{T}(string)"/>
    private Result StepUpRequired(string scope)
        => Fail(
            "This action requires re-authentication",
            _options.CurrentValue.StepUp.ChallengeStatusCode,
            ErrorCodes.IDENTITY_STEP_UP_REQUIRED,
            new { scope });

    /// <summary>
    /// 把挑战状态存进服务端缓存，返回一个不透明句柄。
    /// </summary>
    private async Task<string> StoreStateAsync(string kind, string state, Guid? userId)
    {
        var stateId = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var ttl = TimeSpan.FromSeconds(Math.Max(30, Passkey.ChallengeTimeoutSeconds));

        await _cache.SetAsync(
            StateCacheKey(kind, stateId),
            new PasskeyChallengeState(state, userId),
            ttl);

        return stateId;
    }

    /// <summary>
    /// 取回并<strong>立即删除</strong>挑战状态：一次性，用过即失效。
    /// </summary>
    /// <remarks>
    /// 先删再用。挑战重放是 WebAuthn 的核心威胁，删除失败时宁可让这次登录失败，
    /// 也不能留下一个可以再用一次的挑战。
    /// </remarks>
    private async Task<PasskeyChallengeState?> ConsumeStateAsync(string kind, string? stateId)
    {
        if (string.IsNullOrWhiteSpace(stateId))
        {
            return null;
        }

        var key = StateCacheKey(kind, stateId);
        var state = await _cache.GetAsync<PasskeyChallengeState>(key);
        if (state == null)
        {
            return null;
        }

        await _cache.RemoveAsync(key);
        return state;
    }

    private static string StateCacheKey(string kind, string stateId) => $"{StateCachePrefix}{kind}:{stateId}";

    private static PasskeyCredentialDto ToDto(UserPasskeyInfo passkey) => new()
    {
        CredentialId = WebEncoders.Base64UrlEncode(passkey.CredentialId),
        Name = passkey.Name,
        CreatedAt = passkey.CreatedAt,
        IsBackedUp = passkey.IsBackedUp
    };

    private Result<T> Disabled<T>()
        => Fail<T>("Passkey is not enabled", 400, ErrorCodes.CONFIGURATION_ERROR);

    /// <summary>挑战无效 / 过期 / 已用过共用同一个回答。</summary>
    private Result<T> InvalidChallenge<T>()
        => Fail<T>("Invalid or expired passkey challenge", 400, ErrorCodes.VALIDATION_ERROR);

    /// <summary>缓存里的挑战状态。<c>UserId</c> 只在注册时有值（断言允许 usernameless）。</summary>
    private sealed record PasskeyChallengeState(string State, Guid? UserId);
}
