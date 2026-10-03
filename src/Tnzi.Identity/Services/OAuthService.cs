namespace Tnzi.Identity.Services;

/// <summary>
/// OAuth服务实现
/// </summary>
public class OAuthService : ApplicationService, IOAuthService
{
    private readonly UserManager<User> _userManager;
    private readonly IUserLoginService _userLoginService;
    private readonly IUserDetailService? _userDetailService;
    private readonly IAuthService _authService;
    private readonly IOAuthEmailVerificationPolicy _emailVerificationPolicy;
    private readonly ICurrentTenant? _currentTenant;
    private readonly bool _multiTenancyEnabled;

    /// <summary>
    /// 初始化一个 <see cref="OAuthService"/> 类型的新实例。
    /// </summary>
    /// <remarks>
    /// ★ <strong>这里刻意<b>没有</b> <c>ITokenService</c> / <c>IAuthTokenService</c> /
    /// <c>ILoginSessionCoordinator</c> / <c>ILoginGuardEvaluator</c> / <c>IEventBus</c>。</strong>
    /// 它们曾经都在，因为本服务自己走完了整条签发流程 —— 而那份手抄件漏掉了 2FA 与义务位。
    /// 签发现在整体交给 <see cref="IAuthService.IssueTokenAsync"/>，把这些依赖留在构造函数里
    /// 只会诱使下一个人再抄一遍。
    /// </remarks>
    public OAuthService(
        UserManager<User> userManager,
        IUserLoginService userLoginService,
        IServiceProvider serviceProvider,
        IAuthService authService,
        IOAuthEmailVerificationPolicy emailVerificationPolicy,
        IUserDetailService? userDetailService = null,
        ICurrentTenant? currentTenant = null,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null)
        : base(serviceProvider)
    {
        _userManager = Check.NotNull(userManager);
        _userLoginService = Check.NotNull(userLoginService);
        _authService = Check.NotNull(authService);
        _emailVerificationPolicy = Check.NotNull(emailVerificationPolicy);
        _userDetailService = userDetailService;
        _currentTenant = currentTenant;
        _multiTenancyEnabled = multiTenancyOptions?.Value.Enabled ?? false;
    }

    public async Task<Result<OAuthCallbackResultDto>> HandleOAuthCallbackAsync(string provider, ClaimsPrincipal principal)
    {
        // 从 Claims 中提取用户信息（支持多平台：Google, Microsoft, Facebook, Twitter, GitHub）
        var providerKey = ExtractProviderKey(principal);

        if (string.IsNullOrEmpty(providerKey))
        {
            return Fail<OAuthCallbackResultDto>("Provider key not found in claims", 400, ErrorCodes.IDENTITY_OAUTH_ERROR);
        }

        // 提取 Email
        var email = principal.FindFirstValue(ClaimTypes.Email)
            ?? principal.FindFirstValue("email");

        // 提取名字（支持多平台）
        var firstName = principal.FindFirstValue(ClaimTypes.GivenName)
            ?? principal.FindFirstValue("given_name")
            ?? principal.FindFirstValue("givenname")
            ?? principal.FindFirstValue("first_name");

        // 提取姓氏（支持多平台）
        var lastName = principal.FindFirstValue(ClaimTypes.Surname)
            ?? principal.FindFirstValue("family_name")
            ?? principal.FindFirstValue("surname")
            ?? principal.FindFirstValue("last_name");

        // 提取显示名/昵称（支持多平台）
        var displayName = ExtractDisplayName(principal);

        // 提取头像（支持多平台）
        var avatarUrl = principal.FindFirstValue("picture")
            ?? principal.FindFirstValue("avatar_url")
            ?? principal.FindFirstValue("profile_image_url")
            ?? principal.FindFirstValue("profile_image_url_https");

        // 检查是否已存在该Provider的登录记录
        var existingUser = await _userManager.FindByLoginAsync(provider, providerKey);

        if (existingUser != null)
        {
            // 用户已存在，直接登录
            var result = await GenerateTokenAndPublishLoginEventAsync(existingUser, provider, principal);
            if (result.Succeeded)
            {
                LogInformation("OAuth login successful for user {UserName} via {Provider}", existingUser.UserName ?? string.Empty, provider);
            }
            return result;
        }

        // ★★★ 邮箱经提供商证实过，才谈得上「用它去认领账号」。
        // 有些提供商的资料邮箱是用户自己填的、从不校验，于是不做这一判定的话，
        // 「拿受害者的邮箱去第三方注册一个号，再用它登录」就是一条完整的接管路径 ——
        // 不需要密码，也不经过两步验证。判定可替换，默认实现宁可答「不知道」。
        var emailVerified = _emailVerificationPolicy.IsEmailVerified(provider, principal, email);

        // 用户不存在，检查邮箱是否已注册
        User? userByEmail = null;
        if (!string.IsNullOrEmpty(email) && emailVerified)
        {
            userByEmail = await _userManager.FindByEmailAsync(email);
        }

        if (userByEmail != null)
        {
            // 邮箱已注册且地址已被证实，关联OAuth账户
            var linkResult = await LinkOAuthAccountAsync(userByEmail.Id, provider, providerKey, displayName);
            if (!linkResult.Succeeded)
            {
                return Fail<OAuthCallbackResultDto>(linkResult.Message ?? "Failed to link OAuth account", linkResult.Code ?? 400, linkResult.ErrorCode);
            }

            // 登录并返回Token
            var result = await GenerateTokenAndPublishLoginEventAsync(userByEmail, provider, principal);
            if (result.Succeeded)
            {
                LogInformation("OAuth login successful for user {UserName} via {Provider} (linked account)", userByEmail.UserName ?? string.Empty, provider);
            }
            return result;
        }

        // ★ 地址没被证实，但本地确实有人用着它 —— 这时**既不认领也不新建**：
        //   新建会撞上 RequireUniqueEmail 而以一个看不懂的 400 收场，而认领正是要防的那件事。
        //   出路是让本人用常规方式登录一次，再从个人中心主动绑定（先取 linked-accounts/{provider}/link-token，
        //   再带 linkToken 发起 OAuth，回调走 LinkExternalLoginAsync）：那时「他是不是账号主人」已经被证明过了，绑定就是安全的。
        if (!emailVerified && !string.IsNullOrEmpty(email) && await _userManager.FindByEmailAsync(email) != null)
        {
            LogWarning(
                "OAuth callback from {Provider} carried an unverified email that matches an existing account; "
                + "refusing to auto-link. The user must sign in normally and link from their profile.",
                provider);

            return Fail<OAuthCallbackResultDto>(
                "This email is already registered. Sign in with your existing method, then link this provider from your profile.",
                409,
                ErrorCodes.IDENTITY_OAUTH_LINK_CONFIRMATION_REQUIRED);
        }

        // 用户不存在且邮箱未注册，自动创建无密码账户
        // 用户名优先使用 email，如果没有则使用显示名或 provider key，最后使用 GUID 作为后备
        string baseUserName;
        if (!string.IsNullOrEmpty(email))
        {
            baseUserName = email;
        }
        else if (!string.IsNullOrEmpty(displayName))
        {
            baseUserName = displayName;
        }
        else if (!string.IsNullOrEmpty(providerKey) && providerKey.Length > 0)
        {
            // 使用 providerKey 的前8个字符（如果长度足够）
            var keyPart = providerKey.Length >= 8 ? providerKey[..8] : providerKey;
            baseUserName = $"user_{keyPart}";
        }
        else
        {
            // 所有值都无效时的后备方案：使用 GUID
            baseUserName = $"user_{Guid.NewGuid():N}";
        }
        var finalUserName = await UserNameGenerator.GenerateUniqueAsync(baseUserName, async (name) => await _userManager.FindByNameAsync(name) != null, email);

        // 创建用户（不设置密码）
        var newUser = new User
        {
            UserName = finalUserName,
            Email = email,
            // ★ 只有提供商真的证实过，才置确认位。此前这里是「邮箱非空即已确认」——
            // 于是一个自己填的地址会在本地拿到 EmailConfirmed = true，
            // 而框架把那一位当作对外的断言（找回密码、邮箱 2FA、消费应用按域名授权都读它）。
            EmailConfirmed = emailVerified,
            TenantId = ResolveNewUserTenantId(),
        };

        // 创建用户（不设置密码，使用 CreateAsync 不带密码参数）
        var createResult = await _userManager.CreateAsync(newUser);
        if (!createResult.Succeeded)
        {
            return Fail<OAuthCallbackResultDto>(
                $"Failed to create user: {createResult.FormatErrors()}",
                400, ErrorCodes.IDENTITY_OAUTH_ERROR);
        }

        // 创建用户详情（保存名字、昵称、头像等信息）
        if (_userDetailService != null && (firstName != null || lastName != null || displayName != null || avatarUrl != null))
        {
            try
            {
                await _userDetailService.CreateOrUpdateAsync(newUser.Id, new CreateUserDetailDto
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Nickname = displayName,
                    AvatarUrl = avatarUrl
                });
                LogInformation("User detail created for new OAuth user: {UserName}", newUser.UserName);
            }
            catch (Exception ex)
            {
                // 用户详情创建失败不影响主流程，仅记录警告（包含完整异常信息）
                Logger.LogWarning(ex, "Failed to create user detail for OAuth user {UserName}", newUser.UserName);
            }
        }

        // 关联 OAuth 账户
        var loginInfo = new UserLoginInfo(provider, providerKey, displayName ?? provider);
        var addLoginResult = await _userManager.AddLoginAsync(newUser, loginInfo);
        if (!addLoginResult.Succeeded)
        {
            // 如果关联失败，删除刚创建的用户
            await _userManager.DeleteAsync(newUser);
            return Fail<OAuthCallbackResultDto>(
                $"Failed to link OAuth account: {addLoginResult.FormatErrors()}",
                400, ErrorCodes.IDENTITY_OAUTH_ERROR);
        }

        // 记录登录记录
        await _userLoginService.RecordLoginAsync(newUser.Id, provider, providerKey, displayName);

        // 生成 Token 并返回
        var newUserResult = await GenerateTokenAndPublishLoginEventAsync(newUser, provider, principal);
        if (newUserResult.Succeeded)
        {
            LogInformation("New user created via OAuth: {UserName}, provider: {Provider}", newUser.UserName, provider);
        }
        return newUserResult;
    }


    public async Task<Result> LinkExternalLoginAsync(Guid userId, string provider, ClaimsPrincipal principal)
    {
        Check.NotNull(principal);

        var providerKey = ExtractProviderKey(principal);
        if (string.IsNullOrEmpty(providerKey))
        {
            return Fail("Provider key not found in claims", 400, ErrorCodes.IDENTITY_OAUTH_ERROR);
        }

        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        // ★ 这个外部身份已经属于别人 → 409，两边都不动。这正是登录流程的「按 provider key 直接登录」分支
        //   在绑定语义下的反面：登录时它是「就是这个人」，绑定时它是「这个身份不是你的」。
        var owner = await _userManager.FindByLoginAsync(provider, providerKey);
        if (owner != null && owner.Id != userId)
        {
            LogWarning("Refused to link {Provider} to user {UserId}: the external account is already linked to another user.", provider, userId);
            return Fail("This external account is already linked to another user.", 409, ErrorCodes.DATA_CONFLICT);
        }

        // 与登录流程同一组 claim 取显示名；不取邮箱 —— 绑定不按邮箱认领任何东西。
        var displayName = ExtractDisplayName(principal);
        return await LinkOAuthAccountAsync(userId, provider, providerKey, displayName);
    }

    public async Task<Result> LinkOAuthAccountAsync(Guid userId, string provider, string providerKey, string? displayName = null)
    {
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        // 检查是否已关联
        var hasLogin = await _userLoginService.HasLoginAsync(userId, provider, providerKey);
        if (hasLogin)
        {
            return Ok(); // 已关联，无需重复操作
        }

        // 添加外部登录
        var loginInfo = new UserLoginInfo(provider, providerKey, displayName ?? provider);
        var result = await _userManager.AddLoginAsync(user, loginInfo);

        if (!result.Succeeded)
        {
            return Fail($"Failed to link OAuth account: {result.FormatErrors()}", 400, ErrorCodes.IDENTITY_OAUTH_ERROR);
        }

        // 记录登录记录
        await _userLoginService.RecordLoginAsync(userId, provider, providerKey, displayName);
        LogInformation("OAuth account linked for user {UserName} (ID: {UserId}), provider: {Provider}", user.UserName ?? string.Empty, userId, provider);
        return Ok();
    }

    public async Task<Result> UnlinkOAuthAccountAsync(Guid userId, string provider)
    {
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        // 获取该Provider的所有登录
        var logins = await _userManager.GetLoginsAsync(user);
        var loginToRemove = logins.FirstOrDefault(l => l.LoginProvider == provider);

        if (loginToRemove == null)
        {
            return Fail("OAuth account not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        var result = await _userManager.RemoveLoginAsync(user, loginToRemove.LoginProvider, loginToRemove.ProviderKey);
        if (!result.Succeeded)
        {
            return Fail($"Failed to unlink OAuth account: {result.FormatErrors()}", 400, ErrorCodes.IDENTITY_OAUTH_ERROR);
        }

        // 删除登录记录
        await _userLoginService.RemoveLoginAsync(userId, provider, loginToRemove.ProviderKey);
        LogInformation("OAuth account unlinked for user {UserName} (ID: {UserId}), provider: {Provider}", user.UserName ?? string.Empty, userId, provider);
        return Ok();
    }

    #region Private Methods

    /// <summary>提供商侧的用户标识（支持多平台：Google, Microsoft, Facebook, Twitter, GitHub）。</summary>
    private static string? ExtractProviderKey(ClaimsPrincipal principal)
        => principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub")
            ?? principal.FindFirstValue("id")
            ?? principal.FindFirstValue("user_id");

    /// <summary>显示名 / 昵称（GitHub 用 login，Twitter 用 screen_name）。</summary>
    private static string? ExtractDisplayName(ClaimsPrincipal principal)
        => principal.FindFirstValue("display_name")
            ?? principal.FindFirstValue(ClaimTypes.Name)
            ?? principal.FindFirstValue("name")
            ?? principal.FindFirstValue("login")
            ?? principal.FindFirstValue("screen_name");

    /// <summary>
    /// 生成TokenResult并保存RefreshToken，发布登录事件，返回OAuth回调结果
    /// 统一处理OAuth登录后的Token生成和保存逻辑，减少代码重复。
    /// 先建立登录会话（应用多登录策略），使 OAuth 登录与密码登录一致地受会话强制约束。
    /// </summary>
    private async Task<Result<OAuthCallbackResultDto>> GenerateTokenAndPublishLoginEventAsync(User user, string provider, ClaimsPrincipal principal)
    {
        // ★★★ 整段签发交给共享出口。此前这里是 IssueTokenAsync 后半段的**手抄件**：
        // 守卫链、会话协调器、令牌、登录事件都抄了，唯独漏掉两件 ——
        //   ① 2FA 判定（GetTwoFactorEnabledAsync 从头到尾没出现过）⇒ 开着 TOTP 的账号
        //      只要有一个已关联的第三方身份，就能不过第二因子登进来；
        //   ② 义务位（GetOwedObligations）⇒ 管理员设的临时密码、到期密码、
        //      「必须先绑验证器」在这条路上一律作废。
        // 这与验证码登录上修过的是同一个形态（抄了共享出口的后半段、精确地漏掉其中一步），
        // 而修法也一样：不要再抄一遍，改成调它。
        //
        // 代价是挑战以**失败信封**返回（403 + 错误码 + 临时令牌），调用方必须把
        // ErrorCode / ErrorDetails 原样带给前端 —— 见 OAuthCallbackResultDto 上的注释。
        var issued = await _authService.IssueTokenAsync(user, LoginMethod.OAuth);
        if (!issued.Succeeded)
        {
            return Fail<OAuthCallbackResultDto>(
                issued.Message ?? "Login rejected",
                issued.Code ?? 403,
                issued.ErrorCode,
                issued.ErrorDetails);
        }

        var tokenResult = issued.Data!;
        LogInformation("OAuth sign-in issued via the shared token exit for user {UserId} ({Provider}).", user.Id, provider);

        return Ok(new OAuthCallbackResultDto
        {
            Success = true,
            AccessToken = tokenResult.AccessToken,
            RefreshToken = tokenResult.RefreshToken,
            ExpiresAt = tokenResult.ExpiresAt
        });
    }

    #endregion

    private Guid? ResolveNewUserTenantId()
    {
        if (!_multiTenancyEnabled)
        {
            return null;
        }

        return _currentTenant?.Id ?? CurrentUser?.TenantId;
    }
}
