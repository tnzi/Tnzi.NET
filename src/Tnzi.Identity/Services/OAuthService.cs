namespace Tnzi.Identity.Services;

/// <summary>
/// OAuth服务实现
/// </summary>
public class OAuthService : ApplicationService, IOAuthService
{
    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;
    private readonly ITokenService _tokenService;
    private readonly IUserLoginService _userLoginService;
    private readonly IAuthTokenService? _authTokenService;
    private readonly IEventBus? _eventBus;
    private readonly IUserDetailService? _userDetailService;
    private readonly IdentityOptions _identityOptions;
    private readonly ICurrentTenant? _currentTenant;
    private readonly bool _multiTenancyEnabled;

    public OAuthService(
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        ITokenService tokenService,
        IUserLoginService userLoginService,
        IServiceProvider serviceProvider,
        IOptions<IdentityOptions> identityOptions,
        IAuthTokenService? authTokenService = null,
        IEventBus? eventBus = null,
        IUserDetailService? userDetailService = null,
        ICurrentTenant? currentTenant = null,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null)
        : base(serviceProvider)
    {
        _userManager = Check.NotNull(userManager);
        _signInManager = Check.NotNull(signInManager);
        _tokenService = Check.NotNull(tokenService);
        _userLoginService = Check.NotNull(userLoginService);
        _identityOptions = Check.NotNull(identityOptions).Value;
        _authTokenService = authTokenService;
        _eventBus = eventBus;
        _userDetailService = userDetailService;
        _currentTenant = currentTenant;
        _multiTenancyEnabled = multiTenancyOptions?.Value.Enabled ?? false;
    }

    public async Task<Result<OAuthCallbackResultDto>> HandleOAuthCallbackAsync(string provider, ClaimsPrincipal principal)
    {
        // 从 Claims 中提取用户信息（支持多平台：Google, Microsoft, Facebook, Twitter, GitHub）
        var providerKey = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub")
            ?? principal.FindFirstValue("id")
            ?? principal.FindFirstValue("user_id");

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
        var displayName = principal.FindFirstValue("display_name")
            ?? principal.FindFirstValue(ClaimTypes.Name)
            ?? principal.FindFirstValue("name")
            ?? principal.FindFirstValue("login")       // GitHub 使用 login
            ?? principal.FindFirstValue("screen_name"); // Twitter 使用 screen_name

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
            LogInformation("OAuth login successful for user {UserName} via {Provider}", existingUser.UserName ?? string.Empty, provider);
            return Ok(result);
        }

        // 用户不存在，检查邮箱是否已注册
        User? userByEmail = null;
        if (!string.IsNullOrEmpty(email))
        {
            userByEmail = await _userManager.FindByEmailAsync(email);
        }

        if (userByEmail != null)
        {
            // 邮箱已注册，关联OAuth账户
            var linkResult = await LinkOAuthAccountAsync(userByEmail.Id, provider, providerKey, displayName);
            if (!linkResult.Succeeded)
            {
                return Fail<OAuthCallbackResultDto>(linkResult.Message ?? "Failed to link OAuth account", linkResult.Code ?? 400, linkResult.ErrorCode);
            }

            // 登录并返回Token
            var result = await GenerateTokenAndPublishLoginEventAsync(userByEmail, provider, principal);
            LogInformation("OAuth login successful for user {UserName} via {Provider} (linked account)", userByEmail.UserName ?? string.Empty, provider);
            return Ok(result);
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
        var finalUserName = await UserNameGenerator.GenerateUniqueAsync(baseUserName, async (name) => await _userManager.FindByNameAsync(name) != null);

        // 创建用户（不设置密码）
        var newUser = new User
        {
            UserName = finalUserName,
            Email = email,
            EmailConfirmed = !string.IsNullOrEmpty(email), // OAuth 提供的邮箱视为已验证
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
        LogInformation("New user created via OAuth: {UserName}, provider: {Provider}", newUser.UserName, provider);
        return Ok(newUserResult);
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

    /// <summary>
    /// 生成TokenResult并保存RefreshToken，发布登录事件，返回OAuth回调结果
    /// 统一处理OAuth登录后的Token生成和保存逻辑，减少代码重复
    /// </summary>
    private async Task<OAuthCallbackResultDto> GenerateTokenAndPublishLoginEventAsync(User user, string provider, ClaimsPrincipal principal)
    {
        var roles = await GetRolesWithTenantContextAsync(user);
        var tokenResult = _tokenService.GenerateTokenResult(user, roles);
        var refreshTokenExpiresAt = DateTime.UtcNow.AddDays(_identityOptions.Jwt.RefreshTokenExpirationDays);

        // 保存RefreshToken（核心业务逻辑，必须同步执行）
        if (_authTokenService != null)
        {
            await _authTokenService.SaveTokenAsync(
                user.Id,
                IdentityConstants.TokenProvider.JWT,
                IdentityConstants.TokenName.RefreshToken,
                tokenResult.RefreshToken,
                refreshTokenExpiresAt);
        }

        // 发布登录事件（由事件处理器处理日志记录和会话创建）
        if (_eventBus != null)
        {
            var ipAddress = ScopedContext?.ClientIpAddress;
            var userAgent = ScopedContext?.UserAgent;
            await _eventBus.PublishAsync(new UserLoggedInEvent
            {
                UserId = user.Id,
                UserName = user.UserName ?? string.Empty,
                LoginTime = DateTime.UtcNow,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                LoginProvider = provider
            }, cancellationToken: default);
        }

        return new OAuthCallbackResultDto
        {
            Success = true,
            AccessToken = tokenResult.AccessToken,
            RefreshToken = tokenResult.RefreshToken,
            ExpiresAt = tokenResult.ExpiresAt
        };
    }

    #endregion

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

    private Guid? ResolveNewUserTenantId()
    {
        if (!_multiTenancyEnabled)
        {
            return null;
        }

        return _currentTenant?.Id ?? CurrentUser?.TenantId;
    }
}
